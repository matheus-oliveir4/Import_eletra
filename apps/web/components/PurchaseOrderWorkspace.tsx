"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

type HistoryLine = {
  id: string; sourceRowNumber: number; productCode?: string; productDescription?: string;
  quantity?: number; historicalAmount?: number; currency?: string; necessityDate?: string;
  legacyStatus?: string; ipNumber?: string; estimatedDeliveryDate?: string; ruptureRisk: string;
  sourceSheetName: string;
  sourceValues: Record<string, string | null>;
};
type Process = { id: string; ipNumber: string; logisticsStatus?: string; linkStatus: string; costs: { type: string; currency: string; amount: number }[] };
type Overview = {
  id: string; number: string; importer: string; version: number; identityStatus: string;
  officialItemsKnown: boolean; balanceAvailable: boolean; unresolvedIssueCount: number;
  operationalFields: Record<string, string | null>; historicalLines: HistoryLine[]; processes: Process[];
  coverage: { historicalLines: number; linesWithIp: number; linesWithoutIp: number; ruptureRiskLines: number; unknownRiskLines: number };
};

export default function PurchaseOrderWorkspace({ id }: { id: string }) {
  const [data, setData] = useState<Overview>();
  const [fieldName, setFieldName] = useState("");
  const [fieldValue, setFieldValue] = useState("");
  const [error, setError] = useState<string>();

  const load = () => apiFetch(`/api/v1/purchase-orders/${id}/overview`)
    .then(async response => {
      if (!response.ok) throw new Error("Não foi possível abrir a PO.");
      setData(await response.json() as Overview);
    })
    .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Erro inesperado."));

  useEffect(() => {
    void load();
  }, [id]);

  async function saveOperationalField(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!data || !fieldName.trim()) return;
    const response = await apiFetch(`/api/v1/purchase-orders/${id}/operational-fields`, {
      method: "PATCH",
      headers: { "content-type": "application/json", "if-match": `"${data.version}"` },
      body: JSON.stringify({ expectedVersion: data.version, fields: { [fieldName]: fieldValue || null } })
    });
    if (!response.ok) {
      setError("O campo não foi salvo. Atualize a PO e tente novamente.");
      return;
    }
    setFieldName(""); setFieldValue(""); await load();
  }

  if (!data) return <main className="shell"><p>Carregando PO…</p>{error && <p className="notice error">{error}</p>}</main>;

  return (
    <main className="shell">
      <Link href="/" className="back">← Carteira de POs</Link>
      <header className="page-header">
        <p className="eyebrow">PO TOTVS · {data.importer}</p>
        <h1>{data.number}</h1>
        <p>{data.historicalLines.length} linhas históricas; {data.processes.length} IPs vinculados. Dados de origem são preservados; preenchimentos posteriores são operacionais e auditáveis.</p>
      </header>
      {error && <p className="notice error">{error}</p>}

      <section className="metric-grid" aria-label="Cobertura da PO">
        <Metric label="Linhas históricas" value={data.coverage.historicalLines} />
        <Metric label="Com IP" value={data.coverage.linesWithIp} />
        <Metric label="Sem IP" value={data.coverage.linesWithoutIp} />
        <Metric label="Risco de ruptura" value={data.coverage.ruptureRiskLines} />
      </section>

      <section className="card"><h2>Campos operacionais da PO</h2>
        <form className="field-form" onSubmit={saveOperationalField}>
          <input value={fieldName} onChange={event => setFieldName(event.target.value)} placeholder="Campo da PO" aria-label="Campo da PO" />
          <input value={fieldValue} onChange={event => setFieldValue(event.target.value)} placeholder="Valor" aria-label="Valor" />
          <button className="button" type="submit">Salvar</button>
        </form>
        <dl className="field-list">
          {Object.entries(data.operationalFields).map(([key, value]) => <><dt key={`${key}-name`}>{key}</dt><dd key={`${key}-value`}>{value ?? "—"}</dd></>)}
        </dl>
      </section>

      <section className="card"><h2>Histórico da PO</h2><p className="muted">Cada linha de Pré Embarque é consultável aqui; ela não cria outra PO.</p>
        <table><thead><tr><th>Linha</th><th>Produto</th><th>Qtd.</th><th>Necessidade</th><th>Status</th><th>IP</th><th>Risco</th></tr></thead>
          <tbody>{data.historicalLines.map(line => <tr key={line.id}><td>{line.sourceRowNumber}</td><td><strong>{line.productCode ?? "—"}</strong><br />{line.productDescription}</td><td>{line.quantity ?? "—"}</td><td>{line.necessityDate ?? "—"}</td><td>{line.legacyStatus ?? "—"}</td><td>{line.ipNumber ?? "Sem IP"}</td><td><span className={`risk ${line.ruptureRisk}`}>{line.ruptureRisk}</span></td></tr>)}</tbody>
        </table>
      </section>

      <section className="card"><h2>IPs e custos vinculados</h2><p className="muted">Custos pertencem ao IP e são exibidos uma vez. O rateio para a PO será explícito.</p>
        {data.processes.map(process => <article className="process" key={process.id}><h3>{process.ipNumber} <span>{process.logisticsStatus ?? "Status pendente"}</span></h3>
          <ul>{process.costs.map(cost => <li key={`${cost.type}-${cost.currency}`}>{cost.type}: {new Intl.NumberFormat("pt-BR", { style: "currency", currency: cost.currency }).format(cost.amount)}</li>)}</ul>
        </article>)}
      </section>
    </main>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return <section className="metric"><span>{label}</span><strong>{value}</strong></section>;
}
