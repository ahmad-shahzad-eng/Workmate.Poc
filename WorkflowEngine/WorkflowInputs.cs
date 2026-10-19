using Elsa.Workflows;

namespace Workmate.Poc.WorkflowEngine;

/// <summary>
/// Keys passed into every workflow. The tenant id travels with the workflow so activities can
/// re-apply ABP's tenant context no matter which thread or scope Elsa runs them on.
/// </summary>
public record WorkflowInputs(Guid TenantId, Guid ProcessInstanceId, Guid SubmissionId)
{
    public const string TenantIdKey = "tenantId";
    public const string ProcessInstanceIdKey = "processInstanceId";
    public const string SubmissionIdKey = "submissionId";
    public const string OutcomeKey = "outcome";

    public static IDictionary<string, object> Create(Guid tenantId, Guid processInstanceId, Guid submissionId,
        IDictionary<string, string?> variables)
    {
        var input = new Dictionary<string, object>
        {
            [TenantIdKey] = tenantId.ToString(),
            [ProcessInstanceIdKey] = processInstanceId.ToString(),
            [SubmissionIdKey] = submissionId.ToString()
        };
        foreach (var (key, value) in variables)
            if (value != null && !input.ContainsKey(key)) input[key] = value;
        return input;
    }

    public static WorkflowInputs From(ActivityExecutionContext context)
    {
        var input = context.WorkflowExecutionContext.Input;
        return new WorkflowInputs(
            Guid.Parse(Read(input, TenantIdKey)!),
            Guid.Parse(Read(input, ProcessInstanceIdKey)!),
            Guid.Parse(Read(input, SubmissionIdKey)!));
    }

    public static string? Read(IDictionary<string, object> input, string key) =>
        input.TryGetValue(key, out var value) ? value?.ToString() : null;
}
