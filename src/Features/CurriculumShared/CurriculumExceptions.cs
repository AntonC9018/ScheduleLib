namespace ScheduleLib.Curriculum;

public sealed class UnknownPreliminaryFieldException(PreliminaryField field)
    : NotSupportedException($"Unknown preliminary field {field}")
{
    public PreliminaryField Field { get; } = field;
}

public sealed class ProgramCodeParsingException(NotSupportedException parseFailure)
    : InvalidOperationException($"Error while parsing program code: {parseFailure}", parseFailure)
{
    public NotSupportedException ParseFailure { get; } = parseFailure;
}

public sealed class DisallowedTeacherQualificationTokenException(string token)
    : InvalidOperationException($"Disallowed teacher qualification token: {token}")
{
    public string Token { get; } = token;
}

public sealed class RepeatedPreliminaryFieldException(PreliminaryField field)
    : InvalidOperationException($"'{field}' field appears twice in the preliminary")
{
    public PreliminaryField Field { get; } = field;
}

public sealed class RepeatedDisciplineColumnException(string columnName)
    : NotSupportedException($"The same column {columnName} appears twice, not allowed.")
{
    public string ColumnName { get; } = columnName;
}
