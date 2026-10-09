[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$Version,
    [string]$ExportDirectory = "",
    [string]$OutputRoot = "",
    [switch]$AllowDirty
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ExportDirectory) { $ExportDirectory = Join-Path $repo 'tmp/development-runtime/windows' }
$export = (Resolve-Path -LiteralPath $ExportDirectory).Path
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE) { throw 'Cannot identify the source commit.' }
& git -C $repo diff --quiet
if ($LASTEXITCODE -gt 1) { throw 'Cannot compare the working source tree.' }
$workingDirty = $LASTEXITCODE -ne 0
& git -C $repo diff --cached --quiet
if ($LASTEXITCODE -gt 1) { throw 'Cannot compare staged source changes.' }
$dirty = $workingDirty -or $LASTEXITCODE -ne 0 -or @(& git -C $repo ls-files --others --exclude-standard).Count -ne 0
if ($dirty -and !$AllowDirty) { throw 'Commit the tested source before packaging a public release.' }
$name = "OpenNV-$Version-windows-x64"
if (!$OutputRoot) { $OutputRoot = Join-Path $repo 'local/releases' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
$output = Join-Path $OutputRoot $name
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath ($output + '.zip'))) {
    throw "Package already exists: $output"
}
$runtimeData = @(Get-ChildItem -LiteralPath $export -Directory -Filter 'data_OpenNV_windows_x86_64')
if ($runtimeData.Count -ne 1) { throw 'Expected exactly one official exported .NET runtime directory.' }
foreach ($file in @('OpenNV.exe', 'OpenNV.pck', 'runtime-manifest.json', 'opennv_audio.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $export $file) -PathType Leaf)) { throw "Missing export: $file" }
}
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($file in @('OpenNV.exe', 'OpenNV.pck', 'runtime-manifest.json', 'opennv_audio.dll')) {
    Copy-Item -LiteralPath (Join-Path $export $file) -Destination $output
}
foreach ($file in Get-ChildItem -LiteralPath $runtimeData[0].FullName -Recurse -File) {
    # Export/publish can retain temporary replacement files beside the real
    # runtime libraries. They are build residue, not runtime dependencies.
    if ($file.Extension -ieq '.tmp') { continue }
    $relative = [IO.Path]::GetRelativePath($export, $file.FullName)
    $destination = Join-Path $output $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
$domain = Join-Path $repo 'runtime/generated/native-plugins/Release/opennv_plugin_domain.exe'
if (!(Test-Path -LiteralPath $domain -PathType Leaf)) { throw 'Build the first-party Release native plugin domain before packaging.' }
$domainOutput = Join-Path $output 'generated/native-plugins/Release'
[IO.Directory]::CreateDirectory($domainOutput) | Out-Null
Copy-Item -LiteralPath $domain -Destination $domainOutput
$toolchainPath = Join-Path $export 'build-toolchain.json'
if (Test-Path -LiteralPath $toolchainPath -PathType Leaf) {
    $toolchain = Get-Content -LiteralPath $toolchainPath -Raw | ConvertFrom-Json
    if ($toolchain.schema -ne 'opennv-build-toolchain/v1' -or $toolchain.sourceCommit -ne $commit -or
        $toolchain.sourceTree -ne (& git -C $repo rev-parse 'HEAD^{tree}').Trim()) {
        throw 'Export toolchain metadata does not identify the packaged source.'
    }
    Copy-Item -LiteralPath $toolchainPath -Destination $output
}
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
