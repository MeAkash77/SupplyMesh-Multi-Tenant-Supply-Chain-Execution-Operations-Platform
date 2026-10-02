using System.Net.Sockets;
using System.Text;
namespace SupplyMesh.Shared;

public interface IMetrics { void Count(string name, long v = 1, string? tag = null); void Timing(string name, double ms, string? tag = null); }

/// <summary>Emits StatsD/DogStatsD-format UDP packets -> Telegraf (statsd input) -> InfluxDB -> Grafana.</summary>
public sealed class StatsdMetrics(string host, int port) : IMetrics, IDisposable
{
    private readonly UdpClient _udp = new();
    private void Send(string s) { try { var b = Encoding.UTF8.GetBytes(s); _udp.Send(b, b.Length, host, port); } catch { /* metrics must never break requests */ } }
    public void Count(string n, long v = 1, string? tag = null) => Send($"supplymesh.{n}:{v}|c{(tag is null ? "" : $"|#{tag}")}");
    public void Timing(string n, double ms, string? tag = null) => Send($"supplymesh.{n}:{ms:F1}|ms{(tag is null ? "" : $"|#{tag}")}");
    public void Dispose() => _udp.Dispose();
}
