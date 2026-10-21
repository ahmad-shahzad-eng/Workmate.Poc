using System.Text;
using System.Xml;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Branding;

/// <summary>
/// Spec 007 FR-007 / research R8: a logo is a PNG, JPEG or safe SVG of at most 200 KB. The type comes from the
/// file's own bytes, never from what the client says. SVGs that could run script or load anything are refused
/// (the logo endpoint's CSP sandbox and showing logos only as images are the second line of defence).
/// </summary>
public class LogoValidator : ITransientDependency
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };

    public (string ContentType, byte[] Bytes) Validate(byte[] bytes)
    {
        if (bytes.Length == 0) throw new UserFriendlyException("Choose a logo file.");
        if (bytes.Length > CompanyBranding.MaxLogoBytes) throw new UserFriendlyException("Logos can be at most 200 KB.");

        if (bytes.AsSpan().StartsWith(PngMagic)) return ("image/png", bytes);
        if (bytes.AsSpan().StartsWith(JpegMagic)) return ("image/jpeg", bytes);
        if (LooksLikeSvg(bytes))
        {
            CheckSvg(bytes);
            return ("image/svg+xml", bytes);
        }
        throw new UserFriendlyException("The logo must be a PNG, JPEG or SVG image.");
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckSvg(byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            var sawRoot = false;
            var styleDepth = 0; // > 0 while inside a <style> element, whose text is CSS
            while (reader.Read())
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                {
                    if (reader.Value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                        throw Unsafe("SVG logos may not contain scripts.");
                    if (styleDepth > 0) CheckStyle(reader.Value);
                }
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase))
                    styleDepth--;
                if (reader.NodeType != XmlNodeType.Element) continue;

                var name = reader.LocalName;
                if (!sawRoot)
                {
                    if (!name.Equals("svg", StringComparison.OrdinalIgnoreCase))
                        throw new UserFriendlyException("The logo must be a PNG, JPEG or SVG image.");
                    sawRoot = true;
                }
                if (name.Equals("script", StringComparison.OrdinalIgnoreCase))
                    throw Unsafe("SVG logos may not contain scripts.");
                if (name.Equals("foreignObject", StringComparison.OrdinalIgnoreCase))
                    throw Unsafe("SVG logos may not contain embedded HTML.");
                if (name.Equals("style", StringComparison.OrdinalIgnoreCase) && !reader.IsEmptyElement)
                    styleDepth++;

                if (!reader.HasAttributes) continue;
                while (reader.MoveToNextAttribute())
                {
                    var attribute = reader.LocalName;
                    var value = reader.Value;
                    if (attribute.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                        throw Unsafe("SVG logos may not contain scripts.");
                    if (value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                        throw Unsafe("SVG logos may not contain scripts.");
                    if (attribute.Equals("href", StringComparison.OrdinalIgnoreCase) && !IsLocalReference(value))
                        throw Unsafe("SVG logos may not load external content.");
                    if (attribute.Equals("style", StringComparison.OrdinalIgnoreCase)) CheckStyle(value);
                }
                reader.MoveToElement();
            }
            if (!sawRoot) throw new UserFriendlyException("The logo must be a PNG, JPEG or SVG image.");
        }
        catch (XmlException)
        {
            throw new UserFriendlyException("The SVG logo is not valid XML.");
        }
    }

    /// <summary>Only same-document fragments and embedded raster images; never another file or URL.</summary>
    private static bool IsLocalReference(string href) =>
        href.StartsWith('#') ||
        href.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith("data:image/jpeg", StringComparison.OrdinalIgnoreCase);

    private static void CheckStyle(string css)
    {
        if (css.Contains("url(", StringComparison.OrdinalIgnoreCase) || css.Contains("@import", StringComparison.OrdinalIgnoreCase))
            throw Unsafe("SVG logos may not load external content.");
    }

    private static UserFriendlyException Unsafe(string message) => new(message);
}
