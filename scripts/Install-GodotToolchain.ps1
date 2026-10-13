[CmdletBinding()]
param([string]$Destination = '', [switch]$IncludeExportTemplates)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'GodotToolchain.ps1')
$toolchain = Get-OpenNVGodotToolchain
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $toolchainWorkspace = Split-Path -Parent ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
    $Destination = Join-Path $toolchainWorkspace 'gd'
}
$toolchainDestination = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($toolchainDestination) | Out-Null
$toolchainArchive = Join-Path $toolchainDestination $toolchain.windowsArchive
$toolchainConsole = Join-Path (Join-Path $toolchainDestination $toolchain.windowsDirectory) $toolchain.windowsConsole
if (-not (Test-Path -LiteralPath $toolchainArchive -PathType Leaf)) {
    Invoke-WebRequest -Uri $toolchain.downloadUrl -OutFile $toolchainArchive
}
$toolchainHash = (Get-FileHash -LiteralPath $toolchainArchive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($toolchainHash -ne $toolchain.archiveSha256) { throw "Godot archive hash mismatch: $toolchainHash" }
if (-not (Test-Path -LiteralPath $toolchainConsole -PathType Leaf)) {
    Expand-Archive -LiteralPath $toolchainArchive -DestinationPath $toolchainDestination
}
$null = Assert-OpenNVGodotVersion -Executable $toolchainConsole
if ($IncludeExportTemplates) {
    $toolchainTemplatesArchive = Join-Path $toolchainDestination $toolchain.exportTemplatesArchive
    if (-not (Test-Path -LiteralPath $toolchainTemplatesArchive -PathType Leaf)) {
        Invoke-WebRequest -Uri $toolchain.exportTemplatesUrl -OutFile $toolchainTemplatesArchive
    }
    $toolchainTemplatesHash = (Get-FileHash -LiteralPath $toolchainTemplatesArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($toolchainTemplatesHash -ne $toolchain.exportTemplatesSha256) { throw "Godot export templates hash mismatch: $toolchainTemplatesHash" }
    $toolchainTemplatesRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Godot/export_templates'
    $toolchainTemplatesDirectory = Join-Path $toolchainTemplatesRoot $toolchain.exportTemplatesVersion
    [IO.Directory]::CreateDirectory($toolchainTemplatesDirectory) | Out-Null
    $toolchainTemplatesZip = [IO.Compression.ZipFile]::OpenRead($toolchainTemplatesArchive)
    try {
        $toolchainVersionEntry = $toolchainTemplatesZip.GetEntry('templates/version.txt')
        if ($null -eq $toolchainVersionEntry) { throw 'Godot export templates omit their version marker.' }
        $toolchainVersionReader = [IO.StreamReader]::new($toolchainVersionEntry.Open())
        try { $toolchainTemplatesVersion = $toolchainVersionReader.ReadToEnd().Trim() }
        finally { $toolchainVersionReader.Dispose() }
        if ($toolchainTemplatesVersion -cne $toolchain.exportTemplatesVersion) { throw 'Godot export templates have a different version.' }
        foreach ($toolchainTemplate in @('windows_debug_x86_64.exe', 'windows_debug_x86_64_console.exe',
            'windows_release_x86_64.exe', 'windows_release_x86_64_console.exe')) {
            $toolchainTemplateEntry = $toolchainTemplatesZip.GetEntry('templates/' + $toolchainTemplate)
            if ($null -eq $toolchainTemplateEntry) { throw "Godot export templates omit $toolchainTemplate." }
            $toolchainTemplatePath = Join-Path $toolchainTemplatesDirectory $toolchainTemplate
            if (Test-Path -LiteralPath $toolchainTemplatePath -PathType Leaf) {
                $toolchainTemplateInput = $toolchainTemplateEntry.Open()
                $toolchainTemplateDigest = [Security.Cryptography.SHA256]::Create()
                try { $toolchainExpectedHash = [Convert]::ToHexString($toolchainTemplateDigest.ComputeHash($toolchainTemplateInput)) }
                finally { $toolchainTemplateInput.Dispose(); $toolchainTemplateDigest.Dispose() }
                if ((Get-FileHash -LiteralPath $toolchainTemplatePath -Algorithm SHA256).Hash -cne $toolchainExpectedHash) {
                    throw "Existing Godot export template differs from its verified archive: $toolchainTemplatePath"
                }
                continue
            }
            $toolchainTemplatePending = $toolchainTemplatePath + '.' + [guid]::NewGuid().ToString('N') + '.pending'
            try {
                $toolchainTemplateInput = $toolchainTemplateEntry.Open()
                try {
                    $toolchainTemplateOutput = [IO.File]::Open($toolchainTemplatePending, 'CreateNew', 'Write', 'None')
                    try { $toolchainTemplateInput.CopyTo($toolchainTemplateOutput); $toolchainTemplateOutput.Flush($true) }
                    finally { $toolchainTemplateOutput.Dispose() }
                } finally { $toolchainTemplateInput.Dispose() }
                [IO.File]::Move($toolchainTemplatePending, $toolchainTemplatePath)
            } finally {
                if (Test-Path -LiteralPath $toolchainTemplatePending) { Remove-Item -LiteralPath $toolchainTemplatePending }
            }
        }
        $toolchainVersionPath = Join-Path $toolchainTemplatesDirectory 'version.txt'
        if (Test-Path -LiteralPath $toolchainVersionPath -PathType Leaf) {
            if ([IO.File]::ReadAllText($toolchainVersionPath).Trim() -cne $toolchainTemplatesVersion) { throw 'Existing export templates version marker differs.' }
        } else { [IO.File]::WriteAllText($toolchainVersionPath, $toolchainTemplatesVersion) }
    } finally { $toolchainTemplatesZip.Dispose() }
    Write-Verbose "Verified Windows x64 export templates: $toolchainTemplatesDirectory"
}
return $toolchainConsole
