using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

/// <summary>What happened (spec 005 US1). Workflow events come from ProcessManager and app services; account events from onboarding.</summary>
public static class EmailEvents
{
    public const string Submitted = "Submitted";
    public const string TaskAssigned = "TaskAssigned";
    public const string SentBack = "SentBack";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string TaskReassigned = "TaskReassigned";
    public const string AccountInvite = "AccountInvite";
    public const string PasswordReset = "PasswordReset";
}

/// <summary>Which side of a request the email is for.</summary>
public static class EmailAudiences
{
    public const string Initiator = "Initiator";
    public const string Approver = "Approver";
    public const string Account = "Account";

    /// <summary>The only (event, audience) pairs a template may exist for (data-model.md).</summary>
    public static readonly (string Event, string Audience)[] ValidPairs =
    {
        (EmailEvents.Submitted, Initiator),
        (EmailEvents.TaskAssigned, Approver),
        (EmailEvents.SentBack, Approver),
        (EmailEvents.SentBack, Initiator),
        (EmailEvents.Approved, Initiator),
        (EmailEvents.Rejected, Initiator),
        (EmailEvents.TaskReassigned, Approver),
        (EmailEvents.AccountInvite, Account),
        (EmailEvents.PasswordReset, Account),
    };

    public static bool IsAccountPair(string evt, string audience) =>
        audience == Account && ValidPairs.Contains((evt, audience));

    public static bool IsWorkflowPair(string evt, string audience) =>
        audience != Account && ValidPairs.Contains((evt, audience));
}

public static class EmailStatus
{
    public const string Queued = "Queued";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

/// <summary>Subject + body for one (event, audience). FormDefinitionId null = the company default (US3), set = a form override (US2).</summary>
public class EmailTemplate : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid? FormDefinitionId { get; set; }
    public string Event { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;

    protected EmailTemplate() { }
    public EmailTemplate(Guid id, Guid? tenantId, Guid? formDefinitionId, string evt, string audience, string subject, string body) : base(id)
    {
        TenantId = tenantId; FormDefinitionId = formDefinitionId; Event = evt; Audience = audience; Subject = subject; Body = body;
    }
}

/// <summary>One row per tenant: the From display name (US3). Null means "{company} via Workmate".</summary>
public class CompanyEmailSettings : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string? SenderName { get; set; }

    protected CompanyEmailSettings() { }
    public CompanyEmailSettings(Guid id, Guid? tenantId) : base(id) => TenantId = tenantId;
}

/// <summary>One email: the outbox row the send job works from, and the company's Email log (FR-005).</summary>
public class EmailMessage : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxAttempts = 5;
    public const int MaxErrorLength = 1000;

    public Guid? TenantId { get; set; }
    public string Event { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public Guid? RecipientEmployeeId { get; set; }
    public Guid? RecipientUserId { get; set; }        // account emails: the token is generated for this user at send time
    public string RecipientEmail { get; set; } = default!;
    public Guid? SubmissionId { get; set; }
    public string Subject { get; set; } = default!;
    public string? TextBody { get; set; }             // null for account emails: rendered at send time, never stored (research R6)
    public string? HtmlBody { get; set; }
    public string Status { get; set; } = EmailStatus.Queued;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }

    protected EmailMessage() { }
    public EmailMessage(Guid id, Guid? tenantId, string evt, string audience, string recipientEmail, string subject) : base(id)
    {
        TenantId = tenantId; Event = evt; Audience = audience; RecipientEmail = recipientEmail; Subject = subject;
    }
}
