param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CliArgs
)

# ---- Usage ----
function Show-Usage {
@"
Usage: .\run.ps1 [OPTIONS]

Options:
  -b, --backend        Run .NET API
  -w, --worker         Run .NET Worker
  -a, --all            Build frontend + run API + Worker
  -f, --frontend       Run frontend (npm run dev)

  -n, --npm <args>     Run npm command (client/)
  -d, --dotnet <args>  Run dotnet CLI command

  -k, --kill-port      Kill process on API port
  -h, --help           Show this help

Tests:
  -tf, --test-fe       Frontend unit tests (client/: build + vitest)
  -te, --test-e2e      End-to-end tests (e2e/: playwright)
  -tb, --test-be       Backend unit tests (server/: clean + build + dotnet test)
  -ta, --test-all      Frontend unit + backend unit + e2e

Env:
  SKIP_BUILD=1         Skip the client build step in --test-fe
"@ | Write-Host
}

# ---- Flags ----
$Backend = $false
$Worker = $false
$All = $false
$Frontend = $false
$KillPort = $false
$TestFe = $false
$TestE2e = $false
$TestBe = $false
$Npm = @()
$Dotnet = @()

# ---- Parse args ----
$i = 0
while ($i -lt $CliArgs.Count) {
    switch ($CliArgs[$i]) {

        "-b" { $Backend = $true }
        "--backend" { $Backend = $true }

        "-w" { $Worker = $true }
        "--worker" { $Worker = $true }

        "-a" { $All = $true }
        "--all" { $All = $true }

        "-f" { $Frontend = $true }
        "--frontend" { $Frontend = $true }

        "-k" { $KillPort = $true }
        "--kill-port" { $KillPort = $true }

        "-tf" { $TestFe = $true }
        "--test-fe" { $TestFe = $true }

        "-te" { $TestE2e = $true }
        "--test-e2e" { $TestE2e = $true }

        "-tb" { $TestBe = $true }
        "--test-be" { $TestBe = $true }

        "-ta" { $TestFe = $true; $TestE2e = $true; $TestBe = $true }
        "--test-all" { $TestFe = $true; $TestE2e = $true; $TestBe = $true }

        "-h" { Show-Usage; exit }
        "--help" { Show-Usage; exit }

        "-n" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Npm += $CliArgs[$i]; $i++ }; $i-- }
        "--npm" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Npm += $CliArgs[$i]; $i++ }; $i-- }

        "-d" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Dotnet += $CliArgs[$i]; $i++ }; $i-- }
        "--dotnet" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Dotnet += $CliArgs[$i]; $i++ }; $i-- }

        default {
            Write-Host "Unknown option: $($CliArgs[$i])"
            Show-Usage
            exit 1
        }
    }
    $i++
}

if (-not ($Backend -or $Worker -or $All -or $Frontend -or $KillPort -or $TestFe -or $TestE2e -or $TestBe -or $Npm.Count -or $Dotnet.Count)) {
    Show-Usage
    exit
}

# ---- Paths ----
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ClientDir = Join-Path $ScriptDir "client"
$E2eDir = Join-Path $ScriptDir "e2e"
$Solution = Join-Path $ScriptDir "server/Blocks.slnx"
$ApiProject = Join-Path $ScriptDir "server/Api/Api.csproj"
$WorkerProject = Join-Path $ScriptDir "server/Worker/Worker.csproj"
$Wwwroot = Join-Path $ScriptDir "server/Api/wwwroot"

$ApiPort = 5000

# ---- Helpers ----

function Free-Port {
    $connections = netstat -ano | Select-String ":$ApiPort"
    foreach ($line in $connections) {
        $parts = ($line -split "\s+") | Where-Object { $_ }
        $pid = $parts[-1]
        if ($pid -match "^\d+$") {
            Write-Host "Killing PID $pid on port $ApiPort"
            taskkill /PID $pid /F | Out-Null
        }
    }
}

function Restore-Dotnet {
    Write-Host "Restoring .NET dependencies..."
    dotnet restore $ApiProject
    dotnet restore $WorkerProject
}

