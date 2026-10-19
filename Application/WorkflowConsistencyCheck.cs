using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine;

namespace Workmate.Poc.Application;

/// <summary>
/// Startup-only check: for each demo tenant, warns about any running process whose Elsa workflow
/// instance is missing (e.g. one SQLite file was deleted but not the other). Never throws;
/// acting on the affected task still fails clearly, in <see cref="TaskAppService"/>.
/// </summary>
public class WorkflowConsistencyCheck : ITransientDependency
{
    private readonly IUnitOfWorkManager _uow;
    private readonly ICurrentTenant _currentTenant;
    private readonly IWorkflowGateway _gateway;
    private readonly IRepository<ProcessInstance, Guid> _instances;
    private readonly ILogger<WorkflowConsistencyCheck> _logger;

    public WorkflowConsistencyCheck(IUnitOfWorkManager uow, ICurrentTenant currentTenant, IWorkflowGateway gateway,
        IRepository<ProcessInstance, Guid> instances, ILogger<WorkflowConsistencyCheck> logger)
    {
        _uow = uow; _currentTenant = currentTenant; _gateway = gateway; _instances = instances; _logger = logger;
    }

    public async Task RunAsync()
    {
        foreach (var tenant in DemoTenants.All)
        {
            List<ProcessInstance> running;
            using (_currentTenant.Change(tenant.Id))
            using (var uow = _uow.Begin(requiresNew: true, isTransactional: false))
            {
                running = await _instances.GetListAsync(p =>
                    p.Status == ProcessStatus.Running && p.ElsaWorkflowInstanceId != null);
                await uow.CompleteAsync();
            }

            foreach (var instance in running)
            {
                if (!await _gateway.InstanceExistsAsync(instance.ElsaWorkflowInstanceId!))
                    _logger.LogWarning(
                        "Process instance {ProcessInstanceId} in tenant {Tenant} is Running but its workflow instance {ElsaWorkflowInstanceId} is missing from the workflow store.",
                        instance.Id, tenant.Name, instance.ElsaWorkflowInstanceId);
            }
        }
    }
}
