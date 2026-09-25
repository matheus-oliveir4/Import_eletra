# Checklist e histórico da implementação

Este arquivo é o registro versionado do andamento do ERP Comex. Atualize-o em
todo incremento: marque o status, registre a evidência verificável e acrescente
uma linha no histórico. Não remova registros anteriores.

## Convenções

- `[ ] Não iniciado` — ainda sem implementação ou evidência.
- `[-] Em andamento` — implementação parcial; o aceite ainda não foi atendido.
- `[x] Concluído em teste` — implementado e validado no ambiente local de teste.
- `[!] Bloqueado/decisão` — depende de confirmação de negócio, acesso externo ou aprovação.

Uma marcação `[x]` só pode ser usada quando houver teste ou evidência descrita
na coluna **Evidência**. Produção só é marcada depois de homologação formal.

## Ponto de retomada obrigatório

**Última atualização:** 2026-09-24

**Estado seguro atual:** Banco SQLite local preservado. Os containers Compose
foram parados sem remover volumes, pois não são necessários para publicar o
frontend na Vercel. O serviço PostgreSQL 17 do host continua em `5432`; não foi
possível pará-lo sem permissão administrativa. O SDK .NET 10.0.301 foi instalado
em `.tools/dotnet` (ignorado pelo Git); restore NuGet e build Next.js passaram.
A CLI da Vercel está autenticada. A API ASP.NET Core ainda não está publicada e
a URL de desenvolvimento encaminha à API local em `localhost:5000`; a origem
HTTPS da API VPS ainda não foi cadastrada em ambiente Vercel.
**Próximo incremento obrigatório:** portar endpoints, OIDC e autorização de
ASP.NET Core para a API Node.js/Fastify em `apps/api`, instalada como serviço
persistente na VPS Hostinger junto ao PostgreSQL. A Vercel Free hospeda Next.js;
`proxy.ts` encaminha `/auth/*` e `/api/v1/*` por HTTPS com token server-only. A
API conecta a PostgreSQL local/privado; nenhuma conexão ou porta 5432 é exposta
à Vercel ou ao browser. API, banco e reverse proxy/túnel iniciam automaticamente
na VPS; o computador pessoal pode ficar desligado. OIDC/segredos não foram
fornecidos e serão configurados depois. O MCP Vercel consta em
`~/.codex/config.toml`, mas não está carregado como ferramenta neste processo;
nenhum projeto/setting Vercel foi inspecionado. Esta escolha e documentação
foram registradas em 2026-09-24; a migração de endpoints/auth, migrations
compatíveis, instalação na VPS e E2E continuam pendentes. A referência aceita
para a cópia de trabalho é 7.000 linhas no Pré e 195 no Pós.

### Arquitetura de publicação escolhida

- **Hospedagem:** Vercel; monorepo com Root Directory `apps/web`.
- **Aplicação:** Next.js App Router + React + TypeScript na Vercel Free; Fastify/
  Node.js na VPS; `proxy.ts` encaminha chamadas same-origin com token secreto.
- **Dados:** PostgreSQL na VPS, acessível localmente apenas pela API Node usando
  `DATABASE_URL` armazenada na VPS e role de privilégio mínimo. Sem conexão Vercel
  → PostgreSQL e sem porta 5432 pública.
- **Rede:** publicar API por HTTPS reverse proxy ou Cloudflare Tunnel; listener
  da API em `127.0.0.1`. Token gateway em Vercel e VPS; OIDC/grants/CSRF continuam
  obrigatórios. Static IP Vercel Pro/Enterprise (documentado em US$100/mês/projeto
  Pro + transferência regional) deixa de ser requisito desta topologia.
- **Autenticação:** OIDC Authorization Code + PKCE na API Node da VPS, cookie
  seguro mesmo-origin através do proxy, sessão persistida e CSRF; provider e
  callback ainda pendentes.
- **Arquivos e tarefas longas:** filesystem da função não é armazenamento
  persistente. Imports XLSX e documentos precisam de object storage externo;
  tarefas duráveis/outbox exigem execução assíncrona gerenciada ou cron/queue.
- **Docker:** não é runtime de produção da Vercel e fica fora do caminho de
  deploy. Compose existente pode permanecer como ferramenta opcional de
  desenvolvimento, não requisito de deploy.
- **Migração:** preservar código e migrations .NET como referência até parity,
  testes e corte aprovados. Não marcar DEV como concluído por apenas alterar
  documentação ou obter um build de frontend.

Ao retomar em outra sessão, siga esta sequência:

1. Leia este arquivo e `Plano_Implementacao_ERP_PO_TOTVS.md`; não assuma que
   um item `[-]` está concluído.
2. Confirme que `Follow Up Import 2026 - Copiar.xlsx` continua ignorado e que o
   hash registrado em **Evidência da carga SQLite** não mudou.
3. Não apague `data/local/import_erp_test.db` para “resolver” testes. Quando
   for necessário reiniciar dados de teste, peça autorização explícita e registre
   o motivo no histórico.
4. Execute o próximo item da ordem abaixo; só pule um item quando houver uma
   dependência externa registrada em **Decisões e pendências abertas**.
5. Ao terminar, atualize o status do DEV correspondente, evidência, próximo
   passo e a tabela **Histórico de incrementos** antes de encerrar a sessão.

## Ordem de implementação e retomada

Esta é a ordem de trabalho. As etapas podem sobrepor atividades, mas os itens
da coluna **Condição para avançar** não podem ser ignorados.

