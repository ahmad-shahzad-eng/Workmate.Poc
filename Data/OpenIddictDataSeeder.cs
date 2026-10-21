using OpenIddict.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;

namespace Workmate.Poc.Data;

/// <summary>
/// Seeds the one public OpenIddict client the demo UI and demo.py use to sign in via the
/// resource-owner-password-credentials grant at /connect/token (spec 003 FR-007). No secret:
/// a vanilla-JS page can't keep one, and the password grant doesn't need redirect URIs.
/// </summary>
public class OpenIddictDataSeeder : ITransientDependency
{
    public const string ClientId = "Workmate_Demo";
    public const string ScopeName = "Workmate";

    private readonly IOpenIddictScopeRepository _scopeRepository;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictApplicationRepository _applicationRepository;
    private readonly IAbpApplicationManager _applicationManager;

    public OpenIddictDataSeeder(IOpenIddictScopeRepository scopeRepository, IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationRepository applicationRepository, IAbpApplicationManager applicationManager)
    {
        _scopeRepository = scopeRepository; _scopeManager = scopeManager;
        _applicationRepository = applicationRepository; _applicationManager = applicationManager;
    }

    public async Task SeedAsync()
    {
        if (await _scopeRepository.FindByNameAsync(ScopeName) == null)
        {
            await _scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = ScopeName,
                DisplayName = "Workmate API",
                Resources = { ScopeName }
            });
        }

        if (await _applicationRepository.FindByClientIdAsync(ClientId) != null) return;

        var app = new AbpApplicationDescriptor
        {
            ClientId = ClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            DisplayName = "Workmate demo UI"
        };
        app.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        app.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation);
        app.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
        app.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Password);
        app.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        app.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + ScopeName);
        app.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess);

        await _applicationManager.CreateAsync(app);
    }
}
