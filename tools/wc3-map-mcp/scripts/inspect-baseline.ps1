[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$mcpRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectRoot = (Resolve-Path (Join-Path $mcpRoot "..\..")).Path
$sourcePath = Join-Path $projectRoot "map/HeroTeamWars_M0_2Arena.w3m"
$enginePath = Join-Path $mcpRoot "map-engine/publish/Wc3MapEngine.Cli.exe"
$compatibilityRoot = Join-Path $mcpRoot "docs/compatibility"
$candidateRoot = Join-Path $mcpRoot "artifacts/hero-team-wars"
$buildRoot = Join-Path $mcpRoot "builds/mcp/phase0"
$tempRoot = Join-Path $mcpRoot "snapshots/baseline-probe"

foreach ($directory in @($compatibilityRoot, $candidateRoot, $buildRoot, $tempRoot)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $enginePath)) {
    throw "Published engine was not found. Run scripts/build.ps1 first: $enginePath"
}
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Source map was not found: $sourcePath"
}

function Get-FileEvidence([string] $Path) {
    $item = Get-Item -LiteralPath $Path
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    return [ordered]@{
        path = $Path
        size_bytes = $item.Length
        modified_utc = $item.LastWriteTimeUtc.ToString("o")
        sha256 = $hash
    }
}

function Write-JsonAtomic([string] $Path, [object] $Value) {
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporary = "$Path.$([guid]::NewGuid().ToString()).tmp"
    try {
        $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
        Get-Content -LiteralPath $temporary -Raw | ConvertFrom-Json -Depth 100 | Out-Null
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Invoke-Engine([hashtable] $Request) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $enginePath
    $startInfo.WorkingDirectory = $mcpRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add("--stdio")
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Could not start the map engine." }
    $process.StandardInput.WriteLine(($Request | ConvertTo-Json -Depth 100 -Compress))
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Map engine failed ($($process.ExitCode)): $stderr" }
    $lines = @($stdout -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -ne 1) { throw "Map engine returned $($lines.Count) protocol lines. stderr: $stderr" }
    $response = $lines[0] | ConvertFrom-Json -Depth 100
    if ($response.ok -ne $true) {
        $errorText = $response.error | ConvertTo-Json -Depth 20 -Compress
        throw "Map engine operation '$($Request.operation)' failed: $errorText"
    }
    return $response.result
}

$before = Get-FileEvidence $sourcePath
$requestId = [guid]::NewGuid().ToString()
$environment = Invoke-Engine @{ protocol_version = "1.0"; request_id = $requestId; operation = "environment_status"; payload = @{} }
$inspection = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "inspect_map"; payload = @{ map_path = $sourcePath } }
$archive = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "list_archive_members"; payload = @{ map_path = $sourcePath } }
$probe = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "probe_map"; payload = @{ map_path = $sourcePath } }

$canonicalPath = Join-Path $tempRoot "canonical-map.json"
Write-JsonAtomic $canonicalPath $inspection
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$noopPath = Join-Path $buildRoot "HeroTeamWars_M0_2Arena_MCP_P0_noop_$timestamp.w3m"
$build = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "build_map"; payload = @{ source_map_path = $sourcePath; canonical_path = $canonicalPath; output_path = $noopPath; profile = "phase0-noop" } }
$rebuiltInspection = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "inspect_map"; payload = @{ map_path = $noopPath } }

$after = Get-FileEvidence $sourcePath
if ($before.sha256 -ne $after.sha256 -or $before.size_bytes -ne $after.size_bytes) {
    throw "Source map changed during the read-only baseline/no-op probe. Before=$($before.sha256) After=$($after.sha256)"
}

$sourceMembers = @($inspection.archive_members)
$rebuiltMembers = @($rebuiltInspection.archive_members)
$sourceMemberMap = @{}
foreach ($member in $sourceMembers) { $sourceMemberMap[$member.path] = $member.sha256 }
$rebuiltMemberMap = @{}
foreach ($member in $rebuiltMembers) { $rebuiltMemberMap[$member.path] = $member.sha256 }
$memberDifferences = @(
    foreach ($path in @($sourceMemberMap.Keys | Sort-Object)) {
        if (-not $rebuiltMemberMap.ContainsKey($path)) { [ordered]@{ path = $path; difference = "missing_from_rebuild" } }
        elseif ($sourceMemberMap[$path] -ne $rebuiltMemberMap[$path]) { [ordered]@{ path = $path; difference = "content_hash_changed"; source_sha256 = $sourceMemberMap[$path]; rebuilt_sha256 = $rebuiltMemberMap[$path] } }
    }
    foreach ($path in @($rebuiltMemberMap.Keys | Where-Object { -not $sourceMemberMap.ContainsKey($_) } | Sort-Object)) {
        [ordered]@{ path = $path; difference = "added_to_rebuild" }
    }
)

