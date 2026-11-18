using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;

public sealed class ScheduleLoaderSerializationTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("empty-hash")]
    [InlineData("corrupt-json")]
    [InlineData("teacher-without-contacts")]
    [InlineData("teacher-with-null-contacts")]
    public async Task CacheHitsSkipImportAndChangedIdentityRebuilds(string change)
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var component = new Component();
        var loader = new ScheduleLoader
        {
            CachedPath = CalculateSchedulePath(temp),
            ConfigurationIdentity = "configuration v1",
            Components = [component],
        };
        await loader.Load(CreateContext(), CancellationToken.None);
        Assert.Equal(expected: 1, actual: component.ApplyCount);
        var first = await File.ReadAllBytesAsync(CalculateSchedulePath(temp));
        using (var input = new MemoryStream(first))
        {
            Assert.False(
                string.IsNullOrEmpty((await ScheduleSerializer.Deserialize(input, CancellationToken.None)).Hash));
        }
        var cached = await loader.Load(CreateContext(), CancellationToken.None);
        Assert.Equal(expected: 1, actual: component.ApplyCount);
        Assert.Equal(expected: "Imported course", actual: Assert.Single(cached.Courses).FullName);
        Assert.Equal(expected: first, actual: await File.ReadAllBytesAsync(CalculateSchedulePath(temp)));

        switch (change)
        {
            case "source":
                component.Source = "source v2";
                break;
            case "configuration":
                loader.ConfigurationIdentity = "configuration v2";
                break;
            case "empty-hash":
                await ScheduleSerializer.Serialize()
                    .Input(Schedule.Empty)
                    .Output(CalculateSchedulePath(temp))
                    .ExecuteAsync();
                break;
            case "corrupt-json":
                await File.WriteAllTextAsync(path: CalculateSchedulePath(temp), contents: "{corrupt");
                break;
            case "teacher-without-contacts":
                await AddCachedTeacher(CalculateSchedulePath(temp), writeNullContacts: false);
                break;
            case "teacher-with-null-contacts":
                await AddCachedTeacher(CalculateSchedulePath(temp), writeNullContacts: true);
                break;
        }
        var rebuilt = await loader.Load(CreateContext(), CancellationToken.None);
        Assert.Equal(expected: 2, actual: component.ApplyCount);
        Assert.Equal(expected: "Imported course", actual: Assert.Single(rebuilt.Courses).FullName);
        await loader.Load(CreateContext(), CancellationToken.None);
        Assert.Equal(expected: 2, actual: component.ApplyCount);

        Assert.Empty(Directory.EnumerateFiles(path: temp.Path, searchPattern: "*.tmp"));
    }

    [Fact]
    public async Task CacheHitRestoresTheScheduleTheColdLoadBuilt()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var component = new PopulatedComponent();
        var loader = new ScheduleLoader
        {
            CachedPath = CalculateSchedulePath(temp),
            Components = [component],
        };

        var cold = await loader.Load(CreateRemappingContext(), CancellationToken.None);
        var warm = await loader.Load(CreateRemappingContext(), CancellationToken.None);

        Assert.Equal(expected: 1, actual: component.ApplyCount);
        var group = Assert.Single(cold.Groups);
        // The import remaps IA2401 once; a cache hit must not remap the result again.
        Assert.Equal(expected: "IA2402", actual: group.Name);
        Assert.Equal(expected: Language.Ru, actual: group.Language);
        Assert.Equal(expected: new Specialty("Synthetic specialty"), actual: group.Specialty);
        var teacher = Assert.Single(cold.Teachers);
        Assert.Equal(expected: "teacher@example.com", actual: teacher.Contacts.CorporateEmail);
        var lesson = Assert.Single(cold.WeeklyLessons);
        Assert.Equal(expected: new Alternative("Psihologie"), actual: lesson.Base.Data.Alternative);

        AssertSameSchedule(expected: cold, actual: warm);
    }

    private static void AssertSameSchedule(Schedule expected, Schedule actual)
    {
        Assert.Equal(expected: expected.Groups.Length, actual: actual.Groups.Length);
        for (int i = 0; i < expected.Groups.Length; i++)
        {
            var e = expected.Groups[i];
            var a = actual.Groups[i];
            Assert.Equal(expected: e.Name, actual: a.Name);
            Assert.Equal(expected: e.Grade, actual: a.Grade);
            Assert.Equal(expected: e.GroupNumber, actual: a.GroupNumber);
            Assert.Equal(expected: e.QualificationType, actual: a.QualificationType);
            Assert.Equal(expected: e.Faculty, actual: a.Faculty);
            Assert.Equal(expected: e.AttendanceMode, actual: a.AttendanceMode);
            Assert.Equal(expected: e.Language, actual: a.Language);
            Assert.Equal(expected: e.Specialty, actual: a.Specialty);
        }

        Assert.Equal(expected: expected.Teachers.Length, actual: actual.Teachers.Length);
        for (int i = 0; i < expected.Teachers.Length; i++)
        {
            var e = expected.Teachers[i];
            var a = actual.Teachers[i];
            Assert.Equal(expected: e.PersonName.FirstName, actual: a.PersonName.FirstName);
            Assert.Equal(expected: e.PersonName.LastName, actual: a.PersonName.LastName);
            Assert.Equal(expected: e.Contacts.PersonalEmail, actual: a.Contacts.PersonalEmail);
            Assert.Equal(expected: e.Contacts.CorporateEmail, actual: a.Contacts.CorporateEmail);
            Assert.Equal(expected: e.Contacts.PhoneNumber, actual: a.Contacts.PhoneNumber);
        }

        Assert.Equal(
            expected: expected.Courses.Select(x => x.Names.ToArray()),
            actual: actual.Courses.Select(x => x.Names.ToArray()));
        // ImmutableArray compares by reference, so compare the elements.
        Assert.Equal(expected: expected.WeeklyLessons.ToArray(), actual: actual.WeeklyLessons.ToArray());
        Assert.Equal(expected: expected.OneTimeLessons.ToArray(), actual: actual.OneTimeLessons.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrFailedRebuildKeepsCompleteCache(bool cancel)
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var component = new Component();
        var loader = new ScheduleLoader
        {
            CachedPath = CalculateSchedulePath(temp),
            Components = [component],
        };
        await loader.Load(CreateContext(), CancellationToken.None);
        var previous = await File.ReadAllBytesAsync(CalculateSchedulePath(temp));
        using var cancellation = new CancellationTokenSource();
        component.OnApply = () =>
        {
            if (cancel)
            {
                cancellation.Cancel();
                return;
            }
            throw new IOException("source read failed");
        };
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                loader.Load(
                        CreateContext(),
                        cancellation.Token,
                        bypassCache: true)
                    .AsTask());
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => loader.Load(
                    CreateContext(),
                    cancellation.Token,
                    bypassCache: true)
                .AsTask());
        }
        Assert.Equal(expected: previous, actual: await File.ReadAllBytesAsync(CalculateSchedulePath(temp)));
        using var input = new MemoryStream(previous);
        Assert.NotNull(await ScheduleSerializer.Deserialize(input, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(path: temp.Path, searchPattern: "*.tmp"));
        // Cancellation/failure releases the loader's cache lock as well.
        component.OnApply = null;
        await loader.Load(
            CreateContext(),
            CancellationToken.None,
            bypassCache: true);
    }

    /// <summary>
    /// Adds a malformed teacher to the cache while keeping its hash, so only schema validation
    /// can reject it: its required contacts are either omitted or null.
    /// </summary>
    private static async Task AddCachedTeacher(string path, bool writeNullContacts)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var teacher = new JsonObject
        {
            ["FirstName"] = new JsonArray(),
            ["LastName"] = new JsonArray("Popescu"),
        };
        if (writeNullContacts)
        {
            teacher["Contacts"] = null;
        }
        root["Teachers"] = new JsonArray(teacher);
        await File.WriteAllTextAsync(path: path, contents: root.ToJsonString());
    }

    private static ScheduleImportContext CreateContext()
    {
        return ScheduleImportContext.Create(new()
        {
            DayNameProvider = new(),
            CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new()),
        });
    }

    private static ScheduleImportContext CreateRemappingContext()
    {
        var context = CreateContext();
        var remappings = new Remappings();
        remappings.GroupNameRemappings.Add(key: "IA2401", value: "IA2402");
        remappings.GroupNameRemappings.Add(key: "IA2402", value: "IA2403");
        context.Schedule.UseRemappings(remappings);
        context.Schedule.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2025),
        });
        return context;
    }

    /// <summary>Imports one lesson of a remapped group, a teacher with contacts and an elective.</summary>
    private sealed class PopulatedComponent : IScheduleLoaderComponent
    {
        public int ApplyCount { get; private set; }

        public ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
        {
            byte[] data = Encoding.UTF8.GetBytes("populated v1");
            hasher.AppendData(data);
            return ValueTask.CompletedTask;
        }

        public ValueTask Apply(ScheduleImportContext context, CancellationToken cancellationToken)
        {
            ApplyCount++;
            var s = context.Schedule;
            var group = s.Group("IA2401");
            group.Model.Language = Language.Ru;
            group.Model.Specialty = new("Synthetic specialty");
            var teacher = s.Teacher("Ion Popescu");
            teacher.Contacts(new()
            {
                CorporateEmail = "teacher@example.com",
                PhoneNumber = "+37300000000",
            });

            var lesson = s.RegularLesson();
            var course = s.Course("Elective course");
            lesson.Course(course);
            lesson.Group(group.Id);
            lesson.Teacher(teacher.Id);
            lesson.TimeSlot(new(0));
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.Alternative(new("Psihologie"));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Component : IScheduleLoaderComponent
    {
        public string Source { get; set; } = "source v1";

        public int ApplyCount { get; private set; }

        public Action? OnApply { get; set; }

        public ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var incrementalHash = hasher;
            byte[] data = Encoding.UTF8.GetBytes(Source);
            incrementalHash.AppendData(data);
            return ValueTask.CompletedTask;
        }

        public ValueTask Apply(ScheduleImportContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCount++;
            OnApply?.Invoke();
            context.Schedule.Course("Imported course");
            return ValueTask.CompletedTask;
        }
    }

    private static string CalculateSchedulePath(TestTempDirectory temp) =>
        Path.Combine(temp.Path, "schedule.json");
}
