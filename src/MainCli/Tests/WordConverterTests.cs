using ConvertDocToDocx;
using Xunit;

public sealed class WordConverterTests
{
    [Fact]
    public void MissingComRegistrationBecomesCapabilityFailureAcrossProcessBoundary()
    {
        // Simulate the HRESULT from new Word.Application, then its process exit.
        Assert.True(WordConverterProtocol.IsMissingWordActivation(unchecked((int)0x80040154)));
        var error = Assert.Throws<PlatformNotSupportedException>(() =>
            DocToDocxConversionHelper.ClassifyConverterExit(WordConverterProtocol.MissingWordExitCode));
        Assert.Contains("Microsoft Word", error.Message);
        Assert.Contains("DOCX", error.Message);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070005))] // E_ACCESSDENIED
    [InlineData(unchecked((int)0x80080005))] // CO_E_SERVER_EXEC_FAILURE
    public void OtherComFailuresAreNotReportedAsMissingWord(int hResult)
    {
        Assert.False(WordConverterProtocol.IsMissingWordActivation(hResult));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(-532462766, false)] // Unhandled CLR exception process exit.
    public void OrdinaryConverterExitsRetainSuccessOrConversionFailure(int exitCode, bool success)
    {
        Assert.Equal(success, DocToDocxConversionHelper.ClassifyConverterExit(exitCode));
    }
}
