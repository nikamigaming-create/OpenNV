[CmdletBinding()]
param(
    [string]$Godot = 'D:\code\fnvvr\local\Fo1in2-3D\toolchain\godot-4.7.2-mono\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [string]$TempleCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json",
    [string]$TempleTransitions = "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-transitions-v1.json",
    [string]$ArroyoCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-caves-v1\fo2-arroyo-caves-presentation-cache.json",
    [string]$PlayerCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\arroyo-player-v1\fo2-arroyo-player-presentation-cache.json",
    [string]$CharacterStartCache = "$env:LOCALAPPDATA\OpenNV\cache\fallout2\character-start-v2\fo2-character-start-cache.json",
    [string]$Save = "$env:LOCALAPPDATA\OpenNV\saves\fallout2\character-arroyo-v1.json"
)

$ErrorActionPreference = 'Stop'
$runtime = Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime'
foreach ($inputPath in @($Godot, $TempleCache, $TempleTransitions, $ArroyoCache, $PlayerCache, $CharacterStartCache)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Fallout 2 Arroyo interactive input is missing: $inputPath"
    }
}

& $Godot `
    --path $runtime `
    --windowed `
    --resolution 1280x720 `
    'res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn' `
    -- `
    --fo2-temple-cache $TempleCache `
    --fo2-temple-transitions $TempleTransitions `
    --fo2-arroyo-cache $ArroyoCache `
    --fo2-player-cache $PlayerCache `
    --fo2-character-start-cache $CharacterStartCache `
    --fo2-save $Save
if ($LASTEXITCODE -ne 0) {
    throw "Fallout 2 Arroyo interactive runtime failed with exit code $LASTEXITCODE."
}
