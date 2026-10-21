using System.Security.Cryptography;

namespace Workmate.Poc.Application;

/// <summary>One-time passwords shown on screen until spec 005 sends a set-password email
/// (spec 003 company admins, spec 004 employees).</summary>
public static class PasswordGenerator
{
    public static string New()
    {
        const string alphanumeric = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(10);
        var core = new string(bytes.Select(b => alphanumeric[b % alphanumeric.Length]).ToArray());
        return core + "1!"; // guarantees a digit + a non-alphanumeric, satisfying ABP's default password policy
    }
}
