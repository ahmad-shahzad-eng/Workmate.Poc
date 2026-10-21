using System.Globalization;
using System.Text.RegularExpressions;

namespace Workmate.Poc.Application.Branding;

/// <summary>
/// Readable variants of a brand colour (spec 007 FR-007, research R5), computed on the server so the UI and
/// the email layout agree. Contrast follows WCAG 2.x; every output is lower-case "#rrggbb".
/// </summary>
public static class BrandColors
{
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static bool IsValidHex(string? value) => value != null && Hex.IsMatch(value);

    /// <summary>WCAG contrast ratio, 1–21.</summary>
    public static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Text on brand-coloured surfaces: black or white, whichever contrasts more (always ≥ 4.58:1).</summary>
    public static string OnBrand(string brand) =>
        Contrast(brand, "#000000") >= Contrast(brand, "#ffffff") ? "#000000" : "#ffffff";

    /// <summary>The brand colour darkened in 5% steps until it reads as text on white (≥ 4.5:1).</summary>
    public static string BrandText(string brand)
    {
        var colour = brand.ToLowerInvariant();
        for (var step = 1; Contrast(colour, "#ffffff") < 4.5 && step <= 20; step++)
            colour = Mix(brand, "#000000", step * 0.05);
        return colour;
    }

    /// <summary>A light tint for selected rows and highlights: 12% brand, 88% white.</summary>
    public static string BrandSoft(string brand) => Mix(brand, "#ffffff", 0.88);

    /// <summary>Moves <paramref name="from"/> towards <paramref name="to"/> by <paramref name="amount"/> (0–1).</summary>
    private static string Mix(string from, string to, double amount)
    {
        var (r1, g1, b1) = Rgb(from);
        var (r2, g2, b2) = Rgb(to);
        int Lerp(int x, int y) => (int)Math.Round(x + (y - x) * amount);
        return $"#{Lerp(r1, r2):x2}{Lerp(g1, g2):x2}{Lerp(b1, b2):x2}";
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Channel(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static (int R, int G, int B) Rgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));
}
