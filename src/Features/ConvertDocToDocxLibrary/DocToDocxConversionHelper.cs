using System.Diagnostics;

namespace ConvertDocToDocx;

public static class DocToDocxConversionHelper
{
    public static async Task<bool> TryConvertFile(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Legacy .doc conversion requires Windows and Microsoft Word. Supply DOCX sources on Linux.");
        inputPath = Path.GetFullPath(inputPath);
        outputPath = Path.GetFullPath(outputPath);

        var converterPath = Path.Combine(AppContext.BaseDirectory, "ConvertDocToDocx.exe");
        if (!File.Exists(converterPath))
            throw new PlatformNotSupportedException("Legacy .doc conversion requires the bundled Windows converter and Microsoft Word. Supply DOCX sources instead.");
        var processInfo = new ProcessStartInfo(
            converterPath,
            arguments: [
                inputPath,
                outputPath,
            ]);

        using var process = Process.Start(processInfo);
        if (process is null)
        {
            throw Unreachable();
        }
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return ClassifyConverterExit(process.ExitCode);
    }
    internal static bool ClassifyConverterExit(int exitCode)
    {
        if (exitCode == WordConverterProtocol.MissingWordExitCode)
            throw new PlatformNotSupportedException(WordConverterProtocol.MissingWordMessage);
        return exitCode == 0;
    }
}