| Ordem | Etapa | Itens do plano | Estado atual | Condição para avançar |
|---:|---|---|---|---|
| 0 | E0 — Fundação e contratos | DEV01–DEV05 | Parcial; frontend Next compila; portabilidade para Vercel, API Node, acesso PostgreSQL da VPS, OIDC em URL pública, E2E e CI pendem | API same-origin em Node, migration PostgreSQL, conexão segura VPS e testes automatizados |
| 1 | E1 — Modelo e diagnóstico | DEV05, DEV06, DEV09 | Parcial; modelo inicial e SQLite existem; migrations/fixtures completas não | Modelo revisado contra Pré e Pós Embarque |
| 2 | E2 — Migração histórica | DEV07–DEV12 | Parcial; staging/promoção/idempotência SQLite validados manualmente | Testes de parser e retry, reconciliação aprovada e tela de qualidade |
| 3 | E3 — Carteira PO e núcleo | DEV13–DEV15 | Parcial; lista/detalhe inicial de PO existem | Navegação completa por PO, permissões, pendências e auditoria |
| 4 | E4 — Atendimento e documentos | DEV16, DEV17, DEV23, DEV24 | Não iniciado | Linhas oficiais, alocações, invoice, documentos e auditoria verificados |
| 5 | E5 — Logística e fiscal | DEV18–DEV22 | Não iniciado | Shipment até entrega, fiscal e custos/rateios sem duplicação |
| 6 | E6 — Relatórios e BI | DEV25–DEV28 | Parcial somente no KPI inicial; BI não iniciado | Grãos, moedas, ETL, RLS e totais reconciliados |
| 7 | E7 — Qualidade e homologação | DEV29 | Não iniciado | E2E, segurança, performance, restauração e UAT aprovados |
| 8 | E8 — Corte e operação | DEV30 | Não iniciado | Snapshot final, treinamento, runbooks e aceite formal |

### Sequência exata do próximo bloco

1. Completar E2E autenticado da carteira/detalhe da PO, incluindo filtros,
   paginação, conflito de edição e rastreabilidade até a origem (DEV13).
2. Completar E2E OIDC e escopo por importador, incluindo 401/403/404, CSRF e
   logout (DEV03/DEV04); a API já aplica os grants e filtros server-side.
3. Validar pré-condições e permissões dos comandos de workflow; ligar as
   evidências declaradas aos cadastros de itens, documentos, embarques e saldos
   quando esses módulos forem implementados (DEV15).
4. Implementar auditoria geral e outbox transacional (DEV24) antes de liberar
   módulos de invoice, logística, fiscal, documentos, custos rateados ou BI.

> Se houver dúvida entre duas tarefas, execute primeiro a que protege a
> integridade do dado ou torna a carga verificável; não avance para rateio,
> totalização entre moedas ou cálculo fiscal sem regra de negócio aprovada.

## Matriz de cobertura integral do plano (seções 1–34)

Esta matriz é obrigatória: toda seção do
`Plano_Implementacao_ERP_PO_TOTVS.md` tem um item correspondente. O status do
DEV associado não substitui o aceite desta linha. Quando o plano for alterado,
inclua aqui a nova seção ou subseção antes de implementar a mudança.

