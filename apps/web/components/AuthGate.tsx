"use client";

import { ReactNode, useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

type Identity = { name?: string; roles: string[] };

export default function AuthGate({ children }: { children: ReactNode }) {
  const [identity, setIdentity] = useState<Identity>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  useEffect(() => {
    apiFetch("/auth/me")
      .then(async response => {
        if (response.status === 401) return;
        if (!response.ok) throw new Error("Não foi possível verificar a sessão.");
        setIdentity(await response.json() as Identity);
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : "Erro inesperado."))
      .finally(() => setLoading(false));
  }, []);

  async function logout() {
    try {
      const response = await apiFetch("/auth/logout", { method: "POST" });
      if (!response.ok) throw new Error("Não foi possível encerrar a sessão.");
      window.location.assign(window.location.origin);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Erro inesperado.");
    }
  }

  const returnUrl = typeof window === "undefined" ? "http://localhost:3000" : window.location.origin;

  if (loading) return <main className="shell"><p>Verificando sessão…</p></main>;
  if (error) return <main className="shell"><p className="notice error">{error}</p><a className="button" href={`/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`}>Entrar com a conta corporativa</a></main>;
  if (!identity) return <main className="shell"><header className="page-header"><p className="eyebrow">ERP Comex</p><h1>Acesso ao portal</h1><p>Entre com sua conta corporativa para consultar a carteira e a fila de qualidade.</p><a className="button" href={`/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`}>Entrar com a conta corporativa</a></header></main>;

  return <><div className="session-bar"><span>{identity.name ?? "Usuário autenticado"}</span><button className="button secondary" type="button" onClick={() => void logout()}>Sair</button></div>{children}</>;
}
