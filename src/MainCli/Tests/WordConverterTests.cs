using ConvertDocToDocx;

public sealed class WordConverterTests
{
    [Fact]
    public void MissingComRegistrationBecomesCapabilityFailureAcrossProcessBoundary()
    {
        // Simulate the HRESULT from new Word.Application, then its process exit.
        bool CalculateCondition()
        {
            bool condition = WordConverterProtocol.IsMissingWordActivation(unchecked((int) 0x80040154));
            return condition;
        }

        Assert.True(CalculateCondition());
        var error = Assert.Throws<PlatformNotSupportedException>(() =>
            DocToDocxConversionHelper.ClassifyConverterExit(WordConverterProtocol.MissingWordExitCode));
        Assert.Contains(expectedSubstring: "Microsoft Word", actualString: error.Message);
        Assert.Contains(expectedSubstring: "DOCX", actualString: error.Message);
    }

    [Theory]
    [InlineData(unchecked((int) 0x80070005))] // E_ACCESSDENIED
    [InlineData(unchecked((int) 0x80080005))] // CO_E_SERVER_EXEC_FAILURE
    public void OtherComFailuresAreNotReportedAsMissingWord(int hResult)
    {
        var missing = WordConverterProtocol.IsMissingWordActivation(hResult);
        Assert.False(missing);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(-532462766, false)] // Unhandled CLR exception process exit.
    public void OrdinaryConverterExitsRetainSuccessOrConversionFailure(int exitCode, bool success)
    {
        var actual = DocToDocxConversionHelper.ClassifyConverterExit(exitCode);
        Assert.Equal(expected: success, actual: actual);
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
        bool CalculateCondition()
        {
            bool condition = WordConverterProtocol.TryReadOwnership(
                message: message,
                nonce: "nonce",
                out _,
                out _);
            return condition;
        }

        Assert.False(
            CalculateCondition());
    }

    [Fact]
    public void OwnershipRoundTripsAndRejectsReusedOrUnrelatedProcesses()
    {
        var message = WordConverterProtocol.OwnershipMessage(
            "nonce",
            42,
            100);
        bool condition7 = WordConverterProtocol.TryReadOwnership(
            message: message,
            nonce: "nonce",
            out var pid,
            out var ticks);
        Assert.True(
            condition7);

        bool CalculateCondition()
        {
            bool condition = WordConverterProtocol.IsOwnedProcess(
                pid: pid,
                reportedTicks: ticks,
                actualTicks: 100,
                converterTicks: 90,
                session: 1,
                converterSession: 1,
                "WINWORD",
                false);
            return condition;
        }

        Assert.True(
            CalculateCondition());

        bool CalculateCondition2()
        {
            bool condition2 = WordConverterProtocol.IsOwnedProcess(
                pid: pid,
                reportedTicks: ticks,
                actualTicks: 101,
                converterTicks: 90,
                session: 1,
                converterSession: 1,
                "WINWORD",
                false);
            return condition2;
        }

        Assert.False(
            CalculateCondition2()); // recycled PID

        bool CalculateCondition3()
        {
            bool condition3 = WordConverterProtocol.IsOwnedProcess(
                pid: pid,
                reportedTicks: ticks,
                actualTicks: 100,
                converterTicks: 90,
                session: 1,
                converterSession: 1,
                "WINWORD",
                true);
            return condition3;
        }

        Assert.False(
            CalculateCondition3()); // existing Word

        bool CalculateCondition4()
        {
            bool condition4 = WordConverterProtocol.IsOwnedProcess(
                pid: pid,
                reportedTicks: ticks,
                actualTicks: 100,
                converterTicks: 110,
                session: 1,
                converterSession: 1,
                "WINWORD",
                false);
            return condition4;
        }

        Assert.False(
            CalculateCondition4()); // older process

        bool CalculateCondition5()
        {
            bool condition5 = WordConverterProtocol.IsOwnedProcess(
                pid: pid,
                reportedTicks: ticks,
                actualTicks: 100,
                converterTicks: 90,
                session: 2,
                converterSession: 1,
                "WINWORD",
                false);
            return condition5;
        }

        Assert.False(
            CalculateCondition5()); // other session

        bool CalculateCondition6()
        {
            bool condition6 = WordConverterProtocol.IsOwnedProcess(
                pid: pid,
                reportedTicks: ticks,
                actualTicks: 100,
                converterTicks: 90,
                session: 1,
                converterSession: 1,
                "other",
                false);
            return condition6;
        }

        Assert.False(
            CalculateCondition6());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StagingCleanupCannotMaskCancellation(bool denied)
    {
        using var temp = new TestTempDirectory("word-converter-tests");
        using var staging = OwnedConversionDirectory.Create(temp.Path);
        var error = Assert.Throws<OperationCanceledException>(
            (Action) (() =>
            {
                try
                {
                    throw new OperationCanceledException("original");
                }
                finally
                {
                    staging.Cleanup(_ =>
                    {
                        if (denied)
                        {
                            throw new UnauthorizedAccessException();
                        }

                        throw new IOException("Word still holds a file lock");
                    });
                }
            }));
        Assert.Equal(expected: "original", actual: error.Message);
    }

    [Fact]
    public void StagingCleanupRejectsReplacementLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Creating Windows symlinks can require elevation.
        }