| Plano | Cobertura a manter | Status | Aceite/evidência necessária |
|---:|---|---|---|
| 1 | Uso do plano e precedência: PO central, somente valores salvos, Pré B:AZ e Pós no próprio grão | [-] | Revisão de cada incremento contra este checklist; testes provam que fórmula não é executada |
| 2 | Escopo, RF01–RF16 e limites explícitos do MVP | [ ] | Matriz de requisitos abaixo completa; itens fora de escopo não implementados como se fossem MVP |
| 3 | Diagnóstico verificável da fonte e valores de reconciliação | [-] | Relatório reproduzível da cópia de trabalho, com diferenças em relação aos números de referência do plano formalmente explicadas |
| 4 | ADRs 000–015 e proibições arquiteturais | [-] | ADRs versionadas, aprovadas e coerentes com código, dados, API e BI |
| 5 | Stack fixa, versões compatíveis e dependências travadas | [-] | SDK, lockfiles, imagens/digests e versões sem `latest` documentados e reproduzíveis |
| 6 | Topologia, módulos e fronteiras de escrita | [ ] | Diagrama atualizado; frontend não escreve banco; regras pertencem ao backend |
| 7 | Estrutura de repositório, projetos e responsabilidades | [-] | Estrutura criada; worker, testes, infra e configurações ainda precisam completar a organização prevista |
| 8 | Banco operacional/analítico, convenções, entidades, índices e migrations | [-] | Migrations PostgreSQL executadas do vazio ao upgrade, FKs/checks/índices e separação do DW validados |
| 9 | Propriedade dos valores, PO versus invoice, custos e autoridade TOTVS | [-] | Testes impedem uso de histórico como saldo oficial, duplicação de custo e substituição indevida de fonte mestre |
| 10 | Estados comerciais, atendimento e workflow logístico | [-] | Endpoints de estado/histórico/transição, regras de estados, permissões, evidências, motivo, ETag e journal implementados para PO/IP; faltam E2E e validação das evidências contra entidades operacionais |
| 11 | Espelhamento, divergências e fila de qualidade | [ ] | Política de precedência, resolução auditada e telas de divergência disponíveis |
| 12 | Pipeline histórico: leitura, staging, promoção, qualidade, reconciliação e reimportação | [-] | SQLite validou fluxo básico; falta relatório homologado, retry por falha e testes automatizados completos |
| 13 | API versionada, padrões, recursos e exemplos de contrato | [-] | OpenAPI completo, erros/paginação/filtros/autorização e cliente TypeScript gerado sem alteração manual |
| 14 | Interface, navegação, telas, acessibilidade e estados de UX | [-] | Carteira/detalhe inicial existem; todas as telas e critérios de usabilidade ainda precisam de aceite |
| 15 | Autenticação, autorização, perfis e matriz de permissões | [-] | OIDC local ainda requer E2E; DEV04 persiste identidade, papéis e escopos, mas faltam testes negativos autenticados e homologação Entra |
| 16 | Segurança de documentos e dados | [ ] | Upload/download autorizado, antivírus/validação definida, segredos protegidos e logs sem dados sensíveis |
| 17 | Ambiente de desenvolvimento, perfis e bootstrap | [-] | Compose, serviços locais e scripts seguros existem; falta executar o bootstrap/infra em máquina com Docker e demonstrar o fluxo de 45 minutos |
| 18 | Processamento assíncrono, consistência, jobs e outbox | [-] | Outbox transacional está gravada com atualização de PO, revisão de qualidade e workflow. `M006` adiciona lease, backoff/retry, dead-letter e inbox por consumidor; PostgreSQL usa `FOR UPDATE SKIP LOCKED`. Os checks locais de lease/retry/redelivery passaram; falta definir consumidores de negócio/integração externa. |
| 19 | Arquitetura analítica, ETL, fatos, métricas, modelo semântico e Power BI | [ ] | DW separado, grãos declarados, moedas não misturadas, RLS e refresh testados |
| 20 | Estratégia de testes e casos automatizáveis | [-] | Build manual validado; suíte unitária, integração, E2E, segurança e performance ainda pendem |
| 21 | Requisitos não funcionais | [ ] | Metas de desempenho, disponibilidade, observabilidade, acessibilidade, backup e segurança evidenciados |
| 22 | CI e entrega | [ ] | Pipeline com lint, build, testes, migration check, imagem e promoção controlada |
| 23 | Operação, recuperação, logs, backup e runbooks | [ ] | Exercício de restauração e runbooks aprovados |
| 24 | Etapas E0–E8, backlog DEV01–DEV30 e dependências | [-] | Ordem e status detalhados neste arquivo; cada DEV só encerra com seu aceite específico |
| 25 | Responsabilidades de produto, compras, fiscal, logística, dados, QA, DevOps e desenvolvimento | [!] | Responsáveis nomeados e aprovações registradas para cada decisão de domínio |
| 26 | Decisões de negócio pendentes e tratamento conservador | [!] | Cada decisão tem responsável, data, evidência e impacto; nenhuma regra fiscal/financeira presumida |
| 27 | Critérios para encerrar MVP e entrada em produção | [ ] | Checklist de saída completo, carga reconciliada, permissões reais, restore, treinamento e aceite formal |
| 28 | Definição de pronto de cada entrega | [ ] | Caso de uso, migration, API, backend, tela, auditoria, testes, logs, documentação, revisão e aceite demonstrados |
| 29 | Dicionário de mapeamento de todas as colunas Pré/Pós | [-] | Controle de cobertura de 51 colunas Pré e 43 Pós, com linhagem de arquivo/aba/linha/coluna e regra por campo |
| 30 | Contrato físico PO central, projeções, consulta e rastreabilidade | [-] | Tabelas/campos, constraints, overview e lista atendem ao contrato sem representar desconhecido como zero |
| 31 | Sequência da primeira entrega | [-] | Ordem preservada; divergência de contagens entre plano e cópia atual registrada e aguardando reconciliação |
| 32 | Riscos e respostas obrigatórias | [-] | Controles abaixo cobrem cada risco e são testados antes do corte |
| 33 | Pacote final da equipe | [ ] | Repositório, ADRs, migrations, OpenAPI, pipeline, tela, BI, evidências, guias e plano de corte entregues |
| 34 | Fontes de negócio/técnicas e controle de revisão | [-] | Fontes registradas; qualquer mudança de centralidade/grão/chave/rateio atualiza ADR, modelo, API, testes e BI juntos |

## Cobertura das funcionalidades obrigatórias do MVP (RF01–RF16)

| RF | Funcionalidade | Status | Critério de aceite que falta ou foi atendido |
|---|---|---|---|
| RF01 | Autenticação e autorização | [-] | Sessão OIDC e escopo server-side de PO/qualidade/importação implementados; faltam E2E Keycloak, administração completa, anexos/exportações e Entra |
| RF02 | Cadastros | [ ] | Fornecedor, importador, produto, NCM e auxiliares com ativação e histórico |
| RF03 | Solicitações | [ ] | Solicitação nativa e fila independente para legado sem IP |
| RF04 | Execução logística por IP | [ ] | Criar, editar, pesquisar, filtrar, priorizar, cancelar, reabrir e encerrar IP |
| RF05 | Itens | [-] | Snapshot histórico existe; campos e itens operacionais completos pendem |
| RF06 | Carteira PO TOTVS | [-] | Lista/detalhe inicial existem; itens oficiais, atendimento confirmado e alocações parciais pendem |
| RF07 | Invoices | [ ] | Cabeçalho, itens, associação, documentos e divergências com PO |
| RF08 | Logística | [ ] | Shipments, BL, portos, agente, ETD, ETA e containers |
| RF09 | Desembaraço | [ ] | DUIMP, canal, marcos fiscais, NF, armazenagem e entrega |
| RF10 | Custos | [-] | Custos históricos por IP deduplicados; outros custos, estados, rateios e reversões pendem |
| RF11 | Documentos | [ ] | Upload, versão, download autorizado e vínculo ao processo |
| RF12 | Histórico e auditoria | [-] | Origem histórica é preservada; timeline e auditoria por campo/usuário/instante pendem |
| RF13 | Histórico Excel | [-] | Leitura, staging, promoção e reimportação manual validados; qualidade/reconciliação automatizadas pendem |
| RF14 | Dashboard e relatórios | [-] | KPI de ruptura inicial no backend; filtros, catálogo, atualização e relatórios pendem |
| RF15 | Administração | [ ] | Usuários, perfis, importadores, parâmetros e jobs |
| RF16 | Dados analíticos | [ ] | Banco analítico, cargas verificáveis e modelo semântico sem fato-a-fato |

## Controle completo do dicionário de mapeamento

