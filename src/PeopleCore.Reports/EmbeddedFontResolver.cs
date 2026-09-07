using System.Reflection;
using PdfSharp.Fonts;

namespace PeopleCore.Reports;

/// <summary>
/// Resolves "Arial" / "Arial Bold" against the TTFs embedded in this assembly rather than
/// whatever fonts happen to be installed on the machine PDFsharp is running on.
/// <para>
/// PDFsharp ships with no default <see cref="IFontResolver"/> and throws
/// <c>InvalidOperationException: No appropriate font found for family name 'Arial'</c> the moment
/// <c>XGraphics</c> tries to measure or draw text unless one is assigned. The obvious fix —
/// <c>PdfSharp.Snippets.Font.FailsafeFontResolver</c> — substitutes a bundled fallback font
/// silently, which is fine for a spike but wrong here: a payslip generated on a developer's
/// Windows box (which has "Arial" locally) must render byte-for-byte identically to one generated
/// in a Linux container (which does not), and the only way to guarantee that is for both to draw
/// from the same TTF regardless of what the OS provides.
/// </para>
/// </summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    private const string RegularFace = "Bir2316Arial";
    private const string BoldFace = "Bir2316ArialBold";

    private static readonly Lazy<byte[]> RegularBytes = new(() => ReadEmbeddedFont("arial.ttf"));
    private static readonly Lazy<byte[]> BoldBytes = new(() => ReadEmbeddedFont("arialbd.ttf"));

    public string DefaultFontName => RegularFace;

    public byte[] GetFont(string faceName) => faceName switch
    {
        RegularFace => RegularBytes.Value,
        BoldFace => BoldBytes.Value,
        _ => throw new InvalidOperationException($"No embedded font registered for face '{faceName}'.")
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // The stamper only ever asks for "Arial"; PDFsharp's italic synthesis (if ever requested)
        // is handled by the graphics engine, not by supplying a separate italic face here.
        var isArialFamily = familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase)
            || familyName.Equals(RegularFace, StringComparison.OrdinalIgnoreCase)
            || familyName.Equals(BoldFace, StringComparison.OrdinalIgnoreCase);

        if (!isArialFamily) return null;

        return new FontResolverInfo(isBold ? BoldFace : RegularFace);
    }

    /// <summary>
    /// Assigns this resolver to <see cref="GlobalFontSettings.FontResolver"/> exactly once.
    /// <see cref="Bir2316Stamper"/> may be constructed per HTTP request, but PDFsharp's font
    /// resolver is a process-wide singleton that throws if reassigned after first use, so every
    /// constructor call must be able to call this safely.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver is EmbeddedFontResolver) return;
        GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
    }

    private static byte[] ReadEmbeddedFont(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{typeof(EmbeddedFontResolver).Namespace}.Fonts.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded font resource '{resourceName}' was not found in {assembly.FullName}.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
