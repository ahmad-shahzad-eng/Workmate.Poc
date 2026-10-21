# Implementation Plan: Real sign-in and client onboarding

**Branch**: `003-login-onboarding` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

## Summary

Replace the PoC's fake sign-in (`X-Demo-User`/`__tenant` headers, `Employee.IsAdmin`, config-store tenants) with real ABP Identity + OpenIddict + TenantManagement + PermissionManagement, all open-source. The demo UI gets its own email/password sign-in screen that exchanges credentials for an OpenIddict access token via the resource-owner-password-credentials (ROPC) grant at `/connect/token`, then calls the API with `Authorization: Bearer <token>`. Each `Employee` links to a real `IdentityUser`; `IsAdmin` is replaced by a per-tenant `Workmate.CompanyAdmin` role carrying five permissions, and the vendor admin becomes a genuine host user holding `Workmate.Tenants.Manage`. A new `TenantOnboardingAppService` lets the vendor admin create a company + its first admin user. Tenant resolution for ordinary users already becomes claim-based (and therefore header-proof) the moment real tokens exist — ABP's built-in `CurrentUserTenantResolveContributor` already outranks the `__tenant` header for any authenticated principal; the one gap is the vendor admin (a host user, no tenant of their own), who needs a narrow, explicit way to act within a chosen tenant, added as a small custom resolver (Design §4).

## Technical Context