| Origem | Cobertura obrigatória | Status | Aceite |
|---|---|---|---|
| Pré Embarque B:AZ | As 51 colunas, de `Necessity` até `Rupture Risk`, incluindo campos vazios, erros e valores históricos | [-] | Cada cabeçalho possui campo de origem, tipo, destino, regra de migração e linhagem conforme seção 29.1 do plano |
| Pós Embarque B:AS | As 43 colunas nomeadas, inclusive `Qty Ctnr Dem` vazia e custos por coluna | [-] | Cada cabeçalho possui campo de origem, tipo, destino, regra de migração e linhagem conforme seção 29.2 do plano |
| Fórmulas/células com erro | Somente valor armazenado e indicação de erro; jamais expressão executada | [x] | Leitor OpenXML não calcula fórmula; erro é preservado como pendência de qualidade |
| Valores financeiros | Decimal e moeda preservados; zero conhecido é diferente de vazio | [-] | Testes por moeda, precisão, zero, vazio e não totalização entre moedas |
| Datas e KPIs | Datas preservadas; KPI calculado por regra de negócio documentada | [-] | Fixtures para datas, status e KPI; referência temporal explícita no cálculo |

## Controle dos riscos obrigatórios (seção 32)

| Risco | Controle obrigatório | Status |
|---|---|---|
| IP virar centro comercial | PO central e vínculo N:N PO–IP | [x] |
| Linha oficial TOTVS ser inferida do histórico | Observação histórica separada de item oficial | [-] |
| PO ser consolidada pelo fornecedor | Chave de PO independente do fornecedor e divergência em qualidade | [-] |
| Rateio sem base aprovada | Nenhum rateio automático; saldo não rateado visível | [-] |
| Custo multiplicado em joins | Custo único por linha/coluna da origem e grãos separados | [x] |
| Histórico tratado como saldo oficial | Rótulo/origem e ausência de saldo confirmado até TOTVS | [-] |
| Linhas após autofiltro ignoradas | Leitura de todas as linhas de negócio | [-] |
| Fórmula recalculada | Leitura somente do valor salvo | [x] |
| Erro ficar apenas no log técnico | Pendência de qualidade consultável na interface | [ ] |
| Integração TOTVS presumida | Adaptador histórico até descoberta do conector real | [x] |
| Fiscal sem validação | Somente pago registrado; regras versionadas/aprovadas | [ ] |
| Corte sem congelamento | Janela de corte, snapshot e reconciliação de deltas | [ ] |

## Princípios que não podem regredir

- [x] A PO TOTVS é o centro do sistema; linhas repetidas formam histórico da mesma PO.
- [x] IP é relacionado à PO em N:N; custos pertencem ao IP e não são duplicados no join com PO.
- [x] A origem Excel é lida sem executar ou recalcular fórmulas.
- [x] Pré Embarque considera `Necessity` até `Rupture Risk`; Pós Embarque entra por IP.
- [x] A planilha original permanece preservada e é ignorada pelo Git.
- [x] KPIs são calculados pelo sistema com regras próprias, não por execução de fórmulas Excel.

## Situação por item do plano

