using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Notifications;

/// <summary>The company's Email log (FR-005): what was sent to whom, what failed and why, and Resend.</summary>
[Authorize(WorkmatePermissions.EmployeesManage)]
public class EmailLogAppService : ApplicationService
{
    private const int MaxItems = 200;

    private readonly IRepository<EmailMessage, Guid> _messages;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly NotificationService _notifications;

    public EmailLogAppService(IRepository<EmailMessage, Guid> messages, IRepository<Employee, Guid> employees,
        NotificationService notifications)
    {
        _messages = messages; _employees = employees; _notifications = notifications;
    }

    /// <summary>Newest first, this company only (tenant-filtered).</summary>
    public virtual async Task<List<EmailLogItemDto>> GetListAsync(string? status = null)
    {
        var query = (await _messages.GetQueryableAsync())
            .Where(m => string.IsNullOrEmpty(status) || m.Status == status)
            .OrderByDescending(m => m.CreationTime)
            .Take(MaxItems);
        var messages = await AsyncExecuter.ToListAsync(query);
        var names = (await _employees.GetListAsync()).ToDictionary(e => e.Id, e => e.Name);
        return messages.Select(m => Map(m, names)).ToList();
    }

    /// <summary>Only a Failed email can be resent; it goes back to the queue with its attempts reset.</summary>
    public virtual async Task<EmailLogItemDto> ResendAsync(Guid id)
    {
        var message = await _messages.GetAsync(id);
        if (message.Status != EmailStatus.Failed)
            throw new UserFriendlyException("Only a failed email can be resent.");

        await _notifications.RequeueAsync(message);
        var names = (await _employees.GetListAsync()).ToDictionary(e => e.Id, e => e.Name);
        return Map(message, names);
    }

    private static EmailLogItemDto Map(EmailMessage m, Dictionary<Guid, string> names) =>
        new(m.Id, m.Event, m.Audience, m.RecipientEmail,
            m.RecipientEmployeeId is { } e ? names.GetValueOrDefault(e) : null, m.SubmissionId, m.Subject,
            m.Status, m.Attempts, m.LastError, m.CreationTime, m.SentAt);
}
