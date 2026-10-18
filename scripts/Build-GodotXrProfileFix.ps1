[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceRoot,
    [Parameter(Mandatory = $true)][string]$SCons,
    [Parameter(Mandatory = $true)][string]$Godot,
    [Parameter(Mandatory = $true)][string]$DependenciesRoot,
    [ValidateRange(1, 8)][int]$Jobs = 6
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'GodotToolchain.ps1')
$officialVersion = Assert-OpenNVGodotVersion -Executable $Godot
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$SCons = (Resolve-Path -LiteralPath $SCons).Path
$Godot = (Resolve-Path -LiteralPath $Godot).Path
$sconsVersion = (& $SCons --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify isolated SCons.' }
$DependenciesRoot = [IO.Path]::GetFullPath($DependenciesRoot)
$pin = 'c971f93e7e76b0ef919bf6009e7b868bea04db7f'
$head = & git -C $SourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $pin) { throw "Source checkout must be exactly $pin." }
$patch = Join-Path $repository 'patches/godot/interaction-profile-retention.patch'
$owner = 'modules/openxr/openxr_interface.cpp'
$modified = @(& git -C $SourceRoot status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source checkout.' }
if ($modified.Count -eq 0) {
    & git -C $SourceRoot apply --check --unidiff-zero $patch
    if ($LASTEXITCODE -ne 0) { throw 'Profile retention patch does not apply.' }
    & git -C $SourceRoot apply --unidiff-zero $patch
    if ($LASTEXITCODE -ne 0) { throw 'Profile retention patch failed.' }
} elseif ($modified.Count -ne 1 -or $modified[0] -ne " M $owner") {
    throw 'Source checkout contains unrelated changes; preserve them and use a separate checkout.'
}
# A reversible hunk alone would not reject extra edits elsewhere in this file.
$expectedDiff = (Get-Content -LiteralPath $patch -Raw).Replace("`r`n", "`n").TrimEnd()
$actualDiff = ((& git -C $SourceRoot diff --unified=0 -- $owner) -join "`n").TrimEnd()
if ($LASTEXITCODE -ne 0 -or $actualDiff -ne $expectedDiff) { throw 'Source diff is not the exact reviewed patch.' }

[IO.Directory]::CreateDirectory($DependenciesRoot) | Out-Null
$dependencies = @(
    @{ name = 'mesa'; url = 'https://github.com/godotengine/godot-nir-static/releases/download/25.3.1-3/godot-nir-static-x86_64-msvc-release.zip'; sha256 = 'F7796C97657FAB348774906DC938BB46FBFD1E45434244141FC8886E5E4B5530' },
    @{ name = 'agility'; url = 'https://www.nuget.org/api/v2/package/Microsoft.Direct3D.D3D12/1.618.5'; sha256 = '0027FC24F947C48DBDED13ADA7D280BE221EB651644E23A8A476F0F1F0A079DD' },
    @{ name = 'accesskit'; url = 'https://github.com/godotengine/godot-accesskit-c-static/releases/download/0.23.1/accesskit-c-0.23.1.zip'; sha256 = 'B9B42CCF2443C880AFDB69057347C0D359E10865E29C35BFF92EF7A4981EC3F7' }
)
foreach ($dependency in $dependencies) {
    $archive = Join-Path $DependenciesRoot ($dependency.name + '.zip')
    $directory = Join-Path $DependenciesRoot $dependency.name
    if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $dependency.url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dependency.sha256) {
        throw "Dependency digest differs: $($dependency.name)"
    }
    if (!(Test-Path -LiteralPath $directory)) { Expand-Archive -LiteralPath $archive -DestinationPath $directory }
}

$diagnostics = Join-Path $SourceRoot 'bin/opennv-profile-fix'
[IO.Directory]::CreateDirectory($diagnostics) | Out-Null
$manifestPath = Join-Path $diagnostics 'build.json'
$manifest = [ordered]@{
    schema = 'opennv-godot-profile-fix/1'; status = 'building'; source = $pin
    sourceUrl = 'https://github.com/godotengine/godot'; patchSha256 = (Get-FileHash -LiteralPath $patch).Hash
    officialGlueGenerator = $officialVersion; sconsVersion = $sconsVersion; dependencies = $dependencies
    productToolchainChanged = $false; distributionReady = $false; gameplayAccepted = $false; physicalXrAccepted = $false
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
$previousBuildName = $env:BUILD_NAME
$previousStatus = $env:GODOT_VERSION_STATUS
$buildArguments = @('platform=windows', 'arch=x86_64', 'target=template_release', 'module_mono_enabled=yes', 'd3d12=yes', 'angle=no',
    "mesa_libs=$DependenciesRoot/mesa", "agility_sdk_path=$DependenciesRoot/agility",
    "accesskit_sdk_path=$DependenciesRoot/accesskit/accesskit-c-0.23.1", "-j$Jobs")
Push-Location $SourceRoot
try {
    & $Godot --headless --generate-mono-glue (Join-Path $SourceRoot 'modules/mono/glue') --quit *> (Join-Path $diagnostics 'glue.log')
    if ($LASTEXITCODE -ne 0) { throw 'Exact-pin official Mono glue generation failed.' }
    $env:BUILD_NAME = 'opennv_xr_profile_fix1'
    $env:GODOT_VERSION_STATUS = 'dev7'
    & $SCons @buildArguments *> (Join-Path $diagnostics 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Patched template build failed; inspect build.log.' }
    $binary = Join-Path $SourceRoot 'bin/godot.windows.template_release.x86_64.mono.exe'
    $version = (& $binary --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or !$version.StartsWith('4.8.dev7.mono.opennv_xr_profile_fix1.')) {
        throw "Patched template identity differs: $version"
    }
    foreach ($license in @('LICENSE.txt', 'COPYRIGHT.txt', 'AUTHORS.md')) {
        Copy-Item -LiteralPath (Join-Path $SourceRoot $license) -Destination (Join-Path $diagnostics $license)
    }
    foreach ($dependency in $dependencies) {
        $directory = Join-Path $DependenciesRoot $dependency.name
        $licenseDirectory = Join-Path $diagnostics ('licenses/' + $dependency.name)
        [IO.Directory]::CreateDirectory($licenseDirectory) | Out-Null
        Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|COPYRIGHT|COPYING|NOTICE)' } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseDirectory $_.Name) }
    }
    $manifest.status = 'built'
    $manifest['arguments'] = $buildArguments
    $manifest['version'] = $version
    $manifest['binary'] = $binary
    $manifest['binarySha256'] = (Get-FileHash -LiteralPath $binary).Hash
    $manifest['completedUtc'] = [DateTime]::UtcNow.ToString('o')
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
    Write-Host "OPENNV_GODOT_PROFILE_FIX_BUILT version=$version manifest=$manifestPath"
} catch {
    $manifest.status = 'failed'
    $manifest['error'] = $_.Exception.Message
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath
    throw
} finally {
    Pop-Location
    $env:BUILD_NAME = $previousBuildName
    $env:GODOT_VERSION_STATUS = $previousStatus
}