| ID | Status | Entrega / aceite resumido | Evidência atual | Próxima ação |
|---|---|---|---|---|
| DEV01 | [-] | Monorepo, lockfiles e SDK reproduzíveis | SDK 10.0.301, `pnpm-lock.yaml` e `packages.lock.json` por projeto foram fixados; README lista pré-requisitos. Validação em segunda máquina pendente | Executar `bootstrap` limpo em outra máquina e registrar duração/evidência |
| DEV02 | [-] | Desenvolvimento local reprodutível; Compose opcional | Docker Desktop instalado; containers Compose parados sem apagar volumes. SDK 10.0.301 local; restore NuGet, pnpm install travado e build Next.js passaram. Decisão atual: deploy Vercel não depende de Docker; VPS PostgreSQL será o banco de integração externo. | Manter Docker fora do deploy. Documentar perfil Node local e opcionalmente usar PostgreSQL local/teste para desenvolvimento/CI. |
| DEV03 | [-] | OIDC, sessão e homologação | Implementação existente é ASP.NET Core/Keycloak local e ainda não foi portada para Route Handlers. Vercel exige callback HTTPS e sessão que sobreviva a invocações; nenhum issuer/cliente foi fornecido. | Portar OIDC/PKCE, sessão segura, CSRF e logout para Node; configurar issuer/client/callback quando credenciais forem disponibilizadas; validar E2E. |
| DEV04 | [-] | Perfis e escopo por importador | A autorização `(issuer, subject)`, papéis e escopos existem no backend .NET/PostgreSQL legado. A nova API Node ainda precisa portar permissões, filtros server-side e respostas 401/403/404. | Portar identidade/grants e testar admin/restrito, CSRF e ocultação de PO fora do escopo via preview e Postgres segregado. |
| DEV05 | [-] | Schemas e migrations executam em CI | Migrations SQLite M001–M006 verificadas; SQL PostgreSQL legado existe, mas ainda não foi aplicado/verificado contra a VPS nem adaptado para portabilidade Node. CI ainda não existe. | Inventariar versão PostgreSQL na VPS, criar/validar migrations versionadas para esquema Node e CI com Postgres de teste; aplicar no remoto somente após backup/ambiente aprovado. |
| DEV06 | [ ] | Cadastros e aliases | Não iniciado | Modelar cadastros oficiais e regras de alias |
| DEV07 | [x] | Lote, hash, arquivo e linhas persistidos sem duplicidade | SQLite guarda lote, hash, linhas brutas e unicidade por aba/linha; staging inicial e repetido são validados automaticamente em banco temporário. | Manter a cobertura na CI. |
| DEV08 | [x] | Leitura de valores Excel sem executar fórmulas | Fixture e check executados: fórmula fornece somente o valor salvo, e são cobertos IP cancelado, IP/PO ausentes e PO repetida. | Manter a regressão na CI. |
| DEV09 | [-] | Normalização tipada de decimal, data, NCM e erros | Parser preserva o valor bruto; normalizador aplica Unicode/whitespace, caixa canônica para PO/IP/moeda/status e nomes conhecidos de importador. Separadores múltiplos em campos escalares viram `MIXED_SCALAR_VALUES` e bloqueiam vínculo automático. NCM ainda não recebe inferência de dígitos quando ambíguo. | Completar parsing decimal/data/NCM e validar a lista de sinônimos com Dados; regras sem confirmação continuam apenas mecânicas. |
| DEV10 | [x] | Reconciliação Pré/Pós por IP sem multiplicar processos | Relatório da cópia de trabalho executado: 204 IPs (195 em ambas, 9 só no Pré, 0 só no Pós), 420 custos por moeda. Product Owner aceitou em 2026-09-24 a referência da cópia: Pré 7.000/Pós 195. | Manter a reconciliação por IP e moeda na CI; reabrir somente se a fonte mudar. |
| DEV11 | [x] | Promoção retomável e idempotente | Check automatizado injeta falha antes do commit, confirma rollback de PO/IP/observação/custo, promove o mesmo lote na retomada e reinsere zero na reimportação. | Manter a cobertura na CI. |
| DEV12 | [-] | Tela de qualidade auditável | API e tela `/quality` listam pendências com filtro e valores de origem; `M002` registra revisão, responsável, justificativa e PO/IP propostos sem alterar a fonte. Check automatizado cobre fila, resolução e filtro. | Aplicar OIDC/perfis antes de usar a revisão fora do ambiente local e executar E2E quando o ambiente permitir. |
| DEV13 | [-] | Carteira e detalhe centrados em PO | Filtros, paginação, linhagem, pendências abertas, ETag/If-Match e escopo por importador implementados; build da API, checks de migration e TypeScript passaram. Smoke sem autenticação confirma 401 na API. Dados oficiais TOTVS e E2E autenticado ainda pendem. | Executar E2E autenticado; itens oficiais TOTVS e invoices seguem fora do escopo atual. |
| DEV14 | [-] | Solicitações e filas de pendência | A fila histórica inclui linhas de Pré Embarque sem PO e IP, inclusive lotes promovidos antes da regra explícita; não cria solicitação nem altera a origem. | Modelar solicitação nativa e sua regra de conversão após autenticação, perfis e workflow. |
| DEV15 | [-] | Workflow e histórico de transições | Regras de PO e IP, rotas GET de estado/histórico e POST de transição implementadas; `If-Match`, escopo, grants explícitos, motivo, evidência obrigatória e journal append-only persistido em SQLite/PostgreSQL. Status histórico é mapeado sem inventar transições, com evento inicial e linhagem do XLSX. Build API sem warnings. | Validar E2E; vincular pré-condições às entidades oficiais de item, invoice, shipment, documento e saldo quando cada módulo existir; habilitar saltos simplificados somente com justificativa/permissão própria. |
| DEV16 | [ ] | Alocações de PO por processo | Não iniciado; não há rateio automático | Definir saldo, quantidade e validações |
| DEV17 | [ ] | Invoices e comparação | Não iniciado | Modelar vínculo, moedas e não comparabilidade |
| DEV18 | [ ] | Embarques e marcos | Não iniciado | Modelar múltiplos shipments por IP |
| DEV19 | [ ] | Containers e alocações | Não iniciado | Preservar resumo legado e alocação fracionada |
| DEV20 | [ ] | Desembaraço e NF | Não iniciado | Modelar referências múltiplas com origem |
| DEV21 | [-] | Custos, rateio e reversões | Custos são deduplicados por linha/coluna de origem no IP; rateio/reversão pendentes | Implementar alocação explícita e conciliação por moeda |
| DEV22 | [ ] | Regras e benefícios fiscais aprovados | Não iniciado | Aguardar regra fiscal homologada |
| DEV23 | [ ] | Documentos versionados e autorizados | Não iniciado; filesystem da função Vercel não é storage permanente | Selecionar object storage externo privado, upload/download autorizado, retenção e credenciais server-side antes de implementar anexos |
| DEV24 | [-] | Auditoria e outbox | `M005/M006` implementam auditoria/outbox e dispatcher no .NET/SQLite/PostgreSQL legado. Sem consumidor aprovado; Vercel não executa worker residente. | Portar gravação atômica/auditoria para Node; escolher cron/queue ou worker externo persistente, limitar invocações e validar retry/idempotência antes de ativar consumidores. |
| DEV25 | [-] | Dashboard e exportação | KPI de risco de ruptura calculado no backend; dashboard/exportação completos pendentes | Definir catálogo e telas de indicadores |
| DEV26 | [ ] | ETL e dimensões analíticas | Não iniciado | Definir watermark e reexecução |
| DEV27 | [ ] | Fatos e medidas sem mistura de moedas | Não iniciado | Modelar fatos e medidas por grão |
| DEV28 | [ ] | Power BI e RLS | Não iniciado | Definir modelo semântico e RLS |
| DEV29 | [ ] | Qualidade não funcional | Não iniciado | Planejar benchmark, segurança e acessibilidade |
| DEV30 | [ ] | Corte e operação | Não iniciado | Criar runbooks, plano de corte e retorno |

## Evidência da carga SQLite de teste

| Verificação | Resultado |
|---|---:|
| Data da última validação | 2026-09-23 |
| Banco de teste | `data/local/import_erp_test.db` (ignorado pelo Git) |
| POs centrais promovidas | 338 |
| Linhas históricas de Pré Embarque com PO | 6.923 |
| IPs promovidos | 204 |
| Vínculos PO–IP | 460 |
| Custos promovidos sem duplicação | 420 |
| Pendências de qualidade preservadas | 309 |
| Reimportação do mesmo arquivo | 0 novos registros em todas as entidades |
| Hash da planilha original após validação | `EB9CB9F0B14D7BE75850DB63D8A562A0495422C80BE3EB1745BAF0DDC4A669A0` |

