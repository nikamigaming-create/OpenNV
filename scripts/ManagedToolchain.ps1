function Initialize-OpenNVManagedToolchain {
    $managedRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $managedSdkPin = (Get-Content -LiteralPath (Join-Path $managedRepository 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $managedSdkVersion = [Version]$managedSdkPin
    $managedCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:OPENNV_DOTNET)) { $managedCandidates += $env:OPENNV_DOTNET }
    $managedCandidates += Join-Path (Split-Path -Parent $managedRepository) 'gd/dotnet/dotnet.exe'
    $managedCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $managedCommand) { $managedCandidates += $managedCommand.Source }
    foreach ($managedCandidate in $managedCandidates) {
        if (-not (Test-Path -LiteralPath $managedCandidate -PathType Leaf)) { continue }
        $managedInstalled = @(& $managedCandidate --list-sdks)
        if ($LASTEXITCODE -ne 0) { continue }
        foreach ($managedRow in $managedInstalled) {
            if ($managedRow -notmatch '^(\d+\.\d+\.\d+)\s') { continue }
            $managedVersion = [Version]$Matches[1]
            if ($managedVersion.Major -ne $managedSdkVersion.Major -or $managedVersion.Minor -ne $managedSdkVersion.Minor -or
                [Math]::Floor($managedVersion.Build / 100) -ne [Math]::Floor($managedSdkVersion.Build / 100) -or
                $managedVersion -lt $managedSdkVersion) { continue }
            $managedRoot = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($managedCandidate))
            $env:PATH = $managedRoot + [IO.Path]::PathSeparator + $env:PATH
            $env:DOTNET_ROOT = $managedRoot
            $env:DOTNET_ROOT_X64 = $managedRoot
            return
        }
    }
    throw "OpenNV requires the .NET SDK pinned in global.json ($managedSdkPin). Install it or set OPENNV_DOTNET."
}
