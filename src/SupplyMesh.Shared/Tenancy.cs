using System.Security.Claims;
using Microsoft.AspNetCore.Http;
namespace SupplyMesh.Shared;

public interface ITenantContext { Guid TenantId { get; } }

/// <summary>Tenant comes ONLY from the validated JWT "tid" claim — never from a client header/body.</summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid TenantId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("tid"), out var t)
        ? t : throw new UnauthorizedAccessException("Missing tenant claim");
}
public sealed class FixedTenantContext(Guid id) : ITenantContext { public Guid TenantId => id; }
