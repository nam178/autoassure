using A2.Server.Models;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for <see cref="SensitiveValueMasker.Mask"/>.</summary>
public sealed class SensitiveValueMaskerTests
{
    [Theory]
    [InlineData("", 0.3, "")] // empty string: nothing to keep
    [InlineData("a", 0.3, "")] // one character: 30% of 1 truncates to 0
    [InlineData("abcdefghij", 0.3, "abc")] // 10 characters: 30% keeps the first 3
    [InlineData("abcdefghij", 0.15, "a")] // 10 characters: 15% keeps the first 1
    [InlineData(
        "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz",
        0.15,
        "abcdefg"
    )] // a long value
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
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abcdefghij")]
    [InlineData("a-very-long-value-that-is-much-longer-than-the-others-here")]
    public void Mask_WhenGivenVariousLengths_ReturnsConstantLengthOutput(string value)
    {
        // test
        var masked = SensitiveValueMasker.Mask(value, 0.3);

        // verify: the masked length must not depend on the input length, or the real length leaks.
        Assert.Equal(8, masked.Length - (int)(value.Length * 0.3));
    }

    [Fact]
    public void Mask_WhenGivenDifferentLengths_AlwaysReturnsTheSameSuffixLength()
    {
        // setup
        var shortMasked = SensitiveValueMasker.Mask("short", 0.3);
        var longMasked = SensitiveValueMasker.Mask(
            "a-much-much-much-longer-value-than-short",
            0.3
        );

        // test
        var shortSuffix = shortMasked[1..]; // "short" keeps 1 visible char (30% of 5 truncates to 1)
        var longSuffix = longMasked[12..]; // keeps 12 visible chars (30% of 41 truncates to 12)

        // verify
        Assert.Equal(shortSuffix, longSuffix);
        Assert.Equal(new string('.', 8), shortSuffix);
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
