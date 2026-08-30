[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceMapPath,
    [Parameter(Mandatory)][string]$CanonicalPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateSet("mvp_2arena", "full_6team")][string]$Profile = "mvp_2arena",
    [Parameter(Mandatory)][ValidatePattern("^HTW-[0-9]{2}$")][string]$ChunkId,
    [string[]]$ScenarioIds = @()
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "wc3-map-engine.ps1")
$build = Invoke-Wc3Engine "build_map" @{
    source_map_path = (Resolve-Wc3ProjectPath $SourceMapPath)
    canonical_path = (Resolve-Wc3ProjectPath $CanonicalPath)
    output_path = (Resolve-Wc3ProjectPath $OutputPath)
    profile = "debug"
    validation_context = @{ project_id = "hero-team-wars"; profile = $Profile }
}
$scenarioPayload = @{ profile = $Profile; chunk_id = $ChunkId }
if ($ScenarioIds.Count -gt 0) { $scenarioPayload.scenario_ids = $ScenarioIds }
$scenarios = Invoke-Wc3Engine "run_scenario" $scenarioPayload
@{ build = $build; scenarios = $scenarios } | ConvertTo-Json -Depth 100
