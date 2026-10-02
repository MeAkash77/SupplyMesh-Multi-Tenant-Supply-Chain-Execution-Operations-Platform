# Production Troubleshooting Runbook

Method: **Scope → Correlate → Isolate layer → Fix → Verify → Post-mortem.** Always start from a correlation id (`X-Correlation-Id` header, present in every log line).

## 1. Slow API (p95 > 500ms)
1. Grafana *SupplyMesh Operations*: which route? since when? did a deploy happen (`kubectl rollout history`)?
2. `python ops/scripts/log_analyzer.py app.log` → slowest routes; `--cid <id>` for a single request trace.
3. DB suspect → `ops/scripts/sql_diagnostics.sql` (top CPU, blocking, missing indexes). Typical fix: covering index / rewrite proc (see `IX_Item_Category`, keyset paging in `usp_Catalog_Search`).
4. Host suspect (Windows) → `ops/scripts/Collect-Perf.ps1`, open `.blg` in **PerfMon**: `% Processor Time`, `Pages/sec`, `.NET CLR % Time in GC`, `Exceptions/sec`, `Lock Waits/sec`, `Page life expectancy`.
5. Pod suspect → `ops/scripts/k8s-triage.sh` (OOMKilled, CPU throttling, restarts).

## 2. Network issues (timeouts, resets, TLS failures)
- `kubectl exec` → `curl -w "@curl-format.txt"` to split DNS / connect / TLS / TTFB timings.
- **Wireshark** / `tcpdump -i any -w cap.pcap port 1433 or port 9093` on the node/pod; analyse in Wireshark with filters:
  - `tcp.analysis.retransmission || tcp.analysis.zero_window` → packet loss / receiver overwhelmed
  - `tcp.flags.reset==1` → server/firewall resetting connections (check NSG, idle timeout 4 min on Azure LB → enable TCP keepalive)
  - `tls.alert_message` → cert / protocol mismatch
  - `tds` (SQL) – long gap between request and response = server-side time, not network.
- DNS: `nslookup <svc>.supplymesh.svc.cluster.local`; check CoreDNS logs.
- NetworkPolicy blocking? `kubectl describe networkpolicy -n supplymesh`.

## 3. Kafka: events not processed / inventory stale
1. `ops/scripts/kafka-lag.sh` → lag growing? DLQ non-zero?
2. Outbox backlog query in `sql_diagnostics.sql` (#4): rows pending + old → relay stuck (check orders-api logs `OUTBOX_RELAY_FAILURE`).
3. Consumer logs `POISON_MESSAGE` → inspect DLQ message, fix data/bug, replay.
4. Frequent rebalances → long processing > `max.poll.interval.ms`, or pod flapping.

## 4. Security incident / auth failures
- 401s spike: token expiry/clock skew, wrong `Audience`; 403s: missing `scp` scope. Check Entra app registration + `Auth__*` config.
- Cross-tenant data suspicion: verify RLS (`SELECT * FROM sys.security_policies`), audit `sm.AuditLog`; every proc sets `SESSION_CONTEXT`.

## 5. Post-incident
Timeline, root cause, detection gap, corrective action, add Grafana alert + regression test.
