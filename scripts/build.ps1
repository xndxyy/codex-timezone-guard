[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-preview.1'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment.ps1')
& (Join-Path $PSScriptRoot 'verify.ps1')
$publish = Join-Path $ProjectRoot 'artifacts/publish'
$release = Join-Path $ProjectRoot "artifacts/release/$Version"
[void][IO.Directory]::CreateDirectory($publish)
[void][IO.Directory]::CreateDirectory($release)
Push-Location $ProjectRoot
try {
    Invoke-ProjectDotnet -Arguments @(
        'publish','src/CodexTimezoneGuard','-c','Release','-r','win-x64',
        '--self-contained','true','-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true','-p:PublishTrimmed=false',
        '-p:EnableCompressionInSingleFile=true','-p:DebugType=embedded',
        '-p:DebugSymbols=false',"-p:Version=$Version",'-o',$publish
    ) -LogPath (Join-Path $ProjectRoot 'outputs/verification/publish.log')
    $executable = Join-Path $release 'CodexTimezoneGuard-win-x64.exe'
    Copy-Item -LiteralPath (Join-Path $publish 'CodexTimezoneGuard.exe') -Destination $executable -Force
    foreach ($name in @('README.md','LICENSE','PRIVACY.md','SECURITY.md','THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot $name) -Destination (Join-Path $release $name) -Force
    }
    foreach ($name in @('LICENSE.txt','ThirdPartyNotices.txt')) {
        $runtimeNotice = Join-Path $publish $name
        if (Test-Path -LiteralPath $runtimeNotice) {
            Copy-Item -LiteralPath $runtimeNotice -Destination (Join-Path $release "RUNTIME-$name") -Force
        }
    }
    $report = Join-Path $ProjectRoot ('outputs/verification/packaged-self-test-' + [Guid]::NewGuid().ToString('N') + '.json')
    $startInfo = [Diagnostics.ProcessStartInfo]::new($executable)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.Arguments = '--self-test "' + $report + '"'
    foreach ($name in @('DOTNET_BUNDLE_EXTRACT_BASE_DIR','CODEX_TZ_GUARD_DATA_DIR','TEMP','TMP')) {
        $startInfo.EnvironmentVariables[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    $process = [Diagnostics.Process]::Start($startInfo)
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw 'Timed out while testing only the newly created packaged self-test process'
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw 'Packaged self-test failed or did not create a report' }
    $selfTest = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($selfTest.success -isnot [bool] -or $selfTest.success -ne $true -or $selfTest.failed -ne 0) { throw 'Packaged self-test did not report full success' }
    Copy-Item -LiteralPath $report -Destination (Join-Path $ProjectRoot 'outputs/verification/packaged-self-test.json') -Force
    Copy-Item -LiteralPath $report -Destination (Join-Path $release 'packaged-self-test.json') -Force
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'outputs/verification/tests.json') -Destination (Join-Path $release 'tests.json') -Force
    $packageDocs = Join-Path $release 'docs'
    [void][IO.Directory]::CreateDirectory($packageDocs)
    foreach ($name in @('manual-acceptance.md','verification.md')) {
        $source = Join-Path (Join-Path $ProjectRoot 'docs') $name
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $packageDocs $name) -Force }
    }
    $screenshot = Join-Path $ProjectRoot 'docs/screenshots/main-window.png'
    if (Test-Path -LiteralPath $screenshot) {
        $imageFolder = Join-Path $packageDocs 'screenshots'
        [void][IO.Directory]::CreateDirectory($imageFolder)
        Copy-Item -LiteralPath $screenshot -Destination (Join-Path $imageFolder 'main-window.png') -Force
    }
    $licenseFiles = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'docs/licenses') -File | ForEach-Object {
        $destination = Join-Path $release $_.Name
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        $destination
    })
    $zip = Join-Path (Split-Path $release -Parent) "CodexTimezoneGuard-v$Version-win-x64.zip"
    $zipContents = @($executable) + @('README.md','LICENSE','PRIVACY.md','SECURITY.md','THIRD_PARTY_NOTICES.md','tests.json','packaged-self-test.json' | ForEach-Object { Join-Path $release $_ }) + $licenseFiles
    $zipContents += @(Get-ChildItem -LiteralPath $release -File -Filter 'RUNTIME-*' | ForEach-Object { $_.FullName })
    $zipContents += $packageDocs
    Compress-Archive -LiteralPath $zipContents -DestinationPath $zip -Force
    $files = @($executable,$zip)
    $sums = foreach ($file in $files) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file)
    }
    [IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),[string[]]$sums,[Text.UTF8Encoding]::new($false))
    $receipt = [ordered]@{
        version = $Version; generatedAtUtc = [DateTime]::UtcNow.ToString('O'); runtime = 'win-x64'
        sourceCommit = (git -C $ProjectRoot rev-parse HEAD)
        packagedSelfTestPassed = $true; realCodexRuntimeVerified = $false
        files = @($files | ForEach-Object { [ordered]@{name=[IO.Path]::GetFileName($_);bytes=(Get-Item -LiteralPath $_).Length;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()} })
    }
    [IO.File]::WriteAllText((Join-Path $release 'build-receipt.json'),($receipt|ConvertTo-Json -Depth 6)+"`n",[Text.UTF8Encoding]::new($false))
    Write-Output "Release artifacts: $release"
    Write-Output "ZIP: $zip"
} finally {
    Pop-Location
}
