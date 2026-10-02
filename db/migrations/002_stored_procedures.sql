USE SupplyMesh;
GO
-- Catalog search: keyset pagination, covered by IX_Item_Category (performance-tuned)
CREATE OR ALTER PROCEDURE sm.usp_Catalog_Search @TenantId UNIQUEIDENTIFIER, @Category NVARCHAR(100) = NULL, @AfterItemId BIGINT = 0, @PageSize INT = 50
AS BEGIN
  SET NOCOUNT ON;
  EXEC sp_set_session_context N'TenantId', @TenantId;
  SELECT TOP (@PageSize) i.ItemId, i.Sku, i.Name, i.Category, i.UnitCost, i.Uom, i.Status, s.Name AS SupplierName
  FROM sm.CatalogItem i JOIN sm.Supplier s ON s.SupplierId = i.SupplierId
  WHERE i.TenantId = @TenantId AND i.ItemId > @AfterItemId AND (@Category IS NULL OR i.Category = @Category)
  ORDER BY i.ItemId;
END
GO
CREATE OR ALTER PROCEDURE sm.usp_Catalog_Upsert @TenantId UNIQUEIDENTIFIER, @Sku NVARCHAR(64), @Name NVARCHAR(300), @Category NVARCHAR(100), @SupplierId BIGINT, @UnitCost DECIMAL(18,4), @Uom NVARCHAR(10) = 'EA'
AS BEGIN
  SET NOCOUNT, XACT_ABORT ON;
  EXEC sp_set_session_context N'TenantId', @TenantId;
  MERGE sm.CatalogItem WITH (HOLDLOCK) AS t
  USING (SELECT @TenantId AS TenantId, @Sku AS Sku) s ON t.TenantId = s.TenantId AND t.Sku = s.Sku
  WHEN MATCHED THEN UPDATE SET Name=@Name, Category=@Category, SupplierId=@SupplierId, UnitCost=@UnitCost, Uom=@Uom
  WHEN NOT MATCHED THEN INSERT (TenantId,Sku,Name,Category,SupplierId,UnitCost,Uom) VALUES (@TenantId,@Sku,@Name,@Category,@SupplierId,@UnitCost,@Uom)
  OUTPUT inserted.ItemId;
END
GO
CREATE TYPE sm.PoLineType AS TABLE (ItemId BIGINT NOT NULL, Qty INT NOT NULL);
GO
-- Create PO + lines + outbox event in ONE transaction
CREATE OR ALTER PROCEDURE sm.usp_PurchaseOrder_Create @TenantId UNIQUEIDENTIFIER, @SupplierId BIGINT, @PoNumber NVARCHAR(40), @Lines sm.PoLineType READONLY
AS BEGIN
  SET NOCOUNT, XACT_ABORT ON;
  EXEC sp_set_session_context N'TenantId', @TenantId;
  BEGIN TRAN;
    INSERT sm.PurchaseOrder (TenantId, PoNumber, SupplierId) VALUES (@TenantId, @PoNumber, @SupplierId);
    DECLARE @PoId BIGINT = SCOPE_IDENTITY();
    INSERT sm.PurchaseOrderLine (PoId, TenantId, ItemId, Qty, UnitCost)
      SELECT @PoId, @TenantId, l.ItemId, l.Qty, i.UnitCost FROM @Lines l JOIN sm.CatalogItem i ON i.ItemId = l.ItemId AND i.TenantId = @TenantId;
    IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @Lines) THROW 50001, 'One or more items not found for tenant', 1;
    UPDATE sm.PurchaseOrder SET TotalAmount = (SELECT SUM(Qty*UnitCost) FROM sm.PurchaseOrderLine WHERE PoId=@PoId) WHERE PoId=@PoId;
    INSERT sm.Outbox (TenantId, Topic, MessageKey, Payload)
      VALUES (@TenantId, 'po.events', @PoNumber, (SELECT 'PurchaseOrderCreated' AS type, @PoId AS poId, @PoNumber AS poNumber, @TenantId AS tenantId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));
  COMMIT;
  SELECT @PoId AS PoId;
END
GO
-- State machine guard: only legal transitions
CREATE OR ALTER PROCEDURE sm.usp_PurchaseOrder_Transition @TenantId UNIQUEIDENTIFIER, @PoNumber NVARCHAR(40), @NewStatus VARCHAR(20)
AS BEGIN
  SET NOCOUNT, XACT_ABORT ON;
  EXEC sp_set_session_context N'TenantId', @TenantId;
  DECLARE @Cur VARCHAR(20) = (SELECT Status FROM sm.PurchaseOrder WITH (UPDLOCK) WHERE TenantId=@TenantId AND PoNumber=@PoNumber);
  IF @Cur IS NULL THROW 50002, 'PO not found', 1;
  IF NOT ((@Cur='CREATED' AND @NewStatus IN ('APPROVED','CANCELLED')) OR (@Cur='APPROVED' AND @NewStatus IN ('SHIPPED','CANCELLED')) OR (@Cur='SHIPPED' AND @NewStatus='RECEIVED'))
    THROW 50003, 'Illegal status transition', 1;
  BEGIN TRAN;
    UPDATE sm.PurchaseOrder SET Status=@NewStatus, UpdatedUtc=SYSUTCDATETIME() WHERE TenantId=@TenantId AND PoNumber=@PoNumber;
    INSERT sm.Outbox (TenantId, Topic, MessageKey, Payload)
      VALUES (@TenantId, 'po.events', @PoNumber, (SELECT 'PurchaseOrder' + CASE @NewStatus WHEN 'RECEIVED' THEN 'Received' ELSE 'StatusChanged' END AS type, @PoNumber AS poNumber, @NewStatus AS status, @TenantId AS tenantId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));
  COMMIT;
