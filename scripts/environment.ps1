$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$cache = Join-Path $ProjectRoot '.cache'
foreach ($dir in @('dotnet-home','nuget','tmp','bundle','app-data')) {
    [void][IO.Directory]::CreateDirectory((Join-Path $cache $dir))
}
$env:DOTNET_CLI_HOME = Join-Path $cache 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $cache 'nuget'
$env:TEMP = Join-Path $cache 'tmp'
$env:TMP = $env:TEMP
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cache 'bundle'
$env:CODEX_TZ_GUARD_DATA_DIR = Join-Path $cache 'app-data'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$DotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source

function Invoke-ProjectDotnet {
    param([Parameter(Mandatory)][string[]]$Arguments, [string]$LogPath)
    if ($LogPath) {
        & $DotnetCommand @Arguments 2>&1 | Tee-Object -FilePath $LogPath | Out-Host
    } else {
        & $DotnetCommand @Arguments | Out-Host
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $($Arguments[0])" }
}