- **Language/Version**: C# on `net10.0` (unchanged)
- **Primary Dependencies**: ABP 9.1.0 open-source modules — Identity, OpenIddict (Domain + EntityFrameworkCore + AspNetCore, **not** the Account module — see Design §3), TenantManagement, PermissionManagement (+ its Identity bridge). All confirmed to exist at exactly `9.1.0` on nuget.org. No Elsa changes.
- **Storage**: Same `workmate-poc.db` (connection name `"Default"`), same single-`EnsureCreatedAsync` mechanism — the new modules' tables are added to `PocDbContext` via `[ReplaceDbContext]`, not separate DbContexts/files, so one reset still creates everything (Design §2). **`Poc:ResetDatabaseOnStartup=true` required once.**
- **Testing**: `demo.py` steps 1–10 re-run with real sign-in instead of `X-Demo-User` (FR-008); new step for US1 (vendor onboards a company) and US2 (no sign-up, cross-tenant refused, lockout)
- **Constraints**: no commercial packages; no Account/Razor-UI module (our own vanilla-JS sign-in screen replaces it — see Design §3); single ASP.NET Core process issues *and* validates its own tokens (no separate AuthServer, unlike ABP's "tiered" template)

## Constitution Check

| Principle / gate | Status |
| --- | --- |
| I Immutable form versions | Not touched |
| II Commit before workflow | Not touched |
| III Assignment from HR data | Not touched — `AssignmentResolver` still reads `Employee.Role`/`ManagerId`, unaffected by how the employee signed in |
| IV Tenant isolation | **Strengthened.** Claims-based resolution (already built into ABP, Design §4) means a signed-in employee's own token — not a client-sent header — decides their tenant from now on. The one new resolver added is narrowly scoped to host users only (Design §4) |
| V Typed reporting values | Not touched |
| VI One door into Elsa | Not touched |
| VII Admin is a permission | **Realized for real.** `IsAdmin`/vendor-admin-id checks become ABP permissions (`Workmate.*`) checked via `[Authorize]`, replacing `CurrentEmployeeProvider.EnsureAdminAsync()`'s hand-rolled check (Design §5) |
| Gates | CHANGELOG line per change; `demo.py` re-verified end to end with real sign-in; build/run/demo loop must pass after `Poc:ResetDatabaseOnStartup=true` |

Result: **PASS**, no exceptions needed.

## Design

### 1. Module wiring (`WorkmatePocModule.cs`)

Add to `[DependsOn(...)]` — confirmed against ABP 9.1's own module source (`rel-9.1` branch), not guessed:

```csharp
typeof(AbpIdentityDomainModule), typeof(AbpIdentityEntityFrameworkCoreModule),
typeof(AbpOpenIddictDomainModule), typeof(AbpOpenIddictEntityFrameworkCoreModule), typeof(AbpOpenIddictAspNetCoreModule),
typeof(AbpTenantManagementDomainModule), typeof(AbpTenantManagementEntityFrameworkCoreModule),
typeof(AbpPermissionManagementDomainModule), typeof(AbpPermissionManagementEntityFrameworkCoreModule),
typeof(AbpPermissionManagementDomainIdentityModule) // bridges Identity users/roles into the "U"/"R" permission-grant providers
```

NuGet packages (all `9.1.0`): `Volo.Abp.Identity.Domain`, `Volo.Abp.Identity.EntityFrameworkCore`, `Volo.Abp.OpenIddict.Domain`, `Volo.Abp.OpenIddict.EntityFrameworkCore`, `Volo.Abp.OpenIddict.AspNetCore`, `Volo.Abp.TenantManagement.Domain`, `Volo.Abp.TenantManagement.EntityFrameworkCore`, `Volo.Abp.PermissionManagement.Domain`, `Volo.Abp.PermissionManagement.EntityFrameworkCore`, `Volo.Abp.PermissionManagement.Domain.Identity`.

Remove: the `Configure<AbpDefaultTenantStoreOptions>` block (FR-003 — tenants now come from the `Tenant` entity via `AbpTenantManagementDomainModule`'s own store, which wins automatically once registered) and the `Configure<AbpAntiForgeryOptions> { AutoValidate = false }` line (no longer needed: the demo UI calls the API with a bearer token, not a same-site cookie, so there's nothing for antiforgery to protect against — token auth is immune to CSRF by construction).

### 2. One `PocDbContext`, four more modules' tables (`Data/PocDbContext.cs`)

Same pattern the official ABP template uses for a single-database app — implement each module's DbContext marker interface directly on `PocDbContext` so `AddDefaultRepositories(includeAllEntities: true)` (already called) wires their repositories to *our* context and *our* single `EnsureCreatedAsync()`, instead of each module spinning up its own default DbContext class:

```csharp
[ConnectionStringName("Default")]
public class PocDbContext : AbpDbContext<PocDbContext>, IIdentityDbContext, IOpenIddictDbContext,
    ITenantManagementDbContext, IPermissionManagementDbContext
{
    // existing DbSets unchanged, plus (exact members each interface requires, confirmed from source):
    public DbSet<IdentityUser> Users => Set<IdentityUser>();
    public DbSet<IdentityRole> Roles => Set<IdentityRole>();
    public DbSet<IdentityClaimType> ClaimTypes => Set<IdentityClaimType>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<IdentitySecurityLog> SecurityLogs => Set<IdentitySecurityLog>();
    public DbSet<IdentityLinkUser> LinkUsers => Set<IdentityLinkUser>();
    public DbSet<IdentityUserDelegation> UserDelegations => Set<IdentityUserDelegation>();
    public DbSet<IdentitySession> Sessions => Set<IdentitySession>();
    public DbSet<OpenIddictApplication> Applications => Set<OpenIddictApplication>();
    public DbSet<OpenIddictAuthorization> Authorizations => Set<OpenIddictAuthorization>();
    public DbSet<OpenIddictScope> Scopes => Set<OpenIddictScope>();
    public DbSet<OpenIddictToken> Tokens => Set<OpenIddictToken>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantConnectionString> TenantConnectionStrings => Set<TenantConnectionString>();
    public DbSet<PermissionGroupDefinitionRecord> PermissionGroups => Set<PermissionGroupDefinitionRecord>();
    public DbSet<PermissionDefinitionRecord> Permissions => Set<PermissionDefinitionRecord>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
}
```

`OnModelCreating`: add `b.ConfigureIdentity(); b.ConfigureOpenIddict(); b.ConfigureTenantManagement(); b.ConfigurePermissionManagement();` alongside the existing `b.Entity<...>()` calls (order doesn't matter; these are the exact four extension methods the official template calls for a single shared context).

**Risk flagged for first build**: `[IgnoreMultiTenancy]` on `IOpenIddictDbContext`/`ITenantManagementDbContext` (confirmed on both interfaces) means those tables are *not* filtered by `ICurrentTenant` — correct, since OpenIddict clients/tokens and the tenant catalogue itself are host-level concepts, not per-tenant data; nothing to change, just don't be surprised that `IRepository<OpenIddictApplication>` returns rows across all tenants.

### 3. OpenIddict: server, validation, and why **not** the Account module

`AbpOpenIddictAspNetCoreModule.ConfigureServices` (confirmed by reading its source) already calls `.AllowPasswordFlow()` and registers `/connect/token` with a `TokenController` that fully implements ROPC — looks up the user via `IdentityUserManager`, checks the password and lockout via `SignInManager.CheckPasswordSignInAsync`, and signs in with `SignInManager.CreateUserPrincipalAsync` (claims include `sub`=UserId and the tenant claim ABP always attaches for a tenant-bound user). **None of this lives in `Volo.Abp.Account`** — that module only adds Razor login/registration *pages*, which we don't want (FR-002 is satisfied more strongly by never loading the module that contains a registration endpoint at all, rather than loading it and toggling a setting to disable one). So: no `Volo.Abp.Account.*` package, at all.

Add to `WorkmatePocModule.PreConfigureServices` (new method — today's module has none), so incoming `Authorization: Bearer` tokens on `PocController` calls validate in-process against our own token-issuing server (no second host, no network round trip):

```csharp
public override void PreConfigureServices(ServiceConfigurationContext context)
{
    PreConfigure<OpenIddictBuilder>(b => b.AddValidation(o =>
    {
        o.AddAudiences("Workmate");
        o.UseLocalServer();
        o.UseAspNetCore();
    }));
}
```

Pipeline (`OnApplicationInitializationAsync`): add `app.UseAuthentication();` and `app.UseAuthorization();` (neither exists today — nothing needed them before). Order, confirmed against ABP's own non-tiered Web module: `UseRouting() → UseAuthentication() → UseMultiTenancy() → UseUnitOfWork() → UseAuthorization() → UseConfiguredEndpoints()`. Authentication must run **before** multi-tenancy so `ICurrentUser.IsAuthenticated`/`TenantId` are populated in time for tenant resolution (Design §4).

**Client registration** (new `Data/OpenIddictDataSeeder.cs`, called once from `SeedAsync`, mirrors the official template's `CreateApplicationAsync` helper): one **public** client, `ClientTypes.Public`, no secret (our vanilla-JS page can't keep a secret), `GrantTypes.Password` + `GrantTypes.RefreshToken` only (no authorization-code flow — FR-007 decided ROPC, Clarifications), scope `"Workmate"` (registered via `IOpenIddictScopeManager.CreateAsync`, matching the validation audience above).

### 4. Tenant resolution: mostly free, one real gap

**Confirmed from ABP's own source, not assumed:** `AbpMultiTenancyModule.ConfigureServices` already does `options.TenantResolvers.Insert(0, new CurrentUserTenantResolveContributor())`, and that contributor sets `context.Handled = true` for *any authenticated* `ICurrentUser` using `currentUser.TenantId` — **before** the header/query/route/cookie resolvers (added later, at the end of the list, by `AbpAspNetCoreMultiTenancyModule`) ever run. So the moment `DemoUserMiddleware` is gone and `UseAuthentication()` populates a real, signed, server-issued claims principal, an Acme employee's own token-carried `TenantId` claim **already** wins over any `__tenant` header they might still send — closing US2 scenario 2 with zero new resolver code. (Today's vulnerability exists only because `DemoUserMiddleware` copies the client-sent `__tenant` header straight into a `TenantId` *claim*, laundering an unverified header through the exact mechanism meant to trust only verified identity.)

**The gap:** that same built-in contributor sets `Handled = true` for the vendor admin too (a host user, `TenantId == null`) — so it *always* resolves the vendor admin to "no tenant", with no way for them to act within a specific company's context. That's wrong for this PoC: Aramis needs to open Form designer, Routing, etc. *for* a chosen company. Fix with one small, narrowly-scoped contributor, inserted before the built-in one:

```csharp
// Http/HostActAsTenantResolveContributor.cs
public class HostActAsTenantResolveContributor : TenantResolveContributorBase
{
    public const string ContributorName = "HostActAs";
    public override string Name => ContributorName;

    public override Task ResolveAsync(ITenantResolveContext context)
    {
        var currentUser = context.ServiceProvider.GetRequiredService<ICurrentUser>();
        // Only a signed-in HOST user (no tenant of their own) may pick one explicitly;
        // an ordinary tenant-bound employee's own claim always wins instead (US2 scenario 2).
        if (currentUser.IsAuthenticated && currentUser.TenantId == null)
        {
            var httpContext = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
            var tenantId = httpContext?.Request.Headers["X-Act-As-Tenant"].FirstOrDefault();
            if (Guid.TryParse(tenantId, out _))
            {
                context.Handled = true;
                context.TenantIdOrName = tenantId;
            }
        }
        return Task.CompletedTask;
    }
}
```

Registered in `WorkmatePocModule.ConfigureServices`: `Configure<AbpTenantResolveOptions>(o => o.TenantResolvers.Insert(0, new HostActAsTenantResolveContributor()));`. A header is still the carrier here — but unlike today, it's a *new*, deliberately-named header (`X-Act-As-Tenant`, never `__tenant`) that this contributor only even looks at once the real, signed-in identity is confirmed to be a host user; a tenant-bound employee sending it has no effect, because the built-in `CurrentUserTenantResolveContributor` already claimed the request one slot earlier in the *normal* case — wait, this contributor runs *first* (inserted at index 0, ahead of the built-in one), so correctness instead rests entirely on its own `currentUser.TenantId == null` guard, which an ordinary employee can never satisfy (their token's tenant claim is non-null, set by the server at sign-in, not by them).

The `/connect/token` request itself is unauthenticated (that's the point), so neither contributor fires for it; the pre-existing `HeaderTenantResolveContributor` (`__tenant`) still resolves tenant for the login screen's "Company" field — legitimate here, since nothing is being bypassed: the server still checks the password against whichever tenant's user table that header names (FR-007 covers the login call specifically).

### 5. Permissions (`Application/WorkmatePermissions.cs`, `Application/WorkmatePermissionDefinitionProvider.cs`, both new)

```csharp
public static class WorkmatePermissions
{
    public const string GroupName = "Workmate";
    public const string FormsDesign = GroupName + ".Forms.Design";
    public const string RoutingManage = GroupName + ".Routing.Manage";
    public const string WorkflowsDesign = GroupName + ".Workflows.Design";
    public const string EmployeesManage = GroupName + ".Employees.Manage";
    public const string OrgManage = GroupName + ".Org.Manage";
    public const string TenantsManage = GroupName + ".Tenants.Manage"; // host-only
}

public class WorkmatePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var g = context.AddGroup(WorkmatePermissions.GroupName);
        g.AddPermission(WorkmatePermissions.FormsDesign, multiTenancySide: MultiTenancySides.Tenant);
        g.AddPermission(WorkmatePermissions.RoutingManage, multiTenancySide: MultiTenancySides.Tenant);
        g.AddPermission(WorkmatePermissions.WorkflowsDesign, multiTenancySide: MultiTenancySides.Tenant);
        g.AddPermission(WorkmatePermissions.EmployeesManage, multiTenancySide: MultiTenancySides.Tenant);
        g.AddPermission(WorkmatePermissions.OrgManage, multiTenancySide: MultiTenancySides.Tenant);
        g.AddPermission(WorkmatePermissions.TenantsManage, multiTenancySide: MultiTenancySides.Host);
    }
}
```

`displayName` is optional (confirmed: `PermissionGroupDefinition.AddPermission` defaults it to the permission name itself) — no localization resource needed, consistent with the rest of this PoC having none.

**Company admin = a real per-tenant role**, not a per-user flag: seed an ABP `IdentityRole` named `"Workmate.CompanyAdmin"` inside each tenant's context, grant it the five tenant-side permissions via `IPermissionManager.SetAsync(name, RolePermissionValueProvider.ProviderName, "Workmate.CompanyAdmin", true)` once per tenant, and add Khalid/Noor to it (`UserManager.AddToRoleAsync`). The vendor admin gets `WorkmatePermissions.TenantsManage` granted directly to their *user* id (`UserPermissionValueProvider.ProviderName`) in the **host** context (`_currentTenant.Change(null)`) — a role would be ceremony for a single person.

Controllers/app services: replace every `await _currentEmployee.EnsureAdminAsync()` call with `[Authorize(WorkmatePermissions.X)]` on the method (standard ABP convention — confirmed `[Authorize(string policyName)]` from `Microsoft.AspNetCore.Authorization`, which ABP's dynamic policy provider resolves against any defined permission name with zero extra wiring). `CurrentEmployeeProvider.EnsureAdminAsync()` is deleted; `GetAsync()` (resolve the signed-in user's `Employee` row) stays, since it's still needed for everything that isn't a design/manage action.

### 6. `Employee` ↔ `IdentityUser` linkage and seeding (`Domain/Employee.cs`, `Data/DemoDataSeeder.cs`)

`Employee.IsAdmin` removed; add `public Guid UserId { get; set; }` (FR-004 — the employee *is* identified by this id now, not by `Employee.Id` doubling as the user id as today's comment says). `CurrentEmployeeProvider.GetAsync()` becomes `_employees.FindAsync(e => e.UserId == _currentUser.Id)`.

`DemoDataSeeder` (new `IdentityUserManager _userManager`, `IdentityRoleManager _roleManager`, `IPermissionManager _permissionManager` dependencies) creates one `IdentityUser` per demo person *inside* the tenant's `_currentTenant.Change(tenant.Id)` block (same pattern already used for `Employee`/`CompanyFormSettings`), with a password meeting ABP's default `IdentityOptions.Password` policy (e.g. `"1q2w3E*"`, the same test-safe string ABP's own template seeds its clients with) and `user.Name`/email derived from the demo person's name (`sara@acme.demo`, etc. — emails must be unique per ABP's `RequireUniqueEmail = true` default, confirmed in `AbpIdentityDomainModule`). The vendor admin's `IdentityUser` is created with `_currentTenant.Change(null)` (host).

Tenants themselves move from `DemoTenants.All`'s hard-coded list to real `Tenant` entities, seeded with the **same GUIDs** (`a1a1a1a1-...`, `b2b2b2b2-...`) so every other hard-coded id already in `demo.py`/`HANDOVER.md` keeps working unchanged — via `Volo.Abp.TenantManagement.TenantManager.CreateAsync(id, name)` (domain service, handles the uniqueness check) + `_tenantRepository.InsertAsync`.

### 7. Vendor onboarding (`Application/TenantOnboardingAppService.cs`, new — `[Authorize(WorkmatePermissions.TenantsManage)]`)

`CreateCompanyAsync(name, adminEmail, adminName)`: `TenantManager.CreateAsync` (new tenant, host context) → `using (_currentTenant.Change(tenant.Id))` → create the `IdentityUser` admin (random generated password — US1 AC1 says "shown on screen until \[email, spec 005\]") → seed the tenant's `Workmate.CompanyAdmin` role + grant it the five permissions (same helper as Design §5, extracted so both the seeder and this service call it) → create the matching `Employee` row (`IsAdmin` is gone; being in the role *is* being admin) → add the user to the role. Returns the generated password in the response DTO for the on-screen display (US1 AC1).

`ResetClientAdminPasswordAsync(employeeId, newPassword?)` (Edge Case: "forgot password... until then the vendor admin resets it"): `UserManager.RemovePasswordAsync` + `AddPasswordAsync` with a freshly generated password, returned the same way.

### 8. Demo UI (`wwwroot/index.html`)

Replace the "Acting as" dropdown with a sign-in screen: email + password fields, POST to `/connect/token` as a URL-encoded form body (`grant_type=password&username=...&password=...&client_id=Workmate_Demo&scope=Workmate+offline_access`), storing `access_token` (and `refresh_token`) in `sessionStorage` (cleared on tab close — reasonable for a demo, no "remember me" requirement in scope). Every `api()` call adds `Authorization: Bearer <token>` instead of `X-Demo-User`; the `__tenant` header is dropped entirely for ordinary calls. The "Company" dropdown stays **only for the vendor admin** (detected by the signed-in token having no tenant claim — same `IsAuthenticated && TenantId==null` shape the server checks) and, when present, sends `X-Act-As-Tenant` instead of the old `__tenant`. A 401 from any call clears the stored token and returns to the sign-in screen (token expiry/lockout).

### 9. `demo.py`

New `sign_in(tenant_id_or_none, email, password)` helper: POST `/connect/token` (form-encoded, `tenant_id_or_none` sent as `__tenant` only for this unauthenticated call, matching Design §4's legitimate login-time use), returns the access token; every `call(...)` site's `user=` parameter becomes `token=` sending `Authorization: Bearer`. Demo users need stable emails to sign in as — `DemoDataSeeder` seeds them deterministically (`sara@acme.demo`, etc.) so `demo.py` can hard-code them like it already hard-codes tenant ids.

## Risks and research (verify on first build)

- **Access token format.** `AbpOpenIddictAspNetCoreModule` calls `.DisableAccessTokenEncryption()` but does not call `.UseReferenceAccessTokens()`, so tokens are self-contained (JWT-shaped), not opaque references — irrelevant to correctness here since validation uses `.UseLocalServer()` (in-process, not a signature/JWKS check from the resource-server side), but worth confirming the actual wire format while testing `demo.py`'s token handling.
- **`AbpUnitOfWorkDefaultOptions.TransactionBehavior`.** The official template disables SQLite transactions for exactly this multi-module combination; our existing `PocDbContext` has worked without that so far, but token issuance + permission checks add more reads/writes per request — watch for "database is locked"/transaction errors under the demo's single-SQLite-file setup and add `Configure<AbpUnitOfWorkDefaultOptions>(o => o.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);` if so.
- **Authorization policy wiring.** `[Authorize(WorkmatePermissions.X)]` relies on ABP's dynamic policy provider being active; confirm it resolves correctly on `PocController`'s explicit (non-conventional) routes the same way it does on the official template's conventional ones — if not, an explicit `services.AddAuthorization()` call may be needed (likely already added transitively by the new modules, but unconfirmed until the first real `[Authorize]` check runs).
- **OpenIddict's token cleanup background worker** (`AbpOpenIddictDomainModule.OnApplicationInitializationAsync`) starts automatically once the domain module loads; harmless for a demo, not something to disable.
- **Password/email policy specifics** (exact default length/complexity rules, `RequireUniqueEmail`) are read from ABP's own `IdentityOptions` defaults, not hand-verified against the 9.1.0 source line-by-line — if the chosen demo password/email scheme fails validation on first seed run, adjust the literal strings only, not the approach.

## Files touched

`Workmate.Poc.csproj` (10 new package refs), `WorkmatePocModule.cs` (DependsOn, `PreConfigureServices` new, pipeline), `Data/PocDbContext.cs` (4 `[ReplaceDbContext]` interfaces + DbSets + `Configure*()` calls), `Data/OpenIddictDataSeeder.cs` (new), `Data/DemoDataSeeder.cs` (IdentityUser/role/permission seeding, tenants via `TenantManager`), `Domain/Employee.cs` (`UserId`, drop `IsAdmin`), `Domain/DemoTenants.cs` (ids/names only — tenant rows move to the real store), `Http/DemoUserMiddleware.cs` (deleted), `Http/HostActAsTenantResolveContributor.cs` (new), `Application/CurrentEmployeeProvider.cs` (drop `EnsureAdminAsync`), `Application/WorkmatePermissions.cs`, `WorkmatePermissionDefinitionProvider.cs`, `TenantOnboardingAppService.cs` (new), every existing `*AppService.cs` (`[Authorize(WorkmatePermissions.X)]` replacing `EnsureAdminAsync()` calls), `Application/Dtos.cs` (onboarding DTOs), `Controllers/PocController.cs` (onboarding routes), `wwwroot/index.html` (sign-in screen, token storage, drop "Acting as"), `demo.py` (sign-in helper, bearer tokens), `README.md`, `CHANGELOG.md`, `HANDOVER.md`, `.specify/memory/constitution.md` (§6 boundary: fake sign-in retired).
