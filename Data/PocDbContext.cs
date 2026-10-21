using Microsoft.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Authorizations;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.OpenIddict.Tokens;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Data;

// Phase H: one shared DbContext for every ABP module too (Identity/OpenIddict/TenantManagement/
// PermissionManagement), via [ReplaceDbContext] — so the single EnsureCreatedAsync() call still
// creates the whole schema, same as every other module in this PoC (see HANDOVER's reset-required note).
[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(IOpenIddictDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ReplaceDbContext(typeof(IPermissionManagementDbContext))]
[ReplaceDbContext(typeof(IBackgroundJobsDbContext))]
[ConnectionStringName("Default")]
public class PocDbContext : AbpDbContext<PocDbContext>,
    IIdentityDbContext, IOpenIddictDbContext, ITenantManagementDbContext, IPermissionManagementDbContext,
    IBackgroundJobsDbContext
{
    // --- Identity
    public DbSet<IdentityUser> Users => Set<IdentityUser>();
    public DbSet<IdentityRole> Roles => Set<IdentityRole>();
    public DbSet<IdentityClaimType> ClaimTypes => Set<IdentityClaimType>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<IdentitySecurityLog> SecurityLogs => Set<IdentitySecurityLog>();
    public DbSet<IdentityLinkUser> LinkUsers => Set<IdentityLinkUser>();
    public DbSet<IdentityUserDelegation> UserDelegations => Set<IdentityUserDelegation>();
    public DbSet<IdentitySession> Sessions => Set<IdentitySession>();
    // --- OpenIddict
    public DbSet<OpenIddictApplication> Applications => Set<OpenIddictApplication>();
    public DbSet<OpenIddictAuthorization> Authorizations => Set<OpenIddictAuthorization>();
    public DbSet<OpenIddictScope> Scopes => Set<OpenIddictScope>();
    public DbSet<OpenIddictToken> Tokens => Set<OpenIddictToken>();
    // --- TenantManagement
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantConnectionString> TenantConnectionStrings => Set<TenantConnectionString>();
    // --- PermissionManagement
    public DbSet<PermissionGroupDefinitionRecord> PermissionGroups => Set<PermissionGroupDefinitionRecord>();
    public DbSet<PermissionDefinitionRecord> Permissions => Set<PermissionDefinitionRecord>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
    // --- BackgroundJobs (spec 005: the persistent email queue)
    public DbSet<BackgroundJobRecord> BackgroundJobs => Set<BackgroundJobRecord>();

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<JobRole> JobRoles => Set<JobRole>();
    public DbSet<EmployeeJobRole> EmployeeJobRoles => Set<EmployeeJobRole>();
    public DbSet<DepartmentHead> DepartmentHeads => Set<DepartmentHead>();
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormDefinitionVersion> FormDefinitionVersions => Set<FormDefinitionVersion>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<FormSubmissionValue> FormSubmissionValues => Set<FormSubmissionValue>();
    public DbSet<ProcessBinding> ProcessBindings => Set<ProcessBinding>();
    public DbSet<ProcessInstance> ProcessInstances => Set<ProcessInstance>();
    public DbSet<ProcessTask> ProcessTasks => Set<ProcessTask>();
    public DbSet<ProcessTaskCandidate> ProcessTaskCandidates => Set<ProcessTaskCandidate>();
    public DbSet<ProcessEvent> ProcessEvents => Set<ProcessEvent>();
    public DbSet<ApprovalWorkflow> ApprovalWorkflows => Set<ApprovalWorkflow>();
    public DbSet<ApprovalWorkflowVersion> ApprovalWorkflowVersions => Set<ApprovalWorkflowVersion>();
    public DbSet<CompanyFormSettings> CompanyFormSettings => Set<CompanyFormSettings>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<CompanyEmailSettings> CompanyEmailSettings => Set<CompanyEmailSettings>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<CompanyBranding> CompanyBrandings => Set<CompanyBranding>();

    public PocDbContext(DbContextOptions<PocDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.ConfigureIdentity();
        b.ConfigureOpenIddict();
        b.ConfigureTenantManagement();
        b.ConfigurePermissionManagement();
        b.ConfigureBackgroundJobs();

        // SQLite has no schemas, so module ownership is shown with table prefixes:
        // Hr_ (stand-in for the HR module), Forms_ (Forms module), Proc_ (Processes module).
        b.Entity<Employee>(e => { e.ToTable("Hr_Employees"); e.ConfigureByConvention(); });
        b.Entity<JobRole>(e =>
        {
            e.ToTable("Hr_JobRoles"); e.ConfigureByConvention();
            e.Property(x => x.Name).UseCollation("NOCASE"); // "HR" and "hr" are the same role
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });
        b.Entity<EmployeeJobRole>(e =>
        {
            e.ToTable("Hr_EmployeeJobRoles"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.EmployeeId, x.JobRoleId }).IsUnique();
        });
        b.Entity<DepartmentHead>(e =>
        {
            e.ToTable("Hr_DepartmentHeads"); e.ConfigureByConvention();
            e.HasIndex(x => x.OrganizationUnitId).IsUnique();
        });

        b.Entity<FormDefinition>(e =>
        {
            e.ToTable("Forms_Definitions"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });
        b.Entity<FormDefinitionVersion>(e =>
        {
            e.ToTable("Forms_DefinitionVersions"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.FormDefinitionId, x.VersionNo }).IsUnique();
        });
        b.Entity<FormSubmission>(e => { e.ToTable("Forms_Submissions"); e.ConfigureByConvention(); });
        b.Entity<FormSubmissionValue>(e =>
        {
            e.ToTable("Forms_SubmissionValues"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.SubmissionId, x.FieldKey });
        });

        b.Entity<ProcessBinding>(e => { e.ToTable("Proc_Bindings"); e.ConfigureByConvention(); });
        b.Entity<ProcessInstance>(e => { e.ToTable("Proc_Instances"); e.ConfigureByConvention(); });
        b.Entity<ProcessTask>(e => { e.ToTable("Proc_Tasks"); e.ConfigureByConvention(); });
        b.Entity<ProcessTaskCandidate>(e =>
        {
            e.ToTable("Proc_TaskCandidates"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.UserId, x.TaskId });
        });
        b.Entity<ProcessEvent>(e => { e.ToTable("Proc_Events"); e.ConfigureByConvention(); });

        b.Entity<ApprovalWorkflow>(e =>
        {
            e.ToTable("Proc_ApprovalWorkflows"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });
        b.Entity<ApprovalWorkflowVersion>(e =>
        {
            e.ToTable("Proc_ApprovalWorkflowVersions"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.ApprovalWorkflowId, x.VersionNo }).IsUnique();
        });

        b.Entity<CompanyFormSettings>(e =>
        {
            e.ToTable("Forms_CompanySettings"); e.ConfigureByConvention();
            e.HasIndex(x => x.TenantId).IsUnique();
        });

        // Notif_ (spec 005: email notifications)
        b.Entity<EmailTemplate>(e =>
        {
            e.ToTable("Notif_EmailTemplates"); e.ConfigureByConvention();
            e.HasIndex(x => new { x.TenantId, x.FormDefinitionId, x.Event, x.Audience }).IsUnique();
        });
        b.Entity<CompanyEmailSettings>(e =>
        {
            e.ToTable("Notif_CompanyEmailSettings"); e.ConfigureByConvention();
            e.HasIndex(x => x.TenantId).IsUnique();
        });
        b.Entity<EmailMessage>(e =>
        {
            e.ToTable("Notif_EmailMessages"); e.ConfigureByConvention();
            e.Property(x => x.LastError).HasMaxLength(EmailMessage.MaxErrorLength);
            e.HasIndex(x => new { x.TenantId, x.Status });
        });

        // Ui_ (spec 007: per-company branding)
        b.Entity<CompanyBranding>(e =>
        {
            e.ToTable("Ui_CompanyBranding"); e.ConfigureByConvention();
            e.Property(x => x.DisplayName).HasMaxLength(CompanyBranding.MaxDisplayNameLength);
            e.Property(x => x.BrandColor).HasMaxLength(7);
            e.HasIndex(x => x.TenantId).IsUnique();
        });
    }
}
