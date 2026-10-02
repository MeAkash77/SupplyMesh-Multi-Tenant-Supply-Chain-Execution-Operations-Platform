using NUnit.Framework;
using SupplyMesh.Shared;
namespace SupplyMesh.Tests;
[TestFixture]
public class MetricsTests
{
    [Test] public void Statsd_never_throws_when_collector_is_down() =>
        Assert.DoesNotThrow(() => new StatsdMetrics("127.0.0.1", 1).Count("x"));
}