        using var temp = new TestTempDirectory("word-converter-tests");
        using var staging = OwnedConversionDirectory.Create(temp.Path);
        using var outside = OwnedConversionDirectory.Create(temp.Path);
        var retained = Path.Combine(outside.DirectoryPath, "retained");
        File.WriteAllText(path: retained, contents: "keep");
        Directory.Delete(staging.DirectoryPath);
        Directory.CreateSymbolicLink(path: staging.DirectoryPath, pathToTarget: outside.DirectoryPath);
        try
        {
            staging.Dispose();
            bool condition = File.Exists(retained);
            Assert.True(condition);
            var directoryInfo = new DirectoryInfo(staging.DirectoryPath);
            object? objectValue = directoryInfo.LinkTarget;
            Assert.NotNull(objectValue);
        }
        finally
        {
            Directory.Delete(staging.DirectoryPath);
        }
    }

    [Fact]
    public async Task AlreadyCancelledConversionDoesNotLaunchAnyProcess()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DocToDocxConversionHelper.TryConvertFile(
                inputPath: "missing.doc",
                outputPath: "missing.docx",
                cancelled.Token));
    }

    [Fact]
    public async Task ProcessWaitHasABoundedTimeout()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Linux portable process test, no Word installed.
        }

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName: "/bin/sleep", arguments: "10")
        {
            UseShellExecute = false,
        })!;
        try
        {
            TimeSpan CalculateTimeout()
            {
                var timeout = TimeSpan.FromMilliseconds(50);
                return timeout;
            }

            Assert.False(await DocToDocxConversionHelper.WaitForExitBounded(process, CalculateTimeout()));
            process.Kill();

            TimeSpan CalculateTimeout2()
            {
                var timeout2 = TimeSpan.FromSeconds(2);
                return timeout2;
            }

            Assert.True(await DocToDocxConversionHelper.WaitForExitBounded(process, CalculateTimeout2()));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationSignalsCooperativeConverterAndBoundsBlockedConverter(bool cooperative)
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Exercises the protocol with a portable fake converter.
        }

        using var temp = new TestTempDirectory("word-converter-tests");
        using var staging = OwnedConversionDirectory.Create(temp.Path);
        var cancellationPath = Path.Combine(staging.DirectoryPath, "cancel");
        var info = new System.Diagnostics.ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add("-c");
        var collection = info.ArgumentList;
        string item;
        if (cooperative)
        {
            item = "while [ ! -f \"$1\" ]; do sleep 0.05; done; exit 130";
        }
        else
        {
            item = "sleep 30";
        }

        collection.Add(item);
        info.ArgumentList.Add("fake-converter");
        info.ArgumentList.Add(cancellationPath);
        using var process = System.Diagnostics.Process.Start(info)!;

        TimeSpan CalculateDelay()
        {
            var delay = TimeSpan.FromMilliseconds(100);
            return delay;
        }

        using var cancellation = new CancellationTokenSource(CalculateDelay());
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                DocToDocxConversionHelper.WaitForConverter(
                    process,
                    nonce: "nonce",
                    [],
                    cancellationPath: cancellationPath,
                    cancellation.Token));
            Assert.True(process.HasExited);
            bool condition = File.Exists(cancellationPath);
            Assert.True(condition);
            if (cooperative)
            {
                Assert.Equal(expected: 130, actual: process.ExitCode);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
