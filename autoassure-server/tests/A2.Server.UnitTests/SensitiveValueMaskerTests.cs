using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="SensitiveValueMasker.Mask"/>.</summary>
public sealed class SensitiveValueMaskerTests
{
    // Mirrors the private MaskedLength constant in SensitiveValueMasker: every masked output must be
    // exactly this many characters, whatever the real value's length.
    private const int ExpectedMaskedLength = 20;

    [Theory]
    [InlineData("", 0.3, "")] // empty string: nothing to reveal
    [InlineData("a", 0.3, "a")] // one character: shorter than the visible slice, so shown in full
    [InlineData("abcdefghij", 0.3, "abcdef")] // 10 characters: 30% of the fixed length (20) is 6
    [InlineData("abcdefghij", 0.15, "abc")] // 10 characters: 15% of the fixed length (20) is 3
    [InlineData(
        "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz",
        0.15,
        "abc"
    )] // a long value: still capped at 15% of the fixed length, not 15% of its own length
    public void Mask_WhenGivenVariousInputs_KeepsOnlyTheLeadingFraction(
        string value,
        double fractionToKeep,
        string expectedPrefix
    )
    {
        // test
        var masked = SensitiveValueMasker.Mask(value, fractionToKeep);

        // verify
        Assert.StartsWith(expectedPrefix, masked);
        Assert.Equal(expectedPrefix, masked[..expectedPrefix.Length]);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.15)]
    public void Mask_WhenGivenVariousLengths_ReturnsConstantLengthOutput(double fractionToKeep)
    {
        // setup: values of very different lengths, including empty and one character, must all mask to
        // the same total length -- otherwise the masked output's length would leak the real value's length.
        string[] values =
        [
            "",
            "a",
            "abcdefghij",
            "a-very-long-value-that-is-much-longer-than-the-others-here",
            new string('x', 500),
        ];

        // test
        var maskedLengths = values.Select(value => SensitiveValueMasker.Mask(value, fractionToKeep).Length);

        // verify: every input, regardless of its own length, produces output of the same fixed length.
        Assert.All(maskedLengths, length => Assert.Equal(ExpectedMaskedLength, length));
    }

    [Fact]
    public void Mask_WhenValueIsNonEmpty_NeverReturnsEmptyOrFullyVisibleOutput()
    {
        // setup
        var shortMasked = SensitiveValueMasker.Mask("short", 0.3);
        var longMasked = SensitiveValueMasker.Mask("a-much-much-much-longer-value-than-short", 0.3);

        // test & verify: both are the fixed length, non-empty, and still carry at least one dot so
        // neither looks like an unmasked value.
        Assert.Equal(ExpectedMaskedLength, shortMasked.Length);
        Assert.Equal(ExpectedMaskedLength, longMasked.Length);
        Assert.Contains('.', shortMasked);
        Assert.Contains('.', longMasked);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Mask_WhenFractionOutOfRange_Throws(double fractionToKeep)
    {
        // test & verify
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SensitiveValueMasker.Mask("value", fractionToKeep)
        );
    }
}
