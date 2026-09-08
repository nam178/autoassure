namespace A2.Server.Models;

/// <summary>Masks a sensitive text value for display: keeps a leading slice of the value's own real
/// characters -- answering the only question a reader actually asks, "did I paste the right one?",
/// without exposing enough of it to use -- and hides the rest behind stars. The output is always exactly
/// <see cref="MaskedLength"/> characters, whatever the real value's length, so the masked text never
/// hints at how long the secret is.</summary>
public static class SensitiveValueMasker
{
    // Every masked output is exactly this many characters, regardless of the real value's length, so
    // the output length itself never leaks how long the real value is.
    private const int MaskedLength = 10;

    // How many of the value's own leading characters are revealed, once it's long enough to reveal any.
    private const int VisibleLength = 2;

    // A value shorter than this is hidden completely rather than revealing its leading characters --
    // otherwise the boundary between real characters and stars would fall at the value's own length,
    // leaking it.
    private const int MinimumLengthToReveal = 8;

    /// <summary>Keeps <paramref name="value"/>'s leading <see cref="VisibleLength"/> characters when it's
    /// at least <see cref="MinimumLengthToReveal"/> characters long, and pads the remainder with stars up
    /// to <see cref="MaskedLength"/>. A value shorter than the threshold is hidden completely, rather than
    /// revealing any of it, so the output's shape never depends on the real value's length.</summary>
    public static string Mask(string value) =>
        value.Length >= MinimumLengthToReveal
            ? value[..VisibleLength] + new string('*', MaskedLength - VisibleLength)
            : new string('*', MaskedLength);
}
