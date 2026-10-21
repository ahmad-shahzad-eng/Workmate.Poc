using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Workmate.Poc.Http;

/// <summary>
/// ABP's built-in <see cref="CurrentUserTenantResolveContributor"/> already outranks any client-sent
/// tenant header for an authenticated user — but it also always resolves a signed-in HOST user (no
/// tenant of their own, e.g. the vendor admin) to "no tenant", with no way to act within a chosen
/// company. This contributor fills exactly that gap, and only that gap: it only ever handles the
/// request when the already-authenticated current user is confirmed to be a host user, so an
/// ordinary tenant-bound employee's own token-carried tenant claim always wins instead (spec 003 US2
/// scenario 2) — see plan.md Design §4.
/// </summary>
public class HostActAsTenantResolveContributor : TenantResolveContributorBase
{
    public const string ContributorName = "HostActAs";
    public const string HeaderName = "X-Act-As-Tenant";

    public override string Name => ContributorName;

    public override Task ResolveAsync(ITenantResolveContext context)
    {
        var currentUser = context.ServiceProvider.GetRequiredService<ICurrentUser>();
        if (currentUser.IsAuthenticated && currentUser.TenantId == null)
        {
            var httpContext = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
            var tenantId = httpContext?.Request.Headers[HeaderName].FirstOrDefault();
            if (Guid.TryParse(tenantId, out _))
            {
                context.Handled = true;
                context.TenantIdOrName = tenantId;
            }
        }

        return Task.CompletedTask;
    }
}
