[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$generated = Join-Path $repository 'runtime/generated/native-plugins'
[IO.Directory]::CreateDirectory($generated) | Out-Null
# Native compiler objects are not Wavefront meshes or Godot import resources.
[IO.File]::WriteAllText((Join-Path $generated '.gdignore'), '')
$output = Join-Path $generated $Configuration
$fixtures = Join-Path $output 'fixtures'
$missingImport = Join-Path $output 'missing-import'
$protocolFixtures = Join-Path $output 'protocol-fixtures'
foreach ($directory in @($output, $fixtures, $missingImport, $protocolFixtures)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'The plugin execution-domain proposal requires installed MSVC x86 build tools.' }
$environment = Join-Path $installation 'VC/Auxiliary/Build/vcvarsall.bat'
$environmentLines = & $env:ComSpec /d /s /c ('call "' + $environment + '" x86 >nul && set')
if ($LASTEXITCODE -ne 0) { throw 'MSVC x86 environment initialization failed.' }
$previousEnvironment = @{}
foreach ($line in $environmentLines) {
    $equals = $line.IndexOf('=')
    if ($equals -gt 0) {
        $name = $line.Substring(0, $equals)
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
}
try {
foreach ($line in $environmentLines) {
    $equals = $line.IndexOf('=')
    if ($equals -gt 0) { [Environment]::SetEnvironmentVariable($line.Substring(0, $equals), $line.Substring($equals + 1), 'Process') }
}
$compiler = (Get-Command cl.exe -ErrorAction Stop).Source
$common = @('/nologo', '/std:c++17', '/EHsc', '/MT', '/W4', '/WX', '/DUNICODE', '/D_UNICODE')
if ($Configuration -eq 'Debug') { $common += @('/Od', '/Zi') } else { $common += @('/O2') }
$fixtureSource = Join-Path $repository 'contract-tests/NativePluginGuestMemoryProbe'
function Invoke-Compile([string[]]$CompilerArguments) {
    & $compiler @common @CompilerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Native plugin execution-domain compilation failed.' }
}
Push-Location $output
try {
    Invoke-Compile @((Join-Path $repository 'runtime/native/plugins/opennv_plugin_domain.cpp'),
        "/Fo$output/opennv_plugin_domain.obj", '/link', '/MACHINE:X86', "/OUT:$output/opennv_plugin_domain.exe")
    Invoke-Compile @('/LD', (Join-Path $fixtureSource 'NativeFixtureDependency.cpp'),
        "/Fo$output/dependency.obj", '/link', '/MACHINE:X86', "/DEF:$fixtureSource/NativeFixtureDependency.def",
        "/OUT:$fixtures/opennv_domain_fixture_dependency.dll", "/IMPLIB:$output/opennv_domain_fixture_dependency.lib")
    Invoke-Compile @('/LD', (Join-Path $fixtureSource 'NativeFixture.cpp'), "/Fo$output/fixture.obj", '/link', '/MACHINE:X86',
        "/DEF:$fixtureSource/NativeFixture.def", "/OUT:$fixtures/opennv_domain_fixture.dll", "/IMPLIB:$output/fixture.lib",
        "$output/opennv_domain_fixture_dependency.lib")
    Invoke-Compile @('/LD', '/DOPENNV_FIXTURE_REJECT_ENTRY', (Join-Path $fixtureSource 'NativeFixture.cpp'),
        "/Fo$output/fixture-reject.obj", '/link', '/MACHINE:X86', "/DEF:$fixtureSource/NativeFixture.def",
        "/OUT:$fixtures/opennv_domain_fixture_reject.dll", "/IMPLIB:$output/fixture-reject.lib",
        "$output/opennv_domain_fixture_dependency.lib")
    Copy-Item -LiteralPath (Join-Path $fixtures 'opennv_domain_fixture.dll') -Destination (Join-Path $missingImport 'opennv_domain_fixture.dll') -Force
    for ($mode = 0; $mode -le 6; $mode++) {
        Invoke-Compile @((Join-Path $fixtureSource 'NativeFaultEnvelopeFixture.cpp'), "/DOPENNV_FAULT_ENVELOPE_MODE=$mode",
            "/Fo$output/fault-envelope-$mode.obj", '/link', '/MACHINE:X86', "/OUT:$protocolFixtures/opennv_fault_envelope_$mode.exe")
    }
    $files = @('opennv_plugin_domain.exe', 'fixtures/opennv_domain_fixture.dll',
        'fixtures/opennv_domain_fixture_dependency.dll', 'fixtures/opennv_domain_fixture_reject.dll', 'missing-import/opennv_domain_fixture.dll')
    $files += @(0..6 | ForEach-Object { "protocol-fixtures/opennv_fault_envelope_$_.exe" })
    $manifest = [ordered]@{ authoredOnly = $true; configuration = $Configuration; machine = 'I386'; executionVerified = $false; files = @() }
    foreach ($file in $files) {
        $built = Join-Path $output $file
        $manifest.files += [ordered]@{ path = $file; sha256 = (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash; bytes = (Get-Item -LiteralPath $built).Length }
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding utf8
}
finally { Pop-Location }
}
finally {
    foreach ($entry in $previousEnvironment.GetEnumerator()) {
        $previousValue = if ($null -eq $entry.Value) { [NullString]::Value } else { $entry.Value }
        [Environment]::SetEnvironmentVariable($entry.Key, $previousValue, 'Process')
    }
}
Write-Host "OPENNV_NATIVE_PLUGIN_DOMAIN_BUILD_COMPLETE configuration=$Configuration machine=I386 execution=unverified output=$output"