$report = [ordered]@{
    schema_version = "1.0"
    generated_utc = (Get-Date).ToUniversalTime().ToString("o")
    source = $before
    source_after_probe = $after
    environment = $environment
    archive_members = $archive.members
    parser_probe = $probe.capabilities
    canonical_summary = [ordered]@{
        metadata = $inspection.metadata
        players = $inspection.players
        forces = $inspection.forces
        regions = $inspection.regions
        triggers = $inspection.triggers
        variables = $inspection.variables
        object_data = $inspection.object_data
        placed_objects = $inspection.placed_objects
        terrain_summary = $inspection.terrain_summary
        imports = $inspection.imports
        capabilities = $inspection.capabilities
        opaque_members = $inspection.opaque_members
        parse_warnings = $inspection.parse_warnings
    }
    no_op_rebuild = [ordered]@{
        attempted = $true
        output = Get-FileEvidence $noopPath
        engine_result = $build
        rebuilt_source_semantics = $rebuiltInspection
        member_differences = $memberDifferences
        editor_observed = $false
        game_observed = $false
        evidence_level = "built_reopened_by_engine_only"
        note = "World Editor and Warcraft III were not launched by this unattended probe; process/build compatibility is not runtime evidence."
    }
    phase_0_recommendation = if ($memberDifferences.Count -eq 0) { "GO_READONLY_AND_NOOP_BUILD_WITH_MANUAL_RUNTIME_GATE" } else { "GO_READONLY_ONLY_UNTIL_MEMBER_PRESERVATION_IS_RESOLVED" }
}

$reportJsonPath = Join-Path $compatibilityRoot "hero-team-wars-baseline.json"
$reportMarkdownPath = Join-Path $compatibilityRoot "hero-team-wars-baseline.md"
$candidateJsonPath = Join-Path $candidateRoot "htw-00-candidate.json"
$candidateMarkdownPath = Join-Path $candidateRoot "htw-00-candidate.md"
Write-JsonAtomic $reportJsonPath $report
Write-JsonAtomic $candidateJsonPath ([ordered]@{
    schema_version = "1.0"
    report_type = "HTW-00 STATE REPORT"
    generated_utc = $report.generated_utc
    source = $before
    archive_observations = $report.archive_members
    editor_verification_needed = @("open the separately named no-op build in World Editor", "confirm trigger tree, variables, custom object data, and placed content", "save only a copy if save-round-trip is tested")
    game_verification_needed = @("load the exact no-op build in Warcraft III", "record map-load result against the output SHA-256")
    ledger_conflicts = @("No automatic edit was made to design/07-editor-state.yaml.", "Archive inspection cannot prove visual editor state or gameplay behavior.")
    unsupported_or_absent = @($inspection.opaque_members)
    observed = [ordered]@{ metadata = $inspection.metadata; players = $inspection.players; forces = $inspection.forces; regions = $inspection.regions; triggers = $inspection.triggers; variables = $inspection.variables; object_data = $inspection.object_data; placed_objects = $inspection.placed_objects; terrain_summary = $inspection.terrain_summary; imports = $inspection.imports }
    no_op_build = $report.no_op_rebuild
})

