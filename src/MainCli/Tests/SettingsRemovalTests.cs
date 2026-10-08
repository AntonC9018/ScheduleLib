using System.Text.Json;
using ScheduleLib.Cli;
using Xunit;

public sealed class SettingsRemovalTests
{
    [Fact]
    public async Task BlockSuppressionReloadsAndUnsetRestoresInheritance()
    {
        using var files = new SettingsWriteTests.Fixture();
        await files.User("""{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"inherited"}}}""");
        await files.Success("remove", "GoogleCalendarConfig", "--scope", "project");
        Assert.Equal("", await files.Get("GoogleCalendarConfig"));
        Assert.Contains("operations", await File.ReadAllTextAsync(files.ProjectFile));
        await files.Success("unset", "GoogleCalendarConfig", "--scope", "project");
        Assert.Equal("inherited", await files.Get("GoogleCalendarConfig.calendarName"));
        await files.Success("remove", "GoogleCalendarConfig", "--scope", "project", "--profile", "Curmanschii Anton");
        Assert.Equal("", await files.Get("GoogleCalendarConfig", "--profile", "Curmanschii Anton"));
        Assert.Equal("inherited", await files.Get("GoogleCalendarConfig.calendarName"));
        await files.Success("unset", "GoogleCalendarConfig", "--scope", "project", "--profile", "Curmanschii Anton");
        Assert.NotEqual("", await files.Get("GoogleCalendarConfig", "--profile", "Curmanschii Anton"));
    }

    [Fact]
    public async Task RegisteredFileKeysRemoveAndClearAfterReloadWhileEmptyAndNullInherit()
    {
        using var files = new SettingsWriteTests.Fixture();
        // Absolute identities make the two defining directories irrelevant to this test.
        var first = Path.Combine(files.Root, "first.xlsx");
        var second = Path.Combine(files.Root, "second.xlsx");
        await files.User(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            defaults = new { LessonAttendanceConfig = new { sources = new[] { new { filePath = first }, new { filePath = second } } } },
        }));
        await files.Success("set", "LessonAttendanceConfig.sources", "[]", "--scope", "project");
        Assert.Contains(first, await files.Get("LessonAttendanceConfig.sources"));
        await files.Success("set", "LessonAttendanceConfig.sources", "null", "--scope", "project");
        Assert.Contains(second, await files.Get("LessonAttendanceConfig.sources"));
        await files.Success("remove", "LessonAttendanceConfig.sources", "--item", JsonSerializer.Serialize(new { filePath = first }), "--scope", "project");
        var remaining = await files.Get("LessonAttendanceConfig.sources");
        Assert.DoesNotContain(first, remaining);
        Assert.Contains(second, remaining);
        await files.Success("unset", "LessonAttendanceConfig.sources", "--scope", "project");
        Assert.Contains(first, await files.Get("LessonAttendanceConfig.sources"));
        await files.Success("clear", "LessonAttendanceConfig.sources", "--scope", "project");
        Assert.Equal("[]", (await files.Get("LessonAttendanceConfig.sources")).Replace(" ", "").Replace("\n", "").Replace("\r", ""));
        await files.Success("unset", "LessonAttendanceConfig.sources", "--scope", "project");
        Assert.Contains(second, await files.Get("LessonAttendanceConfig.sources"));
    }


    [Fact]
    public async Task PolymorphicRegisteredKeyRemovalAndExplicitBlockResetReload()
    {
        using var files = new SettingsWriteTests.Fixture();
        var manifest = Path.Combine(files.Root, "topics.json");
        await files.User("{\"schemaVersion\":1,\"defaults\":{\"LessonTopicsConfig\":{\"sources\":[{\"$type\":\"ManifestLessonTopicSourceDefinition\",\"path\":"
            + JsonSerializer.Serialize(manifest) + "}]},\"GoogleCalendarConfig\":{\"calendarName\":\"inherited\"}}}");
        await files.Success("remove", "LessonTopicsConfig.sources", "--item",
            "{\"$type\":\"ManifestLessonTopicSourceDefinition\",\"path\":" + JsonSerializer.Serialize(manifest) + "}", "--scope", "project");
        Assert.DoesNotContain(manifest, await files.Get("LessonTopicsConfig.sources"));
        await files.Success("unset", "LessonTopicsConfig.sources", "--scope", "project");
        Assert.Contains(manifest, await files.Get("LessonTopicsConfig.sources"));
        await File.WriteAllTextAsync(files.ProjectFile, """{"schemaVersion":1,"operations":{"defaults":[{"operation":"reset","key":"GoogleCalendarConfig"}]}}""");
        Assert.NotEqual("inherited", await files.Get("GoogleCalendarConfig.calendarName"));
        await files.Success("unset", "GoogleCalendarConfig", "--scope", "project");
        Assert.Equal("inherited", await files.Get("GoogleCalendarConfig.calendarName"));
    }

    [Theory]
    [InlineData("clear", "GoogleCalendarConfig", null)]
    [InlineData("remove", "GoogleCalendarConfig.calendarName", null)]
    [InlineData("remove", "LessonAttendanceConfig.sources", "{\"filePath\":\"missing.xlsx\"}")]
    [InlineData("remove", "LessonAttendanceConfig.sources", "{\"unknown\":1}")]
    [InlineData("remove", "UnknownBlock", null)]
    public async Task InvalidOperationsPreserveBytesWithoutCreatingLock(string verb, string key, string? item)
    {
        using var files = new SettingsWriteTests.Fixture();
        const string original = """{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"valid"}}}""";
        await File.WriteAllTextAsync(files.ProjectFile, original);
        var args = new List<string> { verb, key, "--scope", "project" };
        if (item is not null) args.AddRange(["--item", item]);
        var result = await files.Run(args.ToArray());
        Assert.Equal(3, result.Exit);
        Assert.NotEmpty(result.Error);
        Assert.Equal(original, await File.ReadAllTextAsync(files.ProjectFile));
        Assert.False(File.Exists(files.ProjectFile + ".lock"));
    }

    [Theory]
    [InlineData("{\"defaults\":[{\"operation\":\"unknown\",\"key\":\"GoogleCalendarConfig\"}]}")]
    [InlineData("{\"defaults\":[{\"operation\":\"remove\",\"key\":\"GoogleCalendarConfig\",\"extra\":true}]}")]
    [InlineData("{\"defaults\":null}")]
    [InlineData("{\"profiles\":{\"Unknown\":[]}}")]
    public async Task HandEditedInvalidOperationsFailValidation(string operations)
    {
        using var files = new SettingsWriteTests.Fixture();
        await File.WriteAllTextAsync(files.ProjectFile, "{\"schemaVersion\":1,\"operations\":" + operations + "}");
        Assert.Equal(3, (await files.Run("validate")).Exit);
    }
}
