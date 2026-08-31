function Get-Wc3ProjectRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\")).Path
}

function Resolve-Wc3ProjectPath {
    param([Parameter(Mandatory)][string]$Path)
    $projectRoot = Get-Wc3ProjectRoot
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Path))
}

function Invoke-Wc3Engine {
    param(
        [Parameter(Mandatory)][string]$Operation,
        [Parameter(Mandatory)][hashtable]$Payload
    )

    $projectRoot = Get-Wc3ProjectRoot
    $engine = Join-Path $projectRoot "tools\wc3-map-mcp\map-engine\publish\Wc3MapEngine.Cli.exe"
    if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
        throw "Published map engine was not found: $engine. Run scripts\build.ps1 first."
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $engine
    $startInfo.Arguments = "--stdio"
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Unable to start the published map engine." }

    $request = @{ protocol_version = "1.0"; request_id = [guid]::NewGuid().ToString(); operation = $Operation; payload = $Payload }
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 100))
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Map engine exited with code $($process.ExitCode): $stderr" }

    $lines = @($stdout -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
    if ($lines.Count -ne 1) { throw "Map engine returned $($lines.Count) responses. stderr: $stderr" }
    $response = $lines[0] | ConvertFrom-Json
    if (-not $response.ok) { throw "$($response.error.code): $($response.error.message)" }
    return $response.result
}
