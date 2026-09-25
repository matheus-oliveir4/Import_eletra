# ERP Comex — acompanhamento de POs TOTVS

O sistema é centrado na PO TOTVS. Um IP pode atender várias POs e uma PO pode
ser atendida por vários IPs; custos nascem no IP e só aparecem na PO após um
rateio explícito.

## Primeira entrega

Esta base contém o domínio da PO central, endpoints de carteira/detalhe,
cálculos de KPI determinísticos e leitor de XLSX que consome apenas valores
cacheados (`<v>`). O leitor não executa, recalcula ou copia fórmulas do Excel.

O arquivo `Follow Up Import 2026 - Copiar.xlsx` é evidência histórica local e
está ignorado por Git. A carga definitiva deverá passar por staging,
reconciliação e promoção idempotente.

Na importação, a normalização mecânica remove espaços redundantes e caracteres
invisíveis e padroniza a caixa de identificadores, moeda, status e os nomes dos
importadores conhecidos. A carga bruta permanece disponível para auditoria.
PO, IP, importador, moeda, status, NCM ou código de produto com quebra de linha,
`;` ou `|` geram `MIXED_SCALAR_VALUES` e não criam identidades ou vínculos
automáticos. Não se escolhe um dos valores misturados.

## Publicação alvo: Vercel Free + API e PostgreSQL na VPS

A Vercel Free hospeda a aplicação Next.js/React. A API Fastify/Node.js e o
PostgreSQL ficam na VPS Hostinger: a API conecta ao banco localmente, e o
`proxy.ts` do Next.js encaminha `/auth/*` e `/api/v1/*` por HTTPS usando
`VPS_API_URL` e `VPS_API_TOKEN` server-side. `DATABASE_URL`, OIDC e o segredo da
sessão ficam somente na VPS. Não exponha a porta 5432 nem credenciais ao browser.

Depois de publicar o frontend e configurar API, banco, HTTPS e início automático
dos serviços na VPS, o computador pessoal pode ficar desligado. A VPS e a
Vercel precisam permanecer disponíveis. Docker não é requisito de produção.
Esta preparação ainda não concluiu a migração da API .NET nem a autenticação;
login e dados remotos só funcionarão após a paridade dos endpoints e a validação
dos fluxos.

Configure `apps/web` como **Root Directory** do projeto Vercel. Cadastre somente
`VPS_API_URL` e `VPS_API_TOKEN` no painel, separados por Preview e Production.
Use [VERCEL_SETUP.md](VERCEL_SETUP.md) para os pré-requisitos, a rede, OIDC,
storage e a ordem de habilitação. `apps/web/.env.example` contém placeholders,
sem segredos.

O modelo de serviço persistente e o procedimento de instalação na VPS estão em
[deploy/hostinger/README.md](deploy/hostinger/README.md). A API Node ainda é um
scaffold, sem os endpoints operacionais e OIDC migrados; não marque produção
como funcional até a checklist registrar essa paridade.

O MCP da Vercel está definido em `~/.codex/config.toml`; a sessão de trabalho
precisa carregar a integração antes de inspecionar/vincular projeto e conferir
deploys pelo MCP.

## Executar API

```powershell
dotnet restore src/ImportErp.Api
dotnet run --project src/ImportErp.Api
```

Em desenvolvimento, a API usa SQLite em `data/local/import_erp_test.db`; esse
arquivo é ignorado pelo Git. A primeira execução cria o esquema de teste. A rota
`POST /api/v1/imports/preview` recebe um caminho local de XLSX e devolve apenas
a prévia de leitura; ela não grava dados operacionais. `stage` e `promote`
gravam apenas neste banco local e são idempotentes para a mesma planilha.

## Endpoints iniciais

### Login local (DEV03)

O backend usa OpenID Connect Authorization Code com PKCE e sessão por cookie; o
frontend não armazena tokens. Para login local, inicie o Keycloak:

```powershell
.\dev.ps1 bootstrap
.\dev.ps1 up -Profile infra
```

O realm importa o usuário de desenvolvimento `local-admin` com a senha
`local-admin-change-me`; ele é mapeado para a identidade local e para o escopo
administrativo explícito `*`. Essa conta e senha não existem na migration
PostgreSQL e não podem ser reutilizadas fora da máquina local. Inicie a API
com `dotnet run --project src/ImportErp.Api` e a interface com
`corepack pnpm --dir apps/web dev`; use `http://localhost:3000`. A sessão tem
30 minutos de inatividade e limite absoluto de 8 horas. Chamadas mutáveis usam
token anti-CSRF e credenciais por cookie. `GET /auth/me` consulta a sessão e
`POST /auth/logout` encerra-a.

O segredo em `appsettings.Development.json` e no realm importado é exclusivo
para desenvolvimento local. Não reutilize esse valor em ambiente compartilhado;
use configuração protegida/secret manager. Entra ID corporativo ainda não está
homologado. A autorização local correlaciona o par OIDC `(issuer, subject)` com
papéis e escopos de importador persistidos; ela não usa e-mail, nem aceita um
escopo vindo do navegador. Papéis concedem permissões específicas
(`purchase-order.read`, `purchase-order.update`, `data-issue.review`,
`migration.commit` e `user.manage`); listas e detalhes de PO são filtrados no
servidor e recursos fora do escopo respondem como ausentes.

