# Mobile app

An offline-first .NET MAUI Blazor Hybrid app for Android and iOS that signs in against this template's
IdentityServer. Users sign in once, then keep working with no connection at all: everything is stored in SQLite
on the phone and queued for sync.

It ships with one sample entity, `Note`, so every layer has something real in it: local storage, the sync
pipeline, the screens and their tests. Replace it with your own records (see
[Adding your own entity](#adding-your-own-entity)).

## Projects

| Project | What it is |
|---|---|
| `IdentityServerProject.Mobile.Core/` | Plain `net10.0` Razor class library: Blazor UI, SQLite store, sign-in, sync. No MAUI dependency, so it builds and is tested anywhere. |
| `IdentityServerProject.Mobile/` | Thin MAUI shell (`BlazorWebView`) plus the four things only a platform can do: secure storage, the system browser, connectivity, and the app data path. |
| `IdentityServerProject.Mobile.Core.Tests/` | xUnit and bUnit tests: services against real SQLite, the sync engine, sign-in, the HTTP client, and the pages. |

`IdentityServerProject.Mobile.Core` and its tests are in `Sales.slnx`, so CI builds and tests them.
`IdentityServerProject.Mobile` is only in `IdentityServerProject.Mobile.slnx`, because restoring a MAUI project needs
the mobile workloads and would break `dotnet build Sales.slnx` on machines (and CI runners) without them.

The library is named `*.Mobile.Core`, not `*.Client`: a namespace ending in `.Client` makes the name `Client` mean
that namespace instead of Duende's `Client` type in every file under the same root namespace.

## Authentication: direct OIDC, no BFF

The app is an OAuth **public client** (client id `mobile-app`) using authorization code + PKCE through the system
browser, as RFC 8252 prescribes for native apps. A BFF exists to keep tokens out of browser JavaScript; a phone has
OS-protected storage instead, so the tokens live in the Android Keystore / iOS Keychain (`SecureStorage`).

- **Refresh tokens** are rotated on every use, slide for 30 days, and are capped at 90 days
  (`Config.MobileClient` in `IdentityServerProject/src/IdentityServerProject/Config.cs`). A phone can be out of
  coverage for days and still sync.
- **Offline sign-in** is the stored profile: opening the app never contacts the identity server.
- **Sign-in always asks for credentials** (`prompt=login`), so a second person on a shared phone cannot ride the
  first person's browser session.
- **Sign-out** works offline and always succeeds locally: tokens and *all local data* are removed. If the phone is
  online it also revokes the refresh token and ends the identity server session. The Settings screen warns first
  if there are unsynced changes.
- **A different account signing in** on a phone that still holds someone else's data clears that data first.
- If the server refuses the refresh token (revoked, expired) the session becomes *needs sign-in*: local data stays
  usable and a banner offers to sign in again; only syncing waits.

`Config.cs` and `IdentityServerProject.Mobile.Core/MobileClientOptions.cs` (`ClientDefaults`) must agree on the client id,
redirect URIs and scopes. `IdentityServerProject.Admin.Tests/Clients/MobileClientTests.cs` fails if they drift.

### Registering the client

In Development the host seeds the client along with the example clients; it needs no secret. In production, create
it in **Admin → Clients** as a public client:

| Setting | Value |
|---|---|
| Client id | `mobile-app` |
| Grant type | Authorization code, PKCE required, no client secret |
| Redirect URI | `com.example.identityserverproject://callback` |
| Post-logout redirect URI | `com.example.identityserverproject://signout-callback` |
| Scopes | `openid`, `profile`, `email`, `roles`, `api`, offline access allowed |
| Refresh tokens | One-time use, sliding 30 days, absolute 90 days |

### The application id

`com.example.identityserverproject` is the Android application id, the iOS bundle id, and the sign-in redirect scheme.
`scripts/rename-template.ps1` replaces it with `-AppId`, or with `com.example.<your prefix, lowercased>` when you do not
pass one. Use a reverse domain you own before you publish the app. It must stay lowercase: Android matches schemes
case-sensitively, and browsers lowercase them.

## Running on Android

Prerequisites: the .NET Android workload, an Android SDK with an emulator image, and **JDK 21** (the Android SDK
rejects newer JDKs; pass `-p:JavaSdkDirectory=` or set `JAVA_HOME`).

1. Start the identity server: `dotnet run --project AppHost` (see the root README for the secrets).
2. Make the host's port reachable from the emulator:
   `adb -s <emulator> reverse tcp:5001 tcp:5001`
3. Build and install:

   ```pwsh
   dotnet build IdentityServerProject.Mobile/IdentityServerProject.Mobile.csproj -f net10.0-android -t:Install `
     -p:EmbedAssembliesIntoApk=true `
     -p:JavaSdkDirectory="C:\Program Files\Android\openjdk\jdk-21.0.8" `
     -p:AdbTarget="-s <emulator-or-device-serial>"
   ```

   Always pass `AdbTarget` when more than one device is attached, or the app is installed on all of them.
4. Open the app and tap **Sign in**. In Debug builds the app accepts the ASP.NET Core development certificate
   (`DevelopmentCertificate.cs`, localhost only). The emulator's *browser* does not, so tap
   *Advanced → Proceed to localhost* once. Sign in as `testuser@sales.local` with the
   `seed-test-user-password` you set.

To see it work offline: sign in, enable airplane mode, add a note, force-stop the app and reopen it. The note is
still there and you are still signed in. (The very first sign-in needs a connection.)

> [!NOTE]
> In Development the host signs tokens with a key it regenerates on every start. Restarting the host between
> signing in and signing out invalidates the stored identity token, so the server-side part of sign-out fails
> (the device still signs out). Sign in again after a restart.

**Physical device:** `localhost` is the phone itself. Expose the identity server on a trusted HTTPS address
(for example a Dev Tunnel) and set `Mobile:Authority` in `IdentityServerProject.Mobile/appsettings.json`. A Release build
validates certificates normally, so it needs a real certificate on the server.

## Running on iOS

Needs a Mac (or Windows paired to one, with `-p:IncludeIos=true`). The configuration is in place (the URL scheme in
`Info.plist`, a keychain access group in `Entitlements.plist`) but **the iOS build has not been run**. Treat the first
Mac build as part of adopting the app; the likeliest snags are code signing and the keychain entitlement on the simulator.

## Local data and sync

Data lives in `mobile-app.db3` in the app's private directory (EF Core + SQLite, migrations applied on launch, so app
updates migrate existing data). Every synced record carries a client-generated id, a sync status and a server version.
Deletes are tombstones until the server has been told.

Sync is **off by default** (`Mobile:SyncEnabled`) because the template ships no sync API: IdentityServer issues the
tokens, but the records belong to an API you build. While sync is off the app is a complete local app, edits are
still queued, and the UI shows "On device".

To turn it on, implement the contract in `IdentityServerProject.Mobile.Core/Sync/Contract/SyncContract.cs` in your API,
point `Mobile:ApiBaseAddress` at it (it defaults to the identity server's address), and set `Mobile:SyncEnabled` to `true`:

```
POST /api/sync/push                         PushRequest  -> PushResponse
GET  /api/sync/pull?cursor=&limit=          -> PullResponse
```

Requests carry a bearer token with the `api` scope. Pushes must be idempotent by record id. Optional JSON members may
be omitted when null; required ones may not, and an unreadable record is skipped rather than blocking the page.

Conflict policy: **the server is the authority.** A push the server reports as a conflict is replaced by the server's
copy and the user is told how many were replaced. A pull never overwrites a record that still has unpushed local edits.
An edit made while a request is in flight stays pending. A change the server refuses is kept, flagged with the reason,
and not resent until edited. Sync runs at start-up, on reconnect, shortly after an edit, every five minutes, and on demand.

## Adding your own entity

Each synced entity is listed explicitly in a handful of places. For a new entity `Widget`:

1. `Domain/Widget.cs`: derive from `SyncEntity`, add validation attributes, `CopyEditableFieldsFrom` and `Clone`.
2. `Data/MobileDbContext.cs`: a `DbSet<Widget>`, `ConfigureSyncEntity<Widget>` and any indexes. Store references to
   other records as plain id properties with no foreign key: a pull can deliver a child before its parent.
3. `Data/LocalDatabase.cs`: add it to `CountUnsyncedAsync` and `ClearAsync`.
4. `Sync/Contract/SyncContract.cs`: a `SyncEntityKind.Widget` value and a `WidgetData` record (optional members
   `= null`), registered on `SyncJsonContext`.
5. `Sync/SyncRecordMapper.cs`: its cases in `ToRecord` and `ApplyTo`.
6. `Sync/SyncEngine.cs`: an `AddPendingAsync` call in `SnapshotPendingAsync` (parents before children), and its cases
   in `FindAsync` and `Create`.
7. A service and interface (copy `NoteService`), registered in `MobileAppServiceCollectionExtensions`. A delete that
   should cascade to children does so in the service, through `RecordStamps.Delete`.
8. Pages and their bUnit tests, and a `BottomNav` link.
9. A migration:

   ```pwsh
   dotnet ef migrations add AddWidget --project IdentityServerProject.Mobile.Core --startup-project IdentityServerProject.Mobile.Core `
     --context MobileDbContext --output-dir Data/Migrations
   ```

   `LocalDatabaseAndMapperTests` fails if the model has changes no migration covers.

Two SQLite limits to design around: it cannot `ORDER BY` a `DateTimeOffset` (the context stores them as UTC ticks,
so they sort), and it stores `decimal` as text, so sum money in memory rather than in the query.

If you add more than a few entities, consider replacing the per-entity `switch` statements with a registry of
per-entity handlers.

## Development

```pwsh
dotnet test IdentityServerProject.Mobile.Core.Tests        # no MAUI workload needed
dotnet build IdentityServerProject.Mobile.slnx             # needs the Android workload and JDK 21
```

The shipped SQLite native library is pinned to `SQLitePCLRaw.bundle_e_sqlite3` 3.x because EF Core's default (2.1.11)
carries advisory GHSA-2m69-gcr7-jv3q.

## Known limitations

- The Android back button leaves the app instead of going back a screen.
- Signing out while online shows IdentityServer's own "Sign out?" page in the browser after the in-app confirmation.
- Any signed-in user can use the app. To restrict it, require a role in the pages' `[Authorize]` attributes and
  have IdentityServer issue that role.
- No release pipeline (signing, store packaging) and no CI job for the Android build.
