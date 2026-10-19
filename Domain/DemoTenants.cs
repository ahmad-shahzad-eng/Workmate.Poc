namespace Workmate.Poc;

public record DemoTenant(Guid Id, string Name, string DisplayName);

public static class DemoTenants
{
    public static readonly DemoTenant Acme =
        new(Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001"), "acme", "Acme Trading");

    public static readonly DemoTenant Globex =
        new(Guid.Parse("b2b2b2b2-0000-0000-0000-000000000002"), "globex", "Globex Logistics");

    public static readonly DemoTenant[] All = { Acme, Globex };

    /// <summary>
    /// Vendor (platform) admin: a host-side user of the HRM vendor, not an employee of any company.
    /// Can design forms and routing for whichever company is selected, but cannot submit or approve.
    /// Production: an ABP host user with the design permission, switching tenant explicitly.
    /// </summary>
    public static readonly Guid VendorAdminId = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    public const string VendorAdminName = "Aramis Support";
}
