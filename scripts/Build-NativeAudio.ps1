[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Godot)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdk = Join-Path $repository 'tmp/runtime-gate/native-audio-sdk'
$output = Join-Path $repository 'runtime/generated/native'
[IO.Directory]::CreateDirectory($sdk) | Out-Null
[IO.Directory]::CreateDirectory($output) | Out-Null
$source = Join-Path $repository 'runtime/native/audio/opennv_audio.cpp'
$floatSource = Join-Path $repository 'runtime/native/host/opennv_float_host.cpp'
$floatAssembly = Join-Path $repository 'runtime/native/host/opennv_float_host_amd64.asm'
$floatObject = Join-Path $sdk 'opennv_float_host.obj'
$floatAssemblyObject = Join-Path $sdk 'opennv_float_host_amd64.obj'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Native audio build requires the installed C++ build tools.' }
$environment = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
Push-Location $sdk
try {
    & $Godot --headless --dump-gdextension-interface --quit
    if ($LASTEXITCODE -ne 0) { throw 'Godot audio SDK export failed.' }
    $floatCommand = 'call "' + $environment + '" >nul && cl /nologo /std:c++17 /EHsc /O2 /MT /W4 /WX /c "' + $floatSource + '" /Fo"' + $floatObject + '" && ml64 /nologo /c /Fo"' + $floatAssemblyObject + '" "' + $floatAssembly + '"'
    & $env:ComSpec /d /s /c $floatCommand
    if ($LASTEXITCODE -ne 0) { throw 'Native calling-thread float host build failed.' }
    $command = 'call "' + $environment + '" >nul && cl /nologo /std:c++17 /EHsc /O2 /MT /W4 /WX /LD /I"' + $sdk + '" "' + $source + '" "' + $floatObject + '" "' + $floatAssemblyObject + '" /Fo"' + (Join-Path $sdk 'opennv_audio.obj') + '" /link /EXPORT:opennv_host_fistp32 /OUT:"' + (Join-Path $output 'opennv_audio.dll') + '" /IMPLIB:"' + (Join-Path $sdk 'opennv_audio.lib') + '"'
    & $env:ComSpec /d /s /c $command
    if ($LASTEXITCODE -ne 0) { throw 'Native audio build failed.' }
}
finally { Pop-Location }
