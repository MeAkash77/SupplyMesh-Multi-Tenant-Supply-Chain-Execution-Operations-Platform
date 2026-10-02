using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyMesh.Shared;
namespace SupplyMesh.Catalog.Api.Controllers;

public record CatalogItemDto(string Sku, string Name, string Category, long SupplierId, decimal UnitCost, string Uom = "EA");

[ApiController, Route("api/v1/catalog")]
public class CatalogController(IDb db, ITenantContext tenant) : ControllerBase
{
    [HttpGet, Authorize(Policy = "catalog.read")]
    public async Task<IActionResult> Search([FromQuery] string? category, [FromQuery] long after = 0, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var rows = await db.ProcAsync<dynamic>("sm.usp_Catalog_Search", new { TenantId = tenant.TenantId, Category = category, AfterItemId = after, PageSize = pageSize }, ct);
        return Ok(rows);
    }

    [HttpPut, Authorize(Policy = "catalog.write")]
    public async Task<IActionResult> Upsert([FromBody] CatalogItemDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Sku) || dto.UnitCost < 0) return ValidationProblem("Invalid SKU or cost");
        var id = (await db.ProcAsync<long>("sm.usp_Catalog_Upsert",
            new { TenantId = tenant.TenantId, dto.Sku, dto.Name, dto.Category, dto.SupplierId, dto.UnitCost, dto.Uom }, ct)).Single();
        return Ok(new { itemId = id });
    }
}
