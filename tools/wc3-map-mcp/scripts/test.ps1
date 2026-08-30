[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$mcpRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineRoot = Join-Path $mcpRoot "map-engine"
$serverRoot = Join-Path $mcpRoot "mcp-server"

Push-Location $engineRoot
try {
    & dotnet test Wc3MapEngine.sln --configuration Release --no-restore --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { throw ".NET tests failed." }
}
finally { Pop-Location }

Push-Location $serverRoot
try {
    & npm test -- --run --reporter=dot
    if ($LASTEXITCODE -ne 0) { throw "MCP server tests failed." }
}
finally { Pop-Location }

Write-Host "All automated tests passed."
