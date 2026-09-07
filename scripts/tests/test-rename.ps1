#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verifies scripts/rename-template.ps1 against a throwaway copy of this repository.

.DESCRIPTION
    Exercises the four properties the rename has to hold:

      1. Copies renamed to two different prefixes build, and the first one passes the full test
         suite. That suite includes the SQL Server migration tests, so it also demonstrates the
         preserved migration history still applies to blank databases under the new namespace.
      2. Invalid prefixes fail before anything is written.
      3. -Preview writes nothing.
      4. Re-running against an already-renamed copy fails with a clear message.

    Every case runs against a copy built from 'git archive HEAD', never against the working tree.

.PARAMETER SkipTests
    Build the renamed copies but skip 'dotnet test'. Much faster; gives up the migration and
    integration coverage, which needs a container runtime.

.PARAMETER KeepArtifacts
    Leave the temporary copies on disk for inspection.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RenameScript = Join-Path $RepoRoot 'scripts/rename-template.ps1'
$OldPrefix = 'IdentityServerProject'
$PrefixA = 'Contoso.Identity'
$PrefixB = 'AcmeAuth'

# Not Process.MainModule.FileName: when PowerShell is installed as a dotnet global tool that
# reports the dotnet host rather than pwsh, and invoking it re-enters dotnet with pwsh arguments.
$PwshPath = Get-Command -Name 'pwsh' -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty Source
if (-not $PwshPath) { $PwshPath = Join-Path $PSHOME 'pwsh' }
if (-not (Test-Path -LiteralPath $PwshPath)) {
    throw 'Could not locate the pwsh executable needed to run the rename script as a child process.'
}

$script:Failures = @()
$script:Checks = 0

function Assert-That {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Because)
    $script:Checks++
    if ($Condition) {
        Write-Host "  PASS  $Because" -ForegroundColor Green
    } else {
        Write-Host "  FAIL  $Because" -ForegroundColor Red
        $script:Failures += $Because
    }
}

function Write-Case { param([string]$Name) Write-Host "`n$Name" -ForegroundColor Cyan }

function New-TemplateCopy {
    param([string]$Name)
    $destination = Join-Path ([System.IO.Path]::GetTempPath()) "rename-test-$Name-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    # git archive gives exactly the tracked tree at HEAD: no bin/obj, no untracked local files.
    $archive = Join-Path ([System.IO.Path]::GetTempPath()) "rename-test-$([Guid]::NewGuid().ToString('N')).tar"
    Push-Location $RepoRoot
    try {
        & git archive --format=tar --output=$archive HEAD
        if ($LASTEXITCODE -ne 0) { throw "git archive failed in $RepoRoot" }
    } finally { Pop-Location }

    & tar -xf $archive -C $destination
    if ($LASTEXITCODE -ne 0) { throw 'tar extraction failed' }
    Remove-Item -LiteralPath $archive -Force

    return $destination
}

function Invoke-Rename {
    param([string]$Root, [string[]]$ScriptArguments)
    $all = @('-NoProfile', '-File', $RenameScript, '-RepositoryRoot', $Root) + $ScriptArguments
    & $PwshPath @all 2>&1 | ForEach-Object { Write-Verbose $_ }
    return $LASTEXITCODE
}

function Test-HostProjectPresent {
    param([string]$Root, [string]$Prefix)
    Test-Path -LiteralPath (Join-Path $Root "$Prefix/src/$Prefix/$Prefix.csproj")
}

function Get-UserSecretsId {
    param([string]$Root, [string]$Prefix)
    $projectFile = Join-Path $Root "$Prefix/src/$Prefix/$Prefix.csproj"
    $content = [System.IO.File]::ReadAllText($projectFile)
    if ($content -match '<UserSecretsId>(.*?)</UserSecretsId>') { return $Matches[1] }
    return $null
}

$copies = @()

