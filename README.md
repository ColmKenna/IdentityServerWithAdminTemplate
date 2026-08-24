# Duende IdentityServer with Admin Console Template

A modern, production-ready **Duende IdentityServer** and **ASP.NET Core Identity** starter template for .NET 10, featuring a comprehensive, built-in **Admin Console UI** (`/Admin`), decoupled service architecture, and enterprise security defaults.

The included **Sales** projects (`Sales.ApiService`, `Sales.WasmClient`, `Sales.Domain`, `Sales.Application`, `Sales.Infrastructure`) are lightweight example / shell applications designed to demonstrate end-to-end OAuth 2.0 and OpenID Connect integration across APIs and clients using **.NET Aspire**.

---

## 📑 Table of Contents

- [Overview & Architecture](#overview--architecture)
- [Key Features](#key-features)
  - [IdentityServer Host & Security](#identityserver-host--security)
  - [Admin Console UI](#admin-console-ui)
  - [Example Shell Applications (Sales)](#example-shell-applications-sales)
- [Project Structure](#project-structure)
- [Database Contexts](#database-contexts)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
  - [1. Configure User Secrets](#1-configure-user-secrets)
  - [2. Run the Solution (.NET Aspire)](#2-run-the-solution-net-aspire)
  - [3. Default Ports & Endpoints](#3-default-ports--endpoints)
  - [4. Default Seeded Credentials](#4-default-seeded-credentials)
- [Database Migrations](#database-migrations)
- [Testing](#testing)
- [Configuration Reference](#configuration-reference)

---

## Overview & Architecture

This solution provides a foundation for central authentication and authorization services:

```text
                               +----------------------------------------+
                               |              .NET Aspire               |
                               |             (Sales.AppHost)            |
                               +-------------------+--------------------+
                                                   |
                   +-------------------------------+-------------------------------+
                   |                               |                               |
                   v                               v                               v
       +-----------------------+       +-----------------------+       +-----------------------+
       |     Sales.WasmClient  |       |   IdentityServerHost  |       |    Sales.ApiService   |
       |  (Blazor WASM OIDC)   |       |   & Admin Console UI  |       |  (Protected API / JWT)|
       +-----------+-----------+       +-----------+-----------+       +-----------+-----------+
                   |                               |                               |
                   |      OIDC Token Flow          |       Validate Bearer JWT     |
                   +------------------------------>|<------------------------------+
                   |                               |
                   |       API Requests + Bearer   |
                   +-------------------------------------------------------------->|
                                                   |
                                 +-----------------+-----------------+
                                 |                 |                 |
                                 v                 v                 v
                          [IdentityDb]    [IdentityConfigDb] [IdentityOperationalDb]
```

- **IdentityServerProject**: The primary authentication host running Duende IdentityServer with ASP.NET Core Identity. Houses the Razor Pages UI for account workflows (Login, Logout, Access Denied) and the `/Admin` management console.
- **IdentityServerProject.Admin.Services**: A decoupled domain services library containing the business logic, validation, audit generation, and management operations for the admin console.
- **Sales Projects**: Minimal reference implementations illustrating how downstream services consume tokens and enforce security policies.

---

## Key Features

### IdentityServer Host & Security

- **Duende IdentityServer 8 on .NET 10**: Fully configured OpenID Connect (OIDC) and OAuth 2.0 authorization server.
- **ASP.NET Core Identity Integration**: User account store with password hashing, account lockout, role management, and claim handling.
- **Enterprise Security Defaults**:
  - **Immediate Session Invalidation**: `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero` ensures credentials and tokens revoked in the admin console take effect on the next request.
  - **ASP.NET Core Data Protection**: Keys persisted to EF Core (`ApplicationDbContext`) with certificate encryption support in production.
  - **Defensive HTTP Headers**: Enforces `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, and `Referrer-Policy: strict-origin-when-cross-origin`.
  - **Production Startup Checks**: `IDatabaseSchemaReadinessValidator` validates that all migrations are in place prior to launching or seeding.
  - **Protected Administrator Guards**: Built-in protection prevents accidental deletion or demotion of the last active system administrator.

### Admin Console UI

The `/Admin` section is restricted to users in the `SysAdmin` role and provides a dashboard and management tools:

| Module | Description |
|---|---|
| **Clients** | Create and manage OAuth2/OIDC clients using presets (SPA, Web App, M2M, Blazor). Manage redirect/post-logout URIs, allowed scopes, grant types, token lifetimes, and secrets with secure one-time reveals. Supports client cloning. |
| **API Resources** | Register protected APIs, map associated user claims, and configure resource secrets. |
| **API Scopes** | Define granular authorization scopes and manage resource associations. |
| **Identity Resources** | Manage OIDC standard scopes (`openid`, `profile`, `email`, `roles`) with protection against editing built-in configurations. |
| **Users** | User search and pagination, profile editing, email confirmation toggles, password resets, role assignment, and custom claims. |
| **Roles** | Role creation and management with safeguards against modifying protected administrator roles. |
| **Persisted Grants** | Search, view, and revoke active authorization codes, refresh tokens, user consent grants, and reference tokens. |
| **Audit Logs** | Filterable, structured audit log tracking all security events, administrator mutations, outcomes, and failure reasons. |
| **Diagnostics** | Live status checks covering database connectivity, store health, signing credentials, and configuration warnings. |
| **Signing Keys** | View active and retired cryptographic signing keys. |

### Example Shell Applications (Sales)

The `Sales.*` projects demonstrate how to integrate client applications and APIs with IdentityServer:

- **`Sales.ApiService`**: Minimal API protected by JWT Bearer authentication requiring the `sales.api` scope and `SysAdmin` role for privileged endpoints. Includes Swagger UI configured for OAuth 2.0 Authorization Code Flow with PKCE.
- **`Sales.WasmClient`**: Blazor WebAssembly frontend demonstrating OIDC client login, authentication state management, and authenticated HTTP requests.
- **`Sales.Domain` / `Sales.Application` / `Sales.Infrastructure`**: Clean architecture skeleton structure demonstrating layer boundaries.
- **`Sales.AppHost`**: .NET Aspire orchestration tying together SQL Server, IdentityServer, API, and client services.

---

## Project Structure

```text
.
├── IdentityServerProject/
│   └── src/IdentityServerProject/       # Duende IdentityServer host & /Admin Razor Pages UI
├── IdentityServerProject.Admin.Services # Domain services, validation & audit logic for Admin UI
├── IdentityServerProject.Admin.Tests    # Unit, integration, characterization & audit coverage tests
├── Sales.ApiService/                    # Example backend API protected by JWT Bearer tokens
├── Sales.AppHost/                       # .NET Aspire AppHost orchestrator
├── Sales.Application/                   # Example application layer
├── Sales.ArchitectureTests/             # Architecture constraint tests
├── Sales.Domain/                        # Example domain layer
├── Sales.Infrastructure/                # Example infrastructure layer
├── Sales.IntegrationTests/              # Integration test suite for example services
├── Sales.RazorClient/                   # Example Razor client shell
├── Sales.ServiceDefaults/               # Aspire service defaults (OTel, health checks, resilience)
├── Sales.UnitTests/                     # Unit tests for example services
├── Sales.WasmClient/                    # Example Blazor WebAssembly client application
├── Sales.Web/                           # Example web shell
├── scripts/                             # Utility scripts (e.g. migration bundle generation)
├── Directory.Packages.props             # Central Package Management (CPM)
├── global.json                          # .NET SDK configuration
└── Sales.slnx                           # Solution definition
```

---

## Database Contexts

The solution separates operational, configuration, and identity data across three distinct Entity Framework Core `DbContext` instances:

1. **`ApplicationDbContext`** (Database: `IdentityDb`):
   - ASP.NET Core Identity (Users, Roles, UserClaims, UserRoles, Logins, Tokens).
   - Administrative audit log entries (`AuditLogEntry`).
   - Bound secret reveal metadata (`SecretRevealRecord`).
   - ASP.NET Core Data Protection key repository (`DataProtectionKeys`).
2. **`ConfigurationDbContext`** (Database: `IdentityConfigDb`):
   - Duende IdentityServer configuration store (Clients, Identity Resources, API Resources, API Scopes).
3. **`PersistedGrantDbContext`** (Database: `IdentityOperationalDb`):
   - Duende IdentityServer operational store (Authorization codes, refresh tokens, reference tokens, user consent, signing keys).

*(Note: The example API uses its own `SalesDbContext` pointing to `SalesDb`.)*

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/) or a compatible container runtime (for .NET Aspire SQL Server containers)
- Node.js (v18+) *optional, only needed for running front-end component tests in IdentityServerProject*

---

## Getting Started

### 1. Configure User Secrets

Before launching via Aspire, configure the required development secrets for `Sales.AppHost`:

```pwsh
cd Sales.AppHost

dotnet user-secrets set "Parameters:sql-password"            "YourStrong@SA!Password"
dotnet user-secrets set "Parameters:razor-client-secret"     "dev-secret-for-razor-client"
dotnet user-secrets set "Parameters:blazor-client-secret"    "dev-secret-for-blazor-client"
dotnet user-secrets set "Parameters:seed-sysadmin-password"  "SysAdminPass123!"
dotnet user-secrets set "Parameters:seed-test-user-password" "TestUserPass123!"
```

### 2. Run the Solution (.NET Aspire)

Run the AppHost project to spin up SQL Server and all dependencies:

```pwsh
dotnet run --project Sales.AppHost
```

Aspire will output the URL for the **Aspire Dashboard**, from which you can monitor logs, traces, metrics, and inspect running endpoints.

### 3. Default Ports & Endpoints

| Service | Port / URL | Description |
|---|---|---|
| **IdentityServer Host** | `https://localhost:5001` | OIDC discovery endpoint (`/.well-known/openid-configuration`) & Admin Console (`/Admin`) |
| **Sales Wasm Client** | `https://localhost:5002` | Blazor WASM client app |
| **Sales Api Service** | `https://localhost:5004` | Protected API & Swagger UI (`/swagger`) |
| **Aspire Dashboard** | Dynamic (see console output) | Telemetry, logs, and distributed application management |

### 4. Default Seeded Credentials

When running in the Development environment, the database is automatically seeded with:

- **System Administrator**:
  - **Username / Email**: `admin@sales.local`
  - **Password**: Configured via `Parameters:seed-sysadmin-password` (or `Password123!` by default)
  - **Role**: `SysAdmin` (has access to `/Admin`)
- **Standard Test User**:
  - **Username / Email**: `testuser@sales.local`
  - **Password**: Configured via `Parameters:seed-test-user-password` (or `Password123!` by default)
  - **Role**: *(None)*

---

## Database Migrations

Each `DbContext` has dedicated migrations under `IdentityServerProject/src/IdentityServerProject/Migrations`:
- `Migrations/Application` (`ApplicationDbContext`)
- `Migrations/Configuration` (`ConfigurationDbContext`)
- `Migrations/Operational` (`PersistedGrantDbContext`)

### First Run with .NET Aspire

The application never creates or migrates database schemas at startup. On a first run, start
AppHost so that it starts its SQL Server container, then apply all three migration bundles before
restarting the failed `identityserver` resource. A schema-readiness error from IdentityServer while
the container starts is expected until this is complete.

From the repository root, build the bundles:

```pwsh
dotnet tool restore
./scripts/Create-MigrationBundles.ps1
```

With AppHost still running, use its active SQL Server container and the AppHost SQL password to run
the bundles in order. This snippet discovers Aspire's dynamically assigned host port and does not
print the password:

```pwsh
$sqlContainer = docker ps --filter "name=sqlserver" --format "{{.Names}}" | Select-Object -First 1
if (-not $sqlContainer) { throw "No running AppHost SQL Server container was found." }

$sqlPort = (docker port $sqlContainer 1433/tcp | Select-Object -First 1) -replace '^.*:', ''
$secretLine = dotnet user-secrets list --project Sales.AppHost/Sales.AppHost.csproj |
    Where-Object { $_ -match '^Parameters:sql-password\s*=\s*(.+)$' } |
    Select-Object -First 1
if (-not $secretLine) { throw "The AppHost SQL password is not configured." }
$sqlPassword = [regex]::Match($secretLine, '^Parameters:sql-password\s*=\s*(.+)$').Groups[1].Value

function Invoke-MigrationBundle([string] $bundle, [string] $database) {
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Data Source'] = "127.0.0.1,$sqlPort"
    $connection['Initial Catalog'] = $database
    $connection['User ID'] = 'sa'
    $connection['Password'] = $sqlPassword
    $connection['TrustServerCertificate'] = 'True'
    & "./artifacts/migrations/$bundle.exe" --connection $connection.ConnectionString
    if ($LASTEXITCODE -ne 0) { throw "$bundle migration bundle failed." }
}

Invoke-MigrationBundle identity IdentityDb
Invoke-MigrationBundle configuration IdentityConfigDb
Invoke-MigrationBundle operational IdentityOperationalDb
```

After all three commands report `Done.`, restart the `identityserver` resource in the Aspire
Dashboard (or restart AppHost). The bundles are idempotent, so it is safe to run them again when
deploying a new migration.

### Generating Migration Bundles for Production

Use the provided PowerShell script to build self-contained EF Core migration executables for CI/CD deployments:

```pwsh
./scripts/Create-MigrationBundles.ps1 -OutputDir "artifacts/migrations"
```

---

## Testing

Run the full automated test suite across the solution:

```pwsh
# Run all unit and integration tests
dotnet test

# Run tests for the Admin Services and Page Models specifically
dotnet test IdentityServerProject.Admin.Tests/IdentityServerProject.Admin.Tests.csproj
```

---

## Configuration Reference

Key configuration sections in `IdentityServerProject`:

```json
{
  "AdminConsole": {
    "DefaultPageSize": 10
  },
  "Clients": {
    "RazorClientUri": "https://localhost:5001",
    "BlazorClientUri": "https://localhost:5002",
    "RazorSecret": "<secret>",
    "BlazorSecret": "<secret>"
  },
  "Seed": {
    "SysAdminEmail": "admin@sales.local",
    "SysAdminPassword": "<password>",
    "TestUserPassword": "<password>"
  },
  "DataProtection": {
    "CertificatePath": "path/to/cert.pfx",
    "CertificatePassword": "<cert-password>"
  },
  "IdentityServer": {
    "SigningCertificatePath": "path/to/signing-cert.pfx",
    "SigningCertificatePassword": "<cert-password>"
  }
}
```

---

## License

This project is licensed under the terms specified in the repository. Please review Duende IdentityServer licensing terms for production commercial deployments.
