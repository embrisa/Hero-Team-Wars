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

function Get-ToolEvidence([string] $Command, [string[]] $Arguments) {
    $commandInfo = Get-Command $Command -ErrorAction Stop
    $version = (& $commandInfo.Source @Arguments).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not read $Command version." }
    return [ordered]@{
        path = $commandInfo.Source
        version = $version
    }
}

$exampleConfigPath = Join-Path $mcpRoot "config/wc3-map-mcp.example.json"
$exampleConfig = Get-Content -LiteralPath $exampleConfigPath -Raw | ConvertFrom-Json -Depth 20
$heroTeamWarsConfig = $exampleConfig.projects.'hero-team-wars'
$worldEditorPath = [string]$heroTeamWarsConfig.world_editor
$warcraftPath = [string]$heroTeamWarsConfig.warcraft
$testMapRoot = [string]$heroTeamWarsConfig.test_map_root
$configuredFiles = [ordered]@{
    source_map = $sourcePath
    world_editor = $worldEditorPath
    warcraft = $warcraftPath
    test_map_root = $testMapRoot
}

$before = Get-FileEvidence $sourcePath
$requestId = [guid]::NewGuid().ToString()
$environment = Invoke-Engine @{ protocol_version = "1.0"; request_id = $requestId; operation = "environment_status"; payload = @{ configured_files = $configuredFiles } }
$tooling = [ordered]@{
    node = Get-ToolEvidence "node" @("--version")
    npm = Get-ToolEvidence "npm" @("--version")
    dotnet = Get-ToolEvidence "dotnet" @("--version")
    dotnet_info = (& dotnet --info | Out-String).Trim()
}
$editorGameEvidence = [ordered]@{
    world_editor = $environment.configured_files.world_editor
    warcraft = $environment.configured_files.warcraft
    test_map_root = $environment.configured_files.test_map_root
}
$inspection = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "inspect_map"; payload = @{ map_path = $sourcePath } }
$archive = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "list_archive_members"; payload = @{ map_path = $sourcePath } }
$probe = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "probe_map"; payload = @{ map_path = $sourcePath } }
$blockingCapabilities = @($probe.capabilities | Where-Object { $_.status -eq "unsupported_blocking" })

$canonicalPath = Join-Path $tempRoot "canonical-map.json"
Write-JsonAtomic $canonicalPath $inspection
$noopAttempted = $blockingCapabilities.Count -eq 0
$noopPath = $null
$build = $null
$rebuiltInspection = $null
$memberDifferences = @()
if ($noopAttempted) {
    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ")
    $noopPath = Join-Path $buildRoot "HeroTeamWars_M0_2Arena_MCP_P0_noop_$timestamp.w3m"
    $build = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "build_map"; payload = @{ source_map_path = $sourcePath; canonical_path = $canonicalPath; output_path = $noopPath; profile = "phase0-noop" } }
    $rebuiltInspection = Invoke-Engine @{ protocol_version = "1.0"; request_id = ([guid]::NewGuid().ToString()); operation = "inspect_map"; payload = @{ map_path = $noopPath } }
}

$after = Get-FileEvidence $sourcePath
if ($before.sha256 -ne $after.sha256 -or $before.size_bytes -ne $after.size_bytes) {
    throw "Source map changed during the read-only baseline/no-op probe. Before=$($before.sha256) After=$($after.sha256)"
}

