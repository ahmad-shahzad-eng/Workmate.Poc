using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Branding;

/// <summary>
/// A company's branding (spec 007 US2). Anyone signed in reads their own company's; the sign-in screen reads a
/// named company's public fields anonymously (FR-008: display name, colours and logo are public by design).
/// Only a company admin (<c>Workmate.Branding.Manage</c>) changes it.
/// </summary>
public class BrandingAppService : ApplicationService
{
    private readonly BrandingReader _reader;
    private readonly ITenantStore _tenantStore;
    private readonly LogoValidator _logoValidator;
    private readonly IRepository<CompanyBranding, Guid> _brandings;

    public BrandingAppService(BrandingReader reader, ITenantStore tenantStore, LogoValidator logoValidator,
        IRepository<CompanyBranding, Guid> brandings)
    {
        _reader = reader; _tenantStore = tenantStore; _logoValidator = logoValidator; _brandings = brandings;
    }

    /// <summary>The current company: the acted-as one for the vendor admin, product defaults for a host user acting as nobody.</summary>
    [Authorize]
    public virtual Task<BrandingDto> GetAsync() => _reader.GetCurrentAsync();

    /// <summary>Anonymous: a company picked on the sign-in screen (by name) or named by a set-password link (by id).
    /// An unknown or missing company gets the product defaults.</summary>
    [AllowAnonymous]
    public virtual async Task<BrandingDto> GetPublicAsync(string? company, Guid? tenantId)
    {
        var tenant = tenantId is { } id ? await _tenantStore.FindAsync(id)
            : string.IsNullOrWhiteSpace(company) ? null
            : await _tenantStore.FindAsync(company.Trim().ToUpperInvariant()); // ABP's normalised tenant name
        using (CurrentTenant.Change(tenant?.Id))
            return await _reader.GetCurrentAsync();
    }

    /// <summary>Saves the display name and colour; an empty value means "use the default" (data-model.md).</summary>
    [Authorize(WorkmatePermissions.BrandingManage)]
    public virtual async Task<BrandingDto> SaveAsync(SaveBrandingInput input)
    {
        var name = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();
        if (name != null && (name.Contains('\r') || name.Contains('\n')))
            throw new UserFriendlyException("The display name must be a single line.");
        if (name?.Length > CompanyBranding.MaxDisplayNameLength)
            throw new UserFriendlyException("The display name can be at most 64 characters.");

        var colour = string.IsNullOrWhiteSpace(input.BrandColor) ? null : input.BrandColor.Trim();
        if (colour != null && !BrandColors.IsValidHex(colour))
            throw new UserFriendlyException("Enter the colour as #rrggbb, for example #0f6e6e.");

        var row = await GetOrCreateRowAsync();
        row.DisplayName = name;
        row.BrandColor = colour?.ToLowerInvariant();
        await _brandings.UpdateAsync(row, autoSave: true);
        return await _reader.GetCurrentAsync();
    }

    /// <summary>Replaces the logo after <see cref="LogoValidator"/> (PNG/JPEG/safe SVG, at most 200 KB).</summary>
    [Authorize(WorkmatePermissions.BrandingManage)]
    public virtual async Task<BrandingDto> SetLogoAsync(byte[] bytes)
    {
        var (contentType, logo) = _logoValidator.Validate(bytes);
        var row = await GetOrCreateRowAsync();
        row.Logo = logo; row.LogoContentType = contentType; row.LogoVersion++;
        await _brandings.UpdateAsync(row, autoSave: true);
        return await _reader.GetCurrentAsync();
    }

    [Authorize(WorkmatePermissions.BrandingManage)]
    public virtual async Task RemoveLogoAsync()
    {
        var row = await _reader.FindAsync();
        if (row?.Logo == null) return;
        row.Logo = null; row.LogoContentType = null; row.LogoVersion++;
        await _brandings.UpdateAsync(row, autoSave: true);
    }

    /// <summary>"Reset to default": the company goes back to its own name, the product colour and no logo.</summary>
    [Authorize(WorkmatePermissions.BrandingManage)]
    public virtual async Task ResetAsync()
    {
        EnsureCompany();
        if (await _reader.FindAsync() is { } row) await _brandings.DeleteAsync(row, autoSave: true);
    }

    private async Task<CompanyBranding> GetOrCreateRowAsync()
    {
        EnsureCompany();
        return await _reader.FindAsync()
               ?? await _brandings.InsertAsync(new CompanyBranding(GuidGenerator.Create(), CurrentTenant.Id), autoSave: true);
    }

    /// <summary>Branding belongs to a company; the vendor admin must be acting as one.</summary>
    private void EnsureCompany()
    {
        if (CurrentTenant.Id == null) throw new UserFriendlyException("Choose a company first.");
    }

    /// <summary>Anonymous: the logo image by company id (the URL carries a version for caching).</summary>
    [AllowAnonymous]
    public virtual async Task<(byte[] Bytes, string ContentType)?> GetLogoAsync(Guid tenantId)
    {
        using (CurrentTenant.Change(tenantId))
        {
            var row = await _reader.FindAsync();
            return row?.Logo is { } bytes && row.LogoContentType is { } type ? (bytes, type) : null;
        }
    }
}