$memberStatus = if ($memberDifferences.Count -eq 0) { "PASS" } else { "DIFFERENCES FOUND" }
$markdown = @(
    "# Hero Team Wars Compatibility Baseline"
    ""
    "Generated UTC: $($report.generated_utc)"
    ""
    "## Source identity"
    ""
    "- Path: ``$($before.path)``"
    "- Size: ``$($before.size_bytes)`` bytes"
    "- Modified UTC: ``$($before.modified_utc)``"
    "- SHA-256: ``$($before.sha256)``"
    "- Source hash after probe: ``$($after.sha256)``"
    ""
    "## Environment"
    ""
    "````json"
    ($environment | ConvertTo-Json -Depth 20)
    "````"
    ""
    "## Archive and parser coverage"
    ""
    "- Archive members observed: $($sourceMembers.Count)"
    "- Parser probe entries: $(@($probe.capabilities).Count)"
    "- Rebuilt member content-hash comparison: **$memberStatus**"
    ""
    "The JSON report contains the complete stable member inventory, parser capability matrix, canonical metadata, players, forces, regions, and unknown/opaque classifications."
    ""
    "## No-op rebuild"
    ""
    "- Output: ``$($noopPath)``"
    "- Output SHA-256: ``$($report.no_op_rebuild.output.sha256)``"
    "- Reopened and re-inspected by the engine: ``$($report.no_op_rebuild.engine_result.reopened)``"
    "- Editor observed: ``false``"
    "- Warcraft III observed: ``false``"
    "- Evidence level: ``built_reopened_by_engine_only``"
    ""
    "This probe did not claim World Editor or game compatibility. Those are explicit manual gates for the exact output above."
    ""
    "## HTW-00 state report"
    ""
    "The candidate report is at ``artifacts/hero-team-wars/htw-00-candidate.json`` and ``.md``. It separates archive observations, editor/game verification needs, ledger conflicts, and unsupported or absent values. ``design/07-editor-state.yaml`` was not modified automatically."
    ""
    "## Recommendation"
    ""
    "``$($report.phase_0_recommendation)``"
) -join [Environment]::NewLine
$opaqueLines = @($inspection.opaque_members | ForEach-Object { "- ``$($_.path)`` ($($_.size_bytes) bytes, SHA-256 ``$($_.sha256)``)" })
$candidateMarkdown = @(
    "# HTW-00 STATE REPORT"
    ""
    "## Archive observations"
    ""
    "- Source SHA-256: ``$($before.sha256)``"
    "- Archive members: $($sourceMembers.Count)"
    "- Parser probe entries: $(@($probe.capabilities).Count)"
    "- Parsed members: ``war3map.w3i``, ``war3map.w3r``, and ``war3map.wts``"
    "- Opaque members preserved through no-op rebuild: $(@($inspection.opaque_members).Count)"
    "- No-op member content-hash differences: $($memberDifferences.Count)"
    ""
    "## Editor verification needs"
    ""
    "- Open the exact no-op output at ``$($noopPath)`` in World Editor."
    "- Confirm trigger tree, variables, custom object data, placed content, and any visual/editor-only state."
    "- If save-round-trip is tested, save only a copy and record the reopened copy hash."
    ""
    "## Ledger conflicts"
    ""
    "- ``design/07-editor-state.yaml`` was not modified automatically."
    "- Archive inspection cannot prove visual state, trigger-tree semantics, or gameplay behavior."
    "- The observed script language is JASS; script mutation remains disabled by ADR 0002."
    ""
    "## Unsupported/absent values"
    ""
    $opaqueLines
    "- Trigger variable details, semantic object data, placed-object details, terrain grid details, and imports are not decoded by this release."
    ""
    "## No-op build and game verification"
    ""
    "- Output SHA-256: ``$($report.no_op_rebuild.output.sha256)``"
    "- Engine reopened and re-inspected: ``$($report.no_op_rebuild.engine_result.reopened)``"
    "- World Editor observed: ``false``"
    "- Warcraft III observed: ``false``"
    "- Evidence level: ``built_reopened_by_engine_only``"
    ""
    "## Recommendation"
    ""
    "``$($report.phase_0_recommendation)``"
) -join [Environment]::NewLine
Set-Content -LiteralPath $reportMarkdownPath -Value $markdown -Encoding utf8NoBOM
Set-Content -LiteralPath $candidateMarkdownPath -Value $candidateMarkdown -Encoding utf8NoBOM

Write-Host "Baseline report: $reportJsonPath"
Write-Host "Compatibility summary: $reportMarkdownPath"
Write-Host "HTW-00 candidate: $candidateJsonPath"
Write-Host "No-op build: $noopPath"
Write-Host "Original source hash verified unchanged: $($after.sha256)"
