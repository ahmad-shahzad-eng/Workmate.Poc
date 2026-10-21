using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Notifications;

/// <summary>The wording used when neither the form nor the company has its own template (FR-003).
/// Uses only the allowed placeholders (research R4), so they pass the same validation as admin templates.</summary>
public static class BuiltInEmailTemplates
{
    public static (string Subject, string Body) Get(string evt, string audience) => (evt, audience) switch
    {
        (EmailEvents.Submitted, EmailAudiences.Initiator) => (
            "We received your {{ request.title }} ({{ request.number }})",
            "Hello {{ initiator.name }},\n\nYour {{ request.title }} request {{ request.number }} was submitted and is now on its way for approval.\n\nTrack it here: {{ link }}"),

        (EmailEvents.TaskAssigned, EmailAudiences.Approver) => (
            "{{ request.title }} from {{ initiator.name }} is waiting for your approval",
            "Hello {{ approver.name }},\n\n{{ initiator.name }} submitted {{ request.title }} {{ request.number }}. It is waiting for you at step \"{{ step.title }}\".\n\nReview it in your inbox: {{ link }}"),

        (EmailEvents.SentBack, EmailAudiences.Approver) => (
            "{{ request.title }} from {{ initiator.name }} was sent back to you",
            "Hello {{ approver.name }},\n\n{{ request.title }} {{ request.number }} from {{ initiator.name }} was sent back for another look at step \"{{ step.title }}\".{{ if comment }}\n\nComment: {{ comment }}{{ end }}\n\nReview it in your inbox: {{ link }}"),

        (EmailEvents.SentBack, EmailAudiences.Initiator) => (
            "Your {{ request.title }} ({{ request.number }}) was sent back a step",
            "Hello {{ initiator.name }},\n\nYour {{ request.title }} request {{ request.number }} was sent back to the previous approver (\"{{ step.title }}\").{{ if comment }}\n\nComment: {{ comment }}{{ end }}\n\nTrack it here: {{ link }}"),

        (EmailEvents.Approved, EmailAudiences.Initiator) => (
            "Your {{ request.title }} ({{ request.number }}) was approved",
            "Hello {{ initiator.name }},\n\nGood news: your {{ request.title }} request {{ request.number }} was approved.\n\nSee the details: {{ link }}"),

        (EmailEvents.Rejected, EmailAudiences.Initiator) => (
            "Your {{ request.title }} ({{ request.number }}) was rejected",
            "Hello {{ initiator.name }},\n\nYour {{ request.title }} request {{ request.number }} was rejected by {{ approver.name }}.{{ if comment }}\n\nComment: {{ comment }}{{ end }}\n\nSee the details: {{ link }}"),

        (EmailEvents.TaskReassigned, EmailAudiences.Approver) => (
            "{{ request.title }} from {{ initiator.name }} was reassigned to you",
            "Hello {{ approver.name }},\n\n{{ request.title }} {{ request.number }} from {{ initiator.name }} was reassigned to you at step \"{{ step.title }}\" because the previous approver is no longer active.\n\nReview it in your inbox: {{ link }}"),

        (EmailEvents.AccountInvite, EmailAudiences.Account) => (
            "Your {{ company.name }} Workmate account",
            "Hello,\n\nAn account was created for you at {{ company.name }} on Workmate. Choose your password to start:\n\n{{ link }}\n\nThe link works once and expires in 72 hours."),

        (EmailEvents.PasswordReset, EmailAudiences.Account) => (
            "Reset your {{ company.name }} Workmate password",
            "Hello,\n\nSomeone asked to reset the password for this account. Choose a new password here:\n\n{{ link }}\n\nThe link works once and expires in 72 hours. If you didn't ask for this, you can ignore this email."),

        _ => throw new ArgumentOutOfRangeException(nameof(evt), $"No email template for {evt}/{audience}.")
    };
}
