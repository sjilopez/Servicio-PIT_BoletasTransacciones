using PIT.Boletas.Domain.Services;

namespace PIT.Boletas.UnitTests;

public sealed class DocumentNameComposerTests
{
    [Theory]
    [InlineData("SJ00PIT01", "SJ00")]
    [InlineData("SJ25GER01", "SJ25")]
    [InlineData("SJAGM2001", "SJAGM")]
    public void ResolveAgency_ReturnsExpectedValue(string host, string expected)
    {
        string agency = DocumentNameComposer.ResolveAgency(host);
        Assert.Equal(expected, agency);
    }

    [Fact]
    public void BuildBaseFileName_UsesOfficialPattern()
    {
        DateTime creation = new(2026, 8, 17, 16, 34, 5, 157);
        string name = DocumentNameComposer.BuildBaseFileName("SJ00", "SJILOPEZ", creation);

        Assert.Equal("SJ00_SJILOPEZ_20260817_163405_157", name);
    }
}
