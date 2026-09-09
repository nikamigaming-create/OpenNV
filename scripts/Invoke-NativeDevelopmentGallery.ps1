[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$Godot,
    [int]$TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
$galleryRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$galleryManifest = (Resolve-Path -LiteralPath $Manifest).Path
$galleryData = (Resolve-Path -LiteralPath $DataRoot).Path
$galleryGodot = (Resolve-Path -LiteralPath $Godot).Path
$galleryOutput = [IO.Path]::GetFullPath($OutputRoot)
$galleryFfmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$galleryDll = Join-Path $galleryRepo 'runtime/.godot/mono/temp/bin/Debug/OpenNV.dll'
if (-not (Test-Path -LiteralPath $galleryDll)) { throw 'Build the Debug runtime before capture.' }
$galleryNewestSource = Get-ChildItem -LiteralPath (Join-Path $galleryRepo 'runtime/src') -Filter '*.cs' -File -Recurse |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($galleryNewestSource.LastWriteTimeUtc -gt (Get-Item -LiteralPath $galleryDll).LastWriteTimeUtc) { throw 'The Debug runtime is older than its C# sources.' }
if (Test-Path -LiteralPath $galleryOutput) { throw 'Gallery output must be fresh.' }
if (Test-Path -LiteralPath ($galleryOutput + '.godot.log')) { throw 'Gallery log already exists.' }
$galleryRequest = Get-Content -LiteralPath $galleryManifest -Raw | ConvertFrom-Json
if ($galleryRequest.schema -ne 'opennv-development-gallery/v1' -or $galleryRequest.subjects.Count -lt 1) { throw 'Invalid gallery manifest.' }
$galleryActive = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq 'FalloutNV' -or $_.ProcessName -like 'Godot*' -or $_.ProcessName -eq 'ffmpeg' }
if ($galleryActive) { throw ('Capture requires idle games and encoder: ' + (($galleryActive | ForEach-Object { "$($_.ProcessName):$($_.Id)" }) -join ', ')) }
# All checks above precede launch. This lane records only the current runtime;
# it never starts retail, sends input, changes source placements, or uses a
# converted scene. The runtime streams frames directly to bounded MP4 clips.
$galleryInfo = [Diagnostics.ProcessStartInfo]::new($galleryGodot)
$galleryInfo.UseShellExecute = $false
$galleryInfo.CreateNoWindow = $true
$galleryInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$galleryInfo.WorkingDirectory = $galleryRepo
$galleryInfo.Environment.Remove('OPENNV_LIVE_HARNESS_CHANNEL') | Out-Null
foreach ($galleryArg in @('--path', (Join-Path $galleryRepo 'runtime'), '--rendering-method', 'forward_plus', '--resolution', '1920x1080', '--windowed', '--log-file', ($galleryOutput + '.godot.log'), '--', '--data-root', $galleryData, '--campaign', 'fallout-new-vegas', '--save-path', ($galleryOutput + '.unused-save.json'), '--development-gallery', $galleryManifest, '--gallery-output', $galleryOutput, '--gallery-ffmpeg', $galleryFfmpeg)) {
    $galleryInfo.ArgumentList.Add($galleryArg)
}
$galleryProcess = [Diagnostics.Process]::Start($galleryInfo)
try {
    Write-Output "Current-runtime gallery started: PID $($galleryProcess.Id), $($galleryRequest.subjects.Count) requested entries."
    if (-not $galleryProcess.WaitForExit($TimeoutSeconds * 1000)) { throw 'Gallery exceeded its bounded capture timeout.' }
    if ($galleryProcess.ExitCode -ne 0) { throw "Gallery exited $($galleryProcess.ExitCode); inspect $galleryOutput.godot.log" }
    $galleryReport = Get-Content -LiteralPath (Join-Path $galleryOutput 'gallery-report.json') -Raw | ConvertFrom-Json
    if ($galleryReport.completedCount -ne $galleryRequest.subjects.Count) { throw 'Gallery did not account for every requested entry.' }
    $galleryReport.subjects | Group-Object status | Select-Object Name, Count
}
finally {
    if (-not $galleryProcess.HasExited) { $galleryProcess.Kill($true); $galleryProcess.WaitForExit() }
    $galleryProcess.Dispose()
}
