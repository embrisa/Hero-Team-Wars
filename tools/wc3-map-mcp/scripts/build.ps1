[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$mcpRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineRoot = Join-Path $mcpRoot "map-engine"
$serverRoot = Join-Path $mcpRoot "mcp-server"
$publishRoot = Join-Path $engineRoot "publish"
$jassApiData = Join-Path $engineRoot "data/jassdoc/jass-api.json"
if (-not (Test-Path -LiteralPath $jassApiData -PathType Leaf)) {
    throw "Canonical JASS API data is missing. Run scripts/sync-jassdoc.ps1 (or bootstrap.ps1) before building."
}

Push-Location $engineRoot
try {
    & dotnet build Wc3MapEngine.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw ".NET build failed." }
    & dotnet publish src/Wc3MapEngine.Cli/Wc3MapEngine.Cli.csproj --configuration Release --no-restore --output $publishRoot
    if ($LASTEXITCODE -ne 0) { throw ".NET engine publish failed." }
}
finally { Pop-Location }

Push-Location $serverRoot
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "MCP server build failed." }
}
finally { Pop-Location }

$engineExecutable = Join-Path $publishRoot "Wc3MapEngine.Cli.exe"
$serverEntry = Join-Path $serverRoot "dist/index.js"
if (-not (Test-Path -LiteralPath $engineExecutable)) { throw "Published engine was not found: $engineExecutable" }
if (-not (Test-Path -LiteralPath $serverEntry)) { throw "Built MCP server was not found: $serverEntry" }
Write-Host "Build completed."
