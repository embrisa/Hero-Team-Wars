[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$mcpRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineRoot = Join-Path $mcpRoot "map-engine"
$serverRoot = Join-Path $mcpRoot "mcp-server"
$jassApiData = Join-Path $engineRoot "data/jassdoc/jass-api.json"
$sourceMap = Join-Path $mcpRoot "../../map/HeroTeamWars_M0_2Arena.w3m"
$sourceHash = (Get-FileHash -LiteralPath $sourceMap -Algorithm SHA256).Hash
if (-not (Test-Path -LiteralPath $jassApiData -PathType Leaf)) {
    throw "Canonical JASS API data is missing. Run scripts/sync-jassdoc.ps1 (or bootstrap.ps1) before testing."
}

try {
    & node (Join-Path $PSScriptRoot "generate-v26-send-content.mjs") --check
    if ($LASTEXITCODE -ne 0) { throw "Send catalog generated files drifted." }
    & node --test (Join-Path $mcpRoot "tests/v26-preparation.test.mjs") (Join-Path $mcpRoot "tests/v28-dev-controls.test.mjs")
    if ($LASTEXITCODE -ne 0) { throw "JASS preparation/send/HUD/dev behavioral checks failed." }
    Push-Location $engineRoot
    try {
        & dotnet test Wc3MapEngine.sln --configuration Release --no-restore --logger "console;verbosity=minimal"
        if ($LASTEXITCODE -ne 0) { throw ".NET tests failed." }
        # MCP integration tests execute publish/, not the DLLs dotnet test built.
        # Publish these exact sources so old engine binaries cannot mask defects.
        & dotnet publish src/Wc3MapEngine.Cli/Wc3MapEngine.Cli.csproj --configuration Release --no-restore --output (Join-Path $engineRoot "publish")
        if ($LASTEXITCODE -ne 0) { throw ".NET engine publish for MCP tests failed." }
    }
    finally { Pop-Location }

    Push-Location $serverRoot
    try {
        & npm test -- --reporter=dot
        if ($LASTEXITCODE -ne 0) { throw "MCP server tests failed." }
    }
    finally { Pop-Location }
}
finally {
    if ((Get-FileHash -LiteralPath $sourceMap -Algorithm SHA256).Hash -ne $sourceHash) {
        throw "Source preservation failed: the immutable source map changed during automated tests."
    }
}

Write-Host "All automated tests passed."
