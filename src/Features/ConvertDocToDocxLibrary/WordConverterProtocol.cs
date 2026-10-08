namespace ConvertDocToDocx;

// Shared with the net48 converter so the process boundary has one exit contract.
internal static class WordConverterProtocol
{
    internal const int MissingWordExitCode = 8;
    internal const string MissingWordMessage = "Legacy .doc conversion requires a registered Microsoft Word installation on Windows. Install or repair Word, or supply DOCX sources instead.";

    // REGDB_E_CLASSNOTREG: Word's COM class cannot be activated. Other activation
    // failures (access denied, server execution failure) remain conversion errors.
    // https://learn.microsoft.com/windows/win32/api/combaseapi/nf-combaseapi-cocreateinstance
    internal static bool IsMissingWordActivation(int hResult) => hResult == unchecked((int)0x80040154);
}
