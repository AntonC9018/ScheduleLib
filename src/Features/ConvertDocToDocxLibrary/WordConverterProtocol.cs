using System.Globalization;

namespace ConvertDocToDocx;

// Shared with the net48 converter so the process boundary has one exit contract.
internal static class WordConverterProtocol
{
    internal const int MissingWordExitCode = 8;
    internal const string MissingWordMessage = "Legacy .doc conversion requires a registered Microsoft Word installation on Windows. Install or repair Word, or supply DOCX sources instead.";
    internal static bool IsMissingWordActivation(int hResult) => hResult == unchecked((int)0x80040154);

    internal static string OwnershipMessage(string nonce, int pid, long startTicks) =>
        string.Join("|", "WORD1", nonce, pid.ToString(CultureInfo.InvariantCulture), startTicks.ToString(CultureInfo.InvariantCulture));

    internal static bool TryReadOwnership(string? message, string nonce, out int pid, out long startTicks)
    {
        pid = 0;
        startTicks = 0;
        var fields = message?.Split('|');
        return fields is { Length: 4 } && fields[0] == "WORD1" && fields[1] == nonce &&
            int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0 &&
            long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks) && startTicks > 0;
    }

    internal static bool IsOwnedProcess(int pid, long reportedTicks, long actualTicks, long converterTicks,
        int session, int converterSession, string processName, bool existedBeforeConversion) =>
        pid > 0 && !existedBeforeConversion && reportedTicks == actualTicks && actualTicks >= converterTicks &&
        session == converterSession && string.Equals(processName, "WINWORD", System.StringComparison.OrdinalIgnoreCase);
}
