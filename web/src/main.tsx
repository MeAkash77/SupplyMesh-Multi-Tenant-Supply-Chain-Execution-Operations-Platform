import { StrictMode, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { api, nextActions, type CatalogItem, type Po, type Stock } from "./api";

function useLoad<T>(fn: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T>(); const [error, setError] = useState<string>(); const [loading, setLoading] = useState(true);
  const reload = () => { setLoading(true); fn().then(setData).catch(e => setError(String(e))).finally(() => setLoading(false)); };
  useEffect(reload, deps); return { data, error, loading, reload };
}
const Table = ({ head, rows }: { head: string[]; rows: (string | number | JSX.Element)[][] }) => (
  <table><thead><tr>{head.map(h => <th key={h}>{h}</th>)}</tr></thead><tbody>{rows.map((r, i) => <tr key={i}>{r.map((c, j) => <td key={j}>{c}</td>)}</tr>)}</tbody></table>);

function Catalog() {
  const [cat, setCat] = useState(""); const { data, error, loading } = useLoad<CatalogItem[]>(() => api.catalog(cat || undefined), [cat]);
  return <section><h2>Catalog (Catman)</h2><input placeholder="Filter by category" value={cat} onChange={e => setCat(e.target.value)} />
    {loading && <p>Loading…</p>}{error && <p role="alert">{error}</p>}
    {data && <Table head={["SKU", "Name", "Category", "Supplier", "Unit cost"]} rows={data.map(i => [i.sku, i.name, i.category, i.supplierName, i.unitCost.toFixed(2)])} />}</section>;
}
function Orders() {
  const { data, error, reload } = useLoad<Po[]>(() => api.pos());
  const act = async (po: string, a: Parameters<typeof api.act>[1]) => { try { await api.act(po, a); } catch (e) { alert(String(e)); } reload(); };
  return <section><h2>Purchase orders</h2>{error && <p role="alert">{error}</p>}
    {data && <Table head={["PO", "Supplier", "Status", "Total", "Actions"]} rows={data.map(p => [p.poNumber, p.supplierName, <span className={`badge ${p.status}`}>{p.status}</span>, p.totalAmount.toFixed(2),
      <>{nextActions[p.status].map(a => <button key={a} onClick={() => act(p.poNumber, a)}>{a}</button>)}</>])} />}</section>;
}
function Inventory() {
  const { data, error } = useLoad<Stock[]>(() => api.inventory()); // updates asynchronously after a PO is received (Kafka -> worker)
  return <section><h2>Inventory</h2><p className="muted">Eventually consistent: stock updates after the PO "received" event is consumed.</p>{error && <p role="alert">{error}</p>}
    {data && <Table head={["SKU", "Name", "Warehouse", "On hand"]} rows={data.map(s => [s.sku, s.name, s.warehouse, s.onHand])} />}</section>;
}
function App() {
  const [tab, setTab] = useState<"catalog" | "orders" | "inventory">("orders");
  return <><style>{`body{font-family:system-ui;margin:0;background:#f6f8fa}header{background:#0b3d91;color:#fff;padding:12px 24px;display:flex;gap:24px;align-items:center}header button{background:none;border:0;color:#cfe0ff;cursor:pointer;font-size:15px}header button.on{color:#fff;border-bottom:2px solid #fff}main{padding:24px}table{border-collapse:collapse;width:100%;background:#fff}th,td{padding:8px 12px;border-bottom:1px solid #e5e7eb;text-align:left}.badge{padding:2px 8px;border-radius:10px;background:#e5e7eb}.badge.RECEIVED{background:#c7f0d2}.badge.CANCELLED{background:#f8c9c9}td button{margin-right:6px}.muted{color:#666}`}</style>
    <header><b>SupplyMesh</b>{(["orders", "catalog", "inventory"] as const).map(t => <button key={t} className={tab === t ? "on" : ""} onClick={() => setTab(t)}>{t}</button>)}</header>
    <main>{tab === "catalog" ? <Catalog /> : tab === "orders" ? <Orders /> : <Inventory />}</main></>;
}
createRoot(document.getElementById("root")!).render(<StrictMode><App /></StrictMode>);