> Os números acima descrevem a cópia de trabalho fornecida e não substituem a
> reconciliação/homologação exigida para produção no plano.

## Decisões e pendências abertas

| Status | Tema | Tratamento atual | Responsável para confirmar |
|---|---|---|---|
| [!] | PostgreSQL da VPS | Configurar `DATABASE_URL` somente na API Node da VPS, com role mínimo, pool limitado, backup e restore testado. Usuário não forneceu credenciais; manter PostgreSQL em loopback/rede privada e não publicar 5432. | Usuário / DevOps |
| [!] | Acesso HTTPS da Vercel à API VPS | Publicar somente a API via HTTPS reverse proxy ou Cloudflare Tunnel e autenticar o proxy com `VPS_API_TOKEN`; esta topologia não exige Static IP da Vercel nem conexão direta Vercel→PostgreSQL. Escolher e configurar domínio/DNS e método de exposição. | Usuário / DevOps |
| [!] | Provedor e credenciais OIDC | Configurar issuer/client/secret e callback HTTPS após escolher/provisionar provedor público; Keycloak local não é endpoint Vercel. | Usuário / TI |
| [!] | Storage e jobs serverless | Selecionar object storage privado e mecanismo cron/queue para imports/outbox; filesystem e worker residente não são assumidos na Vercel. | Arquitetura / Usuário |
| [!] | Dados oficiais TOTVS | A carga atual representa histórico Excel, não linhas oficiais de pedido | Compras / TI TOTVS |
| [!] | Rateio de custos | Nenhum rateio automático é aplicado; custos permanecem no IP | Importação / Financeiro |
| [!] | Conversão de moedas | Não há total consolidado entre moedas sem taxa aprovada | Financeiro |
| [!] | Regras fiscais | Sem cálculo fiscal presumido | Fiscal |
| [!] | Critérios antigos do plano | As contagens do plano diferem da cópia de trabalho analisada; usar a evidência de carga até reconciliação formal | Dados / Product Owner |
| [!] | Consumidores da outbox | ADR 010 já define PostgreSQL, sem broker/Redis inicial. Não há consumidor de negócio, destino externo, credencial ou política de reprocessamento manual aprovada; o dispatcher preserva mensagens pendentes sem consumidor e não finge publicação. | Arquitetura / Product Owner / DevOps |

## Histórico de incrementos

| Data | Incremento | Status | Evidência / observação |
|---|---|---|---|
| 2026-09-24 | Preparação operacional da VPS e documentação de funcionamento sem PC | [-] | README, setup Vercel, plano e checklist alinhados em Vercel Free + Next proxy + API Fastify/PostgreSQL na VPS. Modelos `deploy/hostinger/import-erp-api.service` e `nginx-api.conf.example` mais roteiro de instalação adicionados. Build Next.js e `tsc` da API passaram. Endpoints de negócio/OIDC, migrations da API e instalação real continuam pendentes; MCP Vercel não está disponível como ferramenta nesta sessão. |
| 2026-09-24 | Escolha e preparação da stack Vercel + PostgreSQL VPS | [-] | Product Owner escolheu Vercel Free para Next.js e API Node + PostgreSQL na VPS Hostinger. Documentada API persistente acessada por HTTPS reverse proxy/túnel e gateway token; banco/segredos ficam na VPS, sem porta 5432 pública. O PC pessoal pode ficar desligado após deploy e configuração de início automático dos serviços. Scaffold Node criado, mas endpoints de negócio/OIDC, migrations PostgreSQL, instalação na VPS e E2E seguem pendentes. MCP Vercel está em `~\\.codex\\config.toml`, mas não apareceu carregado como ferramenta nesta sessão; inspeção da conta pendente. |
| 2026-09-23 | Núcleo PO, leitura Excel, plano de promoção e interface inicial | [-] | PO central, histórico por linha, relação PO–IP e KPIs iniciais implementados; aceite integral do MVP pendente |
| 2026-09-23 | Ambiente SQLite de teste | [x] | Staging/promoção idempotentes validados com 338 POs, 6.923 linhas, 204 IPs, 460 vínculos e 420 custos |
| 2026-09-24 | Criação deste checklist versionado | [x] | Estado inicial consolidado a partir do plano e das validações executadas |
| 2026-09-24 | DEV05 — migrations SQLite versionadas | [-] | `M001_historical_core.sqlite.sql`, histórico `__import_erp_migrations`, baseline seguro e verificador temporário criados; execução local passou. CI segue pendente. |
| 2026-09-24 | DEV07–DEV10 — fixture, idempotência e reconciliação | [-] | Fixture OpenXML executada cobre data, decimal com vírgula, zero conhecido, fórmula com valor salvo, erro `#REF!`, IP cancelado, linhas sem PO/IP, PO repetida e IP exclusivo do pós-embarque. Staging/promoção/reimportação idempotentes e reconciliação por IP/moeda passaram em banco temporário; falta retry por falha e aceite da carga real. |
| 2026-09-24 | DEV10–DEV11 — retry e reconciliação da cópia de trabalho | [-] | Falha injetada antes do commit reverteu integralmente o lote; retomada e reimportação passaram. Reconciliação real: 204 IPs, 195 nas duas abas, 9 só no Pré, 0 só no Pós e 420 custos. Contagens 7.000/195 divergem das referências 6.940/190 e aguardam decisão de Dados/Product Owner. |
| 2026-09-24 | DEV01–DEV02 — lockfiles e infraestrutura local segura | [-] | Lockfiles .NET, Compose, realm local Keycloak, scripts PowerShell/Bash e README foram adicionados. A API compilou, os checks de migration passaram e o web build passou. Docker e Bash não estão instalados nesta máquina; a execução do Compose permanece pendente. |
| 2026-09-24 | DEV10, DEV12 e DEV14 — referência aceita e fila de qualidade | [-] | Product Owner aceitou 7.000 linhas Pré/195 Pós como referência da cópia. `M002_historical_quality_reviews.sqlite.sql`, endpoints e tela `/quality` registram revisão auditável sem editar origem. Build .NET e checks de migration passaram; TypeScript passou. Build Next compilou, mas o ambiente bloqueou processo auxiliar com `spawn EPERM`. |
| 2026-09-24 | DEV13 — paginação e filtros da carteira | [-] | `GET /purchase-orders` retorna página com total, filtros por PO/importador e limite máximo de 100; a carteira usa esses filtros e paginação. Build da API, checks de migration e TypeScript passaram. |
| 2026-09-24 | DEV13 — contrato da carteira e detalhe | [-] | A carteira ganhou filtros de fonte histórica, `hasNext` e indicadores explícitos de item/saldo não confirmados. O detalhe expõe pendências abertas, linhagem de aba/linha/valor e endpoint paginado de histórico; ETag/`If-Match` protege edição operacional. Testes de regra foram adicionados ao verificador; a execução final aguarda reconstrução dos artefatos locais .NET. |
| 2026-09-24 | DEV09 — normalização conservadora de células | [-] | `HistoricalValueNormalizer` padroniza espaços/caracteres invisíveis, caixa de identificadores, moeda/status e três importadores conhecidos. Valores escalares com quebra de linha, `;` ou `|` geram pendência de revisão e não participam de chaves ou vínculos. Valores brutos no staging permanecem intactos. |

