[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CorpusRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$RuntimeConfigurationPath = Join-Path $Repository "runtime\config\open-nv-runtime-v1.json"
$ConfigurationJsonDepth = 20
$RuntimeConfiguration = Get-Content -Raw -LiteralPath $RuntimeConfigurationPath |
    ConvertFrom-Json -Depth $ConfigurationJsonDepth
$Recipe = Join-Path $Repository (
    "content\recipes\{0}" -f [string]$RuntimeConfiguration.tooling.recipeFiles.areaCapturePlan)
$Compiler = Join-Path $Repository "content\tools\area_capture_plan.py"
$Validator = Join-Path $Repository "content\tools\validate_area_capture_plan.py"
$ResolvedCorpus = [IO.Path]::GetFullPath($CorpusRoot)
$ResolvedOutput = [IO.Path]::GetFullPath($OutputRoot)

foreach ($File in @($Recipe, $Compiler, $Validator)) {
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
        throw "Missing area capture-plan input: $File"
    }
}
if (-not (Test-Path -LiteralPath $ResolvedCorpus -PathType Container)) {
    throw "Missing CELL parity corpus: $ResolvedCorpus"
}
if (Test-Path -LiteralPath $ResolvedOutput) {
    throw "Refusing to overwrite area capture plan: $ResolvedOutput"
}

& python $Compiler `
    --corpus-root $ResolvedCorpus `
    --output-root $ResolvedOutput `
    --recipe $Recipe
if ($LASTEXITCODE -ne 0) { throw "Area capture-plan compilation failed." }

& python $Validator `
    --plan-root $ResolvedOutput `
    --corpus-root $ResolvedCorpus `
    --recipe $Recipe
if ($LASTEXITCODE -ne 0) { throw "Area capture-plan validation failed." }

$ManifestPath = Join-Path $ResolvedOutput "manifest.json"
$Manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$ManifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output (
    "OPENNV_THIRTEEN_AREA_CAPTURE_PLAN_PASS areas={0} interiors={1} exteriors={2} actors={3} manifestSha256={4}" -f
        $Manifest.counts.areas,
        $Manifest.counts.interiorAreas,
        $Manifest.counts.exteriorAreas,
        $Manifest.counts.actorPlacements,
        $ManifestSha256)