try {
    # ----------------------------------------------------------------------------------------
    Write-Case 'Case 1: invalid prefixes are rejected before anything is written'
    $copy = New-TemplateCopy -Name 'invalid'
    $copies += $copy

    foreach ($bad in @('9Bad.Start', 'Has-Hyphen', 'Trailing.', 'Contoso..Identity', 'Contoso.static', "Some.$OldPrefix.Thing")) {
        $exit = Invoke-Rename -Root $copy -ScriptArguments @('-NewPrefix', $bad, '-Force')
        Assert-That -Condition ($exit -ne 0) -Because "'$bad' is rejected"
    }
    Assert-That -Condition (Test-HostProjectPresent -Root $copy -Prefix $OldPrefix) `
        -Because 'the copy is untouched after every rejection'

    # ----------------------------------------------------------------------------------------
    Write-Case 'Case 2: -Preview writes nothing'
    $exit = Invoke-Rename -Root $copy -ScriptArguments @('-NewPrefix', $PrefixA, '-Preview', '-Force')
    Assert-That -Condition ($exit -eq 0) -Because 'preview succeeds'
    Assert-That -Condition (Test-HostProjectPresent -Root $copy -Prefix $OldPrefix) `
        -Because 'preview left the original project in place'
    Assert-That -Condition (-not (Test-HostProjectPresent -Root $copy -Prefix $PrefixA)) `
        -Because 'preview created no renamed project'

    # ----------------------------------------------------------------------------------------
    Write-Case "Case 3: rename to '$PrefixA'"
    $copyA = New-TemplateCopy -Name 'prefix-a'
    $copies += $copyA

    $exit = Invoke-Rename -Root $copyA -ScriptArguments @('-NewPrefix', $PrefixA, '-Force')
    Assert-That -Condition ($exit -eq 0) -Because 'rename succeeds'
    Assert-That -Condition (Test-HostProjectPresent -Root $copyA -Prefix $PrefixA) -Because 'host project moved to the new prefix'
    Assert-That -Condition (-not (Test-Path -LiteralPath (Join-Path $copyA $OldPrefix))) -Because 'no directory keeps the old prefix'
    Assert-That -Condition (Test-Path -LiteralPath (Join-Path $copyA "$PrefixA.slnx")) -Because 'the solution file is renamed'
    Assert-That -Condition (Test-Path -LiteralPath (Join-Path $copyA "$PrefixA.Admin.Tests/$PrefixA.Admin.Tests.csproj")) `
        -Because 'the test project is renamed'

    $residual = Get-ChildItem -LiteralPath $copyA -Recurse -File |
        Where-Object { $_.Extension -in @('.cs', '.csproj', '.slnx', '.cshtml', '.json') } |
        Where-Object { $_.Name -ne 'rename-template.ps1' } |
        Where-Object { (Select-String -LiteralPath $_.FullName -Pattern $OldPrefix -SimpleMatch -Quiet) }
    Assert-That -Condition ($null -eq $residual -or $residual.Count -eq 0) `
        -Because "no source file still mentions '$OldPrefix'"

    $migrationsDirectory = Join-Path $copyA "$PrefixA/src/$PrefixA/Migrations"
    Assert-That -Condition (Test-Path -LiteralPath (Join-Path $migrationsDirectory 'Application')) `
        -Because 'migration history is preserved, not regenerated'

    # The solution file is named for the original sample domain, not for the old prefix, so the
    # general replace does not cover it. Renaming the file while leaving 'Sales.slnx' in the CI
    # workflow and README gave every renamed instance a CI run that restored a solution that no
    # longer existed.
    $staleSolutionReferences = Get-ChildItem -LiteralPath $copyA -Recurse -File |
        Where-Object { $_.Extension -in @('.yml', '.yaml', '.md', '.ps1', '.json', '.csproj') } |
        Where-Object { $_.Name -ne 'rename-template.ps1' -and $_.Name -ne 'test-rename.ps1' } |
        Where-Object { (Select-String -LiteralPath $_.FullName -Pattern 'Sales.slnx' -SimpleMatch -Quiet) }
    Assert-That -Condition ($null -eq $staleSolutionReferences -or $staleSolutionReferences.Count -eq 0) `
        -Because 'nothing still references the pre-rename solution filename'

    Assert-That -Condition (Select-String -LiteralPath (Join-Path $copyA '.github/workflows/ci.yml') `
            -Pattern "$PrefixA.slnx" -SimpleMatch -Quiet) `
        -Because 'the CI workflow targets the renamed solution'

    $aspireCall = Select-String -LiteralPath (Join-Path $copyA 'AppHost/AppHost.cs') -Pattern 'AddProject<' -SimpleMatch
    Assert-That -Condition ($aspireCall.Line -match ([regex]::Escape(($PrefixA -replace '[^A-Za-z0-9_]', '_')))) `
        -Because 'the Aspire AddProject<T> class name is underscored, not dotted'

    Write-Host "  building $PrefixA.slnx ..."
    Push-Location $copyA
    try {
        & dotnet build "$PrefixA.slnx" --nologo -v quiet 2>&1 | ForEach-Object { Write-Verbose $_ }
        $buildExit = $LASTEXITCODE
    } finally { Pop-Location }
    Assert-That -Condition ($buildExit -eq 0) -Because "$PrefixA builds"

    if (-not $SkipTests) {
        Write-Host "  testing $PrefixA.slnx (includes SQL Server migration coverage) ..."
        Push-Location $copyA
        try {
            & dotnet test "$PrefixA.slnx" --nologo -v quiet 2>&1 | ForEach-Object { Write-Verbose $_ }
            $testExit = $LASTEXITCODE
        } finally { Pop-Location }
        Assert-That -Condition ($testExit -eq 0) -Because "$PrefixA passes its full test suite"
    }

    # ----------------------------------------------------------------------------------------
    Write-Case 'Case 4: re-running against an already-renamed copy fails clearly'
    $exit = Invoke-Rename -Root $copyA -ScriptArguments @('-NewPrefix', 'Another.Prefix', '-Force')
    Assert-That -Condition ($exit -ne 0) -Because 'a second rename is refused'
    Assert-That -Condition (Test-HostProjectPresent -Root $copyA -Prefix $PrefixA) -Because 'the refused rerun changed nothing'

    # ----------------------------------------------------------------------------------------
    Write-Case "Case 5: a second prefix '$PrefixB' is independent"
    $copyB = New-TemplateCopy -Name 'prefix-b'
    $copies += $copyB

    $exit = Invoke-Rename -Root $copyB -ScriptArguments @('-NewPrefix', $PrefixB, '-Force')
    Assert-That -Condition ($exit -eq 0) -Because 'rename succeeds'

    Write-Host "  building $PrefixB.slnx ..."
    Push-Location $copyB
    try {
        & dotnet build "$PrefixB.slnx" --nologo -v quiet 2>&1 | ForEach-Object { Write-Verbose $_ }
        $buildExit = $LASTEXITCODE
    } finally { Pop-Location }
    Assert-That -Condition ($buildExit -eq 0) -Because "$PrefixB builds"

    $secretsA = Get-UserSecretsId -Root $copyA -Prefix $PrefixA
    $secretsB = Get-UserSecretsId -Root $copyB -Prefix $PrefixB
    Assert-That -Condition ($null -ne $secretsA -and $null -ne $secretsB -and $secretsA -ne $secretsB) `
        -Because 'the two instances got different UserSecretsId values'

    $volumeA = Select-String -LiteralPath (Join-Path $copyA 'AppHost/AppHost.cs') -Pattern 'WithDataVolume\("(.+?)"\)'
    $volumeB = Select-String -LiteralPath (Join-Path $copyB 'AppHost/AppHost.cs') -Pattern 'WithDataVolume\("(.+?)"\)'
    Assert-That -Condition ($null -ne $volumeA -and $null -ne $volumeB -and $volumeA.Line -ne $volumeB.Line) `
        -Because 'the two instances got different SQL Server data volumes'
} finally {
    if (-not $KeepArtifacts) {
        foreach ($copy in $copies) {
            if (Test-Path -LiteralPath $copy) { Remove-Item -LiteralPath $copy -Recurse -Force -ErrorAction SilentlyContinue }
        }
    } else {
        Write-Host "`nKept: $($copies -join ', ')" -ForegroundColor Yellow
    }
}

Write-Host ''
if ($script:Failures.Count -gt 0) {
    Write-Host "$($script:Failures.Count) of $($script:Checks) checks failed:" -ForegroundColor Red
    foreach ($failure in $script:Failures) { Write-Host "  - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host "All $($script:Checks) checks passed." -ForegroundColor Green
