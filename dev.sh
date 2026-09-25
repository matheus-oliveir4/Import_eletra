#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
compose_directory="$repository_root/infra/compose"
compose_file="$compose_directory/compose.yaml"
environment_file="$compose_directory/.env"

require_command() {
  command -v "$1" >/dev/null 2>&1 || { echo "Prerequisito ausente: $1. Consulte o README." >&2; exit 1; }
}

require_env_file() {
  [[ -f "$environment_file" ]] || { echo "Configuração local ausente. Execute './dev.sh bootstrap'." >&2; exit 1; }
}

compose() {
  require_command docker
  require_env_file
  docker compose --env-file "$environment_file" -f "$compose_file" "$@"
}

assert_tool_versions() {
  require_command docker
  require_command dotnet
  require_command node
  require_command corepack
  [[ "$(dotnet --version | cut -d. -f1)" == "10" ]] || { echo ".NET 10 é obrigatório." >&2; exit 1; }
  [[ "$(node --version | sed 's/^v//' | cut -d. -f1)" == "24" ]] || { echo "Node.js 24 é obrigatório." >&2; exit 1; }
  docker compose version >/dev/null
}

usage() {
  echo "Uso: ./dev.sh {bootstrap|up|migrate|seed|import-history|test|down|reset} [opções]" >&2
  exit 1
}

[[ $# -ge 1 ]] || usage
command_name="$1"
shift

case "$command_name" in
  bootstrap)
    [[ $# -eq 0 ]] || usage
    assert_tool_versions
    if [[ ! -f "$environment_file" ]]; then
      cp "$compose_directory/.env.example" "$environment_file"
      echo "Criado infra/compose/.env com valores exclusivos para desenvolvimento local."
    fi
    (cd "$repository_root" && corepack pnpm --dir apps/web install --frozen-lockfile && dotnet restore src/ImportErp.Api/ImportErp.Api.csproj --locked-mode)
    echo "Bootstrap concluído. Revise infra/compose/.env antes de subir os serviços."
    ;;
  up)
    profile="infra"
    [[ $# -eq 0 || ( $# -eq 2 && "$1" == "--profile" ) ]] || usage
    [[ $# -eq 0 ]] || profile="$2"
    [[ "$profile" == "infra" ]] || { echo "Somente o perfil infra existe nesta etapa." >&2; exit 1; }
    assert_tool_versions
    compose --profile "$profile" up -d --wait
    echo "Infraestrutura pronta: PostgreSQL na porta configurada em infra/compose/.env, Keycloak em http://127.0.0.1:8180 e Azurite em 127.0.0.1:10000."
    ;;
  migrate)
    [[ $# -eq 0 ]] || usage
    cat "$repository_root/src/ImportErp.Migrations/M001_historical_core.sql" | compose exec -T postgres sh -c 'PGPASSWORD="$ERP_DB_PASSWORD" psql -v ON_ERROR_STOP=1 -U "$ERP_DB_USER" -d import_erp'
    echo "M001_historical_core aplicada ao PostgreSQL local."
    ;;
  test)
    [[ $# -eq 0 ]] || usage
    (cd "$repository_root" && dotnet run --project src/ImportErp.MigrationChecks/ImportErp.MigrationChecks.csproj && corepack pnpm --dir apps/web build)
    ;;
  down)
    [[ $# -eq 0 || ( $# -eq 2 && "$1" == "--profile" && "$2" == "infra" ) ]] || usage
    compose --profile infra down --remove-orphans
    echo "Serviços parados; volumes locais foram preservados."
    ;;
  reset)
    [[ $# -eq 3 && "$1" == "--environment" && "$2" == "local" && "$3" == "--confirm" ]] || {
      echo "Reset é destrutivo e só aceita: ./dev.sh reset --environment local --confirm" >&2
      exit 1
    }
    compose --profile infra down --volumes --remove-orphans
    echo "Volumes locais do Compose foram removidos. Nenhum serviço externo foi acessado."
    ;;
  seed)
    echo "Seed de referência pertence ao DEV06 e ainda não está disponível; nenhum dado foi alterado." >&2
    exit 1
    ;;
  import-history)
    [[ $# -eq 3 && "$1" == "--file" && "$3" == "--preview" ]] || {
      echo "Nesta etapa use somente: ./dev.sh import-history --file <arquivo.xlsx> --preview" >&2
      exit 1
    }
    (cd "$repository_root" && dotnet run --project src/ImportErp.MigrationChecks/ImportErp.MigrationChecks.csproj -- "$2")
    ;;
  *) usage ;;
esac
