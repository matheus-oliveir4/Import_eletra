# Preparação de deploy na Vercel

## Arquitetura escolhida

- Destino do projeto Vercel: repositório GitHub `matheus-oliveir4/Import_eletra`.
- Root Directory: `apps/web` (monorepo Next.js, pnpm e lockfile existente).
- Next.js App Router hospeda a interface; `proxy.ts` encaminha `/auth/*` e
  `/api/v1/*` na mesma origem para uma API Node.js HTTPS na VPS.
- O usuário informou que usará Vercel Free e já tem PostgreSQL na VPS Hostinger.
  A API Node e o PostgreSQL ficam na VPS; o banco só aceita conexão local/privada.
  O proxy usa um token server-side; OIDC/autorização continuam obrigatórios.
- Serviços Node API, PostgreSQL e proxy/túnel iniciam automaticamente na VPS;
  não dependem do computador pessoal do usuário.
- Docker não participa do build nem do runtime de produção.

Esta configuração é um guia de preparação. O frontend atual ainda usa uma API
ASP.NET Core separada e aponta por padrão a `localhost:5000`. O proxy local
permite manter essa API no perfil de desenvolvimento durante a transição; o
serviço Node na VPS, paridade de endpoints, autenticação e implantação ainda
precisam ser concluídos antes de login/dados remotos funcionarem.

## Variáveis para cadastrar na Vercel

Cadastrar valores somente em **Project Settings → Environment Variables**,
separando Preview e Production. O arquivo `apps/web/.env.example` tem apenas os
nomes e placeholders, sem valores utilizáveis.

| Variável | Escopo | Observação |
|---|---|---|
| `VPS_API_URL` | Server only | Origem HTTPS do serviço Node na VPS (ex.: hostname da API/túnel); sem caminho, query ou credenciais |
| `VPS_API_TOKEN` | Secret | Compartilhado com a API VPS, rotacionável; enviar somente pelo Next Proxy |

`DATABASE_URL`, `OIDC_ISSUER`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`,
`AUTH_SESSION_SECRET` e credenciais de storage pertencem ao ambiente protegido
da API na VPS, não ao projeto Vercel. Não adicionar senha, connection string,
client secret ou token neste guia, checklist, plano, issue, log ou variável
`NEXT_PUBLIC_*`. Preview deve usar token e issuer de teste separados; não usar
Preview para gravar no banco operacional.

## Conectividade da VPS

1. Confirmar versão do PostgreSQL, DNS, certificado TLS, backup e restore testado.
2. Criar banco/schema e role exclusiva da aplicação, sem superuser e com os
   privilégios mínimos para as migrations e operações necessárias.
3. Manter PostgreSQL bound a localhost/rede privada, sem abrir porta 5432 para
   Vercel Free nem para `0.0.0.0/0`.
4. Instalar o serviço API Node na VPS como serviço persistente (`systemd` ou
   supervisor equivalente), usuário do sistema sem privilégios, `HOST=127.0.0.1`
   e reinício automático.
5. Expor somente HTTPS para a API através de reverse proxy ou Cloudflare Tunnel;
   a API exige `GATEWAY_TOKEN` e autenticação OIDC de usuário. PostgreSQL fica
   bound a loopback e não tem regra pública.
6. Configurar as duas cópias de `VPS_API_TOKEN` (Vercel e VPS), Preview separado,
   rotação documentada, rate limit e respostas sem secrets.
7. Validar conectividade de Preview sem imprimir URI, senha ou token nos logs.

## OIDC e cookies

Antes de homologar login, provisionar issuer OIDC alcançável pelo serviço Node na
VPS e registrar o redirect URI HTTPS no domínio Vercel (Preview/Production
conforme suportado pelo provider), com callback atendido pelo serviço Node
através do proxy same-origin. Manter issuer fixo/allowlisted, PKCE,
state/nonce, cookies `HttpOnly`, `Secure`, `SameSite` correto ao fluxo, proteção
CSRF em mutações e retorno seguro para URLs da mesma origem. Sessão e estado de
login não podem depender da memória ou do disco efêmero de uma instância.

## Ordem para habilitar deploy funcional

1. Portar endpoints ASP.NET Core usados pela UI para serviço Node em `apps/api`;
   conservar contratos e executar localmente com PostgreSQL da VPS/teste.
2. Migrar chamadas do browser para URLs same-origin; `proxy.ts` encaminha para a
   API Node HTTPS com header token não acessível ao cliente.
3. Portar auth, autorização, escopo por importador, CSRF, auditoria, versionamento
   ETag e contratos de erro; cobrir testes de regressão.
4. Adaptar/validar migrations PostgreSQL e executar contra banco de teste da
   mesma versão da VPS; verificar rollback e backup antes de produção.
5. Configurar env vars Preview, conectividade TLS/rede e OIDC; rodar E2E de login,
   sessão, 401/403/404, escopo e escrita concorrente.
6. Instalar API Node e proxy/túnel na VPS; conectar o repositório à Vercel com
   Root Directory `apps/web`; conferir build e Preview pelo MCP quando carregado.
7. Produção somente depois de backup/restore, UAT e aceite; configurar domínio,
   callback OIDC e env vars de Production separadamente.

O roteiro operacional com modelos de `systemd` e Nginx está em
[`deploy/hostinger/README.md`](deploy/hostinger/README.md). O serviço inclui
`/health/live` e `/health/ready`; readiness exige o token do gateway. Com
`systemctl enable`, a API inicia após reboot e não depende do PC. VPS,
PostgreSQL, Nginx/TLS, DNS e Vercel precisam continuar disponíveis.

## Itens não cobertos por uma função HTTP simples

- Upload XLSX e documentos: escolher object storage privado e desenhar upload
  assinado/validação; não salvar arquivos no filesystem da função.
- Importação, exportação, dispatch da outbox e ETL: escolher queue/cron ou worker
  externo durável, com checkpoint, leases, retry, dead-letter e idempotência.
- Relatórios e Power BI: desenhar acesso ao banco e usuário read-only separado.

O MCP da Vercel está configurado em `~/.codex/config.toml`, mas precisa estar
carregado na sessão para conferir projeto, Root Directory, deployments e
variáveis sem ler valores secretos. Até então, este documento não afirma que o
projeto Vercel foi inspecionado ou vinculado.
