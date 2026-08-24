[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
& python (Join-Path $PSScriptRoot "audit_source_constants.py")
if ($LASTEXITCODE -ne 0) {
    throw "OpenNV source constant policy failed."
}
