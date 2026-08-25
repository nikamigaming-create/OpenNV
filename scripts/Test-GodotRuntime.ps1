[CmdletBinding()]
param(
    [string]$Godot = "D:\code\gd\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe",
    [string]$FalloutNewVegasData = "",
    [string]$ExpectedMeshesBsaSha256 = "",
    [string]$RetailLogicalPath = "meshes\landscape\nv_rocks\nvn_rockcanyon12.nif"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeRoot = Join-Path $repoRoot "runtime"
$contentRoot = Join-Path $repoRoot "content"
$solution = Join-Path $runtimeRoot "OpenNV.sln"
$exporter = Join-Path $contentRoot "tools\export_static_nif_gltf.py"
$preparer = Join-Path $contentRoot "tools\prepare_legal_assets.py"
$reportValidator = Join-Path $contentRoot "tools\validate_runtime_report.py"
$fixtureModel = "res://tests/fixtures/opaque-triangle.gltf"
$fixtureSidecar = "res://tests/fixtures/opaque-triangle.opennv.json"

function Resolve-FnvDataRoot([string]$SelectedRoot) {
    $root = [IO.Path]::GetFullPath($SelectedRoot)
    if (Test-Path -LiteralPath (Join-Path $root "FalloutNV.esm") -PathType Leaf) {
        return $root
    }
    $data = Join-Path $root "Data"
    if (Test-Path -LiteralPath (Join-Path $data "FalloutNV.esm") -PathType Leaf) {
        return [IO.Path]::GetFullPath($data)
    }
    throw "Select either the Fallout: New Vegas installation folder or its Data folder."
}

foreach ($path in @($Godot, $solution, $exporter, $preparer, $reportValidator, (Join-Path $runtimeRoot "project.godot"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing OpenNV Godot gate input: $path"
    }
}

$sourceRoots = @(
    $runtimeRoot,
    $contentRoot,
    (Join-Path $repoRoot ".github"),
    (Join-Path $repoRoot "desktop\src"),
    (Join-Path $repoRoot "release"),
    (Join-Path $repoRoot "scripts")
)
$sourceFiles = @(
    Get-ChildItem -LiteralPath $sourceRoots -Recurse -File |
        Where-Object Extension -in @(".cs", ".csproj", ".gd", ".gdshader", ".json", ".mjs", ".ps1", ".py", ".sln", ".tres", ".yml", ".yaml")
)
$forbiddenPattern = '(?i)open' + 'mw|nif' + 'test|onv' + 'skel'
$forbidden = @($sourceFiles | Select-String -Pattern $forbiddenPattern)
if ($forbidden.Count -gt 0) {
    throw "Quarantined engine dependency found in clean runtime/content source:`n$($forbidden | Out-String)"
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
& (Join-Path $PSScriptRoot "Test-SourceConstantPolicy.ps1")
if ($LASTEXITCODE -ne 0) { throw "OpenNV source constant policy failed." }
& python -m unittest discover -s (Join-Path $contentRoot "tests") -p "test_*.py" -v
if ($LASTEXITCODE -ne 0) { throw "Direct content tests failed." }
& dotnet build $solution --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV Godot Release build failed." }
& dotnet format $solution --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "OpenNV C# format/analyzer gate failed." }
& dotnet build (Join-Path $runtimeRoot "OpenNV.csproj") --configuration Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV Godot Debug build failed." }

$startupOutput = & $Godot --headless --xr-mode off --path $runtimeRoot 2>&1
if ($LASTEXITCODE -ne 0 -or ($startupOutput | Out-String) -notmatch "OPENNV_GODOT_EXPERIMENTAL_READY playable=0") {
    throw "OpenNV experimental startup gate failed:`n$($startupOutput | Out-String)"
}

$xrReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-xr-rig-{0}.json" -f [guid]::NewGuid().ToString("N"))
$xrSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-xr-rig-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
try {
    $xrOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
        --xr-rig-proof --save-path $xrSave --report $xrReport 2>&1
    $xrText = $xrOutput | Out-String
    if ($LASTEXITCODE -ne 0 -or $xrText -notmatch "OPENNV_OPENXR_RIG_PASS" -or $xrText -match "(?m)^ERROR:") {
        throw "OpenNV OpenXR rig gate failed:`n$xrText"
    }
    & python $reportValidator --mode xr --report $xrReport
    if ($LASTEXITCODE -ne 0) { throw "OpenNV OpenXR rig report is invalid." }
}
finally {
    foreach ($temporaryPath in @($xrReport, $xrSave)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$retailModel = ""
$retailSidecar = ""
$temporaryCache = ""
$poolPracticeValidated = $false
$flatControlsValidated = $false
try {
if (-not [string]::IsNullOrWhiteSpace($FalloutNewVegasData)) {
    $resolvedFalloutData = Resolve-FnvDataRoot $FalloutNewVegasData
    $temporaryCache = Join-Path ([IO.Path]::GetTempPath()) ("opennv-legal-cache-{0}" -f [guid]::NewGuid().ToString("N"))
    $prepareArguments = @(
        $preparer,
        "--data-root", $resolvedFalloutData,
        "--cache-root", $temporaryCache,
        "--logical-model", $RetailLogicalPath,
        "--cell-recipe", "goodsprings-saloon-structure-v1"
    )
    if (-not [string]::IsNullOrWhiteSpace($ExpectedMeshesBsaSha256)) {
        $prepareArguments += @("--expected-meshes-bsa-sha256", $ExpectedMeshesBsaSha256)
    }
    & python @prepareArguments
    if ($LASTEXITCODE -ne 0) { throw "Direct legal-asset preparation failed." }
    $install = Get-Content -Raw -LiteralPath (Join-Path $temporaryCache "install-manifest.json") | ConvertFrom-Json
    if ($install.schema -ne "opennv-legal-asset-cache/v1" -or
        $install.status -ne "prepared-legal-assets") {
        throw "Legal-asset cache manifest is invalid."
    }
    $retailModel = [string]$install.outputs.model
    $retailSidecar = [string]$install.outputs.sidecar
    $cellReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-linked-cell-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $cellSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-linked-cell-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $cellOutput = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --cell-scene ([string]$install.outputs.cellScene) `
            --actor-scenes ([string]$install.outputs.actorScenes) `
            --save-path $cellSave --report $cellReport --portal-proof --quit-after-load 2>&1
        $cellText = $cellOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or $cellText -notmatch "OPENNV_GODOT_CELL_PASS" -or $cellText -match "(?m)^ERROR:") {
            throw "OpenNV linked-cell gate failed:`n$cellText"
        }
        & python $reportValidator --mode cell --report $cellReport `
            --install-manifest (Join-Path $temporaryCache "install-manifest.json")
        if ($LASTEXITCODE -ne 0) { throw "OpenNV linked-cell report is invalid." }
    }
    finally {
        foreach ($path in @($cellReport, $cellSave)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }

    $flatReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-flat-controls-{0}.json" -f [guid]::NewGuid().ToString("N"))
    $flatSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-flat-controls-save-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $flatOutput = & $Godot --xr-mode off --path $runtimeRoot -- `
            --cell-scene ([string]$install.outputs.cellScene) `
            --actor-scenes ([string]$install.outputs.actorScenes) `
            --save-path $flatSave --flat-controls-proof --report $flatReport 2>&1
        $flatText = $flatOutput | Out-String
        if ($LASTEXITCODE -ne 0 -or
            $flatText -notmatch "OPENNV_FLAT_CONTROLS_PASS" -or
            $flatText -match "(?m)^ERROR:") {
            throw "OpenNV flat controls gate failed:`n$flatText"
        }
        & python $reportValidator --mode flat-controls --report $flatReport `
            --install-manifest (Join-Path $temporaryCache "install-manifest.json")
        if ($LASTEXITCODE -ne 0) { throw "OpenNV flat controls report is invalid." }
        $flatControlsValidated = $true
    }
    finally {
        foreach ($path in @($flatReport, $flatSave)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }

    function Invoke-PoolPracticeGate([bool]$UseXrLayout, [string]$Label) {
        $poolReport = Join-Path ([IO.Path]::GetTempPath()) ("opennv-pool-{0}-{1}.json" -f $Label, [guid]::NewGuid().ToString("N"))
        $poolSave = Join-Path ([IO.Path]::GetTempPath()) ("opennv-pool-save-{0}-{1}.json" -f $Label, [guid]::NewGuid().ToString("N"))
        try {
            $poolArguments = @(
                "--headless", "--xr-mode", "off", "--path", $runtimeRoot, "--",
                "--cell-scene", ([string]$install.outputs.cellScene),
                "--save-path", $poolSave,
                "--pool-proof",
                "--report", $poolReport
            )
            if ($UseXrLayout) { $poolArguments += "--vr-layout-proof" }
            $poolOutput = & $Godot @poolArguments 2>&1
            $poolText = $poolOutput | Out-String
            if ($LASTEXITCODE -ne 0 -or
                $poolText -notmatch "OPENNV_POOL_PRACTICE_PASS" -or
                $poolText -match "(?m)^ERROR:") {
                throw "OpenNV pool practice gate failed ($Label):`n$poolText"
            }
            $pool = Get-Content -Raw -LiteralPath $poolReport | ConvertFrom-Json
            $expectedAdapter = if ($UseXrLayout) { "openxr-tracked-cue-layout" } else { "desktop-look-and-power" }
            if ($pool.schema -ne "opennv-pool-practice/v1" -or
                $pool.status -ne "pass" -or
                $pool.inputAdapter -ne $expectedAdapter -or
                -not [bool]$pool.sharedSimulation -or
                -not [bool]$pool.cueMounted -or
                -not [bool]$pool.strikeAccepted -or
                [int]$pool.cueBallBallCollisions -lt 1 -or
                -not [bool]$pool.authoredReset -or
                [bool]$pool.hardwareValidated) {
                throw "OpenNV pool practice report is invalid ($Label): $poolReport"
            }
        }
        finally {
            foreach ($path in @($poolReport, $poolSave)) {
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
            }
        }
    }

    Invoke-PoolPracticeGate -UseXrLayout $false -Label "flat"
    Invoke-PoolPracticeGate -UseXrLayout $true -Label "xr-layout"
    $poolPracticeValidated = $true
}

function Invoke-StaticModelGate([string]$Model, [string]$Sidecar, [string]$Label) {
    $report = Join-Path ([IO.Path]::GetTempPath()) ("opennv-{0}-{1}.json" -f $Label, [guid]::NewGuid().ToString("N"))
    try {
        $output = & $Godot --headless --xr-mode off --path $runtimeRoot -- `
            --model $Model --sidecar $Sidecar --report $report --quit-after-load 2>&1
        $exitCode = $LASTEXITCODE
        $text = $output | Out-String
        if ($exitCode -ne 0 -or $text -notmatch "OPENNV_GODOT_STATIC_MODEL_PASS") {
            throw "Godot static-model gate failed ($Label):`n$text"
        }
        $document = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
        if ($document.schema -ne "opennv-godot-static-model/v1" -or
            $document.status -ne "pass" -or
            $document.renderer -ne "forward_plus" -or
            [int]$document.meshes -lt 1 -or
            [int]$document.surfaces -lt 1 -or
            [int]$document.vertices -lt 3) {
            throw "Godot static-model report is invalid ($Label): $report"
        }
        return $document
    }
    finally {
        if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    }
}

$fixture = Invoke-StaticModelGate -Model $fixtureModel -Sidecar $fixtureSidecar -Label "synthetic"
$retail = $null
if (-not [string]::IsNullOrWhiteSpace($retailModel)) {
    $retail = Invoke-StaticModelGate -Model $retailModel -Sidecar $retailSidecar -Label "retail"
}

$result = [pscustomobject][ordered]@{
    schema = "opennv-godot-runtime-gate/v1"
    status = "pass"
    cleanRuntime = $true
    openXrRig = $true
    poolFlatPractice = $poolPracticeValidated
    poolOpenXrLayout = $poolPracticeValidated
    flatControls = $flatControlsValidated
    openXrHardwareValidated = $false
    syntheticSourceSha256 = [string]$fixture.sourceSha256
    retailSourceSha256 = if ($null -eq $retail) { "not-requested" } else { [string]$retail.sourceSha256 }
    godot = $Godot
}
    $result
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($temporaryCache) -and
        (Test-Path -LiteralPath $temporaryCache)) {
        $resolvedCache = [IO.Path]::GetFullPath($temporaryCache)
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedCache.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove non-temporary cache: $resolvedCache"
        }
        Remove-Item -LiteralPath $resolvedCache -Recurse
    }
}
