using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="SensitiveValueMasker.Mask"/>.</summary>
public sealed class SensitiveValueMaskerTests
{
    // Mirrors the private MaskedLength constant in SensitiveValueMasker: every masked output must be
    // exactly this many characters, whatever the real value's length.
    private const int ExpectedMaskedLength = 10;

    [Theory]
    [InlineData("", "**********")] // empty: nothing to reveal
    [InlineData("abcdefg", "**********")] // 7 characters: shorter than the 8-character reveal threshold
    [InlineData("abcdefgh", "ab********")] // 8 characters: exactly at the threshold, so the leading 2 show
    [InlineData("abcdefghij", "ab********")] // 10 characters: still just the leading 2
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", "ab********")] // a long value: still capped at 2 leading characters, not a fraction of its own length
    public void Mask_WhenGivenVariousInputs_RevealsAtMostTwoLeadingCharacters(
        string value,
        string expectedMasked
    )
    {
        // test
        var masked = SensitiveValueMasker.Mask(value);

        // verify
        Assert.Equal(expectedMasked, masked);
    }

    [Fact]
    public void Mask_WhenGivenVariousLengths_ReturnsConstantLengthOutput()
    {
        // setup: values of very different lengths, including empty and one character, must all mask to
        // the same total length -- otherwise the masked output's length would leak the real value's length.
        string[] values =
        [
            "",
            "a",
            "abcdefg",
            "abcdefgh",
            "a-very-long-value-that-is-much-longer-than-the-others-here",
            new string('x', 500),
        ];

        // test
        var maskedLengths = values.Select(value => SensitiveValueMasker.Mask(value).Length);

        // verify: every input, regardless of its own length, produces output of the same fixed length.
        Assert.All(maskedLengths, length => Assert.Equal(ExpectedMaskedLength, length));
    }

    [Fact]
    public void Mask_WhenValueIsShorterThanTheRevealThreshold_HidesItCompletely()
    {
        // setup: a value one character short of the 8-character threshold that gates revealing any of
        // the real value -- revealing where its stars start would otherwise leak its exact length.
        var masked = SensitiveValueMasker.Mask("shortpw");

        // test & verify
        Assert.Equal("**********", masked);
    }

    [Fact]
    public void Mask_WhenValueMeetsTheRevealThreshold_NeverReturnsFullyVisibleOutput()
    {
        // setup
        var shortMasked = SensitiveValueMasker.Mask("shortpwd");
        var longMasked = SensitiveValueMasker.Mask("a-much-much-much-longer-value-than-short");

        // test & verify: both are the fixed length and still carry stars, so neither looks like an
        // unmasked value.
        Assert.Equal(ExpectedMaskedLength, shortMasked.Length);
        Assert.Equal(ExpectedMaskedLength, longMasked.Length);
        Assert.Contains('*', shortMasked);
        Assert.Contains('*', longMasked);
    }
}
