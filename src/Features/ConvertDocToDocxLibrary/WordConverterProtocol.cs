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
        if (!(fields is { Length: 4 }))
        {
            return false;
        }

        if (!(fields[0] == "WORD1"))
        {
            return false;
        }

        if (!(fields[1] == nonce))
        {
            return false;
        }

        if (!(int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out pid)))
        {
            return false;
        }

        if (!(pid > 0))
        {
            return false;
        }

        if (!(long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks)))
        {
            return false;
        }

        if (!(startTicks > 0))
        {
            return false;
        }

        return true;
    }

    internal static bool IsOwnedProcess(int pid, long reportedTicks, long actualTicks, long converterTicks,
        int session, int converterSession, string processName, bool existedBeforeConversion)
    {
        bool CheckCondition()
        {
            if (!(pid > 0))
            {
                return false;
            }

            if (!(!existedBeforeConversion))
            {
                return false;
            }

            if (!(reportedTicks == actualTicks))
            {
                return false;
            }

            if (!(actualTicks >= converterTicks))
            {
                return false;
            }

            if (!(session == converterSession))
            {
                return false;
            }

            if (!(string.Equals(processName, "WINWORD", System.StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        return CheckCondition();
    }
}