END
GO
-- Inventory receipt applied by Kafka consumer; idempotent via ProcessedEvent
CREATE OR ALTER PROCEDURE sm.usp_Inventory_ApplyReceipt @EventId UNIQUEIDENTIFIER, @TenantId UNIQUEIDENTIFIER, @PoNumber NVARCHAR(40), @Warehouse NVARCHAR(20)
AS BEGIN
  SET NOCOUNT, XACT_ABORT ON;
  EXEC sp_set_session_context N'TenantId', @TenantId;
  BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM sm.ProcessedEvent WITH (UPDLOCK, HOLDLOCK) WHERE EventId=@EventId) BEGIN COMMIT; SELECT CAST(0 AS BIT) AS Applied; RETURN; END
    MERGE sm.Inventory WITH (HOLDLOCK) AS t
    USING (SELECT l.ItemId, SUM(l.Qty) AS Qty FROM sm.PurchaseOrderLine l JOIN sm.PurchaseOrder p ON p.PoId=l.PoId WHERE p.TenantId=@TenantId AND p.PoNumber=@PoNumber GROUP BY l.ItemId) s
      ON t.TenantId=@TenantId AND t.ItemId=s.ItemId AND t.Warehouse=@Warehouse
    WHEN MATCHED THEN UPDATE SET OnHand=t.OnHand+s.Qty, UpdatedUtc=SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (TenantId,ItemId,Warehouse,OnHand) VALUES (@TenantId,s.ItemId,@Warehouse,s.Qty);
    INSERT sm.ProcessedEvent (EventId, Consumer) VALUES (@EventId, 'inventory-worker');
  COMMIT;
  SELECT CAST(1 AS BIT) AS Applied;
END
GO
CREATE OR ALTER PROCEDURE sm.usp_Inventory_List @TenantId UNIQUEIDENTIFIER AS BEGIN
  SET NOCOUNT ON; EXEC sp_set_session_context N'TenantId', @TenantId;
  SELECT i.Sku, i.Name, v.Warehouse, v.OnHand, v.UpdatedUtc FROM sm.Inventory v JOIN sm.CatalogItem i ON i.ItemId=v.ItemId WHERE v.TenantId=@TenantId ORDER BY i.Sku;
END
GO
CREATE OR ALTER PROCEDURE sm.usp_PurchaseOrder_List @TenantId UNIQUEIDENTIFIER, @Status VARCHAR(20)=NULL AS BEGIN
  SET NOCOUNT ON; EXEC sp_set_session_context N'TenantId', @TenantId;
  SELECT TOP 200 p.PoNumber, p.Status, p.TotalAmount, p.CreatedUtc, s.Name AS SupplierName FROM sm.PurchaseOrder p JOIN sm.Supplier s ON s.SupplierId=p.SupplierId
  WHERE p.TenantId=@TenantId AND (@Status IS NULL OR p.Status=@Status) ORDER BY p.CreatedUtc DESC;
END
GO
-- Outbox relay helpers (system context bypasses RLS)
CREATE OR ALTER PROCEDURE sm.usp_Outbox_ClaimBatch @BatchSize INT=100 AS BEGIN
  SET NOCOUNT ON;
  SELECT TOP (@BatchSize) OutboxId, Topic, MessageKey, Payload FROM sm.Outbox WITH (UPDLOCK, READPAST) WHERE PublishedUtc IS NULL ORDER BY OutboxId;
END
GO
CREATE OR ALTER PROCEDURE sm.usp_Outbox_MarkPublished @OutboxId BIGINT AS UPDATE sm.Outbox SET PublishedUtc=SYSUTCDATETIME() WHERE OutboxId=@OutboxId;
GO
-- Seed: two tenants to demo isolation
DECLARE @a UNIQUEIDENTIFIER='11111111-1111-1111-1111-111111111111', @b UNIQUEIDENTIFIER='22222222-2222-2222-2222-222222222222';
INSERT sm.Tenant (TenantId, Name) VALUES (@a,'Acme Retail'),(@b,'Globex Grocers');
EXEC sp_set_session_context N'IsSystem', 1;
INSERT sm.Supplier (TenantId,Code,Name,Country) VALUES (@a,'SUP-001','Northwind Foods','US'),(@b,'SUP-100','Tailspin Traders','GB');
INSERT sm.CatalogItem (TenantId,Sku,Name,Category,SupplierId,UnitCost) SELECT @a,'SKU-1001','Organic Oats 1kg','Grocery',SupplierId,2.40 FROM sm.Supplier WHERE Code='SUP-001';
GO
