[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceMapPath,
    [Parameter(Mandatory)][string]$CanonicalPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateSet("debug", "release", "noop")][string]$BuildProfile = "debug"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "wc3-map-engine.ps1")
$result = Invoke-Wc3Engine "build_map" @{
    source_map_path = (Resolve-Wc3ProjectPath $SourceMapPath)
    canonical_path = (Resolve-Wc3ProjectPath $CanonicalPath)
    output_path = (Resolve-Wc3ProjectPath $OutputPath)
    profile = $BuildProfile
    validation_context = @{ project_id = "hero-team-wars" }
}
$result | ConvertTo-Json -Depth 100
