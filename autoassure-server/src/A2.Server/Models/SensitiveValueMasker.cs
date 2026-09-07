namespace A2.Server.Models;

/// <summary>Masks a sensitive text value for display: keeps a fraction of the value's own leading
/// characters and replaces the rest with a fixed-length run of dots, so the masked output never
/// reveals how long the real value is. Different surfaces keep different fractions -- more of a value
/// visible answers "did I paste the right one?" but the record lives on for less time.</summary>
public static class SensitiveValueMasker
{
    // A constant number of dots regardless of input length, so the output length never hints at the
    // real value's length.
    private const int MaskedSuffixLength = 8;

    /// <summary>Keeps the first <paramref name="fractionToKeep"/> of <paramref name="value"/>'s
    /// characters and replaces the remainder with a fixed-length run of dots.</summary>
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

        // When a value and a fraction to keep are given, Then keep only that leading fraction of
        // characters and hide everything else behind a fixed-length run of dots.
        var visibleLength = (int)(value.Length * fractionToKeep);
        return value[..visibleLength] + new string('.', MaskedSuffixLength);
    }
}
