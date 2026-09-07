using System.Reflection;
using PdfSharp.Fonts;

namespace PeopleCore.Reports;

/// <summary>
/// Resolves "Arial" against a TTF embedded in this assembly rather than whatever fonts happen to
/// be installed on the machine PDFsharp is running on.
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
/// <para>
/// The embedded TTF is Liberation Sans, not Microsoft's Arial (whole-branch review Fix 3): the
/// original <c>arial.ttf</c>/<c>arialbd.ttf</c> here were copied out of <c>C:\Windows\Fonts</c>,
/// and Arial's own name-table license restricts its use to content produced with the Microsoft
/// product it shipped with - embedding it inside a distributed .NET assembly falls outside that
/// grant. Liberation Sans is released under the SIL Open Font License and is metrically
/// identical to Arial glyph-for-glyph (it was built specifically as an Arial substitute), so
/// every coordinate in <see cref="Bir2316FieldMap"/> - including the 0.556em digit-advance width
/// the TIN/ZIP/DOB/Contact Number digit cells are spaced on - carries over unchanged; only the
/// face resolved by "Arial" changed, not a single number in the map. The bold face
/// (<c>arialbd.ttf</c>) was deleted outright along with it rather than replaced: nothing in
/// <see cref="Bir2316Stamper"/> ever constructs a bold <c>XFont</c>, so it was 990KB of embedded,
/// resolvable, never-requested dead weight even before the licensing question came up.
/// </para>
/// </summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    private const string RegularFace = "Bir2316Arial";

    private static readonly Lazy<byte[]> RegularBytes = new(() => ReadEmbeddedFont("LiberationSans-Regular.ttf"));

    public string DefaultFontName => RegularFace;

    public byte[] GetFont(string faceName) => faceName switch
    {
        RegularFace => RegularBytes.Value,
        _ => throw new InvalidOperationException($"No embedded font registered for face '{faceName}'.")
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // The stamper only ever asks for "Arial", and only ever in its regular weight - there is
        // no bold face to hand back (see this class's own doc comment), so isBold is accepted but
        // ignored rather than threaded through to a face that does not exist. PDFsharp's italic
        // synthesis (if ever requested) is handled by the graphics engine, not by supplying a
        // separate italic face here.
        var isArialFamily = familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase)
            || familyName.Equals(RegularFace, StringComparison.OrdinalIgnoreCase);

        return isArialFamily ? new FontResolverInfo(RegularFace) : null;
    }

    /// <summary>
    /// Assigns this resolver to <see cref="GlobalFontSettings.FontResolver"/> exactly once.
    /// <see cref="Bir2316Stamper"/> may be constructed per HTTP request, but PDFsharp's font
    /// resolver is a process-wide singleton that throws if reassigned after first use, so every
    /// constructor call must be able to call this safely - including when two requests race to be
    /// the first.
    /// <para>
    /// Whole-branch review Fix 5: the previous body was a plain check-then-set
    /// (<c>if (... is EmbeddedFontResolver) return; ... = new EmbeddedFontResolver();</c>) with no
    /// synchronization. Two concurrent first calls can both observe the "not yet registered" state
    /// before either assigns it, both then call the setter, and PDFsharp's
    /// <c>GlobalFontSettings.FontResolver</c> setter throws once a font source has already been
    /// resolved from it - turning a benign race into an unhandled exception on one of two
    /// simultaneous first requests. <see cref="Registered"/> is a <see cref="Lazy{T}"/> with the
    /// default <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> mode, so concurrent
    /// callers block on the same single assignment instead of racing to perform their own.
    /// </para>
    /// </summary>
    public static void EnsureRegistered() => _ = Registered.Value;

    private static readonly Lazy<bool> Registered = new(() =>
    {
        GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        return true;
    });

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
