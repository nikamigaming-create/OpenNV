[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$contentRoot = Join-Path $repoRoot "content"
$output = [IO.Path]::GetFullPath($OutputRoot)
$work = Join-Path ([IO.Path]::GetTempPath()) ("opennv-content-build-{0}" -f [guid]::NewGuid().ToString("N"))
if (Test-Path -LiteralPath $output) {
    throw "Refusing to overwrite an existing content-tool output: $output"
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

try {
    & python -m PyInstaller --clean --noconfirm `
        --distpath $output `
        --workpath $work `
        (Join-Path $contentRoot "OpenNV.Content.spec") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "OpenNV content-tool packaging failed." }
    $extension = if ($IsWindows) { ".exe" } else { "" }
    $binary = Join-Path $output ("OpenNV.Content" + $extension)
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
        throw "Packaged content tool is missing: $binary"
    }
    & $binary --help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Packaged content tool failed its CLI smoke test." }

    $licenseDirectory = Join-Path $output "licenses"
    New-Item -ItemType Directory -Path $licenseDirectory | Out-Null
    $licenseQueries = @(
        @{ Package = "PyFFI"; Suffix = "LICENSE.rst"; Output = "PyFFI-LICENSE.rst" },
        @{ Package = "Pillow"; Suffix = "licenses/LICENSE"; Output = "Pillow-LICENSE.txt" },
        @{ Package = "PyInstaller"; Suffix = "licenses/COPYING.txt"; Output = "PyInstaller-COPYING.txt" },
        @{ Package = "setuptools"; Suffix = "LICENSE"; Output = "setuptools-LICENSE.txt" }
    )
    foreach ($query in $licenseQueries) {
        $code = "from importlib.metadata import distribution; d=distribution('$($query.Package)'); " +
            "print(next(str(d.locate_file(f)) for f in d.files if str(f).replace(chr(92), '/').endswith('$($query.Suffix)')))"
        $sourceLicense = (& python -c $code | Out-String).Trim()
        if (-not (Test-Path -LiteralPath $sourceLicense -PathType Leaf)) {
            throw "Installed $($query.Package) license is missing: $sourceLicense"
        }
        Copy-Item -LiteralPath $sourceLicense -Destination (Join-Path $licenseDirectory $query.Output)
    }
    $pythonLicense = (& python -c "import sys; from pathlib import Path; print(Path(sys.executable).with_name('LICENSE.txt'))" | Out-String).Trim()
    if (-not (Test-Path -LiteralPath $pythonLicense -PathType Leaf)) {
        throw "Installed Python license is missing: $pythonLicense"
    }
    Copy-Item -LiteralPath $pythonLicense -Destination (Join-Path $licenseDirectory "Python-LICENSE.txt")
    Copy-Item -LiteralPath (Join-Path $contentRoot "THIRD_PARTY.md") -Destination (Join-Path $output "CONTENT-THIRD-PARTY.md")

    [pscustomobject][ordered]@{
        schema = "opennv-content-tool-package/v1"
        status = "pass"
        binary = $binary
        licenses = $licenseDirectory
        bytes = (Get-Item -LiteralPath $binary).Length
        sha256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
