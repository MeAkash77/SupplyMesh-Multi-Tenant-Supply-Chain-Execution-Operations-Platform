// Typed API client. Token comes from MSAL (Azure Entra ID) in production; DEV token is for local docker-compose only.
const token = () => sessionStorage.getItem("access_token") ?? import.meta.env.VITE_DEV_TOKEN ?? "";
async function call<T>(base: string, path: string, init?: RequestInit): Promise<T> {
  const r = await fetch(`${base}${path}`, { ...init, headers: { "Content-Type": "application/json", Authorization: `Bearer ${token()}`, "X-Correlation-Id": crypto.randomUUID(), ...init?.headers } });
  if (!r.ok) throw new Error(`${r.status} ${(await r.text()) || r.statusText}`);
  return r.status === 204 ? (undefined as T) : r.json();
}
export type CatalogItem = { itemId: number; sku: string; name: string; category: string; unitCost: number; supplierName: string };
export type Po = { poNumber: string; status: PoStatus; totalAmount: number; createdUtc: string; supplierName: string };
export type PoStatus = "CREATED" | "APPROVED" | "SHIPPED" | "RECEIVED" | "CANCELLED";
export type Stock = { sku: string; name: string; warehouse: string; onHand: number; updatedUtc: string };
export const api = {
  catalog: (category?: string) => call<CatalogItem[]>("/catalog-api", `/api/v1/catalog${category ? `?category=${encodeURIComponent(category)}` : ""}`),
  pos: (status?: string) => call<Po[]>("/orders-api", `/api/v1/purchase-orders${status ? `?status=${status}` : ""}`),
  createPo: (supplierId: number, lines: { itemId: number; qty: number }[]) => call<{ poNumber: string }>("/orders-api", "/api/v1/purchase-orders", { method: "POST", body: JSON.stringify({ supplierId, lines }) }),
  act: (po: string, action: "approve" | "ship" | "receive" | "cancel") => call<void>("/orders-api", `/api/v1/purchase-orders/${po}/${action}`, { method: "POST" }),
  inventory: () => call<Stock[]>("/orders-api", "/api/v1/inventory"),
};
export const nextActions: Record<PoStatus, ("approve" | "ship" | "receive" | "cancel")[]> = { CREATED: ["approve", "cancel"], APPROVED: ["ship", "cancel"], SHIPPED: ["receive"], RECEIVED: [], CANCELLED: [] };
