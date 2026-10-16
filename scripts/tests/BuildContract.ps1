$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$failed = 0
function Check([string]$Name, [bool]$Condition) {
    if ($Condition) { Write-Output "PASS $Name" }
    else { Write-Output "FAIL $Name"; $script:failed++ }
}
foreach ($name in @('environment.ps1','verify.ps1','build.ps1')) {
    $path = Join-Path $root "scripts/$name"
    Check "script exists: $name" (Test-Path -LiteralPath $path)
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    Check "syntax: $name" ($errors.Count -eq 0)
    $source = [IO.File]::ReadAllText($path)
    Check "no global timezone/proxy edits: $name" ($source -notmatch '(?im)\b(Set-TimeZone|tzutil|setx|Set-ItemProperty)\b')
}
$build = Join-Path $root 'scripts/build.ps1'
if (Test-Path -LiteralPath $build) {
    $source = [IO.File]::ReadAllText($build)
    foreach ($contract in @('PublishSingleFile=true','IncludeNativeLibrariesForSelfExtract=true','PublishTrimmed=false','--self-test','SHA256SUMS.txt')) {
        Check "package contract: $contract" ($source.Contains($contract))
    }
    Check 'packaged test uses explicit non-shell child environment' ($source.Contains('$startInfo.UseShellExecute = $false'))
    Check 'packaged self-test uses a fresh report instead of stale success' ($source.Contains("[Guid]::NewGuid().ToString('N')"))
}
if ($failed) { throw "$failed script contract check(s) failed" }
