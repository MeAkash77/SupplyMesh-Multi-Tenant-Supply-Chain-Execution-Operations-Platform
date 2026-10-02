using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
namespace SupplyMesh.Shared;

public static class KafkaConfig
{
    /// <summary>Same code runs on local Kafka and Azure Event Hubs (Kafka endpoint, SASL_SSL + $ConnectionString).</summary>
    public static ProducerConfig Producer(IConfiguration c) => Apply(new ProducerConfig { Acks = Acks.All, EnableIdempotence = true, LingerMs = 5, CompressionType = CompressionType.Zstd }, c);
    public static ConsumerConfig Consumer(IConfiguration c, string group) =>
        Apply(new ConsumerConfig { GroupId = group, EnableAutoCommit = false, AutoOffsetReset = AutoOffsetReset.Earliest, IsolationLevel = IsolationLevel.ReadCommitted }, c);

    private static T Apply<T>(T cfg, IConfiguration c) where T : ClientConfig
    {
        cfg.BootstrapServers = c["Kafka:BootstrapServers"] ?? "kafka:9092";
        if (c["Kafka:EventHubsConnectionString"] is { Length: > 0 } cs)
        { cfg.SecurityProtocol = SecurityProtocol.SaslSsl; cfg.SaslMechanism = SaslMechanism.Plain; cfg.SaslUsername = "$ConnectionString"; cfg.SaslPassword = cs; }
        return cfg;
    }
}
