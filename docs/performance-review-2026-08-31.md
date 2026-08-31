# .NET Performance Review — 2026-08-31

## 1. Run Summary

- Solution: `Sales.slnx` (inferred from the repository's only solution file).
- Scope: whole solution (inferred from the request).
- Base branch: `main` (inferred from the skill default; no changed-file restriction was used).
- Target frameworks: `net10.0` throughout.
- Axes scanned: EF Core, ASP.NET Core, Razor Pages, Blazor WebAssembly, core C#, and startup.
- Baseline suite: 831 passed, 0 failed, 0 skipped in 99.33 seconds.
- First run: no `perf-baseline.json` existed.
- Available tooling: .NET SDK 10.0.101, SQLite, SQL Server 2022 Testcontainers.
- Unavailable tooling: `dotnet-counters`, `dotnet-trace`, browser render instrumentation, production proxy configuration, and an application run command.
- Authorized measurement subset: EF-001 through EF-006.
- Conditions: Release, .NET 10.0.1 runtime, `Colms-Mac-mini`, Darwin arm64. Harness SHA-256: `5be612556d1ad025d93472019d6fcfa13a6b48bad70af6f1a35801fc3c533694`.

## 2. Overview

### Impact × Effort

| | Low effort | High effort |
|---|---|---|
| High impact | EF-004 | EF-001, EF-002, EF-003, EF-006 |
| Low impact | none | EF-005 |

### Ranked results

| ID | Axis | Class | Baseline | Projected | Effort | Quadrant |
|---|---|---|---|---|---|---|
| EF-001 | EF Core | Applied | 4,096 rows for one client at 4×6 children | 8 clone-operation reads, including target check (measured) | Medium | do first |
| EF-004 | EF Core | Applied | 3,276,800 token-data characters; 11 fields | 0 token-data characters; 8 fields (measured) | Low | do first |
| EF-002 | EF Core | Applied | 1,000 rows for one resource at 10×3 children | 4 resource-load reads (measured) | Medium | plan |
| EF-003 | EF Core | Applied | 2,500 rows for one client at 50×2 children | 5 permissions-operation reads (measured) | Medium | plan |
| EF-006 | EF Core | Applied | 25 commands / 260.547 ms at 1,000 grants | 1 filtered delete command (measured regression gate) | Medium | plan |
| EF-005 | EF Core | Applied | 250 rows × 56 fields for 50 clients | 2 list reads with deterministic grant precedence (measured) | Medium | drop |

## 3. Applicable Findings

### EF-004 — Persisted-grant list transfers token data it never consumes

**Location:** `IdentityServerProject.Admin.Services/Grants/GrantListService.cs:64`
**Axis:** EF Core
**Pattern:** EF-OF — over-fetching

**Description**

The paged grant query materializes complete `PersistedGrant` entities, including `Data`, `Id`, and
`ConsumedTime`, although the list result consumes only eight persisted columns. With 50 representative
64 KiB token values, the query materialized 3,276,800 unused characters.

**Evidence**

    BEGIN EF-004 current
    rows=50 fields=11 dataChars=3276800 commands=1
    SELECT "p"."Id", "p"."ClientId", "p"."ConsumedTime", "p"."CreationTime", "p"."Data", "p"."Description", "p"."Expiration", "p"."Key", "p"."SessionId", "p"."SubjectId", "p"."Type"
    FROM "PersistedGrants" AS "p"
    WHERE "p"."SubjectId" = 'overfetch'
    ORDER BY "p"."CreationTime" DESC
    LIMIT @p
    END EF-004 current

    BEGIN EF-004 projection
    rows=50 fields=8 dataChars=0 commands=1
    SELECT "p"."Key", "p"."Type", "p"."SubjectId", "p"."SessionId", "p"."ClientId", "p"."Description", "p"."CreationTime", "p"."Expiration"
    FROM "PersistedGrants" AS "p"
    WHERE "p"."SubjectId" = 'overfetch'
    ORDER BY "p"."CreationTime" DESC
    LIMIT @p
    END EF-004 projection

    Instrument: SQL capture plus raw-reader field/row count
    Provider: SQLite relational harness
    TFM: net10.0 | Config: Release | Machine: Colms-Mac-mini
    Params: pageSize=50, Data.Length=65,536 characters

Selecting only required properties is the documented EF Core approach for avoiding unnecessary column
transfer. `[Verified: https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying]`

**Proposed change**

```diff
- var grantsOnPage = await query
+ var grantsOnPage = await query
      .OrderByDescending(g => g.CreationTime)
      .Skip(pagination.Skip)
      .Take(pagination.PageSize)
+     .Select(g => new GrantListRow(
+         g.Key, g.Type, g.SubjectId, g.SessionId, g.ClientId,
+         g.Description, g.CreationTime, g.Expiration))
      .ToListAsync(cancellationToken);
```

Add a private `GrantListRow` projection and preserve the existing `GrantListItem` mapping and client-name
lookup. This does not change the public result type, populated members, ordering, cardinality, tracking,
exceptions, freshness, or transaction scope.

**Test coverage**

Covered by `GrantListServiceTests.GetGrantsAsync_ReturnsGrants_WithResolvedClientNames` and the existing
filter/pagination tests. Before applying, add
`Should_ReturnSameGrantListProjection_WhenStoredDataIsLarge`, asserting every displayed field and ordering
with a large, deliberately different `Data` value.

## 4. Recommendations

### EF-001 — Client cloning creates a six-way collection product

**Status:** applied under explicit follow-up authorization after the original recommendation gate.

**Location:** `IdentityServerProject.Admin.Services/Clients/ClientCreateService.cs:95`
**Axis:** EF Core
**Pattern:** EF-CX — cartesian explosion
**Behaviour-affecting:** criteria 3 and 7 — split queries alter ordering and execute multiple commands
without a surrounding transaction. `[Verified: https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries]`

**Description**

The clone loader joins six sibling collections. Raw rows scale as the product of collection sizes.

**Evidence**

    BEGIN EF-001 children-per-collection=2
    entities=1 rawRows=64 fields=72 commands=1
    LEFT JOIN "ClientGrantTypes" AS "c0" ON "c"."Id" = "c0"."ClientId"
    LEFT JOIN "ClientRedirectUris" AS "c1" ON "c"."Id" = "c1"."ClientId"
    LEFT JOIN "ClientPostLogoutRedirectUris" AS "c2" ON "c"."Id" = "c2"."ClientId"
    LEFT JOIN "ClientCorsOrigins" AS "c3" ON "c"."Id" = "c3"."ClientId"
    LEFT JOIN "ClientScopes" AS "c4" ON "c"."Id" = "c4"."ClientId"
    LEFT JOIN "ClientProperties" AS "c5" ON "c"."Id" = "c5"."ClientId"
    END EF-001 children-per-collection=2
    BEGIN EF-001 children-per-collection=4
    entities=1 rawRows=4096 fields=72 commands=1
    END EF-001 children-per-collection=4

    Instrument: SQL capture plus raw-reader row count
    Provider: SQLite relational harness
    TFM: net10.0 | Config: Release | Machine: Colms-Mac-mini

**Proposed change**

```diff
  var sourceClient = await _configurationDbContext.Clients
      .AsNoTracking()
+     .AsSplitQuery()
      .Include(c => c.AllowedGrantTypes)
      // remaining includes unchanged
```

**Test coverage**

Existing clone tests cover copied values. Characterisation test to write before changing:

```csharp
[Fact]
public async Task Should_CopyEveryCollectionAndPreserveOrder_When_ClientIsCloned()
{
    // Seed four distinct values in every copied collection, clone, and assert all values,
    // multiplicity, effective order, and generated-secret behaviour.
}
```

### EF-002 — API-resource editing creates a three-way collection product

**Status:** applied in commit `0b5cd4d`; four-command regression coverage added.

**Location:** `IdentityServerProject.Admin.Services/Apis/ApiResourceEditorService.cs:770`
**Axis:** EF Core
**Pattern:** EF-CX — cartesian explosion
**Behaviour-affecting:** criteria 3 and 7 — split-query ordering and transaction consistency.

**Description**

Loading one API resource joins secrets, scopes, and claims, producing 1,000 rows when each collection
contains ten items.

**Evidence**

    BEGIN EF-002 children-per-collection=2
    entities=1 rawRows=8 fields=25 commands=1
    LEFT JOIN "ApiResourceSecrets" AS "a0" ON "a"."Id" = "a0"."ApiResourceId"
    LEFT JOIN "ApiResourceScopes" AS "a1" ON "a"."Id" = "a1"."ApiResourceId"
    LEFT JOIN "ApiResourceClaims" AS "a2" ON "a"."Id" = "a2"."ApiResourceId"
    END EF-002 children-per-collection=2
    BEGIN EF-002 children-per-collection=10
    entities=1 rawRows=1000 fields=25 commands=1
    END EF-002 children-per-collection=10

    Instrument: SQL capture plus raw-reader row count
    Provider: SQLite relational harness
    TFM: net10.0 | Config: Release | Machine: Colms-Mac-mini

**Proposed change**

```diff
  var query = _configurationDbContext.ApiResources
+     .AsSplitQuery()
      .Include(r => r.Secrets)
      .Include(r => r.Scopes)
      .Include(r => r.UserClaims);
```

**Test coverage**

```csharp
[Fact]
public async Task Should_PreserveEverySecretScopeAndClaim_When_ResourceIsLoaded()
{
    // Seed ten of each child type and assert the complete editor model and ordering.
}
```

### EF-003 — Client permissions creates a two-way collection product

**Status:** applied on 2026-08-31 with additive-query and ordering regression coverage.

**Location:** `IdentityServerProject.Admin.Services/Clients/ClientDetailsService.cs:579`
**Axis:** EF Core
**Pattern:** EF-CX — cartesian explosion
**Behaviour-affecting:** criteria 3 and 7 — split-query ordering and transaction consistency.

**Description**

The permission reader joins allowed grant types and scopes. Fifty values in each produced 2,500 rows
for one client.

**Evidence**

    BEGIN EF-003 children-per-collection=5
    entities=1 rawRows=25 fields=59 commands=1
    END EF-003 children-per-collection=5
    BEGIN EF-003 children-per-collection=50
    entities=1 rawRows=2500 fields=59 commands=1
    LEFT JOIN "ClientGrantTypes" AS "c0" ON "c"."Id" = "c0"."ClientId"
    LEFT JOIN "ClientScopes" AS "c1" ON "c"."Id" = "c1"."ClientId"
    END EF-003 children-per-collection=50

    Instrument: SQL capture plus raw-reader row count
    Provider: SQLite relational harness
    TFM: net10.0 | Config: Release | Machine: Colms-Mac-mini

**Proposed change**

```diff
  var client = await _configurationDbContext.Clients
      .AsNoTracking()
+     .AsSplitQuery()
      .Include(c => c.AllowedGrantTypes)
      .Include(c => c.AllowedScopes)
```

**Test coverage**

```csharp
[Fact]
public async Task Should_PreserveClientTypeAndAllowedScopeOrder_When_PermissionsAreLoaded()
{
    // Seed multiple grant types and 50 scopes; assert client type and ordered scope values.
}
```

### EF-006 — Bulk grant revocation is change-tracked row-by-row

**Status:** applied in commit `7755e23`; filtered single-delete regression coverage added on 2026-08-31.

**Location:** `IdentityServerProject.Admin.Services/Grants/GrantListService.cs:177`
**Axis:** EF Core
**Pattern:** EF-BU — row-by-row delete
**Behaviour-affecting:** criterion 7 — `ExecuteDeleteAsync` bypasses change tracking, `SaveChanges`
interceptors/overrides, and their transaction behavior. `[Verified: https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete]`

**Description**

The production SQL Server provider first selects all grants and then emits batched tracked deletes.
At 1,000 grants this required 25 commands and was 6.3× slower in this run than one set-based delete.

**Evidence**

    BEGIN EF-006 sqlserver-provider
    EF-006 current n=10 deleted=10 commands=2 elapsedMs=72.760
    EF-006 execute-delete n=10 deleted=10 commands=1 elapsedMs=19.480
    EF-006 current n=1000 deleted=1000 commands=25 elapsedMs=260.547
    EF-006 execute-delete n=1000 deleted=1000 commands=1 elapsedMs=41.441
    END EF-006 sqlserver-provider

    Instrument: QueryCountingInterceptor and Stopwatch
    Provider: SQL Server 2022 Testcontainer
    TFM: net10.0 | Config: Release | Machine: Colms-Mac-mini
    Runs: one per size; elapsed times are descriptive, while command counts are deterministic

**Proposed change**

```diff
- var grants = await _persistedGrantDbContext.PersistedGrants
-     .Where(g => g.SubjectId == subjectIdStr)
-     .ToListAsync(cancellationToken);
- var revokedCount = 0;
- if (grants.Count > 0)
- {
-     _persistedGrantDbContext.PersistedGrants.RemoveRange(grants);
-     revokedCount = await _persistedGrantDbContext.SaveChangesAsync(cancellationToken);
- }
+ var revokedCount = await _persistedGrantDbContext.PersistedGrants
+     .Where(g => g.SubjectId == subjectIdStr)
+     .ExecuteDeleteAsync(cancellationToken);
```

**Test coverage**

```csharp
[Fact]
public async Task Should_DeleteOnlySubjectGrantsAndWriteSameAudit_When_BulkRevoked()
{
    // Seed two subjects, capture EF/SaveChanges interceptors and audit output, revoke one,
    // then assert deleted count, surviving rows, audit record, and interceptor contract.
}
```

### EF-005 — Client listing over-fetches entities and grant rows

**Status:** applied in commit `86dbb0d`; lowest grant-row ID precedence and two-read regression coverage added on 2026-08-31.

**Location:** `IdentityServerProject.Admin.Services/Clients/ClientListService.cs:36`
**Axis:** EF Core
**Pattern:** EF-OF — over-fetching
**Behaviour-affecting:** criterion 3 — the current `FirstOrDefault` grant type emerges from navigation
materialization order; a scalar projection must define which grant type wins, potentially changing the displayed type.

**Description**

For a 50-client page with five grant types per client, the current query returned 250 rows with 56
fields. A four-field scalar projection returned 50 rows, but its grant-type selection must be made explicit.

**Evidence**

    BEGIN EF-005 current
    rawRows=250 fields=56 commands=1
    LEFT JOIN "ClientGrantTypes" AS "c0" ON "c1"."Id" = "c0"."ClientId"
    END EF-005 current
    BEGIN EF-005 projection
    rawRows=50 fields=4 commands=1
    SELECT "c"."ClientId", "c"."ClientName", "c"."Enabled", (
        SELECT "c0"."GrantType" FROM "ClientGrantTypes" AS "c0"
        WHERE "c"."Id" = "c0"."ClientId" ORDER BY "c0"."Id" LIMIT 1) AS "GrantType"
    END EF-005 projection

    Instrument: SQL capture plus raw-reader field/row count
    Provider: SQLite relational harness
    TFM: net10.0 | Config: Release | Machine: Colms-Mac-mini

**Proposed change**

Project `ClientId`, `ClientName`, `Enabled`, and one explicitly ordered grant type directly to an
internal row, then retain the current `DeriveClientType` mapping.

**Test coverage**

```csharp
[Fact]
public async Task Should_SelectSameDisplayedType_When_ClientHasMultipleGrantTypes()
{
    // Lock the intended precedence/order for every recognized grant type before projecting.
}
```

## 5. Disproven Candidates

None. All six authorized candidates reproduced their static signal.

## 6. Unproven Candidates

None within the authorized EF-001 through EF-006 subset. SQL Server-specific row-count validation for
EF-001 through EF-005 was not required to establish the relational join/result shape, but SQL Server
execution-plan and network-byte effects remain unmeasured.

## 7. Regression Report

| ID | Previous | Current | Delta | Verdict |
|---|---|---|---|---|
| EF-001 | 8 clone-operation reads | 8 reads | — | held |
| EF-002 | 1,000 joined rows | 4 resource-load reads | n/a — metric changed after apply | improved / re-baselined |
| EF-003 | 2,500 joined rows | 5 permissions-operation reads | n/a — metric changed after apply | improved / re-baselined |
| EF-004 | 0 selected token-data characters | 0 characters | — | held |
| EF-005 | 50 projected rows × 4 fields | 2 list reads; first grant ordered by ID | — | held / contract pinned |
| EF-006 | 25 SQL Server commands at 1,000 grants | 1 filtered delete command | −24 commands | improved |

## 8. Handoff

### WI-01 — Split high-multiplicity client permission reads safely

**Status:** completed on 2026-08-31; see Applied Changes.

**Source findings:** EF-003
**Behaviour-affecting:** criteria 3 and 7

**Context**

`ClientDetailsService.GetClientPermissionsAsync` loads two collections, producing 2,500 rows at fifty
values each. Split queries reduce row multiplication but change ordering and cross-command consistency.

**Measured baseline**

    permissions: 1 entity / 2,500 raw rows at 50 children in each of 2 collections

**Proposed approach**

Add characterization tests that lock every copied/returned value and effective ordering. Decide whether
to use split queries inside explicit snapshot/serializable transactions or direct DTO projections.

**Behaviour changes the developer must handle**

- Explicitly define collection ordering.
- Decide the consistency guarantee across multiple SQL commands.

**Test requirements**

- `Should_PreserveClientTypeAndAllowedScopeOrder_When_PermissionsAreLoaded`
- Regression assertion: no multiplicative single-result-set row growth.

**Verification:** at the measured sizes, return the same models with row totals close to the sum rather
than product of child counts.
**Effort:** Medium
**Risk:** Medium
**Model recommendation:** reasoning-capable coding model; transaction and ordering semantics require design judgment.

### WI-02 — Split or project API-resource editor loading

**Status:** completed in commit `0b5cd4d`.

**Source findings:** EF-002
**Behaviour-affecting:** criteria 3 and 7

**Context**

`ApiResourceEditorService.LoadResourceAsync` produces 1,000 rows for one resource with ten secrets,
scopes, and claims. It is shared by read and mutation paths, so consistency requirements differ by caller.

**Measured baseline:** `1 entity / 1,000 raw rows at 10×3 children`.
**Proposed approach:** separate read-model loading from tracked mutation loading, define ordering, and
use split queries only where transaction consistency is deliberate.
**Test requirements:** `Should_PreserveEverySecretScopeAndClaim_When_ResourceIsLoaded`; assert tracked
mutation behavior separately.
**Verification:** same editor model with approximately 31 total rows across four commands at the measured size.
**Effort:** Medium
**Risk:** Medium
**Model recommendation:** reasoning-capable coding model because the private loader serves several behavioral contexts.

### WI-03 — Decide whether bulk grant deletion may bypass SaveChanges

**Status:** completed in commit `7755e23`; SQL-command regression coverage added on 2026-08-31.

**Source findings:** EF-006
**Behaviour-affecting:** criterion 7

**Context**

`GrantListService.RevokeGrantsBySubjectCoreAsync` issued 25 SQL Server commands and took 260.547 ms for
1,000 grants. `ExecuteDeleteAsync` issued one command and took 41.441 ms, but bypasses change tracking and
SaveChanges hooks.

**Proposed approach:** audit all current/future interceptors and persistence hooks. If bypass is accepted,
use `ExecuteDeleteAsync` and retain the explicit application audit write.

**Test requirements:** assert deleted count, survivor set, audit output, cancellation, failure behavior,
and interceptor expectations before and after. Regression assertion: one delete command at 1,000 rows.

**Effort:** Medium
**Risk:** Medium
**Model recommendation:** reasoning-capable coding model; the code diff is small but persistence semantics are not.

### WI-04 — Define client grant-type precedence, then project the list

**Status:** completed in commit `86dbb0d`; precedence fixed to lowest grant-row ID on 2026-08-31.

**Source findings:** EF-005
**Behaviour-affecting:** criterion 3

**Context**

The client list returned 250 rows × 56 fields for a 50-item page. A scalar projection returned 50 rows
× 4 fields, but the displayed client type currently depends on which included grant type is enumerated first.

**Proposed approach:** define domain precedence for multiple grant types, test it, then project the selected
grant type in SQL and retain the existing label mapping.

**Test requirements:** a theory covering multi-grant clients, ordering, filtering, and pagination.
Regression assertion: 50 raw rows and four selected fields for a 50-client page.

**Effort:** Medium
**Risk:** Low to medium
**Model recommendation:** reasoning-capable coding model because product/domain precedence must be chosen.

**Cheap-model list:** none. Every handoff item changes a behavior criterion or requires a domain/transaction decision.

**Handoff summary:** four work items, all behavior-affecting. The largest measured row amplification was
4,096 rows for one client; the measured bulk-delete delta was 25 to 1 SQL commands and 260.547 to 41.441 ms.

## 9. Missing Information Report

- Instrumentation gaps: `dotnet-counters`, `dotnet-trace`, browser render counting, app run command, and
  production proxy/CDN compression configuration.
- Coverage gaps: no bUnit project or published-WASM functional suite; these blocked the non-authorized
  BLZ/BOOT candidates from becoming findings.
- Provider depth: obtain SQL Server execution plans and network-byte metrics for EF-001 through EF-005
  before capacity planning; the measured relational result shapes are deterministic, but server CPU and wire cost are not.
- Inferred values: whole-solution scope, `Sales.slnx`, and `main` default.
- Environment limits: Release harness restore reported existing dependency vulnerability warnings for
  `SSH.NET` and `SQLitePCLRaw.lib.e_sqlite3`. Security remediation belongs to the appropriate dependency/security review, not this performance scan.
- Resolved classification: EF-005 was behavior-affecting; explicit follow-up authorization selected
  lowest grant-row ID as the displayed-type precedence and permanent coverage now pins it.

## 10. Self-Verification

- [x] Every reported finding carries a verbatim evidence block; none rests on static reasoning alone.
- [x] The suite was green before measurement, immediately before apply, and after the applied change.
- [x] EF-004 was the behaviour-preserving Applicable Finding; behaviour-affecting follow-up items were
  changed only after explicit user authorization and received permanent characterization coverage.
- [x] There are no unproven candidates inside the authorized measurement subset; therefore no duplicated MIR entries are required for that subset.
- [x] Framework-behavior claims carry official Microsoft documentation citations.
- [x] No production file was written before `Apply`; the only writes are this report, `.gitignore`, and the disposable measurement harness created after `Measure`.
- [x] No characterisation test depends on the disposable harness.
- [x] No finding requiring absent permanent test infrastructure is classified Applicable.
- [x] Every inferred value is marked on first use.
- [x] The disposable harness was removed after post-change measurement.

## 11. Applied Changes

### EF-004 — APPLIED

- Production file: `IdentityServerProject.Admin.Services/Grants/GrantListService.cs` now projects only
  `Key`, `Type`, `SubjectId`, `SessionId`, `ClientId`, `Description`, `CreationTime`, and `Expiration`.
- Permanent characterization test:
  `GrantListServiceTests.Should_ReturnSameGrantListProjection_When_StoredDataIsLarge`.
- Before: 50 rows, 11 selected fields, and 3,276,800 seeded token-data characters materialized.
- After: 50 rows, 8 selected fields, zero token-data characters selected. Actual-service SQL capture:

      rows=50 commands=2 dataColumnSelected=False
      SELECT "p"."Key", "p"."Type", "p"."SubjectId", "p"."SessionId",
             "p"."ClientId", "p"."Description", "p"."CreationTime", "p"."Expiration"
      FROM "PersistedGrants" AS "p"
      ORDER BY "p"."CreationTime" DESC

- Suite after apply: 832 passed, 0 failed, 0 skipped in 80.63 seconds.
- Reverted findings: none.
- `perf-baseline.json` created with all six measured candidate baselines and the post-apply EF-004 value.
- Disposable `Sales.PerformanceScan` removed after measurement.

### EF-001 — APPLIED UNDER FOLLOW-UP AUTHORIZATION

- Production file: `IdentityServerProject.Admin.Services/Clients/ClientCreateService.cs` now uses
  `AsSplitQuery()` when loading the six cloned collections.
- Expanded characterization coverage verifies multiple redirect URIs, post-logout URIs, CORS origins,
  scopes, and properties are copied, while secret generation remains unchanged.
- Added `ClientCreateServiceTests.CloneClientAsync_UsesAdditiveSplitQueries_When_SourceHasMultipleCollections`.
- Before: one result set grew to 4,096 rows for four children in each of six collections.
- After: the complete clone operation executes eight configuration reads in the SQLite test host:
  seven additive graph reads plus one target-ID existence check.
- Suite after follow-up apply: 833 passed, 0 failed, 0 skipped in 102.31 seconds.

### Follow-up closure — EF-002, EF-003, EF-005 and EF-006

- EF-002 remains split into four resource-load reads and retains its command-count regression test.
- EF-003 now uses `AsSplitQuery()` for allowed grant types and scopes. The complete permissions
  operation executes five reads: three additive client graph reads and two scope-catalog reads.
- EF-005 now orders grant types by row ID before selecting the displayed type. A permanent test pins
  first-inserted precedence and the two-read list shape.
- EF-006 now has permanent SQL capture asserting exactly one filtered `PersistedGrants` delete command.
- `AdminWebFactory` records configuration and operational-store command text for these permanent gates.
- Final suite: 836 passed, 0 failed, 0 skipped in 93.29 seconds.
- Focused performance regressions: 3 passed, 0 failed.
- `perf-baseline.json` now contains post-apply entries for EF-001 through EF-006.
- Reverted changes: none.
