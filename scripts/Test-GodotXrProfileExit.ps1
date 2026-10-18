[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Godot,
    [Parameter(Mandatory = $true)][string]$Template,
    [Parameter(Mandatory = $true)][string]$SimulatorRuntime,
    [ValidateRange(0, 1)][int]$ExpectedProfileLeaks = 0,
    [switch]$DuplicateProfile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'GodotToolchain.ps1')
$officialVersion = Assert-OpenNVGodotVersion -Executable $Godot
$Godot = (Resolve-Path -LiteralPath $Godot).Path
$Template = (Resolve-Path -LiteralPath $Template).Path
$SimulatorRuntime = (Resolve-Path -LiteralPath $SimulatorRuntime).Path
$root = Join-Path $repository 'tmp/godot-xr-profile-exit'
[IO.Directory]::CreateDirectory($root) | Out-Null
$run = Join-Path $root ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
Copy-Item -Path (Join-Path $repository 'tools/GodotXrProfileExit/*') -Destination $run
$actionMap = Get-Content -LiteralPath (Join-Path $repository 'runtime/openxr_action_map.tres') -Raw
if ($DuplicateProfile) {
    $list = 'interaction_profiles = [SubResource("OpenXRInteractionProfile_generic"), SubResource("OpenXRInteractionProfile_oculus_touch")]'
    if (!$actionMap.Contains($list)) { throw 'First-party action-map fixture layout changed.' }
    # Distinct resources with the same path exercise RID reuse. Repeating the same
    # resource reference would be deduplicated by OpenXRActionMap before loading.
    $profile = [regex]::Match($actionMap, '(?s)\[sub_resource type="OpenXRInteractionProfile" id="OpenXRInteractionProfile_oculus_touch"\].*?(?=\r?\n\[)')
    if (!$profile.Success) { throw 'Touch profile block was not found.' }
    $duplicate = $profile.Value.Replace('id="OpenXRInteractionProfile_oculus_touch"', 'id="OpenXRInteractionProfile_duplicate"')
    $actionMap = $actionMap.Replace('[resource]', "$duplicate`n`n[resource]")
    $actionMap = $actionMap.Replace($list, 'interaction_profiles = [SubResource("OpenXRInteractionProfile_generic"), SubResource("OpenXRInteractionProfile_oculus_touch"), SubResource("OpenXRInteractionProfile_duplicate")]')
}
[IO.File]::WriteAllText((Join-Path $run 'openxr_action_map.tres'), $actionMap, [Text.UTF8Encoding]::new($false))
$presetPath = Join-Path $run 'export_presets.cfg'
$preset = Get-Content -LiteralPath $presetPath -Raw
$preset += "`ncustom_template/release=`"$($Template.Replace('\', '/'))`"`n"
[IO.File]::WriteAllText($presetPath, $preset, [Text.UTF8Encoding]::new($false))
$executable = Join-Path $run 'fixture.exe'
& $Godot --headless --path $run --xr-mode off --export-release 'Windows Fixture' $executable *> (Join-Path $run 'export.log')
if ($LASTEXITCODE -ne 0) { throw "Fixture export failed: $run/export.log" }
$info = [Diagnostics.ProcessStartInfo]::new($executable)
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($argument in @('--rendering-method', 'forward_plus', '--rendering-driver', 'd3d12', '--xr-mode', 'on')) {
    $info.ArgumentList.Add($argument)
}
$info.Environment['XR_RUNTIME_JSON'] = $SimulatorRuntime
$info.Environment['OPENXR_SIMULATOR_DATA_DIR'] = Join-Path $run 'simulator'
$info.Environment['OPENXR_SIMULATOR_HEADLESS'] = '1'
$info.Environment['OPENXR_SIMULATOR_LOG_PATH'] = Join-Path $run 'simulator.log'
$reportPath = Join-Path $run 'report.json'
$report = [ordered]@{
    schema = 'opennv-godot-xr-profile-exit/1'; status = 'running'; template = $Template
    templateSha256 = (Get-FileHash -LiteralPath $Template).Hash; officialExporter = $officialVersion
    exportedExecutableSha256 = (Get-FileHash -LiteralPath $executable).Hash
    duplicateProfile = [bool]$DuplicateProfile; expectedProfileLeaks = $ExpectedProfileLeaks
    ownedData = $false; openNvRuntimeAssembly = $false; frameRecording = $false
    overallXrAccepted = $false
}
$report | ConvertTo-Json | Set-Content -LiteralPath $reportPath
$process = $null
try {
    $process = [Diagnostics.Process]::Start($info)
    $handle = $process.Handle
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) { throw 'Synthetic XR fixture did not exit within 60 seconds.' }
    $output = $stdout.GetAwaiter().GetResult()
    $errors = $stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $run 'stdout.log'), $output)
    [IO.File]::WriteAllText((Join-Path $run 'stderr.log'), $errors)
    $combined = $output + "`n" + $errors
    $report['engineHeader'] = ($output -split "`r?`n" | Select-Object -First 1)
    $leaks = [regex]::Matches($combined, 'ERROR: (\d+) RID allocations of type .+InteractionProfile.+ were leaked at exit')
    $count = 0
    foreach ($match in $leaks) { $count += [int]$match.Groups[1].Value }
    $otherErrors = [regex]::Replace($combined, '(?m)^ERROR: \d+ RID allocations of type .+InteractionProfile.+ were leaked at exit\.\r?$', '')
    $report['exitCode'] = $process.ExitCode
    $report['profileLeaks'] = $count
    $report['fixtureCompleted'] = $combined.Contains('OPENNV_XR_PROFILE_FIXTURE_DONE processFrames=45')
    $expectedProfiles = if ($DuplicateProfile) { 'profileResources=3 touchProfiles=2' } else { 'profileResources=2 touchProfiles=1' }
    $report['profileFixtureMatched'] = $combined.Contains($expectedProfiles)
    $report['otherErrors'] = [regex]::Matches($otherErrors, '(?m)^(ERROR:|SCRIPT ERROR:)').Count
    $report.status = 'complete'
    $report | ConvertTo-Json | Set-Content -LiteralPath $reportPath
    if ($report.exitCode -ne 0 -or !$report.fixtureCompleted -or !$report.profileFixtureMatched -or
        $report.templateSha256 -ne $report.exportedExecutableSha256 -or $count -ne $ExpectedProfileLeaks -or $report.otherErrors -ne 0) {
        throw "Synthetic profile ownership check failed: $reportPath"
    }
    Write-Host "OPENNV_GODOT_XR_PROFILE_EXIT_COMPONENT_PASS expectedLeaks=$ExpectedProfileLeaks report=$reportPath"
} finally {
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
