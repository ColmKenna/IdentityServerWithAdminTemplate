param (
    [string]$OutputDir = "artifacts/migrations",
    [string]$RuntimeIdentifier = ""
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "IdentityServerProject/src/IdentityServerProject/IdentityServerProject.csproj"
$resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDir))

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Invoke-DotNet -Arguments @("tool", "restore")

$bundles = @(
    @{ Context = "ApplicationDbContext"; Name = "identity" },
    @{ Context = "ConfigurationDbContext"; Name = "configuration" },
    @{ Context = "PersistedGrantDbContext"; Name = "operational" }
)

foreach ($bundle in $bundles) {
    $artifactName = if ($IsWindows) { "$($bundle.Name).exe" } else { $bundle.Name }
    $artifactPath = Join-Path $resolvedOutputDir $artifactName
    $arguments = @(
        "ef", "migrations", "bundle",
        "--project", $projectPath,
        "--startup-project", $projectPath,
        "--context", $bundle.Context,
        "--configuration", "Release",
        "--output", $artifactPath,
        "--force"
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $arguments += @("--runtime", $RuntimeIdentifier)
    }

    Write-Host "Building $($bundle.Context) migration bundle: $artifactPath"
    Invoke-DotNet -Arguments $arguments

    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
        throw "Expected migration bundle was not created: $artifactPath"
    }
}

Write-Host "All three migration bundles were created in $resolvedOutputDir."
