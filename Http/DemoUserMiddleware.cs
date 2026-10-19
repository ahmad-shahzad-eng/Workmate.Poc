using System.Security.Claims;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Security.Claims;

namespace Workmate.Poc.Http;

/// <summary>
/// PoC ONLY. Signs the request in as the user named in the X-Demo-User header so the demo can
/// switch people with a dropdown. Production replaces this with ABP Identity + OpenIddict.
/// </summary>
public class DemoUserMiddleware : IMiddleware, ITransientDependency
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var userId = context.Request.Headers["X-Demo-User"].FirstOrDefault();
        var tenantId = context.Request.Headers["__tenant"].FirstOrDefault();

        if (Guid.TryParse(userId, out _))
        {
            var claims = new List<Claim> { new(AbpClaimTypes.UserId, userId!) };
            if (Guid.TryParse(tenantId, out _)) claims.Add(new(AbpClaimTypes.TenantId, tenantId!));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "DemoHeader"));
        }

        await next(context);
    }
}
