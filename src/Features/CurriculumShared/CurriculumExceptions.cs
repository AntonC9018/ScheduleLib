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

public enum ProgramCodeSyntaxError
{
    MissingCode,
    MissingSeparatorAfterPrefix,
    MissingNumberAfterSeparator,
}

public sealed class InvalidProgramCodeSyntaxException(ProgramCodeSyntaxError error)
    : NotSupportedException(CreateMessage(error))
{
    public ProgramCodeSyntaxError Error { get; } = error;

    private static string CreateMessage(ProgramCodeSyntaxError error)
    {
        return error switch
        {
            ProgramCodeSyntaxError.MissingCode => "Expected code.",
            ProgramCodeSyntaxError.MissingSeparatorAfterPrefix => "Expected '.' after first part of code.",
            ProgramCodeSyntaxError.MissingNumberAfterSeparator => "Expected number after '.'.",
            _ => $"Invalid program code syntax ({error}).",
        };
    }
}

public sealed class DuplicateCurriculumAttendanceModeException(AttendanceMode mode)
    : NotSupportedException($"The same attendance mode '{mode}' specified a second time.")
{
    public AttendanceMode Mode { get; } = mode;
}

public sealed class UnparsedCurriculumGroupSegmentPlusException(string segmentText)
    : NotSupportedException($"'+' in the last group segment without it parsing: '{segmentText}'.")
{
    public string SegmentText { get; } = segmentText;
}

public sealed class UnknownCurriculumQualificationException(string qualificationText)
    : NotSupportedException($"Unknown qualification '{qualificationText}'.")
{
    public string QualificationText { get; } = qualificationText;
}

public enum CurriculumKeySyntaxError
{
    MissingCourseSegment,
    MissingCourseSeparator,
    MissingGradePrefix,
    MissingGradeNumber,
    MissingGradeSeparator,
    MissingYear,
    LeadingNumberTooLarge,
    MissingSpecialtySegment,
    MissingSpecialtySeparator,
}

public sealed class InvalidCurriculumKeySyntaxException(CurriculumKeySyntaxError error)
    : NotSupportedException(CreateMessage(error))
{
    public CurriculumKeySyntaxError Error { get; } = error;

    private static string CreateMessage(CurriculumKeySyntaxError error)
    {
        return error switch
        {
            CurriculumKeySyntaxError.MissingCourseSegment => "There must be the short course name following the year.",
            CurriculumKeySyntaxError.MissingCourseSeparator => "Expected '_' after the course name.",
            CurriculumKeySyntaxError.MissingGradePrefix => "Expected 'anX' where X is the year after the specialty.",
            CurriculumKeySyntaxError.MissingGradeNumber => "A number must follow 'an'.",
            CurriculumKeySyntaxError.MissingGradeSeparator => "Expected '_' after the grade.",
            CurriculumKeySyntaxError.MissingYear => "Expecting year at the end.",
            CurriculumKeySyntaxError.LeadingNumberTooLarge => "Number in front too large.",
            CurriculumKeySyntaxError.MissingSpecialtySegment =>
                "There must be the short specialty name following the initial numbers.",
            CurriculumKeySyntaxError.MissingSpecialtySeparator => "Expected '_' after the specialty.",
            _ => $"Invalid curriculum key syntax ({error}).",
        };
    }
}

public sealed class UnparsedCurriculumDirectoryNameException(string directoryPath)
    : InvalidOperationException($"Directory name not parsed fully: '{directoryPath}'.")
{
    public string DirectoryPath { get; } = directoryPath;
}

public sealed class CurriculumSelectionFailedException(int candidateCount)
    : InvalidOperationException($"Could not narrow down the curriculum (found {candidateCount} candidate(s)).")
{
    public int CandidateCount { get; } = candidateCount;
}

public sealed class MissingCurriculumDocumentException(string filePath)
    : InvalidOperationException($"No document found in '{filePath}'.")
{
    public string FilePath { get; } = filePath;
}

public sealed class MissingCurriculumDocumentBodyException(string filePath)
    : InvalidOperationException($"No body found in '{filePath}'.")
{
    public string FilePath { get; } = filePath;
}

