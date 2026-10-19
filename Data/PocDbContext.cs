using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Data;

[ConnectionStringName("Default")]
public class PocDbContext : AbpDbContext<PocDbContext>
{
    public DbSet<Employee> Employees => Set<Employee>();
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

    public PocDbContext(DbContextOptions<PocDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // SQLite has no schemas, so module ownership is shown with table prefixes:
        // Hr_ (stand-in for the HR module), Forms_ (Forms module), Proc_ (Processes module).
        b.Entity<Employee>(e => { e.ToTable("Hr_Employees"); e.ConfigureByConvention(); });

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
    }
}
