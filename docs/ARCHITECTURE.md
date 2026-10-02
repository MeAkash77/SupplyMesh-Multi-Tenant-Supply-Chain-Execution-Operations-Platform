# SupplyMesh Architecture

```
React SPA ──HTTPS+JWT(Entra ID)──► Ingress (NGINX, TLS, rate-limit)
                                      ├─► Catalog API (.NET 8)  ─┐
                                      └─► Orders API  (.NET 8)  ─┼─► Azure SQL / MSSQL  (stored procs, RLS, outbox)
                                              │ Outbox relay      │
                                              ▼                   │
                         Kafka / Azure Event Hubs (po.events, po.events.dlq; key = PoNumber)
                                              ▼
                                   Inventory Worker (consumer group, idempotent, DLQ) ──► sm.Inventory
Observability: Serilog JSON ─► Elasticsearch | StatsD ─► Telegraf ─► InfluxDB ─► Grafana | Azure Monitor / App Insights
```

## Key decisions
| Concern | Decision | Why |
|---|---|---|
| Multi-tenancy | Shared DB, `TenantId` column + **SQL Row-Level Security**; tenant from JWT `tid` claim only | Isolation enforced by DB even if app code has a bug (defence in depth) |
| Data access | Dapper + **stored procedures** (TVPs, MERGE, keyset paging) | Explicit plans, tunable, matches JD stored-proc focus |
| Consistency | **Transactional outbox** + idempotent consumer (`ProcessedEvent`) | No dual-write problem; at-least-once delivery yields exactly-once effect |
| Ordering | Kafka key = PoNumber | All events of one PO land in one partition, in order |
| Failure handling | 3 retries w/ backoff → DLQ → alert on DLQ depth | Poison messages never block a partition |
| Security | OAuth2/OIDC JWT, scope policies (`po.approve` etc.), no secrets in repo (Key Vault + CSI), non-root read-only containers, NetworkPolicy default-deny, TLS 1.2+, private SQL endpoint | |
| Scalability | Stateless APIs behind HPA (3–12), PDB, zone spread; consumers scale to partition count | |
| Cloud portability | Same Kafka client for local Kafka and Event Hubs; Bicep for Azure | |

## Event flow: receiving a PO
1. `POST /purchase-orders/{po}/receive` → `usp_PurchaseOrder_Transition` updates status **and** inserts an outbox row (one txn).
2. OutboxRelay publishes `PurchaseOrderReceived` (acks=all, idempotent producer) and marks it published.
3. Inventory worker consumes, calls `usp_Inventory_ApplyReceipt` (dedupes on EventId), commits offset only after DB commit.
4. React Inventory tab shows the new stock (eventually consistent).
