#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Re-brands this template to a new namespace/project prefix.

.DESCRIPTION
    Renames the IdentityServerProject family of projects — namespaces, folders, project files,
    the solution file, and every textual reference — to a prefix you choose, and gives the
    generated instance its own identity so two instances generated from this template can run
    side by side: fresh UserSecretsId values, its own Data Protection application name, and its
    own SQL Server data volume.

    EF Core migration history is preserved. Migrations, their .Designer.cs files, and the model
    snapshots are rewritten in place rather than regenerated, so existing schema upgrades stay
    reproducible and the reviewed migration bundles keep working.

    What this deliberately does NOT rename:
      * The AppHost and ServiceDefaults projects. They carry no domain identity to begin with,
        and leaving them alone keeps every path that points at them valid.
      * The IdentityDb / IdentityConfigDb / IdentityOperationalDb names. They are already
        generic. Instance isolation comes from the per-instance data volume instead, which is
        set explicitly in AppHost.cs so it is visible rather than emergent.

.PARAMETER NewPrefix
    The new prefix, in C# namespace form. For example 'Contoso.Identity' or 'AcmeAuth'.

.PARAMETER RepositoryRoot
    Repository to rewrite. Defaults to the repository containing this script. Point it at a copy
    to rehearse the rename somewhere disposable.

.PARAMETER HttpsPort
    Optional. Rewrites the AppHost HTTPS endpoint (default 5001) and the matching documentation.
    Give two generated instances different ports if you intend to run both at once.

.PARAMETER Preview
    Report every change that would be made and write nothing.

.PARAMETER Force
    Proceed even when the git working tree has uncommitted or untracked changes.

.EXAMPLE
    ./scripts/rename-template.ps1 -NewPrefix Contoso.Identity -Preview
    Shows what would change, touching nothing.

.EXAMPLE
    ./scripts/rename-template.ps1 -NewPrefix Contoso.Identity
    Performs the rename. Review with 'git diff', then build and test.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$NewPrefix,

    [string]$RepositoryRoot,

    [int]$HttpsPort = 0,

    [switch]$Preview,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$OldPrefix = 'IdentityServerProject'
$DefaultHttpsPort = 5001

# Directory names that never hold source worth rewriting. Matched per path segment at any depth,
# so a nested bin/ or obj/ is skipped as readily as one at the root.
$ExcludedSegments = @(
    '.git', '.vs', '.vscode', '.idea', 'bin', 'obj', 'node_modules',
    'artifacts', '.artifacts', 'TestResults', 'BenchmarkDotNet.Artifacts',
    'Sales.PerformanceScan', 'Library', '.local'
)

$TextExtensions = @(
    '.cs', '.cshtml', '.razor', '.csproj', '.slnx', '.sln', '.props', '.targets',
    '.json', '.md', '.yml', '.yaml', '.ps1', '.sh', '.js', '.mjs', '.css', '.scss',
    '.html', '.xml', '.config', '.txt', '.sql', '.http', '.editorconfig'
)

# Files that must not be rewritten even though their extension says otherwise. This script holds
# the old prefix as a literal; rewriting itself mid-run would corrupt it.
$ExcludedFileNames = @('rename-template.ps1', 'test-rename.ps1')

# Reserved words cannot appear as a namespace segment. Contextual keywords are legal and omitted.
$CSharpKeywords = @(
    'abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch', 'char', 'checked',
    'class', 'const', 'continue', 'decimal', 'default', 'delegate', 'do', 'double', 'else',
    'enum', 'event', 'explicit', 'extern', 'false', 'finally', 'fixed', 'float', 'for',
    'foreach', 'goto', 'if', 'implicit', 'in', 'int', 'interface', 'internal', 'is', 'lock',
    'long', 'namespace', 'new', 'null', 'object', 'operator', 'out', 'override', 'params',
    'private', 'protected', 'public', 'readonly', 'ref', 'return', 'sbyte', 'sealed', 'short',
    'sizeof', 'stackalloc', 'static', 'string', 'struct', 'switch', 'this', 'throw', 'true',
    'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', 'using', 'virtual',
    'void', 'volatile', 'while'
)

