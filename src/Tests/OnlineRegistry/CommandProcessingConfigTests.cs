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

        Assert.True(config.HasProcess(LessonEquationCommandType.Update));
        Assert.False(config.HasProcess(LessonEquationCommandType.Delete));
        Assert.True(config.HasAnyProcess(LessonEquationCommandTypes.All));
        Assert.False(config.HasAnyProcess(LessonEquationCommandTypes.Delete | LessonEquationCommandTypes.Create));

        Assert.True(config.HasDryRun(LessonEquationCommandType.Delete));
        Assert.False(config.HasDryRun(LessonEquationCommandType.Update));
        Assert.True(config.HasAnyDryRun(LessonEquationCommandTypes.All));
        Assert.False(config.HasAnyDryRun(LessonEquationCommandTypes.Update | LessonEquationCommandTypes.Create));
    }

    [Fact]
    public void DryRunRemovesProcessAfterNormalize()
    {
        var config = CommandProcessingConfig.None
            .WithProcess(LessonEquationCommandTypes.All)
            .WithDryRun(LessonEquationCommandTypes.All)
            .Normalized;

        Assert.False(config.HasAnyProcess(LessonEquationCommandTypes.All));
        Assert.True(config.HasAnyDryRun(LessonEquationCommandTypes.All));
    }

    [Fact]
    public void NoProcessGainedAfterNormalize()
    {
        var config = CommandProcessingConfig.None
            .WithDryRun(LessonEquationCommandTypes.All)
            .Normalized;

        Assert.False(config.HasAnyProcess(LessonEquationCommandTypes.All));
        Assert.True(config.HasAnyDryRun(LessonEquationCommandTypes.All));
    }
}
