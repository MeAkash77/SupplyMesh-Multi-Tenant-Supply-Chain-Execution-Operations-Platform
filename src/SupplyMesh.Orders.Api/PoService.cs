using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SupplyMesh.Shared;
namespace SupplyMesh.Orders.Api;

public record PoLineDto(long ItemId, int Qty);
public record CreatePoRequest(long SupplierId, List<PoLineDto> Lines);

public static class PoRules
{
    // Pure, unit-testable domain logic (mirrors the DB state machine; DB remains the source of truth)
    private static readonly Dictionary<string, string[]> Allowed = new()
    { ["CREATED"] = ["APPROVED", "CANCELLED"], ["APPROVED"] = ["SHIPPED", "CANCELLED"], ["SHIPPED"] = ["RECEIVED"], ["RECEIVED"] = [], ["CANCELLED"] = [] };
    public static bool CanTransition(string from, string to) => Allowed.TryGetValue(from, out var n) && n.Contains(to);
    public static string ValidateCreate(CreatePoRequest r) =>
        r.Lines is null || r.Lines.Count == 0 ? "PO needs at least one line"
        : r.Lines.Any(l => l.Qty <= 0) ? "Line quantity must be positive"
        : r.Lines.GroupBy(l => l.ItemId).Any(g => g.Count() > 1) ? "Duplicate items; merge lines" : "";
    public static string NewPoNumber(DateTime utc) => $"PO-{utc:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}

public class PoService(IDb db, ITenantContext tenant, IMetrics metrics)
{
    public async Task<string> CreateAsync(CreatePoRequest req, CancellationToken ct)
    {
        var po = PoRules.NewPoNumber(DateTime.UtcNow);
        var tvp = new DataTable(); tvp.Columns.Add("ItemId", typeof(long)); tvp.Columns.Add("Qty", typeof(int));
        foreach (var l in req.Lines) tvp.Rows.Add(l.ItemId, l.Qty);
        using var con = await db.OpenAsync();
        await con.ExecuteAsync(new CommandDefinition("sm.usp_PurchaseOrder_Create",
            new { TenantId = tenant.TenantId, req.SupplierId, PoNumber = po, Lines = tvp.AsTableValuedParameter("sm.PoLineType") },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        metrics.Count("po.created", 1, $"tenant:{tenant.TenantId}");
        return po;
    }

    public async Task TransitionAsync(string po, string status, CancellationToken ct)
    {
        using var con = await db.OpenAsync();
        try { await con.ExecuteAsync(new CommandDefinition("sm.usp_PurchaseOrder_Transition", new { TenantId = tenant.TenantId, PoNumber = po, NewStatus = status }, commandType: CommandType.StoredProcedure, cancellationToken: ct)); }
        catch (SqlException e) when (e.Number is 50002 or 50003) { throw new InvalidOperationException(e.Message, e); }  // mapped to 404/409 in controller
    }
}
