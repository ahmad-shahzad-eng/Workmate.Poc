using Elsa.Extensions;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.AntiForgery;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.Autofac;
using Volo.Abp.Application;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.MultiTenancy.ConfigurationStore;
using Workmate.Poc.Data;
using Workmate.Poc.Http;

namespace Workmate.Poc;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMvcModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(AbpEntityFrameworkCoreSqliteModule),
    typeof(AbpDddApplicationModule))]
public class WorkmatePocModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // --- Reset must run before anything opens either SQLite file: Elsa's own migrations
        // run from a hosted service that can open workmate-elsa.db before the seeder does.
        PocDatabaseReset.Apply(context.Services.GetConfiguration());

        // --- Multi-tenancy: two demo tenants held in configuration (no tenant management module needed)
        Configure<AbpMultiTenancyOptions>(o => o.IsEnabled = true);
        Configure<AbpDefaultTenantStoreOptions>(o =>
        {
            o.Tenants = DemoTenants.All
                .Select(t => new TenantConfiguration(t.Id, t.Name, t.Name.ToUpperInvariant()) { IsActive = true })
                .ToArray();
        });

        // --- PoC only: no cookie auth, so antiforgery validation is off
        Configure<AbpAntiForgeryOptions>(o => o.AutoValidate = false);

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

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseMiddleware<DemoUserMiddleware>(); // fake sign-in from X-Demo-User header
        app.UseMultiTenancy();                    // resolves tenant from __tenant header / user claim
        app.UseUnitOfWork();
        app.UseConfiguredEndpoints();

        using var scope = context.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
        await scope.ServiceProvider.GetRequiredService<Application.WorkflowConsistencyCheck>().RunAsync();
    }
}