| 2026-09-24 | DEV13 — validação local concluída | [-] | Build da API e do verificador .NET passaram sem warnings; checks de migration passaram; TypeScript passou. E2E e escopo por importador continuam pendentes. |
| 2026-09-24 | DEV03 — OIDC local e sessão web | [-] | Backend/frontend implementados com PKCE, cookie HttpOnly/Secure, CSRF e endpoints de login/me/logout; API compilou e smoke confirmou 401 sem sessão e redirects de login. Keycloak não disponível sem Docker daemon; callback real, CSRF autenticado, provisionamento e Entra ainda pendem. |
| 2026-09-24 | DEV04 — papéis e escopo por importador | [-] | `M003` cria o mapa de identidade `(issuer, subject)`, papéis e escopos; permissões explícitas filtram carteira antes da paginação e ocultam PO fora do escopo. A fila de qualidade e os comandos de importação também exigem permissão. Realm local traz `local-admin` exclusivamente para desenvolvimento. Build API, checks de migration/escopo e TypeScript passaram; E2E OIDC permanece bloqueado pelo daemon Docker inativo. |
| 2026-09-24 | DEV15 — workflows comerciais e logísticos | [-] | `M004` cria estado de workflow e journal; API oferece GET de estado/histórico e POST de transição para PO/IP. Regras da seção 10 validam sequência, permissões, evidências, justificativa, escopo e versão; promoção histórica registra estado inicial com origem. Build API passou sem warnings. E2E aguarda Keycloak ativo e parte das evidências depende de módulos operacionais futuros. |
| 2026-09-24 | DEV24 — trilha de auditoria e outbox | [-] | `M005` cria auditoria append-only (com proteção contra update/delete SQLite e trigger PostgreSQL) e outbox pendente. Edição operacional de PO, revisão de qualidade e mudança de estado salvam alteração, auditoria e evento numa transação; `GET /audit/{PO|IP}/{id}` pagina registros no escopo. IDs dos eventos são reutilizáveis para deduplicação; dispatcher, leases, retry e inbox de consumidor ficaram para a etapa assíncrona. |
| 2026-09-24 | DEV24 — dispatcher e inbox | [-] | `M006` adiciona lease, retry exponencial limitado, dead-letter e inbox única por consumidor. O dispatcher PostgreSQL faz claim com `FOR UPDATE SKIP LOCKED`; o check temporário cobre falha transitória, expiração de lease e redelivery idempotente. A execução não pôde ser concluída: o SDK 10.0.301 local não contém os resolvedores de workload requeridos e a restauração não alcança o NuGet. Não há consumidor/integração externa aprovada, portanto nenhuma mensagem é confirmada sem um consumidor registrado. |
| 2026-09-24 | DEV24 — validação do dispatcher | [-] | Com restore travado e MSBuild do SDK 11 preview usado somente como contorno para a instalação incompleta do SDK 10 local, a API e `MigrationChecks` compilaram; `MigrationChecks` passou. A prova cobre migrations M001–M006, falha transitória com retry, exclusão/recuperação de lease e redelivery sem segunda execução do consumidor. DEV24 permanece parcial pois não existe consumidor ou transporte externo aprovado. |
| 2026-09-24 | DEV02 — infraestrutura local em execução | [-] | Docker Desktop 4.92.0, Docker CLI 29.8.0 e Compose v5.5.1 instalados. `POSTGRES_HOST_PORT` é configurável; `.env` local usa 5433 porque o serviço PostgreSQL 17 do host ocupa 5432. `docker ps` executado fora do sandbox confirmou PostgreSQL, Keycloak, Azurite e OTEL saudáveis. A tentativa autorizada pelo usuário de parar o serviço PostgreSQL falhou por falta de permissão Windows. Volumes preservados. Bootstrap/migrations e API aguardam SDK .NET 10.0.301 visível. |
| 2026-09-24 | DEV02 — tentativa de liberar 5432 | [-] | `sc query` confirmou `postgresql-x64-17` em execução, PID 6540 ouvindo em `0.0.0.0:5432` e `[::]:5432`; `Stop-Service` foi tentado com autorização explícita do usuário, mas o Windows retornou que não é possível abrir o serviço. PostgreSQL Compose responde em `127.0.0.1:5433` (PID 31400); Keycloak discovery responde HTTP 200 em `127.0.0.1:8180`; Azurite responde na porta 10000. Para usar 5432, o serviço do host precisa ser parado por uma sessão administrativa. |
| 2026-09-24 | Preparação do frontend para Vercel | [-] | SDK .NET 10.0.301 instalado localmente em `.tools/dotnet` (ignorado pelo Git); restore NuGet travado passou. pnpm 12.6 instalou dependências com `allowBuilds: sharp` e lockfile congelado; `next build` passou. CLI Vercel autenticada. Containers Compose foram desligados sem remover volumes. A URL padrão do frontend ainda aponta para `localhost:5000`; API .NET e OIDC precisam de hospedagem pública para login e dados funcionarem. |