$sourceMembers = @($inspection.archive_members)
$archiveComparison = [ordered]@{
    attempted = $noopAttempted
    source_member_order = @($sourceMembers | ForEach-Object { $_.path })
    rebuilt_member_order = @()
    member_order_changed = $null
    compression_metadata_changes = @()
    special_member_checks = @()
}
if ($noopAttempted) {
    $rebuiltMembers = @($rebuiltInspection.archive_members)
    $sourceMemberMap = @{}
    $sourceMemberByPath = @{}
    foreach ($member in $sourceMembers) {
        $sourceMemberMap[$member.path] = $member.sha256
        $sourceMemberByPath[$member.path] = $member
    }
    $rebuiltMemberMap = @{}
    $rebuiltMemberByPath = @{}
    foreach ($member in $rebuiltMembers) {
        $rebuiltMemberMap[$member.path] = $member.sha256
        $rebuiltMemberByPath[$member.path] = $member
    }
    $rebuiltOrder = @($rebuiltMembers | ForEach-Object { $_.path })
    $archiveComparison.rebuilt_member_order = $rebuiltOrder
    if ($archiveComparison.source_member_order.Count -ne $rebuiltOrder.Count) {
        $archiveComparison.member_order_changed = $true
    }
    else {
        $orderChanged = $false
        for ($index = 0; $index -lt $archiveComparison.source_member_order.Count; $index++) {
            if ($archiveComparison.source_member_order[$index] -ne $rebuiltOrder[$index]) {
                $orderChanged = $true
                break
            }
        }
        $archiveComparison.member_order_changed = $orderChanged
    }

    $archiveComparison.compression_metadata_changes = @(
        foreach ($path in @($sourceMemberByPath.Keys + $rebuiltMemberByPath.Keys | Sort-Object -Unique)) {
            if ($sourceMemberByPath.ContainsKey($path) -and $rebuiltMemberByPath.ContainsKey($path)) {
                $sourceMember = $sourceMemberByPath[$path]
                $rebuiltMember = $rebuiltMemberByPath[$path]
                if ($sourceMember.compressed_size_bytes -ne $rebuiltMember.compressed_size_bytes -or $sourceMember.flags -ne $rebuiltMember.flags) {
                    [ordered]@{
                        path = $path
                        source_compressed_size_bytes = $sourceMember.compressed_size_bytes
                        rebuilt_compressed_size_bytes = $rebuiltMember.compressed_size_bytes
                        source_flags = $sourceMember.flags
                        rebuilt_flags = $rebuiltMember.flags
                    }
                }
            }
        }
    )

    $archiveComparison.special_member_checks = @(
        foreach ($path in @("(listfile)", "(attributes)")) {
            $sourceMember = $sourceMemberByPath[$path]
            $rebuiltMember = $rebuiltMemberByPath[$path]
            [ordered]@{
                path = $path
                present_in_source = $null -ne $sourceMember
                present_in_rebuild = $null -ne $rebuiltMember
                source_sha256 = if ($null -ne $sourceMember) { $sourceMember.sha256 } else { $null }
                rebuilt_sha256 = if ($null -ne $rebuiltMember) { $rebuiltMember.sha256 } else { $null }
                content_hash_equal = $null -ne $sourceMember -and $null -ne $rebuiltMember -and $sourceMember.sha256 -eq $rebuiltMember.sha256
            }
        }
    )
    $memberDifferences = @(
        foreach ($path in @($sourceMemberMap.Keys | Sort-Object)) {
            if (-not $rebuiltMemberMap.ContainsKey($path)) { [ordered]@{ path = $path; difference = "missing_from_rebuild" } }
            elseif ($sourceMemberMap[$path] -ne $rebuiltMemberMap[$path]) { [ordered]@{ path = $path; difference = "content_hash_changed"; source_sha256 = $sourceMemberMap[$path]; rebuilt_sha256 = $rebuiltMemberMap[$path] } }
        }
        foreach ($path in @($rebuiltMemberMap.Keys | Where-Object { -not $sourceMemberMap.ContainsKey($_) } | Sort-Object)) {
            [ordered]@{ path = $path; difference = "added_to_rebuild" }
        }
    )
}

