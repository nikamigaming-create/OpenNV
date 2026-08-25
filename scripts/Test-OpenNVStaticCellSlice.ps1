[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DataRoot,
    [Parameter(Mandatory = $true)]
    [string]$CorpusRoot,
    [Parameter(Mandatory = $true)]
    [string]$PlanRoot,
    [Parameter(Mandatory = $true)]
    [string]$CellFormKey,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeReport,
    [string]$Godot = "D:\code\gd\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtime = Join-Path $repository "runtime"
$compiler = Join-Path $repository "content\tools\cell_static_compile.py"
$validator = Join-Path $repository "content\tools\validate_cell_static_compile.py"
$resolvedOutput = [IO.Path]::GetFullPath($OutputRoot)
$resolvedReport = [IO.Path]::GetFullPath($RuntimeReport)

foreach ($file in @($Godot, $compiler, $validator, (Join-Path $runtime "OpenNV.csproj"))) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing static CELL gate input: $file"
    }
}
foreach ($directory in @($DataRoot, $CorpusRoot, $PlanRoot)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Missing static CELL gate directory: $directory"
    }
}
foreach ($target in @($resolvedOutput, $resolvedReport)) {
    if (Test-Path -LiteralPath $target) {
        throw "Refusing to overwrite static CELL gate output: $target"
    }
}

& python $compiler `
    --data-root $DataRoot `
    --corpus-root $CorpusRoot `
    --plan-root $PlanRoot `
    --cell-form-key $CellFormKey `
    --output-root $resolvedOutput
if ($LASTEXITCODE -ne 0) { throw "Static CELL compilation failed." }

& python $validator `
    --compile-root $resolvedOutput `
    --data-root $DataRoot `
    --corpus-root $CorpusRoot `
    --plan-root $PlanRoot
if ($LASTEXITCODE -ne 0) { throw "Static CELL validation failed." }

& dotnet build (Join-Path $runtime "OpenNV.csproj") --configuration Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "OpenNV Debug build failed." }

$godotOutput = & $Godot --headless --xr-mode off --path $runtime -- `
    --static-cell-compile $resolvedOutput `
    --report $resolvedReport `
    --quit-after-load 2>&1
$godotText = $godotOutput | Out-String
if ($LASTEXITCODE -ne 0 -or
    $godotText -notmatch "OPENNV_GODOT_STATIC_CELL_PASS" -or
    $godotText -match "(?m)^ERROR:") {
    throw "Godot static CELL load failed:`n$godotText"
}

$report = Get-Content -LiteralPath $resolvedReport -Raw | ConvertFrom-Json
$manifestPath = Join-Path $resolvedOutput "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($report.schema -ne "opennv-godot-static-cell-runtime/v1" -or
    $report.status -ne "pass" -or
    $report.scope -ne "compiled-static-presentation" -or
    $report.playable -ne $false -or
    $report.parity -ne $false -or
    $report.manifestSha256 -ne $manifestSha256 -or
    $report.cellFormKey -ne $CellFormKey -or
    $report.assets -ne $manifest.counts.assets -or
    $report.textures -ne $manifest.counts.textures -or
    $report.placements -ne $manifest.counts.compiledPlacements) {
    throw "Godot static CELL report differs from the compiled manifest."
}

Write-Output (
    (
        "OPENNV_STATIC_CELL_SLICE_PASS cell={0} assets={1} textures={2} placements={3} " +
        "collision={4} manifestSha256={5}"
    ) -f
        $report.cellFormKey,
        $report.assets,
        $report.textures,
        $report.placements,
        $report.collisionMeshes,
        $manifestSha256)