- `GET /api/v1/purchase-orders` (filtros: `number`, `importer`, `supplier`, `product`, `ipNumber`, `historicalStatus`, `fulfillmentStatus`, `quality`, `necessityFrom`, `necessityTo`, `sortBy`, `page`, `pageSize`)
- `GET /api/v1/purchase-orders/{id}/overview`
- `GET /api/v1/purchase-orders/{id}/history-items` (paginado)
- `PATCH /api/v1/purchase-orders/{id}/operational-fields` (exige `If-Match` com o ETag do detalhe)
- `GET /api/v1/purchase-orders/{id}/workflow` e `/api/v1/processes/{id}/workflow` (estado e ETag)
- `GET /api/v1/purchase-orders/{id}/workflow/history` e `/api/v1/processes/{id}/workflow/history`
- `POST /api/v1/purchase-orders/{id}/workflow/transitions` e `/api/v1/processes/{id}/workflow/transitions` (comando validado, motivo, evidência e `If-Match`)
- `GET /api/v1/audit/{PO|IP}/{id}` (trilha paginada, respeitando o escopo do agregado)
- `GET /api/v1/quality-issues` (filtros: `status`, `code`, `sheetName`, `page`, `pageSize`)
- `POST /api/v1/quality-issues/{sourceRowId}/reviews`
- `POST /api/v1/imports/preview`
- `POST /api/v1/imports/plan`
- `POST /api/v1/imports/stage` (SQLite local em desenvolvimento)
- `POST /api/v1/imports/promote` (SQLite local em desenvolvimento)

Transições comerciais e logísticas seguem a seção 10 do plano. O backend valida
os estados permitidos, permissão específica, escopo de importador, versão ETag,
motivo e evidências declaradas para cada mudança. Pré-condição ausente retorna
422 com as chaves faltantes; conflito de versão retorna 409. A trilha registra
identidade `(issuer, subject)`, antes/depois, justificativa, instante e evidências.
Estados iniciais do Excel são registrados como históricos e não simulam ações no
TOTVS; status legado sem mapeamento conhecido fica `INDETERMINADO`. As evidências
operacionais ainda são declarações do comando; validação de documentos, itens,
embarques, saldo e fechamento depende dos módulos posteriores.

Alterações operacionais de PO, revisões de qualidade e transições gravam a
trilha imutável e uma mensagem de outbox na mesma transação do comando. IDs de
evento são estáveis para deduplicação; o worker de publicação e os consumidores
idempotentes ficam pendentes no trabalho assíncrono (DEV18/DEV24).

## Outbox dispatch (DEV24)

`M006_outbox_dispatch` adds leases, bounded exponential retry, dead-letter state,
and a consumer inbox keyed by `(consumerName, eventId)`. PostgreSQL claims with
`FOR UPDATE SKIP LOCKED`; SQLite supports the single-instance local test profile.
Delivery is at-least-once, never exactly-once. A completed inbox entry suppresses
duplicate local handling, while external effects must remain idempotent by event ID.

ADR 010 already selects PostgreSQL as the initial queue, without a broker or Redis.
No business consumer or external transport has been approved. With no registered
`IOutboxConsumer`, the dispatcher leaves messages pending and does not acknowledge
publication.

## Verificar migrations SQLite

```powershell
dotnet run --project src/ImportErp.MigrationChecks
```

O verificador usa um banco temporário, aplica as migrations históricas (`M001`
até `M006`) e confirma que uma segunda execução não as reaplica. Ele também cobre
a fila de qualidade, retry transitório, exclusão por lease, recuperação após
expiração e redelivery idempotente por consumidor.

Para obter a reconciliação por IP e moeda de uma planilha, informe o caminho
local como argumento. O resultado é somente leitura e é emitido em JSON.

```powershell
dotnet run --project src/ImportErp.MigrationChecks -- "Follow Up Import 2026 - Copiar.xlsx"
```

## Ambiente local (DEV02)

Pré-requisitos: Docker Desktop com Compose V2, .NET SDK 10.0.301, Node.js 24 e
Corepack. O bootstrap usa os lockfiles e cria `infra/compose/.env` somente se
ele ainda não existir; revise os valores locais antes de iniciar a infraestrutura.

```powershell
.\dev.ps1 bootstrap
.\dev.ps1 up -Profile infra
.\dev.ps1 migrate
.\dev.ps1 test
.\dev.ps1 down
```

Se a política de execução local bloquear arquivos `.ps1`, execute os mesmos
comandos somente nesta sessão com `powershell -ExecutionPolicy Bypass -File`;
não é necessário alterar a política global da máquina.

Em Linux/macOS, a interface equivalente é `./dev.sh bootstrap`,
`./dev.sh up --profile infra`, `./dev.sh migrate`, `./dev.sh test` e
`./dev.sh down`. `down` preserva os volumes. O único comando que os remove é
explícito e limitado ao Compose local:

```powershell
.\dev.ps1 reset -Environment local -ConfirmReset
```

```bash
./dev.sh reset --environment local --confirm
```

O perfil `infra` expõe somente em loopback PostgreSQL (5432 por padrão;
configure `POSTGRES_HOST_PORT` em `infra/compose/.env` se a porta estiver
ocupada), Keycloak (8180), Azurite Blob (10000) e OTLP (4317/4318). A API e a interface continuam no host
para depuração; o perfil `full` e o gateway permanecem pendentes. A importação
histórica neste momento é apenas leitura:

```powershell
.\dev.ps1 import-history -File "Follow Up Import 2026 - Copiar.xlsx" -Preview
```
