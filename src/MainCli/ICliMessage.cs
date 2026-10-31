namespace ScheduleLib.Cli;

/// <summary>Structured presentation data; the output boundary chooses when and how to render it.</summary>
public interface ICliMessage
{
    string Format(IFormatProvider provider);
}
