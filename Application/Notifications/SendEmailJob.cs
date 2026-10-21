using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Notifications;

/// <summary>Only ids travel in the job record; the EmailMessage row holds the content (research R2).</summary>
[BackgroundJobName("Workmate.SendEmail")]
public class SendEmailArgs
{
    public Guid? TenantId { get; set; }
    public Guid EmailMessageId { get; set; }
}

/// <summary>
/// Sends one queued EmailMessage after its unit of work committed (plan Design §3). A failure is recorded on
/// the row and rethrown so ABP retries with backoff; the fifth failure marks the row Failed and stops
/// (clarification Q3). Account emails are rendered here, with a fresh set-password token, at every attempt.
/// </summary>
public class SendEmailJob : AsyncBackgroundJob<SendEmailArgs>, ITransientDependency
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _uow;
    private readonly IClock _clock;
    private readonly IRepository<EmailMessage, Guid> _messages;
    private readonly NotificationService _notifications;
    private readonly IdentityUserManager _userManager;
    private readonly IEmailSender _emailSender;
    private readonly ISettingProvider _settings;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _environment;

    public SendEmailJob(ICurrentTenant currentTenant, IUnitOfWorkManager uow, IClock clock,
        IRepository<EmailMessage, Guid> messages, NotificationService notifications, IdentityUserManager userManager,
        IEmailSender emailSender, ISettingProvider settings, IConfiguration config, IHostEnvironment environment)
    {
        _currentTenant = currentTenant; _uow = uow; _clock = clock; _messages = messages; _notifications = notifications;
        _userManager = userManager; _emailSender = emailSender; _settings = settings; _config = config; _environment = environment;
    }

    public override async Task ExecuteAsync(SendEmailArgs args)
    {
        using (_currentTenant.Change(args.TenantId))
        {
            Exception? failure = null;

            using (var uow = _uow.Begin(requiresNew: true, isTransactional: false))
            {
                var message = await _messages.FindAsync(args.EmailMessageId);
                if (message == null || message.Status != EmailStatus.Queued)
                {
                    await uow.CompleteAsync();
                    return; // already sent, failed for good, or gone
                }

                try
                {
                    await SendAsync(message);
                    message.Status = EmailStatus.Sent;
                    message.SentAt = _clock.Now;
                    message.Attempts++;
                    message.LastError = null;
                }
                catch (Exception ex)
                {
                    message.Attempts++;
                    var error = ex.GetBaseException().Message;
                    message.LastError = error.Length > EmailMessage.MaxErrorLength ? error[..EmailMessage.MaxErrorLength] : error;
                    if (message.Attempts >= EmailMessage.MaxAttempts)
                        message.Status = EmailStatus.Failed;
                    else
                        failure = ex;
                }

                await _messages.UpdateAsync(message);
                await uow.CompleteAsync();
            }

            // Rethrown after the attempt is saved, so ABP reschedules the job with its backoff.
            if (failure != null)
                throw new InvalidOperationException($"Email {args.EmailMessageId} not sent yet: {failure.Message}", failure);
        }
    }

    private async Task SendAsync(EmailMessage message)
    {
        var (subject, text, html) = (message.Subject, message.TextBody ?? "", message.HtmlBody ?? "");
        if (message.RecipientUserId is { } userId)
        {
            var rendered = await _notifications.RenderAccountAsync(message.Event, await CreateSetPasswordLinkAsync(userId));
            (subject, text, html) = (rendered.Subject, rendered.Text, rendered.Html);
        }

        // Test seam for demo.py 13i (research R3): never honoured outside Development.
        var failPrefix = _config["Poc:Email:FailRecipientPrefix"];
        if (_environment.IsDevelopment() && !string.IsNullOrEmpty(failPrefix) &&
            message.RecipientEmail.StartsWith(failPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test failure (FailRecipientPrefix)");

        var fromAddress = await _settings.GetOrNullAsync(EmailSettingNames.DefaultFromAddress) ?? "no-reply@workmate.local";
        var (_, senderName) = await _notifications.GetSenderNameAsync();

        using var mail = new MailMessage
        {
            From = new MailAddress(fromAddress, senderName),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        mail.To.Add(message.RecipientEmail);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(text, Encoding.UTF8, MediaTypeNames.Text.Plain));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));

        await _emailSender.SendAsync(mail, normalize: false);
    }

    /// <summary>R7: a reset token works once (a successful reset changes the security stamp) and expires in 72 h.</summary>
    private async Task<string> CreateSetPasswordLinkAsync(Guid userId)
    {
        var user = await _userManager.GetByIdAsync(userId);
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        return $"{_notifications.PublicBaseUrl}/?setPassword=1&tenant={_currentTenant.Id}&user={userId}&token={Uri.EscapeDataString(token)}";
    }
}