function Build-Frontend {
    Write-Host "Building frontend..."
    npm --prefix $ClientDir install
    npm --prefix $ClientDir run build

    if (!(Test-Path $Wwwroot)) {
        New-Item -ItemType Directory -Path $Wwwroot | Out-Null
    }

    if (Test-Path "$ClientDir/dist") {
        Write-Host "Syncing dist → wwwroot..."
        robocopy "$ClientDir/dist" $Wwwroot /MIR | Out-Null
    }
}

function Run-Backend {
    Write-Host "Running .NET API..."
    dotnet run --project $ApiProject
}

function Run-Worker {
    Write-Host "Running .NET Worker..."
    dotnet run --project $WorkerProject
}

# ---- Tests ----

# Native commands do not stop the script on failure, so check the exit code.
function Invoke-Step {
    param([scriptblock]$Step, [string]$Name)

    & $Step
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$Name failed (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

function Ensure-NodeModules {
    param([string]$Dir)

    if (!(Test-Path (Join-Path $Dir "node_modules"))) {
        Write-Host "Installing dependencies in $(Split-Path -Leaf $Dir)..."
        Invoke-Step { npm --prefix $Dir clean-install } "npm clean-install"
    }
}

# Vitest does not read dist/, so the build is only a TypeScript gate.
# Skip it with $env:SKIP_BUILD = "1" for a faster loop.
function Test-Frontend {
    Write-Host "=== Frontend unit tests ==="

    Ensure-NodeModules $ClientDir

    if ($env:SKIP_BUILD -eq "1") {
        Write-Host "SKIP_BUILD=1 - skipping client build."
    }
    else {
        Invoke-Step { npm --prefix $ClientDir run build } "client build"
    }

    Invoke-Step { npm --prefix $ClientDir run test } "client unit tests"
}

# Playwright reads e2e/.env.e2e. With E2E_NO_WEBSERVER=1 it tests the host in
# E2E_BASE_URL as-is; otherwise it starts the app itself (webServer: run.sh -b).
function Test-E2E {
    Write-Host "=== E2E tests ==="

    if (!(Test-Path (Join-Path $E2eDir ".env.e2e"))) {
        Write-Host "Missing $E2eDir\.env.e2e - copy .env.e2e.example and set E2E_BASE_URL + credentials."
        exit 1
    }

    Ensure-NodeModules $E2eDir

    Push-Location $E2eDir
    try {
        Invoke-Step { npx playwright install --no-shell chromium } "playwright install"
        Invoke-Step { npm run test } "e2e tests"
    }
    finally {
        Pop-Location
    }
}

function Test-Backend {
    Write-Host "=== Backend unit tests ==="

    Invoke-Step { dotnet clean $Solution } "dotnet clean"
    Invoke-Step { dotnet build $Solution } "dotnet build"
    Invoke-Step { dotnet test $Solution --no-build } "dotnet test"
}

# ---- Execution ----

if ($KillPort) {
    Free-Port
    exit
}

if ($TestFe -or $TestE2e -or $TestBe) {
    if ($TestFe) { Test-Frontend }
    if ($TestBe) { Test-Backend }
    if ($TestE2e) { Test-E2E }
    exit
}

if ($Dotnet.Count -gt 0) {
    dotnet @Dotnet
    exit
}

if ($Npm.Count -gt 0) {
    npm --prefix $ClientDir @Npm
    exit
}

if ($Backend) {
    Free-Port
    Restore-Dotnet
    Run-Backend
    exit
}

if ($Worker) {
    Restore-Dotnet
    Run-Worker
    exit
}

if ($Frontend) {
    npm --prefix $ClientDir install
    npm --prefix $ClientDir run dev
    exit
}

if ($All) {
    Free-Port
    Restore-Dotnet
    Build-Frontend

    Write-Host "Starting API + Worker..."

    $api = Start-Process powershell `
        -ArgumentList "-NoExit", "-Command", "dotnet run --project '$ApiProject'" `
        -PassThru

    $worker = Start-Process powershell `
        -ArgumentList "-NoExit", "-Command", "dotnet run --project '$WorkerProject'" `
        -PassThru

    Write-Host "API PID: $($api.Id)"
    Write-Host "Worker PID: $($worker.Id)"
    Write-Host "Press Enter to stop..."

    [void][Console]::ReadLine()

    try { Stop-Process $api.Id -Force } catch {}
    try { Stop-Process $worker.Id -Force } catch {}
}