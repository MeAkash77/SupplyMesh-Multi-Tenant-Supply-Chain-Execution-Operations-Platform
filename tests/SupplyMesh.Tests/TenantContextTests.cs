using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using SupplyMesh.Shared;
namespace SupplyMesh.Tests;

[TestFixture]
public class TenantContextTests
{
    private static HttpTenantContext With(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        var acc = new Mock<IHttpContextAccessor>(); acc.Setup(a => a.HttpContext).Returns(ctx);
        return new HttpTenantContext(acc.Object);
    }

    [Test] public void Reads_tenant_from_jwt_claim()
    {
        var id = Guid.NewGuid();
        Assert.That(With(new Claim("tid", id.ToString())).TenantId, Is.EqualTo(id));
    }
    [Test] public void Missing_claim_is_unauthorized() =>
        Assert.Throws<UnauthorizedAccessException>(() => _ = With().TenantId);
    [Test] public void Malformed_claim_is_unauthorized() =>
        Assert.Throws<UnauthorizedAccessException>(() => _ = With(new Claim("tid", "not-a-guid")).TenantId);
}
