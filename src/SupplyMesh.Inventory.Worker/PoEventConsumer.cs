using System.Text.Json;
using Confluent.Kafka;
using SupplyMesh.Shared;
namespace SupplyMesh.Inventory.Worker;

/// <summary>At-least-once consumer: manual commit AFTER DB success, idempotent proc, retry w/ backoff, poison messages -> DLQ.</summary>
public sealed class PoEventConsumer(IConfiguration cfg, IDb db, IMetrics metrics, ILogger<PoEventConsumer> log) : BackgroundService
{
    private const int MaxAttempts = 3;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();
        using var consumer = new ConsumerBuilder<string, string>(KafkaConfig.Consumer(cfg, "inventory-worker")).Build();
        using var dlq = new ProducerBuilder<string, string>(KafkaConfig.Producer(cfg)).Build();
        consumer.Subscribe(Topics.PoEvents);
        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? msg = null;
            try
            {
                msg = consumer.Consume(ct);
                var lag = DateTime.UtcNow - msg.Message.Timestamp.UtcDateTime;
                metrics.Timing("consumer.lag_ms", lag.TotalMilliseconds);
                await HandleWithRetryAsync(msg.Message.Value, ct);
                consumer.Commit(msg);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (msg is not null)
            {
                log.LogError(ex, "POISON_MESSAGE key={Key} partition={P} offset={O}", msg.Message.Key, msg.Partition.Value, msg.Offset.Value);
                await dlq.ProduceAsync(Topics.PoEventsDlq, new Message<string, string> { Key = msg.Message.Key, Value = msg.Message.Value }, ct);
                metrics.Count("consumer.dlq");
                consumer.Commit(msg);
            }
            catch (ConsumeException ce) { log.LogError(ce, "KAFKA_CONSUME_ERROR {Reason}", ce.Error.Reason); await Task.Delay(1000, ct); }
        }
        consumer.Close();
    }

    private async Task HandleWithRetryAsync(string json, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await HandleAsync(json, ct); return; }
            catch (Exception) when (attempt < MaxAttempts) { await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(3, attempt)), ct); }
        }
    }

    private async Task HandleAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("type").GetString() != "PurchaseOrderReceived") return;   // ignore other event types
        var tenant = root.GetProperty("tenantId").GetGuid();
        var po = root.GetProperty("poNumber").GetString()!;
        // Deterministic EventId from (tenant, po, type): redelivery of the same event is a no-op
        var eventId = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{tenant}|{po}|received")));
        var applied = (await db.ProcAsync<bool>("sm.usp_Inventory_ApplyReceipt", new { EventId = eventId, TenantId = tenant, PoNumber = po, Warehouse = "WH-MAIN" }, ct)).Single();
        metrics.Count(applied ? "inventory.applied" : "inventory.duplicate_skipped");
    }
}
