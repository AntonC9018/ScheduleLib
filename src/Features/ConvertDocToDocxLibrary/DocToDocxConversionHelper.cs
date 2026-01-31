using System.Diagnostics;

namespace ConvertDocToDocx;

public static class DocToDocxConversionHelper
{
    // TODO: This is only for windows
    public static async Task<bool> TryConvertFile(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        inputPath = Path.GetFullPath(inputPath);
        outputPath = Path.GetFullPath(outputPath);

        var converterPath = ConverterPath;
        var processInfo = new ProcessStartInfo(
            converterPath,
            arguments: [
                inputPath,
                outputPath,
            ]);

        var process = Process.Start(processInfo);
        if (process is null)
        {
            throw Unreachable();
        }
        await process.WaitForExitAsync(cancellationToken);

        // File.Delete(filePath);

        if (process.ExitCode != 0)
        {
            return false;
        }
        return true;
    }

    private static string ReplaceLastSegmentOfPath(string input, string fileName)
    {
        var directory = Path.GetDirectoryName(input);
        if (directory is null)
        {
            throw new InvalidOperationException("Path has no directory");
        }
        return Path.Combine(directory, fileName);
    }

    private static string CreateConverterPath()
    {
        var executablePath = Environment.ProcessPath;
        if (executablePath is null)
        {
            throw Unreachable();
        }
        var converterPath = ReplaceLastSegmentOfPath(executablePath, "ConvertDocToDocx.exe");
        return converterPath;
    }
    private static readonly string ConverterPath = CreateConverterPath();
}