## Atualização do incremento atual

| Item | Status | Evidência | Próximo passo |
|---|---|---|---|
| DEV01 | [-] | `packages.lock.json` foi gerado para todos os projetos .NET; README documenta .NET 10, Node 24, Corepack e Docker Compose. | Executar bootstrap limpo em uma segunda máquina e registrar a evidência. |
| DEV02 | [-] | Docker Desktop instalado, mas containers Compose desligados sem apagar volumes; Docker não é requisito de Vercel nem VPS. Build Next.js e compilação TypeScript Fastify passaram em 2026-09-24. | Manter Docker fora do deploy; preparar PostgreSQL de teste segregado para CI. |
| DEV03 | [-] | OIDC .NET/Keycloak local existe, mas ainda não foi portado para API Node persistente na VPS. Não há issuer/cliente/callback público nem sessão Node persistida. | Portar OIDC/PKCE, cookie, persistência, CSRF e logout; validar com provedor público quando disponível. |
| DEV04 | [-] | M003 e políticas atuais existem no backend .NET; API Node precisa portar permissão e escopo completos. | Portar autorização server-side e validar casos 401/403/404, filtros, CSRF e escopo em preview. |
| DEV15 | [-] | Transições para PO e IP seguem os estados e pré-condições da seção 10; endpoint de escrita exige `If-Match`, papel autorizado, escopo, motivo e evidências por transição. O estado e journal são gravados atomicamente em SQLite/PostgreSQL; a promoção importa um evento inicial ligado à linha XLSX e mapeia os seis status legados definidos no plano. ETag de estado, log de auditoria e exceção de pré-condição 422 documentados. Build API concluído sem warnings. | Validar execução com Keycloak e integrar evidências a entidades dos módulos subsequentes; testar concorrência/cancelamento/reabertura antes de aceite. |
| DEV24 | [-] | `M006` e os repositórios SQLite/PostgreSQL implementam dispatcher com lease, retry limitado, dead-letter e inbox com chave única `(consumer,eventId)`; a API registra as dependências sem ativar consumidor inexistente. `MigrationChecks` passou os cenários de retry, lease e redelivery; a API compilou pelo MSBuild de contorno. A entrega é at-least-once. | Definir consumidor(es), destino, credenciais, alertas e reprocessamento manual; então registrar e validar o worker em ambiente compartilhado. |
| Vercel/produção | [-] | Next.js e proxy same-origin compilam. Fastify tem health checks, validação do gateway e conexão PostgreSQL; modelos systemd/Nginx e roteiro Hostinger adicionados. Ainda não há rotas de negócio/OIDC, paridade de banco nem deploy. | Portar contratos e auth, implementar migrations/repos Node, validar segurança/paridade, instalar VPS e executar E2E antes de produção. |
| Vercel/produção | [-] | Topologia escolhida: Vercel Free para Next.js; API Node e PostgreSQL na VPS. Frontend e VPS não dependem do PC pessoal, desde que API, banco e proxy/túnel estejam instalados como serviços com início automático e a VPS tenha conectividade. Esse requisito está documentado; o ambiente ainda não foi implantado. | Concluir migração e testes, configurar serviços persistentes/TLS/OIDC e executar smoke/E2E antes de considerar produção disponível. |

## Modelo para o próximo incremento

Copie esta linha para a tabela de histórico e atualize os itens afetados acima:

```text
| AAAA-MM-DD | nome do incremento | [ ] / [-] / [x] / [!] | arquivos alterados, teste executado, resultado e decisão pendente |
```

## Evidência atual do DEV13

| Data | Status | Evidência | Próximo passo |
|---|---|---|---|
| 2026-09-24 | [-] | `GET /api/v1/purchase-orders` aceita filtros por PO, importador, fornecedor, produto, IP, status histórico, atendimento, qualidade e período de necessidade; cada página retorna `totalCount` e `hasNext`. `GET /history-items` pagina observações históricas imutáveis e preserva a linhagem de aba/linha/valor. O detalhe declara `officialItemsKnown: false`, `balanceAvailable: false` e pendências abertas. GET retorna ETag; PATCH exige `If-Match` compatível e rejeita escrita obsoleta. Os checks de migration cobrem filtros, paginação, linhagem e concorrência; compilação C# e TypeScript passaram localmente. | Aplicar o escopo por importador de DEV03/DEV04 antes de expor o contrato fora do ambiente local; itens oficiais TOTVS e invoices continuam fora do escopo. |

## Evidência atual do DEV03

| Data | Status | Evidência | Próximo passo |
|---|---|---|---|
| 2026-09-24 | [-] | OIDC Authorization Code + PKCE em API, sessão cookie HttpOnly/Secure com limite de inatividade de 30 min e absoluto de 8 h, CSRF nas mutações e frontend com verificação/logout implementados. Build da API passou; smoke local sem sessão confirmou 401 para API e redirecionamento para login em rotas de sessão. O desafio/login real, callback OIDC, cookies no navegador e CSRF autenticado não foram validados porque Docker daemon/Keycloak estavam indisponíveis. | Subir o realm local com `dev.ps1 up -Profile infra`, provisionar usuário de teste e validar jornada completa; não marcar concluído até os testes passarem. |
