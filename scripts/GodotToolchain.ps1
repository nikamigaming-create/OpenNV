function Get-OpenNVGodotToolchain {
    $toolchainRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $toolchainPath = Join-Path $toolchainRepository 'runtime/godot-toolchain.json'
    $toolchain = Get-Content -LiteralPath $toolchainPath -Raw | ConvertFrom-Json
    foreach ($field in @('release', 'sdk', 'versionPrefix', 'windowsArchive', 'windowsDirectory', 'windowsConsole', 'downloadUrl', 'archiveSha256',
        'exportTemplatesArchive', 'exportTemplatesVersion', 'exportTemplatesUrl', 'exportTemplatesSha256')) {
        if ([string]::IsNullOrWhiteSpace($toolchain.$field)) { throw "Godot toolchain omitted $field." }
    }
    foreach ($field in @('windowsArchive', 'windowsDirectory', 'windowsConsole', 'exportTemplatesArchive', 'exportTemplatesVersion')) {
        if ([IO.Path]::GetFileName($toolchain.$field) -ne $toolchain.$field -or $toolchain.$field -in @('.', '..')) {
            throw "Godot toolchain $field is not a single filename."
        }
    }
    if ($toolchain.archiveSha256 -notmatch '^[a-f0-9]{64}$') { throw 'Godot archive digest is malformed.' }
    if ($toolchain.exportTemplatesSha256 -notmatch '^[a-f0-9]{64}$') { throw 'Godot export templates digest is malformed.' }
    $toolchainUrl = [Uri]$toolchain.downloadUrl
    if ($toolchainUrl.Scheme -ne 'https' -or $toolchainUrl.Host -ne 'github.com' -or
        $toolchainUrl.AbsolutePath -ne "/godotengine/godot-builds/releases/download/$($toolchain.release)/$($toolchain.windowsArchive)") {
        throw 'Godot toolchain download is not its pinned official release asset.'
    }
    $toolchainTemplatesUrl = [Uri]$toolchain.exportTemplatesUrl
    if ($toolchainTemplatesUrl.Scheme -ne 'https' -or $toolchainTemplatesUrl.Host -ne 'github.com' -or
        $toolchainTemplatesUrl.AbsolutePath -ne "/godotengine/godot-builds/releases/download/$($toolchain.release)/$($toolchain.exportTemplatesArchive)") {
        throw 'Godot export templates download is not its pinned official release asset.'
    }
    [xml]$toolchainProject = Get-Content -LiteralPath (Join-Path $toolchainRepository 'runtime/OpenNV.csproj') -Raw
    if ($toolchainProject.Project.Sdk -ne "Godot.NET.Sdk/$($toolchain.sdk)") {
        throw 'Godot toolchain and the C# SDK pin disagree.'
    }
    return $toolchain
}

function Assert-OpenNVGodotVersion {
    param([Parameter(Mandatory = $true)][string]$Executable)
    $toolchain = Get-OpenNVGodotToolchain
    $versionOutput = & $Executable --version 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Godot version check failed for: $Executable" }
    $toolchainVersion = ($versionOutput | Out-String).Trim()
    if (-not $toolchainVersion.StartsWith($toolchain.versionPrefix, [StringComparison]::Ordinal)) {
        throw "OpenNV requires official Godot $($toolchain.release) Mono; '$Executable' reports '$toolchainVersion'."
    }
    return $toolchainVersion
}
