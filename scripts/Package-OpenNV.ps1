[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$Version,
    [string]$ExportDirectory = "",
    [switch]$AllowDirty
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ExportDirectory) { $ExportDirectory = Join-Path $repo 'tmp/development-runtime/windows' }
$export = (Resolve-Path -LiteralPath $ExportDirectory).Path
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE) { throw 'Cannot identify the source commit.' }
$dirty = @(& git -C $repo status --porcelain).Count -ne 0
if ($dirty -and !$AllowDirty) { throw 'Commit the tested source before packaging a public release.' }
$name = "OpenNV-$Version-windows-x64"
$output = Join-Path $repo "local/releases/$name"
if (Test-Path -LiteralPath $output) { throw "Package directory already exists: $output" }
$runtimeData = @(Get-ChildItem -LiteralPath $export -Directory -Filter 'data_OpenNV_windows_x86_64')
if ($runtimeData.Count -ne 1) { throw 'Expected exactly one official exported .NET runtime directory.' }
foreach ($file in @('OpenNV.exe', 'OpenNV.pck', 'runtime-manifest.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $export $file) -PathType Leaf)) { throw "Missing export: $file" }
}
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($file in @('OpenNV.exe', 'OpenNV.pck', 'runtime-manifest.json')) {
    Copy-Item -LiteralPath (Join-Path $export $file) -Destination $output
}
Copy-Item -LiteralPath $runtimeData[0].FullName -Destination $output -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'runtime/licenses') -Destination $output -Recurse
foreach ($file in @('NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $repo 'runtime/THIRD_PARTY.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $repo 'docs/playtest-candidate.md') -Destination (Join-Path $output 'PLAYTEST.md')
@('@echo off', 'cd /d "%~dp0"', 'start "OpenNV" "%~dp0OpenNV.exe" --xr-mode off -- --launcher %*') |
    Set-Content -LiteralPath (Join-Path $output 'OpenNV Flat.cmd') -Encoding ascii
@('@echo off', 'cd /d "%~dp0"', 'start "OpenNV VR" "%~dp0OpenNV.exe" --xr-mode on -- --launcher %*') |
    Set-Content -LiteralPath (Join-Path $output 'OpenNV VR.cmd') -Encoding ascii
$forbidden = @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object {
    $_.Extension -match '^\.(esm|esp|bsa|nif|dds|kf|lip|fuz|fos)$' -or $_.Name -match 'Fallout(NV|3)?\.exe|save\.json|courier.*\.json'
})
if ($forbidden.Count) { throw "Non-distributable input reached package: $($forbidden.Name -join ', ')" }
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
    @{ path=[IO.Path]::GetRelativePath($output, $_.FullName).Replace('\','/'); bytes=$_.Length;
       sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
@{ schema='opennv-release/v1'; version=$Version; commit=$commit; dirty=$dirty; experimental=$true;
   ownedDataIncluded=$false; physicalHeadsetAcceptance='pending-user-playtest'; files=$files } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8
$archive = "$output.zip"
Compress-Archive -LiteralPath $output -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Output "OPENNV_PACKAGE_READY archive=$archive sha256=$hash files=$($files.Count) dirty=$dirty"
