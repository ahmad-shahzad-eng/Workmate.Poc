using System.Text.RegularExpressions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine;

namespace Workmate.Poc.Application;

/// <summary>Admin-only designer for approval workflows (Phase D, option B): draft/publish, mirrors FormAppService.</summary>
public class ApprovalWorkflowAppService : ApplicationService
{
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9-]{1,49}$", RegexOptions.Compiled);

    private const string StarterStepsJson = """
    {"steps":[{"title":"Step 1","approverType":"line-manager","onReject":"end"}]}
    """;

    private readonly CurrentEmployeeProvider _currentEmployee;
    private readonly IWorkflowGateway _gateway;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IRepository<ApprovalWorkflow, Guid> _workflows;
    private readonly IRepository<ApprovalWorkflowVersion, Guid> _versions;

    public ApprovalWorkflowAppService(CurrentEmployeeProvider currentEmployee, IWorkflowGateway gateway,
        IRepository<Employee, Guid> employees, IRepository<ApprovalWorkflow, Guid> workflows,
        IRepository<ApprovalWorkflowVersion, Guid> versions)
    {
        _currentEmployee = currentEmployee; _gateway = gateway; _employees = employees;
        _workflows = workflows; _versions = versions;
    }

    public virtual async Task<List<ApprovalWorkflowListItemDto>> GetAllAsync()
    {
        await _currentEmployee.EnsureAdminAsync();
        var workflows = await _workflows.GetListAsync();
        var workflowIds = workflows.Select(w => w.Id).ToList();
        var versions = await _versions.GetListAsync(v => workflowIds.Contains(v.ApprovalWorkflowId));

        return workflows.OrderBy(w => w.Name).Select(w =>
        {
            var mine = versions.Where(v => v.ApprovalWorkflowId == w.Id).ToList();
            var published = mine.FirstOrDefault(v => v.Id == w.CurrentVersionId);
            return new ApprovalWorkflowListItemDto(w.Key, w.Name, w.IsActive, published?.VersionNo,
                mine.Any(v => v.Status == FormVersionStatus.Draft));
        }).ToList();
    }

    public virtual async Task<ApprovalWorkflowDesignDto> CreateAsync(CreateApprovalWorkflowInput input)
    {
        await _currentEmployee.EnsureAdminAsync();
        var key = (input.Key ?? "").Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(key))
            throw new UserFriendlyException("Workflow key must be 2-50 characters: lowercase letters, digits and dashes, starting with a letter.");
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new UserFriendlyException("Workflow name is required.");
        if (await _workflows.AnyAsync(w => w.Key == key))
            throw new UserFriendlyException($"A workflow with key '{key}' already exists.");

        var workflow = new ApprovalWorkflow(GuidGenerator.Create(), CurrentTenant.Id, key, input.Name.Trim(),
            GuidGenerator.Create().ToString());
        await _workflows.InsertAsync(workflow);
        await _versions.InsertAsync(new ApprovalWorkflowVersion(GuidGenerator.Create(), CurrentTenant.Id, workflow.Id, 1, StarterStepsJson));

        await CurrentUnitOfWork!.SaveChangesAsync();
        return await GetDesignAsync(key);
    }

    public virtual async Task<ApprovalWorkflowDesignDto> GetDesignAsync(string key)
    {
        var workflow = await GetWorkflowAsync(key);
        var versions = await GetVersionsAsync(workflow.Id);
        return new ApprovalWorkflowDesignDto(workflow.Key, workflow.Name, workflow.CurrentVersionId, versions.Select(Map).ToList());
    }

    /// <summary>Updates the open draft, or opens a new draft after the latest published version.</summary>
    public virtual async Task<ApprovalWorkflowVersionDto> SaveDraftAsync(string key, SaveApprovalWorkflowDraftInput input)
    {
        await _currentEmployee.EnsureAdminAsync();
        var schema = ApprovalWorkflowSchema.Parse(input.StepsJson);
        await ValidateTargetsAsync(schema, input.FallbackRule, requireFallback: false);

        var workflow = await GetWorkflowAsync(key);
        var latest = (await GetVersionsAsync(workflow.Id)).First();

        if (latest.Status == FormVersionStatus.Draft)
        {
            latest.StepsJson = input.StepsJson;
            latest.FallbackRule = input.FallbackRule;
            await _versions.UpdateAsync(latest);
            return Map(latest);
        }

        var draft = new ApprovalWorkflowVersion(GuidGenerator.Create(), CurrentTenant.Id, workflow.Id, latest.VersionNo + 1, input.StepsJson)
        {
            FallbackRule = input.FallbackRule
        };
        await _versions.InsertAsync(draft);
        return Map(draft);
    }

    /// <summary>Builds the Elsa graph and publishes it through the gateway. The previous version is retired but keeps running instances on their own version.</summary>
    public virtual async Task<ApprovalWorkflowVersionDto> PublishAsync(string key)
    {
        await _currentEmployee.EnsureAdminAsync();
        var workflow = await GetWorkflowAsync(key);
        var versions = await GetVersionsAsync(workflow.Id);
        var draft = versions.FirstOrDefault(v => v.Status == FormVersionStatus.Draft)
                    ?? throw new UserFriendlyException("There is no draft to publish. Save a draft first.");

        var schema = ApprovalWorkflowSchema.Parse(draft.StepsJson);
        await ValidateTargetsAsync(schema, draft.FallbackRule, requireFallback: true);

        var root = ApprovalWorkflowGraphBuilder.Build(schema.Steps, draft.FallbackRule!);
        var (_, elsaVersion) = await _gateway.PublishDesignedWorkflowAsync(
            workflow.ElsaDefinitionId, workflow.Name, CurrentTenant.Id!.Value, root);

        foreach (var published in versions.Where(v => v.Status == FormVersionStatus.Published))
        {
            published.Status = FormVersionStatus.Retired;
            await _versions.UpdateAsync(published);
        }

        draft.Status = FormVersionStatus.Published;
        draft.ElsaDefinitionVersion = elsaVersion;
        draft.PublishedAt = Clock.Now;
        await _versions.UpdateAsync(draft);

        workflow.CurrentVersionId = draft.Id;
        await _workflows.UpdateAsync(workflow);
        return Map(draft);
    }

    /// <summary>Checks every role:/user: step target and the fallback rule against this tenant's HR data (FR-005, FR-010).</summary>
    private async Task ValidateTargetsAsync(ApprovalWorkflowSchema schema, string? fallbackRule, bool requireFallback)
    {
        foreach (var step in schema.Steps)
        {
            if (step.ApproverType == ApproverTypes.Role && !await _employees.AnyAsync(e => e.Role == step.Role))
                throw new UserFriendlyException($"Role '{step.Role}' has no employees in this company yet.");
            if (step.ApproverType == ApproverTypes.User && !await _employees.AnyAsync(e => e.Id == step.UserId))
                throw new UserFriendlyException($"The chosen user for '{step.Title}' does not belong to this company.");
        }

        if (!requireFallback && string.IsNullOrWhiteSpace(fallbackRule)) return; // draft: fallback is optional until publish

        var (type, role, userId) = FallbackRules.Parse(fallbackRule);
        if (type == ApproverTypes.Role && !await _employees.AnyAsync(e => e.Role == role))
            throw new UserFriendlyException($"Fallback role '{role}' has no employees in this company yet.");
        if (type == ApproverTypes.User && !await _employees.AnyAsync(e => e.Id == userId))
            throw new UserFriendlyException("The fallback user does not belong to this company.");
    }

    private async Task<ApprovalWorkflow> GetWorkflowAsync(string key) =>
        await _workflows.FindAsync(w => w.Key == key) ?? throw new UserFriendlyException($"Workflow '{key}' does not exist.");

    private async Task<List<ApprovalWorkflowVersion>> GetVersionsAsync(Guid workflowId) =>
        (await _versions.GetListAsync(v => v.ApprovalWorkflowId == workflowId)).OrderByDescending(v => v.VersionNo).ToList();

    private static ApprovalWorkflowVersionDto Map(ApprovalWorkflowVersion v) =>
        new(v.Id, v.VersionNo, v.Status, v.StepsJson, v.FallbackRule, v.PublishedAt);
}
