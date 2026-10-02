# TDD workflow used on SupplyMesh
1. **Red** – write a failing NUnit test for the rule (`PoRulesTests.CanTransition_follows_state_machine`).
2. **Green** – smallest implementation in `PoRules`.
3. **Refactor** – extract pure functions; DB remains authority (`usp_PurchaseOrder_Transition`), tests mirror it.
Run: `dotnet test`. CI enforces build with warnings-as-errors, tests, vulnerable-package scan.
Next: integration tests with Testcontainers (MSSQL + Kafka) to verify RLS isolation and outbox → consumer idempotency.
