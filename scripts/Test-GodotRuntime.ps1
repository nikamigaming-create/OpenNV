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
$policyViolations = @(& git -C $repository grep -ni -E "python|prepared content|content tool|asset conversion" -- $policyTargets)
if ($policyViolations.Count -ne 0) {
    throw "Removed conversion workflow terminology is present:`n$($policyViolations -join "`n")"
}
if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) {
    throw "Godot executable is missing: $Godot"
}

$missingSceneScripts = @(
    foreach ($sceneRelative in @(& git -C $repository ls-files --cached --others --exclude-standard -- 'runtime/*.tscn')) {
        $scene = Join-Path $repository $sceneRelative
        if (-not (Test-Path -LiteralPath $scene -PathType Leaf)) { continue }
        $source = [IO.File]::ReadAllText($scene)
        foreach ($reference in [regex]::Matches($source, 'path="res://([^"\r\n]+\.cs)"')) {
            $script = Join-Path $runtime $reference.Groups[1].Value
            if (-not (Test-Path -LiteralPath $script -PathType Leaf)) {
                "$scene -> $script"
            }
        }
    }
)
if ($missingSceneScripts.Count -ne 0) {
    throw "Godot scenes reference missing C# owners:`n$($missingSceneScripts -join "`n")"
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

& (Join-Path $PSScriptRoot 'Build-NativeAudio.ps1') -Godot $Godot

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
    "ReactiveSteeringContractProbe",
    "RuntimeLaunchContractProbe",
    "ClassicFrameContractProbe",
    "ClassicCharacterContractProbe",
    "ReferenceScriptContractProbe",
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
    "FalloutLoveTesterProbe",
    "FalloutBuiltinFormProbe",
    "FalloutSoundRuntimeProbe",
    "FalloutAnimationSoundProbe",
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
    "NativePluginGuestMemoryProbe",
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

$godotFailurePattern = '(?im)^(?:ERROR:|WARNING:.*(?:leaked at exit|ObjectDB.*leak|RIDs? .*not freed|resources still in use))'
$output = & $Godot --headless --verbose --editor --quit --path $runtime 2>&1
$text = $output | Out-String
if ($LASTEXITCODE -ne 0 -or $text -match $godotFailurePattern) {
    throw "OpenNV Godot startup failed:`n$text"
}

$pcmOutput = & $Godot --headless --verbose --path $runtime res://tools/NativePcmPlaybackAudit/NativePcmPlaybackAudit.tscn 2>&1
$pcmText = $pcmOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $pcmText -match $godotFailurePattern -or $pcmText -notmatch "OPENNV_NATIVE_PCM_PLAYBACK_PASS") {
    throw "OpenNV native PCM continuation failed:`n$pcmText"
}
Write-Output "OPENNV_NATIVE_PCM_PLAYBACK_PASS fractionalCold=true envelope=true mixer=true pause=true"

$instanceOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeNifInstanceAudit/NativeNifInstanceAudit.tscn 2>&1
$instanceText = $instanceOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $instanceText -match $godotFailurePattern -or
    $instanceText -notmatch "OPENNV_NIF_INSTANCE_AUDIT_PASS") {
    throw "OpenNV native instance binding failed:`n$instanceText"
}
Write-Output "OPENNV_NIF_INSTANCE_AUDIT_PASS controllers=independent targets=instance-owned prototype=unchanged"

$referenceOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn 2>&1
$referenceText = $referenceOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $referenceText -match $godotFailurePattern -or
    $referenceText -notmatch "OPENNV_NATIVE_REFERENCE_EVENTS_AUDIT_PASS") {
    throw "OpenNV native reference events failed:`n$referenceText"
}
Write-Output "OPENNV_NATIVE_REFERENCE_EVENTS_AUDIT_PASS physicalContacts=true activation=true localState=true"

$doorOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn -- --animated-activators 2>&1
$doorText = $doorOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $doorText -match $godotFailurePattern -or
    $doorText -notmatch "OPENNV_NATIVE_DOOR_CONSTRUCTION_LIFETIME_PASS" -or
    $doorText -notmatch "OPENNV_NATIVE_ANIMATED_ACTIVATORS_PASS") {
    throw "OpenNV native door construction and activation failed:`n$doorText"
}
Write-Output "OPENNV_NATIVE_DOOR_CONSTRUCTION_LIFETIME_PASS sourceRefusals=true nativeColdClock=true orphanNodes=unchanged"

$activationOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeDefaultActivationAudit/NativeDefaultActivationAudit.tscn 2>&1
$activationText = $activationOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $activationText -match $godotFailurePattern -or
    $activationText -notmatch "OPENNV_NATIVE_DEFAULT_ACTIVATION_PASS") {
    throw "OpenNV independent default activation failed:`n$activationText"
}
Write-Output "OPENNV_NATIVE_DEFAULT_ACTIVATION_PASS retainedFault=true queuedOnce=true availabilityGuarded=true"

$ragdollOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeAuthoredRagdollAudit/NativeAuthoredRagdollAudit.tscn 2>&1
$ragdollText = $ragdollOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $ragdollText -match $godotFailurePattern -or
    $ragdollText -notmatch "OPENNV_NATIVE_AUTHORED_RAGDOLL_PASS") {
    throw "OpenNV native authored ragdoll binding failed:`n$ragdollText"
}
Write-Output "OPENNV_NATIVE_AUTHORED_RAGDOLL_PASS sourceFirstChild=true componentState=true"

$locomotionOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeLocomotionAudit/NativeLocomotionAudit.tscn 2>&1
$locomotionText = $locomotionOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $locomotionText -match $godotFailurePattern -or
    $locomotionText -notmatch "OPENNV_NATIVE_LOCOMOTION_PASS" -or $locomotionText -notmatch "steepSlopeRefused=true") {
    throw "OpenNV native locomotion failed:`n$locomotionText"
}
Write-Output "OPENNV_NATIVE_LOCOMOTION_PASS slopes=true steps=true walls=true ceilings=true unsupportedFloorRefused=true"

$traceOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeRenderTraceAudit/NativeRenderTraceAudit.tscn 2>&1
$traceText = $traceOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $traceText -match $godotFailurePattern -or
    $traceText -notmatch "OPENNV_NATIVE_RENDER_TRACE_AUDIT_PASS") {
    throw "OpenNV render-trace projection failed:`n$traceText"
}
Write-Output "OPENNV_NATIVE_RENDER_TRACE_AUDIT_PASS nearPlane=clipped coverage=bounding-box-candidates exactPixels=unverified"

$inputOutput = & $Godot --headless --verbose --path $runtime res://tools/NativeRecordedInputAudit/NativeRecordedInputAudit.tscn 2>&1
$inputText = $inputOutput | Out-String
if ($LASTEXITCODE -ne 0 -or $inputText -match $godotFailurePattern -or
    $inputText -notmatch "OPENNV_NATIVE_RECORDED_INPUT_PASS") {
    throw "OpenNV native recorded input failed:`n$inputText"
}
Write-Output "OPENNV_NATIVE_RECORDED_INPUT_PASS saveGuard=true sourceBound=true playback=true framesRecorded=false fixture=true campaign=false"

Write-Output "OPENNV_CSHARP_GODOT_GATE_PASS"
