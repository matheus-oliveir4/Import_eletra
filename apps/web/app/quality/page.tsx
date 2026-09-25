"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { apiFetch } from "../../lib/api";

type Review = { id: string; outcome: string; reviewer: string; notes: string; proposedPurchaseOrder?: string; proposedIpNumber?: string; recordedAt: string };
type QualityItem = { sourceRowId: string; code: string; severity: string; message: string; columnName?: string; sheetName?: string; sourceRowNumber?: number; sourceValues: Record<string, string | null>; latestReview?: Review };
type QualityPageData = { page: number; pageSize: number; totalCount: number; openCount: number; resolvedCount: number; items: QualityItem[] };

export default function QualityPage() {
  const [data, setData] = useState<QualityPageData>();
  const [status, setStatus] = useState("open");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string>();
  const [reviewing, setReviewing] = useState<string>();

  const load = () => {
    const query = new URLSearchParams({ status, pageSize: "50" });
    if (code.trim()) query.set("code", code.trim());
    return apiFetch(`/api/v1/quality-issues?${query}`)
      .then(async response => {
        if (!response.ok) throw new Error("Não foi possível carregar a fila de qualidade.");
        setData(await response.json() as QualityPageData);
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Erro inesperado."));
  };

  useEffect(() => { void load(); }, [status]);
  function applyFilter(event: FormEvent<HTMLFormElement>) { event.preventDefault(); void load(); }
  async function submitReview(item: QualityItem, form: HTMLFormElement) {
    const fields = new FormData(form);
    const response = await apiFetch(`/api/v1/quality-issues/${item.sourceRowId}/reviews`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ code: item.code, outcome: fields.get("outcome"), reviewer: fields.get("reviewer"), notes: fields.get("notes"), proposedPurchaseOrder: fields.get("proposedPurchaseOrder") || null, proposedIpNumber: fields.get("proposedIpNumber") || null })
    });
    if (!response.ok) { setError("A revisão não foi registrada. Confira responsável, justificativa e tente novamente."); return; }
    setReviewing(undefined); await load();
  }

  return <main className="shell">
    <Link href="/" className="back">← Carteira de POs</Link>
    <header className="page-header"><p className="eyebrow">Histórico Excel · fila de revisão</p><h1>Qualidade dos dados</h1><p>As decisões não alteram a planilha nem a carga bruta. Propostas de PO ou IP ficam registradas como revisão e exigem confirmação operacional posterior.</p></header>
    {error && <p className="notice error">{error}</p>}
    <section className="metric-grid" aria-label="Resumo da fila de qualidade"><Metric label="Pendências abertas" value={data?.openCount ?? "…"} /><Metric label="Pendências resolvidas" value={data?.resolvedCount ?? "…"} /><Metric label="Itens no filtro" value={data?.totalCount ?? "…"} /><Metric label="Linhas por página" value={data?.pageSize ?? 50} /></section>
    <section className="card"><form className="quality-filter" onSubmit={applyFilter}><label>Status<select value={status} onChange={event => setStatus(event.target.value)}><option value="open">Abertas</option><option value="resolved">Resolvidas</option><option value="all">Todas</option></select></label><label>Código<input value={code} onChange={event => setCode(event.target.value)} placeholder="EXCEL_ERROR" /></label><button className="button" type="submit">Filtrar</button></form></section>
    {!data && !error && <p>Carregando fila…</p>}
    {data?.items.length === 0 && <section className="card"><p>Nenhuma pendência encontrada para este filtro.</p></section>}
    {data?.items.map(item => <article className="card quality-item" key={`${item.sourceRowId}-${item.code}`}>
      <div className="quality-heading"><div><p className="eyebrow">{item.sheetName ?? "Origem indisponível"} · linha {item.sourceRowNumber ?? "—"}</p><h2>{item.code}</h2></div><span className={`quality-status ${item.latestReview?.outcome ?? "OPEN"}`}>{item.latestReview?.outcome ?? "OPEN"}</span></div>
      <p>{item.message}</p>{item.columnName && <p className="muted">Campo de origem: <strong>{item.columnName}</strong></p>}
      <details><summary>Ver valores preservados da origem</summary><dl className="source-values">{Object.entries(item.sourceValues).map(([name, value]) => <><dt key={`${name}-name`}>{name}</dt><dd key={`${name}-value`}>{value ?? "—"}</dd></>)}</dl></details>
      {item.latestReview && <section className="review-history"><h3>Última revisão</h3><p><strong>{item.latestReview.outcome}</strong> por {item.latestReview.reviewer} em {new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short" }).format(new Date(item.latestReview.recordedAt))}</p><p>{item.latestReview.notes}</p><p className="muted">PO proposta: {item.latestReview.proposedPurchaseOrder ?? "—"} · IP proposto: {item.latestReview.proposedIpNumber ?? "—"}</p></section>}
      {reviewing === `${item.sourceRowId}-${item.code}` ? <form className="review-form" onSubmit={event => { event.preventDefault(); void submitReview(item, event.currentTarget); }}><label>Resultado<select name="outcome" defaultValue="Resolved"><option value="Resolved">Resolver sem alterar origem</option><option value="Escalated">Escalar para decisão</option></select></label><label>Responsável<input name="reviewer" required /></label><label>Justificativa<textarea name="notes" required /></label><label>PO proposta (opcional)<input name="proposedPurchaseOrder" /></label><label>IP proposto (opcional)<input name="proposedIpNumber" /></label><div><button className="button" type="submit">Registrar revisão</button><button className="button secondary" type="button" onClick={() => setReviewing(undefined)}>Cancelar</button></div></form> : <button className="button" type="button" onClick={() => setReviewing(`${item.sourceRowId}-${item.code}`)}>Revisar pendência</button>}
    </article>)}
  </main>;
}

function Metric({ label, value }: { label: string; value: number | string }) { return <section className="metric"><span>{label}</span><strong>{value}</strong></section>; }
