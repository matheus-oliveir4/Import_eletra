[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('bootstrap', 'up', 'migrate', 'seed', 'import-history', 'test', 'down', 'reset')]
    [string]$Command,
    [ValidateSet('infra')]
    [string]$Profile = 'infra',
    [ValidateSet('local')]
    [string]$Environment,
    [switch]$ConfirmReset,
    [string]$File,
    [switch]$Preview,
    [string]$Batch,
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSCommandPath
$composeDirectory = Join-Path $repositoryRoot 'infra/compose'
$composeFile = Join-Path $composeDirectory 'compose.yaml'
$environmentFile = Join-Path $composeDirectory '.env'

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Prerequisito ausente: $Name. Consulte o README antes de continuar."
    }
}

function Require-LocalEnvironmentFile {
    if (-not (Test-Path -LiteralPath $environmentFile)) {
        throw "Configuração local ausente. Execute '.\dev.ps1 bootstrap' para criar infra/compose/.env."
    }
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ComposeArguments)
    Require-Command docker
    Require-LocalEnvironmentFile
    & docker compose --env-file $environmentFile -f $composeFile @ComposeArguments
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose terminou com falha.' }
}

function Assert-ToolVersions {
    Require-Command docker
    Require-Command dotnet
    Require-Command node
    Require-Command corepack

    $dotnetMajor = [int]((dotnet --version).Split('.')[0])
    $nodeMajor = [int]((node --version).TrimStart('v').Split('.')[0])
    if ($dotnetMajor -ne 10) { throw "SDK .NET 10 é obrigatório; encontrado $(dotnet --version)." }
    if ($nodeMajor -ne 24) { throw "Node.js 24 é obrigatório; encontrado $(node --version)." }

    & docker compose version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose V2 é obrigatório.' }
}

switch ($Command) {
    'bootstrap' {
        Assert-ToolVersions
        if (-not (Test-Path -LiteralPath $environmentFile)) {
            Copy-Item (Join-Path $composeDirectory '.env.example') $environmentFile
            Write-Host 'Criado infra/compose/.env com valores exclusivos para desenvolvimento local.'
        }
        Push-Location $repositoryRoot
        try {
            & corepack pnpm --dir apps/web install --frozen-lockfile
            if ($LASTEXITCODE -ne 0) { throw 'Não foi possível instalar as dependências frontend travadas.' }
            & dotnet restore src/ImportErp.Api/ImportErp.Api.csproj --locked-mode
            if ($LASTEXITCODE -ne 0) { throw 'Não foi possível restaurar os pacotes .NET travados.' }
        }
        finally { Pop-Location }
        Write-Host 'Bootstrap concluído. Revise infra/compose/.env antes de subir os serviços.'
    }
    'up' {
        Assert-ToolVersions
        Invoke-Compose --profile $Profile up -d --wait
        Write-Host 'Infraestrutura pronta: PostgreSQL na porta configurada em infra/compose/.env, Keycloak em http://127.0.0.1:8180 e Azurite em 127.0.0.1:10000.'
    }
    'migrate' {
        Require-LocalEnvironmentFile
        $migration = Join-Path $repositoryRoot 'src/ImportErp.Migrations/M001_historical_core.sql'
        Require-Command docker
        Get-Content -Raw -LiteralPath $migration | & docker compose --env-file $environmentFile -f $composeFile exec -T postgres sh -c 'PGPASSWORD="$ERP_DB_PASSWORD" psql -v ON_ERROR_STOP=1 -U "$ERP_DB_USER" -d import_erp'
        if ($LASTEXITCODE -ne 0) { throw 'A migration PostgreSQL falhou.' }
        Write-Host 'M001_historical_core aplicada ao PostgreSQL local.'
    }
    'test' {
        Push-Location $repositoryRoot
        try {
            & dotnet run --project src/ImportErp.MigrationChecks/ImportErp.MigrationChecks.csproj
            if ($LASTEXITCODE -ne 0) { throw 'Os checks de migration/histórico falharam.' }
            & corepack pnpm --dir apps/web build
            if ($LASTEXITCODE -ne 0) { throw 'O build frontend falhou.' }
        }
        finally { Pop-Location }
    }
    'down' {
        Invoke-Compose --profile $Profile down --remove-orphans
        Write-Host 'Serviços parados; volumes locais foram preservados.'
    }
    'reset' {
        if ($Environment -ne 'local' -or -not $ConfirmReset) {
            throw "Reset é destrutivo e só aceita '-Environment local -ConfirmReset'."
        }
        Invoke-Compose --profile infra down --volumes --remove-orphans
        Write-Host 'Volumes locais do Compose foram removidos. Nenhum serviço externo foi acessado.'
    }
    'seed' {
        throw 'Seed de referência pertence ao DEV06 e ainda não está disponível; nenhum dado foi alterado.'
    }
    'import-history' {
        if (-not $Preview -or [string]::IsNullOrWhiteSpace($File) -or $Commit -or $Batch) {
            throw 'Nesta etapa use somente: .\dev.ps1 import-history -File <arquivo.xlsx> -Preview. Commit por lote será habilitado após a reconciliação DEV10.'
        }
        Push-Location $repositoryRoot
        try {
            & dotnet run --project src/ImportErp.MigrationChecks/ImportErp.MigrationChecks.csproj -- $File
            if ($LASTEXITCODE -ne 0) { throw 'A prévia de reconciliação falhou.' }
        }
        finally { Pop-Location }
    }
}
