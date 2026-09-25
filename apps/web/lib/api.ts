export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const method = (init.method ?? "GET").toUpperCase();
  const headers = new Headers(init.headers);
  if (!["GET", "HEAD", "OPTIONS"].includes(method)) {
    const csrf = await fetch("/auth/csrf", { credentials: "same-origin", cache: "no-store" });
    if (!csrf.ok) throw new Error("Sua sessão expirou. Entre novamente para continuar.");
    const payload = await csrf.json() as { requestToken: string };
    headers.set("X-CSRF-TOKEN", payload.requestToken);
  }

  if (/^https?:\/\//i.test(path)) {
    throw new Error("A API deve usar a mesma origem da aplicação.");
  }

  return fetch(path, {
    ...init,
    method,
    headers,
    credentials: "same-origin"
  });
}