function Write-Step { param([string]$Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Change { param([string]$Message) Write-Host "  $Message" }
function Write-Done { param([string]$Message) Write-Host $Message -ForegroundColor Green }

function Fail {
    param([string]$Message)
    Write-Host "rename-template: $Message" -ForegroundColor Red
    exit 1
}

# --------------------------------------------------------------------------------------------
# 1. Validate the prefix before touching anything.
#    Every check here runs before the first write, so an invalid name leaves the tree untouched.
# --------------------------------------------------------------------------------------------

if ($NewPrefix -notmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$') {
    Fail "'$NewPrefix' is not a valid C# namespace. Use dot-separated identifiers, e.g. 'Contoso.Identity'."
}

foreach ($segment in $NewPrefix.Split('.')) {
    if ($CSharpKeywords -contains $segment) {
        Fail "'$segment' is a C# reserved word and cannot be a namespace segment."
    }
}

if ($NewPrefix -eq $OldPrefix) {
    Fail "The new prefix is already '$OldPrefix'. Nothing to do."
}

if ($NewPrefix -like "*$OldPrefix*") {
    Fail "The new prefix must not contain '$OldPrefix' — the rewrite would be ambiguous."
}

if ($HttpsPort -ne 0 -and ($HttpsPort -lt 1024 -or $HttpsPort -gt 65535)) {
    Fail "-HttpsPort must be between 1024 and 65535."
}

# --------------------------------------------------------------------------------------------
# 2. Locate and sanity-check the repository.
# --------------------------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}
$Root = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$HostProjectFile = Join-Path $Root "$OldPrefix/src/$OldPrefix/$OldPrefix.csproj"
if (-not (Test-Path -LiteralPath $HostProjectFile)) {
    Fail @"
'$Root' does not look like an un-renamed copy of this template — expected to find
$OldPrefix/src/$OldPrefix/$OldPrefix.csproj.
If you have already run this script, the rename is done; re-running it is not supported.
"@
}

if ((Test-Path -LiteralPath (Join-Path $Root '.git')) -and -not $Force -and -not $Preview) {
    Push-Location $Root
    try { $gitStatus = & git status --porcelain 2>$null } finally { Pop-Location }
    if ($LASTEXITCODE -eq 0 -and $gitStatus) {
        Fail @"
The git working tree has uncommitted or untracked changes. Commit or stash them first so the
rename lands as a reviewable diff, or pass -Force to proceed anyway.
"@
    }
}

# Aspire generates one class per referenced project under the Projects namespace, replacing any
# character that is illegal in an identifier with an underscore. A dotted prefix therefore has to
# be underscored at the AddProject<T> call site; a plain textual replace would emit
# AddProject<Contoso.Identity>, which does not compile.
$AspireProjectClass = $NewPrefix -replace '[^A-Za-z0-9_]', '_'

# Docker volume names allow [a-zA-Z0-9][a-zA-Z0-9_.-]*; lowercase-with-hyphens is always safe.
$VolumeName = ($NewPrefix -replace '[^A-Za-z0-9]', '-').ToLowerInvariant() + '-sqlserver-data'

$mode = if ($Preview) { 'PREVIEW — no files will be written' } else { 'applying changes' }
Write-Step "Renaming '$OldPrefix' to '$NewPrefix' in $Root ($mode)"

# --------------------------------------------------------------------------------------------
# 3. Rewrite file contents.
# --------------------------------------------------------------------------------------------

function Test-ShouldProcess {
    param([System.IO.FileInfo]$File)

    if ($ExcludedFileNames -contains $File.Name) { return $false }
    if ($TextExtensions -notcontains $File.Extension.ToLowerInvariant()) { return $false }

    $relative = $File.FullName.Substring($Root.Length).TrimStart([char]'/', [char]'\')
    foreach ($segment in $relative -split '[\\/]') {
        if ($ExcludedSegments -contains $segment) { return $false }
    }
    return $true
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$rewritten = 0

Write-Step 'Rewriting file contents'
foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
    if (-not (Test-ShouldProcess -File $file)) { continue }

    $original = [System.IO.File]::ReadAllText($file.FullName)
    $content = $original

    # Ordered before the general replace so the underscored form survives it intact.
    $content = $content -replace "AddProject<\s*$([regex]::Escape($OldPrefix))\s*>", "AddProject<$AspireProjectClass>"

    $content = $content -replace [regex]::Escape($OldPrefix), $NewPrefix

    if ($content -ne $original) {
        $rewritten++
        $relative = $file.FullName.Substring($Root.Length).TrimStart([char]'/', [char]'\')
        Write-Verbose "  $relative"
        if (-not $Preview) {
            [System.IO.File]::WriteAllText($file.FullName, $content, $utf8NoBom)
        }
    }
}
Write-Change "$rewritten file(s) contained '$OldPrefix'"

# --------------------------------------------------------------------------------------------
# 4. Give this instance its own identity.
# --------------------------------------------------------------------------------------------

Write-Step 'Assigning per-instance identity'

# Fresh user-secret stores. Without this, every instance generated from the template reads and
# writes the same secrets on a developer machine.
$secretsUpdated = 0
foreach ($projectFile in Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.csproj') {
    $relative = $projectFile.FullName.Substring($Root.Length).TrimStart([char]'/', [char]'\')
    $skip = $false
    foreach ($segment in $relative -split '[\\/]') {
        if ($ExcludedSegments -contains $segment) { $skip = $true; break }
    }
    if ($skip) { continue }

    $content = [System.IO.File]::ReadAllText($projectFile.FullName)
    if ($content -match '<UserSecretsId>.*?</UserSecretsId>') {
        $newId = [Guid]::NewGuid().ToString()
        $updated = $content -replace '<UserSecretsId>.*?</UserSecretsId>', "<UserSecretsId>$newId</UserSecretsId>"
        $secretsUpdated++
        Write-Change "$relative -> UserSecretsId $newId"
        if (-not $Preview) {
            [System.IO.File]::WriteAllText($projectFile.FullName, $updated, $utf8NoBom)
        }
    }
}
if ($secretsUpdated -eq 0) { Write-Change 'no UserSecretsId entries found' }

# A named data volume, so two generated instances do not share one SQL Server data directory.
# Left unnamed, Aspire derives the name from the AppHost project — which this script does not
# rename, so both instances would land on the same volume and the same databases.
$appHostFile = Join-Path $Root 'AppHost/AppHost.cs'
if (Test-Path -LiteralPath $appHostFile) {
    $content = [System.IO.File]::ReadAllText($appHostFile)
    $updated = $content -replace '\.WithDataVolume\(\s*\)', ".WithDataVolume(`"$VolumeName`")"
    if ($updated -ne $content) {
        Write-Change "AppHost/AppHost.cs -> data volume '$VolumeName'"
        if (-not $Preview) { [System.IO.File]::WriteAllText($appHostFile, $updated, $utf8NoBom) }
    }

    if ($HttpsPort -ne 0) {
        $content = [System.IO.File]::ReadAllText($appHostFile)
        $updated = $content -replace "WithHttpsEndpoint\(\s*$DefaultHttpsPort\s*,", "WithHttpsEndpoint($HttpsPort,"
        if ($updated -ne $content) {
            Write-Change "AppHost/AppHost.cs -> HTTPS endpoint $HttpsPort"
            if (-not $Preview) { [System.IO.File]::WriteAllText($appHostFile, $updated, $utf8NoBom) }
        }
    }
}

if ($HttpsPort -ne 0) {
    $readme = Join-Path $Root 'README.md'
    if (Test-Path -LiteralPath $readme) {
        $content = [System.IO.File]::ReadAllText($readme)
        $updated = $content -replace "https://localhost:$DefaultHttpsPort", "https://localhost:$HttpsPort"
        if ($updated -ne $content) {
            Write-Change "README.md -> https://localhost:$HttpsPort"
            if (-not $Preview) { [System.IO.File]::WriteAllText($readme, $updated, $utf8NoBom) }
        }
    }
}

# --------------------------------------------------------------------------------------------
# 5. Rename files, then directories.
#    Deepest paths first, so renaming a parent never invalidates a queued child path.
# --------------------------------------------------------------------------------------------

Write-Step 'Renaming files and directories'

function Get-Depth { param([string]$Path) ($Path -split '[\\/]').Count }

$fileRenames = Get-ChildItem -LiteralPath $Root -Recurse -File |
    Where-Object {
        $_.Name -like "*$OldPrefix*" -and
        ($ExcludedSegments -notcontains ($_.FullName.Substring($Root.Length).TrimStart([char]'/', [char]'\') -split '[\\/]')[0])
    } |
    Sort-Object { Get-Depth $_.FullName } -Descending

foreach ($file in $fileRenames) {
    $relative = $file.FullName.Substring($Root.Length).TrimStart([char]'/', [char]'\')
    foreach ($segment in $relative -split '[\\/]') {
        if ($ExcludedSegments -contains $segment) { $file = $null; break }
    }
    if ($null -eq $file) { continue }

    $newName = $file.Name.Replace($OldPrefix, $NewPrefix)
    Write-Change "$relative -> $newName"
    if (-not $Preview) {
        Rename-Item -LiteralPath $file.FullName -NewName $newName
    }
}

$directoryRenames = Get-ChildItem -LiteralPath $Root -Recurse -Directory |
    Where-Object {
        $_.Name -like "*$OldPrefix*" -and
        ($ExcludedSegments -notcontains $_.Name)
    } |
    Sort-Object { Get-Depth $_.FullName } -Descending

foreach ($directory in $directoryRenames) {
    if (-not (Test-Path -LiteralPath $directory.FullName)) { continue }
    $relative = $directory.FullName.Substring($Root.Length).TrimStart([char]'/', [char]'\')
    $newName = $directory.Name.Replace($OldPrefix, $NewPrefix)
    Write-Change "$relative/ -> $newName/"
    if (-not $Preview) {
        Rename-Item -LiteralPath $directory.FullName -NewName $newName
    }
}

# The solution file carries the old sample domain's name rather than the project prefix.
$solution = Get-ChildItem -LiteralPath $Root -File -Filter '*.slnx' | Select-Object -First 1
if ($null -ne $solution -and $solution.BaseName -ne $NewPrefix) {
    Write-Change "$($solution.Name) -> $NewPrefix.slnx"
    if (-not $Preview) {
        Rename-Item -LiteralPath $solution.FullName -NewName "$NewPrefix.slnx"
    }
}

# --------------------------------------------------------------------------------------------
# 6. Report.
# --------------------------------------------------------------------------------------------

Write-Host ''
if ($Preview) {
    Write-Done 'Preview complete. Nothing was written. Re-run without -Preview to apply.'
} else {
    Write-Done "Renamed to '$NewPrefix'."
    Write-Host ''
    Write-Host 'Next steps:' -ForegroundColor Cyan
    Write-Host "  1. dotnet build $NewPrefix.slnx"
    Write-Host "  2. dotnet test $NewPrefix.slnx"
    Write-Host '  3. Set the AppHost user secrets listed in README.md (the previous ones do not carry over).'
    Write-Host '  4. Review the diff before committing.'
}
