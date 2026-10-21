namespace Workmate.Poc;

/// <summary>
/// Names used to seed the two demo companies as real ABP <c>Tenant</c> rows (Data/DemoDataSeeder.cs,
/// via <c>TenantManager.CreateAsync</c>) — this class is no longer a tenant source itself (spec 003
/// FR-003; the config store it used to feed WorkmatePocModule's AbpDefaultTenantStoreOptions is gone).
/// No fixed tenant ids anymore either: <c>Tenant</c>'s constructor is internal to ABP's own module, so
/// the id is whatever <c>TenantManager.CreateAsync</c> generates at seed time — everything that used to
/// reference a hardcoded guid (demo.py included) now looks the tenant up by <see cref="Name"/> instead,
/// which ABP's tenant resolution already supports natively (normalized, case-insensitive).
/// </summary>
public record DemoTenant(string Name);

public static class DemoTenants
{
    public static readonly DemoTenant Acme = new("Acme Trading");
    public static readonly DemoTenant Globex = new("Globex Logistics");

    public static readonly DemoTenant[] All = { Acme, Globex };

    /// <summary>
    /// Vendor (platform) admin: a genuine ABP host user (no TenantId), seeded in DemoDataSeeder with
    /// this display name and a deterministic email for demo.py to sign in as. No longer a magic id —
    /// "is this a vendor admin?" is now just "is the signed-in user a host user?" (TenantId == null).
    /// </summary>
    public const string VendorAdminName = "Aramis Support";
    public const string VendorAdminEmail = "aramis@workmate.vendor";
}
