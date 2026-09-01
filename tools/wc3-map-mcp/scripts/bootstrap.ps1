[CmdletBinding()]
param(
    [switch] $CreateLocalConfig
)

$ErrorActionPreference = "Stop"
$mcpRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineRoot = Join-Path $mcpRoot "map-engine"
$serverRoot = Join-Path $mcpRoot "mcp-server"

function Require-Command([string] $Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. Install it from its official source, then rerun this script."
    }
}

Require-Command "dotnet"
Require-Command "node"
Require-Command "npm"
Require-Command "git"

$nodeVersion = (& node --version).Trim()
$npmVersion = (& npm --version).Trim()
$dotnetVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw "Runtime version detection failed." }

Write-Host "MCP root: $mcpRoot"
Write-Host "Node: $nodeVersion"
Write-Host "npm: $npmVersion"
Write-Host ".NET SDK: $dotnetVersion"

& (Join-Path $PSScriptRoot "sync-jassdoc.ps1")
if ($LASTEXITCODE -ne 0) { throw "Pinned jassdoc dataset generation failed." }

Push-Location $engineRoot
try {
    & dotnet restore Wc3MapEngine.sln
    if ($LASTEXITCODE -ne 0) { throw ".NET dependency restore failed." }
}
finally { Pop-Location }

Push-Location $serverRoot
try {
    & npm ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "MCP server dependency restore failed." }
}
finally { Pop-Location }

if ($CreateLocalConfig) {
    $example = Join-Path $mcpRoot "config/wc3-map-mcp.example.json"
    $local = Join-Path $mcpRoot "config/wc3-map-mcp.local.json"
    if (-not (Test-Path -LiteralPath $local)) {
        Copy-Item -LiteralPath $example -Destination $local
        Write-Host "Created $local"
    }
    else {
        Write-Host "Keeping existing local configuration: $local"
    }
}

Write-Host "Bootstrap completed. No machine-level prerequisites were installed."
