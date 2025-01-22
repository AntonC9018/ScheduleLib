namespace ScheduleLib;

public static class LanguageHelper
{
    private static readonly string[] LanguageNames = GetLanguageNames();

    private static string[] GetLanguageNames()
    {
        var names = new string[(int) Language._Count];
        for (int i = 0; i < names.Length; i++)
        {
            var lang = (Language) i;
            names[i] = lang.ToString().ToLower();
        }
        return names;
    }

    public static string GetName(this Language lang)
    {
        return LanguageNames[(int) lang];
    }

    public static Language? ParseName(ReadOnlySpan<char> chars)
    {
        for (int i = 0; i < LanguageNames.Length; i++)
        {
            if (chars.Equals(LanguageNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return (Language) i;
            }
        }
        return null;
    }
}
