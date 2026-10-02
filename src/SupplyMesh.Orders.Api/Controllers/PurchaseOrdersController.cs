using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyMesh.Shared;
namespace SupplyMesh.Orders.Api.Controllers;

[ApiController, Route("api/v1/purchase-orders")]
public class PurchaseOrdersController(PoService svc, IDb db, ITenantContext tenant) : ControllerBase
{
    [HttpGet, Authorize(Policy = "po.read")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        Ok(await db.ProcAsync<dynamic>("sm.usp_PurchaseOrder_List", new { TenantId = tenant.TenantId, Status = status }, ct));

    [HttpPost, Authorize(Policy = "po.write")]
    public async Task<IActionResult> Create([FromBody] CreatePoRequest req, CancellationToken ct)
    {
        var err = PoRules.ValidateCreate(req);
        if (err != "") return ValidationProblem(err);
        var po = await svc.CreateAsync(req, ct);
        return Created($"api/v1/purchase-orders/{po}", new { poNumber = po });
    }

    [HttpPost("{po}/approve"), Authorize(Policy = "po.approve")] public Task<IActionResult> Approve(string po, CancellationToken ct) => Move(po, "APPROVED", ct);
    [HttpPost("{po}/ship"), Authorize(Policy = "po.write")] public Task<IActionResult> Ship(string po, CancellationToken ct) => Move(po, "SHIPPED", ct);
    [HttpPost("{po}/receive"), Authorize(Policy = "po.write")] public Task<IActionResult> Receive(string po, CancellationToken ct) => Move(po, "RECEIVED", ct);
    [HttpPost("{po}/cancel"), Authorize(Policy = "po.write")] public Task<IActionResult> Cancel(string po, CancellationToken ct) => Move(po, "CANCELLED", ct);

    [HttpGet("/api/v1/inventory"), Authorize(Policy = "po.read")]
    public async Task<IActionResult> Inventory(CancellationToken ct) => Ok(await db.ProcAsync<dynamic>("sm.usp_Inventory_List", new { TenantId = tenant.TenantId }, ct));

    private async Task<IActionResult> Move(string po, string to, CancellationToken ct)
    {
        try { await svc.TransitionAsync(po, to, ct); return NoContent(); }
        catch (InvalidOperationException e) { return e.Message.Contains("not found") ? NotFound(e.Message) : Conflict(e.Message); }
    }
}
