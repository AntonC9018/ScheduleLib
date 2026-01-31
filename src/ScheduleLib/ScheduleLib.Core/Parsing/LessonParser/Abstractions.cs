using System.Text;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing.Lesson;

public sealed class LessonParserFactory
{
    public readonly struct Deps()
    {
        public LessonTypeParser LessonTypeParser { get; init; } = LessonTypeParser.Instance;
        public ParityParser ParityParser { get; init; } = ParityParser.Instance;
        public RoomParser RoomParser { get; init; } = RoomParser.Instance;
        public ProcessSpaces ProcessSpacesCourseName { get; init; } = c => c.DefaultAll();
    }

    private readonly Deps _deps;

    public LessonParserFactory(Deps deps)
    {
        _deps = deps;
    }

    public LessonParser Create()
    {
        var lexer = LessonParsingHelper.CreateLexer();
        return new LessonParser(_deps, lexer);
    }
}

public readonly struct LessonParser
{
    private readonly LessonParserFactory.Deps _deps;

    public LessonParser(
        LessonParserFactory.Deps deps,
        Lexer lexer)
    {
        _deps = deps;
        Lexer = lexer;
    }

    public Lexer Lexer { get; }

    public IEnumerable<ParsedLesson> ParseLessons(StringBuilder sb)
    {
        return LessonParsingHelper.ParseLessons(new()
        {
            Lexer = Lexer,
            StringBuilder = sb,
            LessonTypeParser = _deps.LessonTypeParser,
            ParityParser = _deps.ParityParser,
            RoomParser = _deps.RoomParser,
            ProcessSpacesCourseName = _deps.ProcessSpacesCourseName,
        });
    }
}
