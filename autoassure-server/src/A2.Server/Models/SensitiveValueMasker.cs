namespace A2.Server.Models;

/// <summary>Masks a sensitive text value for display: keeps a leading slice of the value's own real
/// characters -- answering the only question a reader actually asks, "did I paste the right one?",
/// without exposing enough of it to use -- and hides the rest behind dots. The output is always exactly
/// <see cref="MaskedLength"/> characters, whatever the real value's length, so the masked text never
/// hints at how long the secret is. Different surfaces pass a different fraction to keep: more of a
/// value visible trades off against how long the masked record lives on for.</summary>
public static class SensitiveValueMasker
{
    // Every masked output is exactly this many characters, regardless of the real value's length, so
    // the output length itself never leaks how long the real value is.
    private const int MaskedLength = 20;

    /// <summary>Keeps a leading slice of <paramref name="value"/>'s own characters -- sized to
    /// <paramref name="fractionToKeep"/> of the fixed output length, not of the value's own length --
    /// and pads the remainder with dots so the result is always <see cref="MaskedLength"/> characters
    /// long. A value shorter than the visible slice is shown in full and padded with extra dots, rather
    /// than fabricating characters, which keeps the output length constant even for short values.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="fractionToKeep"/> is
    /// outside the 0-1 range.</exception>
    public static string Mask(string value, double fractionToKeep)
    {
        if (fractionToKeep is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fractionToKeep),
                fractionToKeep,
                "Must be between 0 and 1."
            );
        }

        // When a value and a fraction to keep are given, Then reveal that fraction of the fixed output
        // length in real leading characters -- capped at however many the value actually has -- and fill
        // the rest with dots, so the total length never varies with the real value's length.
        var targetVisibleLength = (int)
            Math.Round(MaskedLength * fractionToKeep, MidpointRounding.AwayFromZero);
        var visibleLength = Math.Min(value.Length, targetVisibleLength);
        return value[..visibleLength] + new string('.', MaskedLength - visibleLength);
    }
}