$report = [ordered]@{
    schema_version = "1.0"
    generated_utc = (Get-Date).ToUniversalTime().ToString("o")
    source = $before
    source_after_probe = $after
    environment = $environment
    tooling = $tooling
    editor_game = $editorGameEvidence
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
        attempted = $noopAttempted
        output = if ($noopAttempted) { Get-FileEvidence $noopPath } else { $null }
        engine_result = $build
        rebuilt_source_semantics = $rebuiltInspection
        member_differences = $memberDifferences
        archive_comparison = $archiveComparison
        editor_observed = $false
        game_observed = $false
        evidence_level = if ($noopAttempted) { "built_reopened_by_engine_only" } else { "blocked_by_unsupported_member" }
        note = if ($noopAttempted) { "World Editor and Warcraft III were not launched by this unattended probe; process/build compatibility is not runtime evidence." } else { "No-op rebuild was not attempted because at least one member was classified unsupported_blocking." }
    }
    blocking_capabilities = $blockingCapabilities
    phase_0_recommendation = if ($memberDifferences.Count -eq 0 -and $blockingCapabilities.Count -eq 0) { "GO_READONLY_AND_NOOP_BUILD_WITH_MANUAL_RUNTIME_GATE" } else { "GO_READONLY_ONLY_UNTIL_MEMBER_PRESERVATION_IS_RESOLVED" }
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
    editor_version = $environment.configured_files.world_editor
    map_copy = [ordered]@{ source = $before; no_op_output = $report.no_op_rebuild.output }
    map_properties = $inspection.metadata
    trigger_tree = $inspection.triggers
    variables = $inspection.variables
    regions = $inspection.regions
    player_slots_and_forces = [ordered]@{ players = $inspection.players; forces = $inspection.forces }
    heroes_units_abilities_items = [ordered]@{ object_data = $inspection.object_data; placed_objects = $inspection.placed_objects; capability = "unsupported_or_absent" }
    test_result = [ordered]@{ engine_reopened = $report.no_op_rebuild.engine_result.reopened; world_editor_observed = $false; warcraft_observed = $false; evidence_level = "built_reopened_by_engine_only" }
    unknown_or_unclear = @("trigger tree semantics", "trigger variables", "custom object data", "placed object details", "terrain grid details", "runtime behavior")
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
    '```json'
    ($environment | ConvertTo-Json -Depth 20)
    '```'
    ""
    "## Tooling and configured paths"
    ""
    '```json'
    ([ordered]@{ tooling = $tooling; editor_game = $editorGameEvidence } | ConvertTo-Json -Depth 20)
    '```'
    ""
    "## Archive and parser coverage"
    ""
    "- Archive members observed: $($sourceMembers.Count)"
    "- Parser probe entries: $(@($probe.capabilities).Count)"
    "- Rebuilt member content-hash comparison: **$memberStatus**"
    "- Archive member order changed: ``$($report.no_op_rebuild.archive_comparison.member_order_changed)``"
    "- Compression/flag metadata changes: $(@($report.no_op_rebuild.archive_comparison.compression_metadata_changes).Count)"
    "- Listfile/attributes content hashes equal: $(@($report.no_op_rebuild.archive_comparison.special_member_checks | Where-Object { $_.content_hash_equal }).Count)/$(@($report.no_op_rebuild.archive_comparison.special_member_checks).Count)"
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
    "## editor_version"
    ""
    "- World Editor: ``$($environment.configured_files.world_editor.product_version)`` at ``$($environment.configured_files.world_editor.path)`` (archive report only; no Phase 1 launch)."
    ""
    "## map_copy"
    ""
    "- Source: ``$($before.path)`` (SHA-256 ``$($before.sha256)``)."
    "- Existing Phase 0 no-op copy: ``$($noopPath)`` (SHA-256 ``$($report.no_op_rebuild.output.sha256)``)."
    ""
    "## map_properties"
    ""
    "- Parsed read-only from ``war3map.w3i``; see the JSON candidate for stored values, resolved WTS text, capability, and provenance."
    ""
    "## trigger_tree"
    ""
    "- ``war3map.j`` is the MCP-owned JASS entry point; ``war3map.wtg`` and ``war3map.wct`` remain preserved opaque and semantic trigger-tree verification requires World Editor."
    ""
    "## variables"
    ""
    "- Unknown: trigger variable details are not semantically decoded by the Phase 0 parser."
    ""
    "## regions"
    ""
    "- $(@($inspection.regions).Count) regions parsed read-only from ``war3map.w3r``; exact names and bounds are in the JSON candidate."
    ""
    "## player_slots_and_forces"
    ""
    "- $(@($inspection.players).Count) player slots and $(@($inspection.forces).Count) forces parsed read-only from ``war3map.w3i``."
    ""
    "## heroes_units_abilities_items"
    ""
    "- Unsupported/absent semantically; opaque object and placement members are reported only by size and SHA-256."
    ""
    "## test_result"
    ""
    "- Phase 0 engine reopen passed; World Editor and Warcraft III were not observed. Evidence level: ``built_reopened_by_engine_only``."
    ""
    "## unknown_or_unclear"
    ""
    "- Trigger semantics, variables, custom object data, placed-object details, terrain details, imports, and gameplay behavior remain unknown or unsupported."
    ""
    "## Archive observations"
    ""
    "- Source SHA-256: ``$($before.sha256)``"
    "- Archive members: $($sourceMembers.Count)"
    "- Parser probe entries: $(@($probe.capabilities).Count)"
    "- Parsed members: ``war3map.w3i``, ``war3map.w3r``, and ``war3map.wts``"
    "- Opaque members preserved through no-op rebuild: $(@($inspection.opaque_members).Count)"
    "- No-op member content-hash differences: $($memberDifferences.Count)"
    "- Archive member order changed: ``$($report.no_op_rebuild.archive_comparison.member_order_changed)``"
    "- Compression/flag metadata changes: $(@($report.no_op_rebuild.archive_comparison.compression_metadata_changes).Count)"
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
    "- The observed script language is JASS; MCP-owned source mutation is available only when the writes-enabled local configuration sets ``script_policy`` to ``mcp_owned_jass``."
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
