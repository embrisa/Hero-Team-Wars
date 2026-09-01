[CmdletBinding()]
param(
    [string] $SourceRoot,
    [string] $OutputPath = ""
)

$ErrorActionPreference = "Stop"
$mcpRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$importer = Join-Path $mcpRoot "scripts/import-jassdoc.mjs"
$canonicalRepository = "https://github.com/lep/jassdoc.git"
$canonicalRepositoryDisplay = "https://github.com/lep/jassdoc"
$pinnedCommit = "deddec452ec16ea355ca0aa47046b88d416dbc65"
$sourceFiles = @("common.j", "Blizzard.j", "builtin-types.j")

function Require-Command([string] $Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found. Install it from its official source, then rerun this script."
    }
}

function Invoke-Git([string[]] $Arguments) {
    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Require-Command "git"
Require-Command "node"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $mcpRoot "map-engine/data/jassdoc/jass-api.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$temporarySourceRoot = $null
try {
    if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
        $temporarySourceRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wc3-jassdoc-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $temporarySourceRoot -Force | Out-Null

        Invoke-Git @("clone", "--quiet", "--no-checkout", "--filter=blob:none", $canonicalRepository, $temporarySourceRoot)
        Invoke-Git @("-C", $temporarySourceRoot, "fetch", "--quiet", "--depth=1", "origin", $pinnedCommit)
        $resolvedCommit = (& git -C $temporarySourceRoot rev-parse --verify "$pinnedCommit^{commit}").Trim().ToLowerInvariant()
        if ($LASTEXITCODE -ne 0 -or $resolvedCommit -ne $pinnedCommit) {
            throw "The downloaded jassdoc checkout did not resolve to pinned commit $pinnedCommit (resolved '$resolvedCommit')."
        }
        Invoke-Git @("-C", $temporarySourceRoot, "checkout", "--quiet", $resolvedCommit, "--") + $sourceFiles
        $SourceRoot = $temporarySourceRoot
    }
    else {
        $SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
        $gitRoot = (& git -C $SourceRoot rev-parse --show-toplevel 2>$null).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
            throw "-SourceRoot must be a jassdoc Git checkout at the pinned commit; '$SourceRoot' is not inside a Git work tree."
        }
        $resolvedCommit = (& git -C $SourceRoot rev-parse --verify HEAD).Trim().ToLowerInvariant()
        if ($LASTEXITCODE -ne 0 -or $resolvedCommit -ne $pinnedCommit) {
            throw "-SourceRoot must resolve to pinned jassdoc commit $pinnedCommit; resolved '$resolvedCommit'."
        }
        & git -C $SourceRoot diff --quiet -- $sourceFiles
        if ($LASTEXITCODE -ne 0) {
            throw "-SourceRoot has uncommitted changes in one of the imported jassdoc files; use a clean checkout of the pinned commit."
        }
        & git -C $SourceRoot diff --cached --quiet -- $sourceFiles
        if ($LASTEXITCODE -ne 0) {
            throw "-SourceRoot has staged changes in one of the imported jassdoc files; use a clean checkout of the pinned commit."
        }
    }

    foreach ($sourceFile in $sourceFiles) {
        $sourcePath = Join-Path $SourceRoot $sourceFile
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required jassdoc source file is missing: $sourcePath"
        }
    }

    & node $importer --source-root $SourceRoot --output $OutputPath --source-commit $pinnedCommit --source-repository $canonicalRepositoryDisplay
    if ($LASTEXITCODE -ne 0) {
        throw "jassdoc import failed with exit code $LASTEXITCODE."
    }
    Write-Host "jassdoc sync completed from $canonicalRepositoryDisplay@$pinnedCommit"
    Write-Host "Generated local dataset: $OutputPath"
}
finally {
    if ($null -ne $temporarySourceRoot -and (Test-Path -LiteralPath $temporarySourceRoot)) {
        Remove-Item -LiteralPath $temporarySourceRoot -Recurse -Force
    }
}
