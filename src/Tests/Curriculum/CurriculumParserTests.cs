using ScheduleLib.Curriculum;

namespace Curriculum.Tests;

public sealed class CurriculumParserTests
{
    private const string TestCurriculumFileName = @"data\11_I_an1_RC_Capcelea_2024.docx";

    [Fact]
    public async Task FileNameParsedCorrectly()
    {
        var key = CurriculumDirectoryHelper.ParseCurriculumFileKey(TestCurriculumFileName);
        Assert.NotNull(key);
        await Verify(key);
    }

    [Fact]
    public async Task CurriculumParsedCorrectly()
    {
        var file = CurriculumFile.FromFilePath(TestCurriculumFileName);
        var curriculum = await CurriculumParser.ReadFile(file!.Value);
        await Verify(curriculum);
    }
}
