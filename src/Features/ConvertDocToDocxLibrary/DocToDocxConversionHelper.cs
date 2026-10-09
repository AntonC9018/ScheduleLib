using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ConvertDocToDocx;

public static class DocToDocxConversionHelper
{
    public static async Task<bool> TryConvertFile(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Legacy .doc conversion requires Windows and Microsoft Word. Supply DOCX sources on Linux.");
        var converterPath = Path.Combine(AppContext.BaseDirectory, "ConvertDocToDocx.exe");
        if (!File.Exists(converterPath))
            throw new PlatformNotSupportedException("Legacy .doc conversion requires the bundled Windows converter and Microsoft Word. Supply DOCX sources instead.");

        var existingWord = new HashSet<int>();
        foreach (var word in Process.GetProcessesByName("WINWORD"))
        {
            using (word) existingWord.Add(word.Id);
        }
        using var staging = OwnedConversionDirectory.Create(Path.GetTempPath());
        var cancellationPath = Path.Combine(staging.DirectoryPath, "cancel");
        var nonce = Guid.NewGuid().ToString("N");
        var processInfo = new ProcessStartInfo(converterPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), nonce, cancellationPath })
            processInfo.ArgumentList.Add(argument);
        using var process = Process.Start(processInfo) ?? throw Unreachable();
        return await WaitForConverter(process, nonce, existingWord, cancellationPath, cancellationToken);
    }

    internal static async Task<bool> WaitForConverter(Process process, string nonce, HashSet<int> existingWord,
        string cancellationPath, CancellationToken cancellationToken)
    {
        using var ownershipReadCancellation = new CancellationTokenSource();
        // Read immediately, pinning the process handle while Word is still alive.
        var ownership = ReadOwnedWord(process, nonce, existingWord, ownershipReadCancellation.Token);
        Process? ownedWord = null;
        try
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try { File.WriteAllText(cancellationPath, "cancel"); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                var converterExited = await WaitForExitBounded(process, TimeSpan.FromSeconds(2));
                // A blocked COM call cannot observe cooperative cancellation; a
                // failing Quit can also leave Word behind after the converter exits.
                // Never terminate an unknown, preexisting, or recycled PID.
                ownershipReadCancellation.Cancel();
                ownedWord = await ownership;
                if (ownedWord is not null) TerminateProcess(ownedWord.SafeHandle, 130);
                if (!converterExited)
                {
                    TryKillConverter(process);
                    await WaitForExitBounded(process, TimeSpan.FromSeconds(3));
                }
                throw;
            }
            return ClassifyConverterExit(process.ExitCode);
        }
        finally
        {
            ownershipReadCancellation.Cancel();
            ownedWord ??= await ownership;
            ownedWord?.Dispose();
        }
    }

    private static async Task<Process?> ReadOwnedWord(Process converter, string nonce, HashSet<int> existingWord, CancellationToken cancellationToken)
    {
        Process? word = null;
        try
        {
            var message = await converter.StandardOutput.ReadLineAsync(cancellationToken);
            if (!WordConverterProtocol.TryReadOwnership(message, nonce, out var pid, out var ticks)) return null;
            word = Process.GetProcessById(pid);
            // Opening the handle before reading identity keeps it attached to this
            // process object even if the PID is subsequently recycled.
            _ = word.SafeHandle;
            if (WordConverterProtocol.IsOwnedProcess(pid, ticks, word.StartTime.ToUniversalTime().Ticks,
                    converter.StartTime.ToUniversalTime().Ticks, word.SessionId, converter.SessionId,
                    word.ProcessName, existingWord.Contains(pid))) return word;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        {
            // An unverifiable identity is never a license to kill a process.
        }
        word?.Dispose();
        return null;
    }

    internal static async Task<bool> WaitForExitBounded(Process process, TimeSpan timeout)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    private static void TryKillConverter(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    internal static bool ClassifyConverterExit(int exitCode)
    {
        if (exitCode == WordConverterProtocol.MissingWordExitCode)
            throw new PlatformNotSupportedException(WordConverterProtocol.MissingWordMessage);
        return exitCode == 0;
    }
}
