using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Branding;

/// <summary>
/// The current company's effective branding (data-model.md "Derived"), shared by the branding API, the UI and
/// the email layout so they always agree. Reads in the ambient tenant; callers switch tenant when needed.
/// </summary>
public class BrandingReader : ITransientDependency
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantStore _tenantStore;
    private readonly IRepository<CompanyBranding, Guid> _brandings;

    public BrandingReader(ICurrentTenant currentTenant, ITenantStore tenantStore, IRepository<CompanyBranding, Guid> brandings)
    {
        _currentTenant = currentTenant; _tenantStore = tenantStore; _brandings = brandings;
    }

    public async Task<BrandingDto> GetCurrentAsync()
    {
        if (_currentTenant.Id is not { } tenantId) return ToDto(null, null, CompanyBranding.ProductName);

        var row = await FindAsync();
        var companyName = (await _tenantStore.FindAsync(tenantId))?.Name ?? CompanyBranding.ProductName;
        return ToDto(row, tenantId, companyName);
    }

    /// <summary>The current company's row, if it has one (host has none).</summary>
    public async Task<CompanyBranding?> FindAsync() =>
        _currentTenant.Id == null ? null : await _brandings.FindAsync(b => b.TenantId == _currentTenant.Id);

    private static BrandingDto ToDto(CompanyBranding? row, Guid? tenantId, string companyName)
    {
        var colour = row?.BrandColor ?? CompanyBranding.DefaultColor;
        var logoUrl = row?.Logo != null && tenantId != null ? $"/api/poc/branding/logo/{tenantId}?v={row.LogoVersion}" : null;
        return new BrandingDto(row?.DisplayName, row?.DisplayName ?? companyName, row?.BrandColor, colour,
            BrandColors.OnBrand(colour), BrandColors.BrandText(colour), BrandColors.BrandSoft(colour), logoUrl);
    }
}
