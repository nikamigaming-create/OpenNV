[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][string]$DestinationDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
$destinationRoot = [IO.Path]::GetFullPath($DestinationDirectory)
$files = @('opennv_plugin_domain.exe', 'build-manifest.json')
foreach ($relative in $files) {
    if (!(Test-Path -LiteralPath (Join-Path $sourceRoot $relative) -PathType Leaf)) {
        throw "Missing first-party native runtime companion: $relative"
    }
}
foreach ($relative in $files) {
    $sourceFile = Join-Path $sourceRoot $relative
    $destinationFile = Join-Path $destinationRoot $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destinationFile)) | Out-Null
    if (![StringComparer]::OrdinalIgnoreCase.Equals($sourceFile, $destinationFile)) {
        Copy-Item -LiteralPath $sourceFile -Destination $destinationFile
    }
    if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash) {
        throw "Native runtime companion differs after staging: $relative"
    }
}
