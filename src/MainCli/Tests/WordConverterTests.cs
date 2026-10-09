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

public sealed class WordConverterOwnershipTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("WORD1|other|42|100")]
    [InlineData("WORD1|nonce|0|100")]
    [InlineData("WORD1|nonce|42|-1")]
    [InlineData("WORD1|nonce|42|100|extra")]
    [InlineData("WORD1|nonce|NaN|100")]
    public void InvalidOwnershipMessagesCannotAuthorizeTermination(string? message)
    {
        Assert.False(WordConverterProtocol.TryReadOwnership(message, "nonce", out _, out _));
    }

    [Fact]
    public void OwnershipRoundTripsAndRejectsReusedOrUnrelatedProcesses()
    {
        var message = WordConverterProtocol.OwnershipMessage("nonce", 42, 100);
        Assert.True(WordConverterProtocol.TryReadOwnership(message, "nonce", out var pid, out var ticks));
        Assert.True(WordConverterProtocol.IsOwnedProcess(pid, ticks, 100, 90, 1, 1, "WINWORD", false));
        Assert.False(WordConverterProtocol.IsOwnedProcess(pid, ticks, 101, 90, 1, 1, "WINWORD", false)); // recycled PID
        Assert.False(WordConverterProtocol.IsOwnedProcess(pid, ticks, 100, 90, 1, 1, "WINWORD", true)); // existing Word
        Assert.False(WordConverterProtocol.IsOwnedProcess(pid, ticks, 100, 110, 1, 1, "WINWORD", false)); // older process
        Assert.False(WordConverterProtocol.IsOwnedProcess(pid, ticks, 100, 90, 2, 1, "WINWORD", false)); // other session
        Assert.False(WordConverterProtocol.IsOwnedProcess(pid, ticks, 100, 90, 1, 1, "other", false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StagingCleanupCannotMaskCancellation(bool denied)
    {
        using var staging = OwnedConversionDirectory.Create(Path.GetTempPath());
        var error = Assert.Throws<OperationCanceledException>((Action)(() =>
        {
            try { throw new OperationCanceledException("original"); }
            finally
            {
                staging.Cleanup(_ =>
                {
                    if (denied) throw new UnauthorizedAccessException();
                    throw new IOException("Word still holds a file lock");
                });
            }
        }));
        Assert.Equal("original", error.Message);
    }

    [Fact]
    public void StagingCleanupRejectsReplacementLink()
    {
        if (OperatingSystem.IsWindows()) return; // Creating Windows symlinks can require elevation.
        using var staging = OwnedConversionDirectory.Create(Path.GetTempPath());
        using var outside = OwnedConversionDirectory.Create(Path.GetTempPath());
        var retained = Path.Combine(outside.DirectoryPath, "retained");
        File.WriteAllText(retained, "keep");
        Directory.Delete(staging.DirectoryPath);
        Directory.CreateSymbolicLink(staging.DirectoryPath, outside.DirectoryPath);
        try
        {
            staging.Dispose();
            Assert.True(File.Exists(retained));
            Assert.NotNull(new DirectoryInfo(staging.DirectoryPath).LinkTarget);
        }
        finally { Directory.Delete(staging.DirectoryPath); }
    }

    [Fact]
    public async Task AlreadyCancelledConversionDoesNotLaunchAnyProcess()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DocToDocxConversionHelper.TryConvertFile("missing.doc", "missing.docx", cancelled.Token));
    }

    [Fact]
    public async Task ProcessWaitHasABoundedTimeout()
    {
        if (OperatingSystem.IsWindows()) return; // Linux portable process test, no Word installed.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sleep", "10") { UseShellExecute = false })!;
        try
        {
            Assert.False(await DocToDocxConversionHelper.WaitForExitBounded(process, TimeSpan.FromMilliseconds(50)));
            process.Kill();
            Assert.True(await DocToDocxConversionHelper.WaitForExitBounded(process, TimeSpan.FromSeconds(2)));
        }
        finally { if (!process.HasExited) process.Kill(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationSignalsCooperativeConverterAndBoundsBlockedConverter(bool cooperative)
    {
        if (OperatingSystem.IsWindows()) return; // Exercises the protocol with a portable fake converter.
        using var staging = OwnedConversionDirectory.Create(Path.GetTempPath());
        var cancellationPath = Path.Combine(staging.DirectoryPath, "cancel");
        var info = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(cooperative ? "while [ ! -f \"$1\" ]; do sleep 0.05; done; exit 130" : "sleep 30");
        info.ArgumentList.Add("fake-converter");
        info.ArgumentList.Add(cancellationPath);
        using var process = System.Diagnostics.Process.Start(info)!;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                DocToDocxConversionHelper.WaitForConverter(process, "nonce", [], cancellationPath, cancellation.Token));
            Assert.True(process.HasExited);
            Assert.True(File.Exists(cancellationPath));
            if (cooperative) Assert.Equal(130, process.ExitCode);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

}
