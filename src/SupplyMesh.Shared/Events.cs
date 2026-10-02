using System.Text.Json;
namespace SupplyMesh.Shared;

public static class Topics { public const string PoEvents = "po.events"; public const string PoEventsDlq = "po.events.dlq"; }

/// <summary>Envelope for all domain events. EventId drives consumer idempotency.</summary>
public record EventEnvelope(Guid EventId, string Type, Guid TenantId, string PoNumber, string? Status, DateTime OccurredUtc)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
