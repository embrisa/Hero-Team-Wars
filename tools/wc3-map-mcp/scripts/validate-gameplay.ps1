[CmdletBinding()]
param(
    [string]$ManifestPath = "tools/wc3-map-mcp/scripts/mcp/manifest.json",
    [ValidateSet("mvp_2arena", "full_6team", "gui_compatible")][string]$Profile = "mvp_2arena"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "wc3-map-engine.ps1")
$result = Invoke-Wc3Engine "validate_gameplay_source" @{ manifest_path = (Resolve-Wc3ProjectPath $ManifestPath); profile = $Profile }
$result | ConvertTo-Json -Depth 100