public sealed class DuplicateCurriculumSectionException(string sectionName)
    : NotSupportedException($"The same section '{sectionName}' appears twice, which is not allowed.")
{
    public string SectionName { get; } = sectionName;
}

public enum CurriculumGeneralInfoStructureError
{
    MissingCurriculumHeading,
    MissingParagraphAfterCurriculum,
    ExpectedCourseModuleLabel,
    MissingCourseName,
    MissingYearAndQualification,
    MissingProgramAndSpecialty,
    MissingAuthorSection,
    ExpectedAutorPrefix,
    MissingAuthorNameParagraph,
    MissingAuthorName,
    MissingTrailingContentAfterAuthor,
    MissingCityYearLine,
    MissingYearAfterCity,
    InvalidYearDigitCount,
    ExpectedProgramLabel,
    ExpectedWhitespaceAroundPrograms,
    MissingSpecialtyNameAfterCode,
    ExpectedCommaAfterYear,
    ExpectedWhitespaceBeforeQualification,
    ExpectedCiclulPrefix,
    MissingContentAfterCiclul,
    MissingRomanNumberAfterCiclul,
    MultipleQualificationTypes,
}

public sealed class InvalidCurriculumGeneralInfoException(CurriculumGeneralInfoStructureError error)
    : InvalidOperationException(CreateMessage(error))
{
    public CurriculumGeneralInfoStructureError Error { get; } = error;

    private static string CreateMessage(CurriculumGeneralInfoStructureError error)
    {
        return error switch
        {
            CurriculumGeneralInfoStructureError.MissingCurriculumHeading => "Document does not contain CURRICULUM.",
            CurriculumGeneralInfoStructureError.MissingParagraphAfterCurriculum =>
                "No paragraphs found after CURRICULUM.",
            CurriculumGeneralInfoStructureError.ExpectedCourseModuleLabel =>
                "Expected string 'la unitatea de curs / modulul'.",
            CurriculumGeneralInfoStructureError.MissingCourseName => "Expected the course name to follow.",
            CurriculumGeneralInfoStructureError.MissingYearAndQualification =>
                "Expected the year and qualification to follow.",
            CurriculumGeneralInfoStructureError.MissingProgramAndSpecialty =>
                "Expected the program & specialty after year.",
            CurriculumGeneralInfoStructureError.MissingAuthorSection => "Expected author after the program.",
            CurriculumGeneralInfoStructureError.ExpectedAutorPrefix => "Expected 'AUTOR:'.",
            CurriculumGeneralInfoStructureError.MissingAuthorNameParagraph => "Expected author name paragraph.",
            CurriculumGeneralInfoStructureError.MissingAuthorName => "No author name specified.",
            CurriculumGeneralInfoStructureError.MissingTrailingContentAfterAuthor =>
                "Expected more content after the author.",
            CurriculumGeneralInfoStructureError.MissingCityYearLine => "Expected the city-year line as the last line.",
            CurriculumGeneralInfoStructureError.MissingYearAfterCity => "Expected year after city on the last line.",
            CurriculumGeneralInfoStructureError.InvalidYearDigitCount => "The year must be 4 digits.",
            CurriculumGeneralInfoStructureError.ExpectedProgramLabel => "Expected string 'Program / Specialitatea:'.",
            CurriculumGeneralInfoStructureError.ExpectedWhitespaceAroundPrograms =>
                "Expected whitespace around programs.",
            CurriculumGeneralInfoStructureError.MissingSpecialtyNameAfterCode =>
                "Expected specialty name after the code.",
            CurriculumGeneralInfoStructureError.ExpectedCommaAfterYear => "Expected ',' after the year.",
            CurriculumGeneralInfoStructureError.ExpectedWhitespaceBeforeQualification =>
                "Expected whitespace before qualification type.",
            CurriculumGeneralInfoStructureError.ExpectedCiclulPrefix =>
                "Expected the year string to start with 'Ciclul'.",
            CurriculumGeneralInfoStructureError.MissingContentAfterCiclul =>
                "Did not expect the string to end after 'Ciclul'.",
            CurriculumGeneralInfoStructureError.MissingRomanNumberAfterCiclul =>
                "Roman number must follow after 'Ciclul'.",
            CurriculumGeneralInfoStructureError.MultipleQualificationTypes => "Expected only one qualification type.",
            _ => $"Invalid curriculum general info ({error}).",
        };
    }
}

