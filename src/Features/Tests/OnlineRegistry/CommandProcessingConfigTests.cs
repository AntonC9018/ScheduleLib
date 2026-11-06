namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class CommandProcessingConfigTests
{
    [Fact]
    public void HasMethodsWork()
    {
        var config = CommandProcessingConfig.None
            .WithProcess(LessonEquationCommandTypes.Update)
            .WithDryRun(LessonEquationCommandTypes.Delete)
            .Normalized;

        bool condition = config.HasProcess(LessonEquationCommandType.Update);
        Assert.True(condition);
        bool condition2 = config.HasProcess(LessonEquationCommandType.Delete);
        Assert.False(condition2);
        bool condition3 = config.HasAnyProcess(LessonEquationCommandTypes.All);
        Assert.True(condition3);
        bool condition4 = config.HasAnyProcess(LessonEquationCommandTypes.Delete | LessonEquationCommandTypes.Create);
        Assert.False(condition4);

        bool condition5 = config.HasDryRun(LessonEquationCommandType.Delete);
        Assert.True(condition5);
        bool condition6 = config.HasDryRun(LessonEquationCommandType.Update);
        Assert.False(condition6);
        bool condition7 = config.HasAnyDryRun(LessonEquationCommandTypes.All);
        Assert.True(condition7);
        bool condition8 = config.HasAnyDryRun(LessonEquationCommandTypes.Update | LessonEquationCommandTypes.Create);
        Assert.False(condition8);
    }

    [Fact]
    public void DryRunRemovesProcessAfterNormalize()
    {
        var config = CommandProcessingConfig.None
            .WithProcess(LessonEquationCommandTypes.All)
            .WithDryRun(LessonEquationCommandTypes.All)
            .Normalized;

        bool condition = config.HasAnyProcess(LessonEquationCommandTypes.All);
        Assert.False(condition);
        bool condition2 = config.HasAnyDryRun(LessonEquationCommandTypes.All);
        Assert.True(condition2);
    }

    [Fact]
    public void NoProcessGainedAfterNormalize()
    {
        var config = CommandProcessingConfig.None
            .WithDryRun(LessonEquationCommandTypes.All)
            .Normalized;

        bool condition = config.HasAnyProcess(LessonEquationCommandTypes.All);
        Assert.False(condition);
        bool condition2 = config.HasAnyDryRun(LessonEquationCommandTypes.All);
        Assert.True(condition2);
    }
}
