# Instalar API na VPS Hostinger

Este roteiro deixa a API independente do computador pessoal. A API escuta
somente `127.0.0.1:4000`; Nginx publica HTTPS e PostgreSQL permanece em
loopback. Os arquivos `.service` e `.conf.example` são modelos sem credenciais
nem certificados.

## Pré-requisitos

- VPS Linux suportada pela Hostinger, com atualizações de segurança aplicadas.
- Node.js 24.x, PostgreSQL ativo, Nginx e um hostname sob seu controle.
- DNS do hostname apontando para a VPS e certificado TLS válido.
- Backup do banco e procedimento de restauração verificado.

Não abra a porta PostgreSQL 5432 na firewall pública. Libere 443 para HTTPS;
22 deve ser limitado às origens administrativas conhecidas. Não é necessário
instalar Docker.

## Instalação inicial

Os comandos abaixo são um roteiro para executar na VPS como administrador;
substitua os diretórios somente pelos caminhos escolhidos na VPS.

```sh
useradd --system --home /var/lib/import-erp --create-home --shell /usr/sbin/nologin import-erp
install -d -o import-erp -g import-erp /opt/import-erp /var/lib/import-erp
install -d -o root -g import-erp -m 0750 /etc/import-erp
```

Copie o repositório para `/opt/import-erp`, instale dependências a partir do
`apps/api/pnpm-lock.yaml` e compile `apps/api` com Node.js 24 e Corepack:

```sh
cd /opt/import-erp/apps/api
corepack pnpm install --frozen-lockfile
corepack pnpm build
```

Crie `/etc/import-erp/api.env` no próprio servidor, modo `0640`, pertencente a
`root:import-erp`. Preencha `DATABASE_URL` com a role exclusiva da aplicação e
um endpoint local; adicione `GATEWAY_TOKEN`, issuer/client OIDC, segredo de
sessão e origem pública da aplicação. Gere os segredos no servidor e não os
coloque no Git, no histórico do shell ou neste arquivo.

## Serviço persistente

Instale `import-erp-api.service` em `/etc/systemd/system/` e ajuste
`WorkingDirectory` e `ExecStart` se o diretório de instalação mudar. Então:

```sh
systemctl daemon-reload
systemctl enable --now import-erp-api
systemctl status import-erp-api
journalctl -u import-erp-api --since today
```

`enable` configura início após reinicialização; `Restart=always` reinicia o
processo se ele falhar. `systemctl status` e os logs não devem exibir segredos.

## HTTPS e proxy reverso

Configure DNS e TLS para o hostname da API. Copie
`nginx-api.conf.example` para a configuração do virtual host, substitua
`api.example.com`, aponte `ssl_certificate` e `ssl_certificate_key` para os
arquivos protegidos do certificado e valide/recarregue o Nginx:

```sh
nginx -t
systemctl reload nginx
```

Configure `VPS_API_URL` na Vercel como a origem HTTPS, por exemplo
`https://api.seudominio.com`, sem caminho ou credenciais. Configure o mesmo
`GATEWAY_TOKEN` no painel Vercel como `VPS_API_TOKEN`. Preview e Production
devem usar tokens e issuer separados.

## Atualizações e disponibilidade

Faça backup, atualize o checkout em diretório de release, rode a instalação
congelada e `pnpm build`, então troque o release e reinicie com
`systemctl restart import-erp-api`. Confirme `/health/live` pelo endpoint
HTTPS e `/health/ready` por uma chamada com gateway autorizado, sem registrar o
token. Em falha, volte ao release anterior e preserve o banco.

O serviço não depende do PC. A VPS, o PostgreSQL, Nginx/TLS, DNS e a conta
Vercel continuam sendo dependências de produção; configure monitoramento,
backup e renovação automática do certificado.

## Limite atual

O serviço Fastify existente ainda é scaffold: health check, validação do token
de gateway e conexão ao PostgreSQL estão preparados, mas os contratos de
carteira, detalhe, qualidade e OIDC ainda não foram migrados. Portanto, não
coloque este scaffold em produção para uso operacional até a checklist marcar
paridade e validações de segurança como concluídas.
