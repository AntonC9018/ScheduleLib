using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;

namespace MainCli;

public static class Config
{
    public static CourseNameParserConfig CourseNameParser => new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#", "Python"],
        IgnoredFullWords = ["p/u", "pentru"],
        IgnoredShortenedWords = ["Opț"],
        IgnoredProgrammingRelatedWords = ["Programare", "limbaj"],
        MinUsefulWordLength = 3,
    });

    public static void ConfigureRemappings(Remappings remap)
    {
        var teach = remap.TeacherLastNameRemappings;
        teach.Add("Curmanschi", "Curmanschii");
        teach.Add("Vișnevschi", "Vișnevschii");
        teach.Add("Băț", "Beț");
        teach.Add("Spincean", "Sprîncean");
        teach.Add("Anghelova", "Anghelov");
    }
}
