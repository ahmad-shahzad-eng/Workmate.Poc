using System.Security.Cryptography;

namespace Workmate.Poc.Application;

/// <summary>One-time passwords shown on screen until spec 005 sends a set-password email
/// (spec 003 company admins, spec 004 employees).</summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Alphanumeric = Upper + Lower + "23456789";

    /// <summary>
    /// ABP's default policy needs an uppercase letter, a lowercase letter, a digit and a symbol. Random
    /// characters alone missed a letter case about 1 time in 130, so one of each is always included.
    /// </summary>
    public static string New()
    {
        var core = new string(RandomNumberGenerator.GetBytes(10).Select(b => Alphanumeric[b % Alphanumeric.Length]).ToArray());
        return core + Upper[RandomNumberGenerator.GetInt32(Upper.Length)] + Lower[RandomNumberGenerator.GetInt32(Lower.Length)] + "1!";
    }
}
