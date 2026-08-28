[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Godot,

    [string]$Profile = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
if ([string]::IsNullOrWhiteSpace($Profile)) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $Profile = Join-Path $localData "OpenNV\profiles\fallout3\vanilla\fallout3-profile.json"
}

foreach ($path in @($Godot, $Profile, (Join-Path $runtimeRoot "project.godot"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Fallout 3 profile test input: $path"
    }
}

$output = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
    --fo3-profile ([IO.Path]::GetFullPath($Profile)) --quit-after-load 2>&1
$text = $output | Out-String
$expectedContract =
    "OPENNV_FO3_BIRTH_CONTRACT_READY .*cell=00028138 .*playerSpawn=00039562 " +
    ".*doctor=000290a5 .*references=1610 .*models=299 rendered=0 interactive=0"
if ($LASTEXITCODE -ne 0 -or $text -notmatch $expectedContract) {
    throw "Fallout 3 owned birth-slice contract test failed:`n$text"
}

$text
