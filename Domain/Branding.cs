using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

/// <summary>A company's look (spec 007 US2): display name, brand colour and logo. No row = the product defaults.</summary>
public class CompanyBranding : AggregateRoot<Guid>, IMultiTenant
{
    public const int MaxDisplayNameLength = 64;
    public const int MaxLogoBytes = 200 * 1024;
    public const string DefaultColor = "#355dff"; // LeptonX Lite's own primary colour
    public const string ProductName = "Workmate HRM";

    public Guid? TenantId { get; set; }
    public string? DisplayName { get; set; }       // null = the company (tenant) name
    public string? BrandColor { get; set; }        // "#rrggbb", lower-case; null = DefaultColor
    public string? LogoContentType { get; set; }   // decided from the file's own bytes (research R8)
    public byte[]? Logo { get; set; }
    public int LogoVersion { get; set; }           // +1 on every logo change; part of the logo URL

    protected CompanyBranding() { }
    public CompanyBranding(Guid id, Guid? tenantId) : base(id) => TenantId = tenantId;
}
