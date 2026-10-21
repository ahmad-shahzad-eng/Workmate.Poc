using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Identity;
using Workmate.Poc.Application.Notifications;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Anonymous account routes (spec 005 FR-008): passwords are only ever chosen through an emailed one-time
/// link. Forgot-password never says whether an account exists; set-password is protected by the token.
/// </summary>
[AllowAnonymous]
public class AccountAppService : ApplicationService
{
    public const string InvalidLinkMessage = "This link is invalid or has expired.";

    private readonly IdentityUserManager _userManager;
    private readonly NotificationService _notifications;

    public AccountAppService(IdentityUserManager userManager, NotificationService notifications)
    {
        _userManager = userManager; _notifications = notifications;
    }

    /// <summary>The company comes from the <c>__tenant</c> header, as at sign-in. Always returns normally.</summary>
    public virtual async Task ForgotPasswordAsync(ForgotPasswordInput input)
    {
        var email = input.Email?.Trim();
        if (string.IsNullOrEmpty(email)) return;

        var user = await _userManager.FindByEmailAsync(email);
        if (user is { IsActive: true })
            await _notifications.QueueAccountAsync(EmailEvents.PasswordReset, user.Id, user.Email);
    }

    /// <summary>Sets the password from a link's token, which then stops working (it changes the security stamp),
    /// and lifts any lockout. Every token or user problem gets the same message.</summary>
    public virtual async Task SetPasswordAsync(SetPasswordInput input)
    {
        if (string.IsNullOrEmpty(input.Token)) throw new UserFriendlyException(InvalidLinkMessage);
        if (string.IsNullOrEmpty(input.NewPassword)) throw new UserFriendlyException("Choose a password.");

        using (CurrentTenant.Change(input.TenantId))
        {
            var user = await _userManager.FindByIdAsync(input.UserId.ToString())
                       ?? throw new UserFriendlyException(InvalidLinkMessage);

            var result = await _userManager.ResetPasswordAsync(user, input.Token, input.NewPassword);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code == "InvalidToken")) throw new UserFriendlyException(InvalidLinkMessage);
                throw new UserFriendlyException(string.Join(" ", result.Errors.Select(e => e.Description)));
            }

            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
        }
    }
}
