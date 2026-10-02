using System.Data;
using Dapper;
using Confluent.Kafka;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace SupplyMesh.Shared;

/// <summary>Transactional-outbox relay: polls sm.Outbox (READPAST so replicas don't collide) and publishes to Kafka, then marks rows published.</summary>
public sealed class OutboxRelay(IConfiguration cfg, IMetrics metrics, ILogger<OutboxRelay> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var producer = new ProducerBuilder<string, string>(KafkaConfig.Producer(cfg)).Build();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var con = new SqlConnection(cfg.GetConnectionString("Sql"));
                await con.OpenAsync(ct);
                await con.ExecuteAsync("EXEC sp_set_session_context N'IsSystem', 1");
                using var tx = con.BeginTransaction();
                var rows = (await con.QueryAsync<(long OutboxId, string Topic, string MessageKey, string Payload)>(
                    "sm.usp_Outbox_ClaimBatch", new { BatchSize = 100 }, tx, commandType: CommandType.StoredProcedure)).ToList();
                foreach (var r in rows)
                {
                    await producer.ProduceAsync(r.Topic, new Message<string, string> { Key = r.MessageKey, Value = r.Payload }, ct); // key = PoNumber -> per-PO ordering
                    await con.ExecuteAsync("sm.usp_Outbox_MarkPublished", new { r.OutboxId }, tx, commandType: CommandType.StoredProcedure);
                }
                tx.Commit();
                if (rows.Count > 0) metrics.Count("outbox.published", rows.Count);
                if (rows.Count == 0) await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log.LogError(ex, "OUTBOX_RELAY_FAILURE"); metrics.Count("outbox.errors"); await Task.Delay(2000, ct); }
        }
    }
}
