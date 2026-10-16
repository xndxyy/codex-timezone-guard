[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment.ps1')
$reports = Join-Path $ProjectRoot 'outputs/verification'
[void][IO.Directory]::CreateDirectory($reports)
Push-Location $ProjectRoot
try {
    & (Join-Path $PSScriptRoot 'tests/BuildContract.ps1')
    Invoke-ProjectDotnet -Arguments @('run','--project','tests/CodexTimezoneGuard.Tests','-c','Release','--','--report',(Join-Path $reports 'tests.json')) -LogPath (Join-Path $reports 'tests.log')
    Invoke-ProjectDotnet -Arguments @('build','src/CodexTimezoneGuard','-c','Release','--nologo') -LogPath (Join-Path $reports 'build.log')
} finally {
    Pop-Location
}
