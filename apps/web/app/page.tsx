"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

type PurchaseOrder = {
  id: string;
  number: string;
  importer: string;
  identityStatus: string;
  officialItemsKnown: boolean;
  historicalItemCount: number;
  linkedProcessCount: number;
  historicalItemsWithIp: number;
  historicalItemsWithoutIp: number;
  unresolvedIssueCount: number;
  balanceAvailable: boolean;
};

type PurchaseOrderPage = { page: number; pageSize: number; totalCount: number; items: PurchaseOrder[] };

export default function PortfolioPage() {
  const [orders, setOrders] = useState<PurchaseOrder[]>([]);
  const [number, setNumber] = useState("");
  const [importer, setImporter] = useState("");
  const [product, setProduct] = useState("");
  const [ipNumber, setIpNumber] = useState("");
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string>();

  useEffect(() => {
    const query = new URLSearchParams({ page: String(page), pageSize: "50" });
    if (number.trim()) query.set("number", number.trim());
    if (importer.trim()) query.set("importer", importer.trim());
    if (product.trim()) query.set("product", product.trim());
    if (ipNumber.trim()) query.set("ipNumber", ipNumber.trim());
    apiFetch(`/api/v1/purchase-orders?${query}`)
      .then(async (response) => {
        if (!response.ok) throw new Error("Não foi possível carregar a carteira de POs.");
        const result = await response.json() as PurchaseOrderPage;
        setOrders(result.items);
        setTotalCount(result.totalCount);
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Erro inesperado."));
  }, [number, importer, product, ipNumber, page]);

  function filter(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setPage(1); }

  return (
    <main className="shell">
      <header className="page-header">
        <p className="eyebrow">ERP Comex</p>
        <h1>Carteira de POs TOTVS</h1>
        <p>Uma linha por pedido. O detalhe reúne o histórico, os IPs e todos os campos da PO.</p>
        <Link className="text-link" href="/quality">Revisar qualidade do histórico →</Link>
      </header>
      {error && <p className="notice error">{error}</p>}
      <section className="card"><form className="quality-filter" onSubmit={filter}><label>Numero da PO<input value={number} onChange={event => setNumber(event.target.value)} placeholder="18751" /></label><label>Importador<input value={importer} onChange={event => setImporter(event.target.value)} placeholder="ELETRA MATRIZ" /></label><button className="button" type="submit">Filtrar</button></form><p className="muted">{totalCount} POs encontradas</p></section>
      <section className="card">
        <table>
          <thead>
            <tr><th>PO TOTVS</th><th>Importador</th><th>Histórico</th><th>IPs</th><th>Pendências sem IP</th><th /></tr>
          </thead>
          <tbody>
            {orders.map((order) => (
              <tr key={order.id}>
                <td><strong>{order.number}</strong></td>
                <td>{order.importer}</td>
                <td>{order.historicalItemCount} linhas</td>
                <td>{order.linkedProcessCount}</td>
                <td>{order.historicalItemsWithoutIp}</td>
                <td><Link className="button" href={`/purchase-orders/${order.id}`}>Abrir PO</Link></td>
              </tr>
            ))}
          </tbody>
        </table>
        {totalCount > 50 && <p className="pagination"><button className="button secondary" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {page}</span><button className="button" disabled={page * 50 >= totalCount} onClick={() => setPage(value => value + 1)}>Próxima</button></p>}
      </section>
    </main>
  );
}