public sealed class UnknownCurriculumQualificationTypeException(string qualificationText)
    : InvalidOperationException($"Unknown qualification type '{qualificationText}'.")
{
    public string QualificationText { get; } = qualificationText;
}

public sealed class NonParagraphElementInPreliminaryException(Type elementType)
    : InvalidOperationException($"Only expected paragraphs in the preliminary, found {elementType.Name}.")
{
    public Type ElementType { get; } = elementType;
}

public sealed class EmptyPreliminarySubsectionException(PreliminaryField field)
    : NotSupportedException($"Subsection '{field}' in preliminary must not be empty.")
{
    public PreliminaryField Field { get; } = field;
}

public enum DisciplineProvisionsTableDataError
{
    MissingRows,
    MissingDataRows,
}

public sealed class InvalidDisciplineProvisionsTableException(DisciplineProvisionsTableDataError error)
    : NotSupportedException(CreateMessage(error))
{
    public DisciplineProvisionsTableDataError Error { get; } = error;

    private static string CreateMessage(DisciplineProvisionsTableDataError error)
    {
        return error switch
        {
            DisciplineProvisionsTableDataError.MissingRows => "Expected rows in the table.",
            DisciplineProvisionsTableDataError.MissingDataRows => "Table without data.",
            _ => $"Invalid discipline provisions table ({error}).",
        };
    }
}

public enum CurriculumAttendanceModeParseError
{
    Empty,
    Unrecognized,
}

public sealed class InvalidCurriculumAttendanceModeException(CurriculumAttendanceModeParseError error)
    : NotSupportedException(CreateMessage(error))
{
    public CurriculumAttendanceModeParseError Error { get; } = error;

    private static string CreateMessage(CurriculumAttendanceModeParseError error)
    {
        return error switch
        {
            CurriculumAttendanceModeParseError.Empty => "Attendance mode must not be empty.",
            CurriculumAttendanceModeParseError.Unrecognized => "Unrecognized attendance mode.",
            _ => $"Invalid attendance mode ({error}).",
        };
    }
}

public sealed class CurriculumColumnParsingNotImplementedException(string columnName)
    : NotImplementedException($"Parsing for curriculum column '{columnName}' is not implemented.")
{
    public string ColumnName { get; } = columnName;
}

public sealed class InvalidDisciplineDistributionColumnPlacementException(
    string columnName,
    string existingColumnName)
    : NotSupportedException(
        $"Distribution column '{columnName}' cannot overlap existing column '{existingColumnName}'; it must be standalone or inside an hours column.")
{
    public string ColumnName { get; } = columnName;
    public string ExistingColumnName { get; } = existingColumnName;
}

public sealed class DuplicateDisciplineHeaderColumnOverlapException(
    string columnName,
    string existingColumnName)
    : NotSupportedException($"Column '{columnName}' unexpectedly overlaps existing column '{existingColumnName}'.")
{
    public string ColumnName { get; } = columnName;
    public string ExistingColumnName { get; } = existingColumnName;
}

public enum DisciplineProvisionsTablePresenceError
{
    MissingTable,
    MultipleTables,
}

public sealed class DisciplineProvisionsTablePresenceException(DisciplineProvisionsTablePresenceError error)
    : InvalidOperationException(CreateMessage(error))
{
    public DisciplineProvisionsTablePresenceError Error { get; } = error;

    private static string CreateMessage(DisciplineProvisionsTablePresenceError error)
    {
        return error switch
        {
            DisciplineProvisionsTablePresenceError.MissingTable => "Expected a table in the discipline provisions.",
            DisciplineProvisionsTablePresenceError.MultipleTables =>
                "Expected only one table in the discipline provisions.",
            _ => $"Invalid discipline provisions table count ({error}).",
        };
    }
}
