using Elsa.Extensions;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Emailing;
using Volo.Abp.Application;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Volo.Abp.TextTemplating.Scriban;
using Volo.Abp.Uow;
using Workmate.Poc.Data;
using Workmate.Poc.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Workmate.Poc;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMvcModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(AbpEntityFrameworkCoreSqliteModule),
    typeof(AbpDddApplicationModule),
    // --- Phase H: real sign-in (Identity + OpenIddict + TenantManagement + PermissionManagement)
    typeof(AbpIdentityDomainModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpIdentityAspNetCoreModule), // SignInManager — TokenController's password grant needs it
    typeof(AbpOpenIddictDomainModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpOpenIddictAspNetCoreModule),
    typeof(AbpTenantManagementDomainModule),
    typeof(AbpTenantManagementEntityFrameworkCoreModule),
    typeof(AbpPermissionManagementDomainModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpPermissionManagementDomainIdentityModule),
    // --- Spec 005: email notifications
    typeof(AbpEmailingModule),
    typeof(AbpTextTemplatingScribanModule),
    typeof(AbpBackgroundJobsDomainModule),
    typeof(AbpBackgroundJobsEntityFrameworkCoreModule))]
public class WorkmatePocModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        // --- Validate incoming bearer tokens against our own in-process OpenIddict server (no
        // separate AuthServer host, unlike ABP's "tiered" template — see plan Design §3).
        PreConfigure<OpenIddictBuilder>(b => b.AddValidation(o =>
        {
            o.AddAudiences("Workmate");
            o.UseLocalServer();
            o.UseAspNetCore();
        }));

        // --- PoC only: this app runs on plain http://localhost, not https — OpenIddict refuses
        // connect/token over http by default (ID2083). Production serves this over HTTPS and drops this line.
        PreConfigure<OpenIddictServerBuilder>(b => b.UseAspNetCore().DisableTransportSecurityRequirement());

        // --- This is a pure bearer-token API (no server-rendered Razor login pages), so
        // AbpIdentityAspNetCoreModule's own cookie-scheme authentication setup would otherwise
        // compete with OpenIddict's validation scheme for which one is "default".
        PreConfigure<AbpIdentityAspNetCoreOptions>(o => o.ConfigureAuthentication = false);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // --- The only auth scheme in this app (confirmed: disabling AbpIdentityAspNetCoreModule's own
        // cookie-scheme setup above left NO default challenge scheme registered at all — authorization
        // failures 500'd with "There was no DefaultChallengeScheme found" instead of a clean 401/403).
        context.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        // --- Reset must run before anything opens either SQLite file: Elsa's own migrations
        // run from a hosted service that can open workmate-elsa.db before the seeder does.
        PocDatabaseReset.Apply(context.Services.GetConfiguration());

        // --- Multi-tenancy: tenants now live in the real TenantManagement store (seeded in
        // DemoDataSeeder), not the config store.
        Configure<AbpMultiTenancyOptions>(o => o.IsEnabled = true);

        // --- SQLite only allows one writer at a time. Identity's own security-log write
        // (TokenController.SetSuccessResultAsync, on every sign-in) opens its own nested unit of
        // work while the outer per-request one is still open, and without this it throws
        // "database is locked" (confirmed by actually calling /connect/token, not theoretical —
        // this is also exactly what ABP's own official template sets for the SQLite DBMS).
        Configure<AbpUnitOfWorkDefaultOptions>(o => o.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        // --- No ABP MVC UI theme/bundling is used (wwwroot/index.html is hand-rolled, no Razor views) —
        // only pulled in transitively via AbpOpenIddictAspNetCoreModule. Without this, AbpMvcLibsService
        // 500s every single request because wwwroot/libs (npm-installed client libs for that theme) doesn't exist.
        Configure<AbpMvcLibsOptions>(o => o.CheckLibs = false);

        // --- Only a signed-in HOST user (the vendor admin) may act within a chosen tenant via the
        // X-Act-As-Tenant header; an ordinary employee's own token-carried tenant claim always wins
        // instead, via ABP's built-in CurrentUserTenantResolveContributor (plan Design §4).
        Configure<AbpTenantResolveOptions>(o => o.TenantResolvers.Insert(0, new HostActAsTenantResolveContributor()));

        // --- Spec 005: email retries back off from these (research R3); Development shortens them for demo.py.
        var config = context.Services.GetConfiguration();
        Configure<AbpBackgroundJobWorkerOptions>(o =>
        {
            o.DefaultFirstWaitDuration = config.GetValue("Poc:Email:RetryFirstWaitSeconds", 60);
            o.DefaultWaitFactor = config.GetValue("Poc:Email:RetryWaitFactor", 2.5);
        });

        // --- Set-password links (invite, reset) stay valid for 72 hours (research R7).
        Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(72));

        // --- EF Core + SQLite for ABP-side data (forms, submissions, processes, tasks)
        Configure<AbpDbContextOptions>(o => o.UseSqlite());
        context.Services.AddAbpDbContext<PocDbContext>(o => o.AddDefaultRepositories(includeAllEntities: true));

        // --- Elsa 3: code-defined workflows + custom activities, EF Core SQLite persistence (own file)
        var elsaConnectionString = context.Services.GetConfiguration().GetConnectionString("Elsa");
        context.Services.AddElsa(elsa =>
        {
            elsa.AddActivitiesFrom<WorkmatePocModule>();
            elsa.AddWorkflowsFrom<WorkmatePocModule>();
            elsa.UseWorkflowManagement(m => m.UseEntityFrameworkCore(ef => ef.UseSqlite(elsaConnectionString)));
            elsa.UseWorkflowRuntime(r => r.UseEntityFrameworkCore(ef => ef.UseSqlite(elsaConnectionString)));
        });
    }

    public override async Task OnPreApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // Runs before any module's OnApplicationInitializationAsync (ABP completes this phase for every
        // module first) — AbpPermissionManagementDomainModule's own hook needs these tables to exist
        // already, in that same later phase, to save WorkmatePermissionDefinitionProvider's definitions.
        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().EnsureDatabaseCreatedAsync();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();  // validates the bearer token (connect/token-issued), populates ICurrentUser
        app.UseMultiTenancy();    // resolves tenant from the now-authenticated user's claim (or X-Act-As-Tenant)
        app.UseUnitOfWork();
        app.UseAuthorization();   // [Authorize(WorkmatePermissions.X)] checks (Phase 5 / US3)
        app.UseConfiguredEndpoints();

        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
        await scope.ServiceProvider.GetRequiredService<Application.WorkflowConsistencyCheck>().RunAsync();
    }
}
