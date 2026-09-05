[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Godot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtime = Join-Path $repository "runtime"
$solution = Join-Path $runtime "OpenNV.sln"

$pythonFiles = @(& git -C $repository ls-files --cached --others --exclude-standard -- "*.py" "*.pyw" "*.spec" |
    Where-Object { Test-Path -LiteralPath (Join-Path $repository $_) -PathType Leaf })
if ($pythonFiles.Count -ne 0) {
    throw "Python files are not allowed in the OpenNV product tree: $($pythonFiles -join ', ')"
}
$conversionFiles = @(& git -C $repository ls-files --cached --others --exclude-standard -- "content/**" |
    Where-Object { Test-Path -LiteralPath (Join-Path $repository $_) -PathType Leaf })
if ($conversionFiles.Count -ne 0) {
    throw "The removed content-conversion tree was restored: $($conversionFiles -join ', ')"
}
$policyTargets = @(
    "README.md",
    "AGENTS.md",
    "docs",
    ".github/workflows",
    "runtime/README.md",
    "runtime/runtime-manifest.json"
)
$policyViolations = @(& git -C $repository grep -ni -E "cache|python|prepared content|content tool|asset conversion" -- $policyTargets)
if ($policyViolations.Count -ne 0) {
    throw "Removed conversion workflow terminology is present:`n$($policyViolations -join "`n")"
}
if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) {
    throw "Godot executable is missing: $Godot"
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

& dotnet build $solution --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV Release build failed." }
& dotnet format $solution --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "OpenNV formatting or analyzer checks failed." }
& dotnet build $solution --configuration Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV Debug build failed." }

$harnessOutput = Join-Path $repository "tmp\runtime-gate\live-harness"
& dotnet build (Join-Path $repository "tools\OpenNV.LiveHarness\OpenNV.LiveHarness.csproj") --configuration Release --nologo --output $harnessOutput
if ($LASTEXITCODE -ne 0) { throw "OpenNV live harness build failed." }

$probes = @(
    "ActorAnimationPlaybackProbe",
    "ActorComplexionContractProbe",
    "ClassicMapInitializationProbe",
    "ContainerInventoryContractProbe",
    "FalloutPluginRuntimeProbe",
    "FalloutNifPhysicsContractProbe",
    "FalloutNifRenderingContractProbe",
    "FalloutShaderProbe",
    "FalloutImageSpaceProbe",
    "FalloutNifSkinningContractProbe",
    "FalloutNifAnimationContractProbe",
    "FalloutNpcAppearanceProbe",
    "FalloutFaceGenGeometryProbe",
    "FalloutFaceGenControlProbe",
    "FalloutDialogueProbe",
    "FalloutMovieProbe",
    "FalloutBuiltinFormProbe",
    "FalloutSoundRuntimeProbe",
    "GamebryoDialoguePlaybackProbe",
    "GamebryoFaceGenMorphProbe",
    "GamebryoPackagePlacementProbe",
    "GamebryoPackageSelectionProbe",
    "GamebryoPackageTravelProbe",
    "GamebryoRangedCombatProbe",
    "GamebryoResultCommandProbe",
    "GamebryoStageCommandProbe",
    "GamebryoUiTileContractProbe",
    "OwnedAuxResourceProbe",
    "ParityTelemetryContractProbe",
    "RuntimeSaveSlotContractProbe",
    "RuntimeSettingsContractProbe"
)
foreach ($probe in $probes) {
    $project = Join-Path $repository "contract-tests\$probe\$probe.csproj"
    & dotnet run --project $project --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "$probe failed." }
}

& npm run check --prefix (Join-Path $repository "desktop")
if ($LASTEXITCODE -ne 0) { throw "Desktop launcher syntax checks failed." }
& npm test --prefix (Join-Path $repository "desktop")
if ($LASTEXITCODE -ne 0) { throw "Desktop launcher tests failed." }

$output = & $Godot --headless --editor --quit --path $runtime 2>&1
$text = $output | Out-String
if ($LASTEXITCODE -ne 0 -or $text -match "(?m)^ERROR:") {
    throw "OpenNV Godot startup failed:`n$text"
}

$instanceOutput = & $Godot --headless --path $runtime res://tools/NativeNifInstanceAudit/NativeNifInstanceAudit.tscn 2>&1
$instanceText = $instanceOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $instanceText -match "(?m)^ERROR:" -or
    $instanceText -notmatch "OPENNV_NIF_INSTANCE_AUDIT_PASS") {
    throw "OpenNV native instance binding failed:`n$instanceText"
}
Write-Output "OPENNV_NIF_INSTANCE_AUDIT_PASS controllers=independent targets=instance-owned prototype=unchanged"

$traceOutput = & $Godot --headless --path $runtime res://tools/NativeRenderTraceAudit/NativeRenderTraceAudit.tscn 2>&1
$traceText = $traceOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $traceText -match "(?m)^ERROR:" -or
    $traceText -notmatch "OPENNV_NATIVE_RENDER_TRACE_AUDIT_PASS") {
    throw "OpenNV render-trace projection failed:`n$traceText"
}
Write-Output "OPENNV_NATIVE_RENDER_TRACE_AUDIT_PASS nearPlane=clipped coverage=bounding-box-candidates exactPixels=unverified"

Write-Output "OPENNV_CSHARP_GODOT_GATE_PASS"
