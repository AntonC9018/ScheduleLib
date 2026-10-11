using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;
using Xunit;

public sealed class RegistryLabDestinationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task IntakeLabsWarnAndSkipBeforeReadingOrPlanningTheDestination(bool combined, bool ownLecture, bool explicitApply)
    {
        await using var fixture = new Fixture(ownLecture, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(navigator, diagnostics, explicitApply);

        Assert.Empty(plan);
        var warning = Assert.Single(diagnostics.Items.Where(item => item.Kind == "unsafe-registry-lab-destination"));
        Assert.Contains("explicit parent-group and partition", warning.Detail);
        Assert.Contains(navigator.Group.Uri.ToString(), warning.Detail);
        Assert.Contains("Synchronization continues", warning.Detail);
        Assert.Equal(0, navigator.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CliReportsAWarningAndNoActionsForUnsafeLabs(bool apply)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined: false);
        var diagnostics = new RegistrySyncDiagnostics();
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            await using var provider = new Provider(fixture, navigator, diagnostics);
            var command = new Commands(provider);
            Assert.Equal(0, await command.Sync(new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = apply }));
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(apply ? "applied" : "preview", result.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, result.RootElement.GetProperty("errors").GetArrayLength());
            Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(),
                warning => warning.GetString()!.Contains("laboratory sessions require explicit parent-group", StringComparison.Ordinal));
            Assert.Equal(0, result.RootElement.GetProperty("data").GetProperty("outcomes").GetArrayLength());
            Assert.Equal(0, navigator.Reads);
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsafeDestinationDoesNotStopPlanningTheNextExplicitDestination(bool explicitApply)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined: false, includeExplicitDestination: true);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(navigator, diagnostics, explicitApply);

        Assert.NotEmpty(plan);
        Assert.All(plan, action => Assert.Equal("IA2403", action.Groups));
        Assert.All(plan, action => Assert.Equal("lab", action.Topic));
        Assert.DoesNotContain(navigator.Group.Uri, navigator.ReadUris);
        Assert.Contains(new Uri("https://registry.test/explicit-group"), navigator.ReadUris);
        Assert.Single(diagnostics.Items.Where(item => item.Kind == "unsafe-registry-lab-destination"));
    }

    private sealed class Commands(Provider provider) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => provider;
        protected override IRegistryAccountLock CreateAccountLock() => new NoopLock();
    }

    private sealed class NoopLock : IRegistryAccountLock, IAsyncDisposable
    {
        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
            => Task.FromResult<IAsyncDisposable>(this);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Provider(Fixture fixture, Navigator navigator, RegistrySyncDiagnostics diagnostics) : IRegistryProvider, IRegistrySession
    {
        public RegistryDestination Target => new("synthetic-teacher", "https://registry.test/");
        public IReadOnlyList<RegistrySyncDiagnostic> Diagnostics => diagnostics.Items;
        public RegistrySyncDiagnostics LiveDiagnostics => diagnostics;
        public Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token)
            => Task.FromResult<IRegistrySession>(this);
        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token)
            => fixture.Plan(navigator, diagnostics, explicitApply: true);
        public Task PlanIntakeLabCleanup(Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned, CancellationToken token)
            => fixture.Cleanup(navigator, diagnostics, onGroupPlanned, token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CleanupSelectsOnlyUnsafeLabColumnsAndUsesTheCliApplyBoundary(bool apply, bool failDelete)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined: false, includeExplicitDestination: true);
        navigator.AddMixedLessons();
        navigator.AllowDeletes = true;
        navigator.FailDelete = failDelete;
        var diagnostics = new RegistrySyncDiagnostics();
        await using var provider = new Provider(fixture, navigator, diagnostics);
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await new Commands(provider).CleanupIntakeLabs(new(), new() { Profile = "teacher" },
                new() { Json = true }, new() { Apply = apply });
            Assert.Equal(failDelete ? 6 : 0, exit);
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            var outcomes = result.RootElement.GetProperty("data").GetProperty("outcomes").EnumerateArray().ToArray();
            Assert.Equal(2, outcomes.Length);
            Assert.All(outcomes, action => Assert.Equal("delete", action.GetProperty("kind").GetString()));
            Assert.All(outcomes, action => Assert.Equal(navigator.Group.Uri.ToString(), action.GetProperty("destination").GetString()));
            Assert.Equal(1, outcomes[0].GetProperty("attendanceCount").GetInt32());
            Assert.Equal(apply ? failDelete ? "uncertain" : "completed" : "planned", outcomes[0].GetProperty("status").GetString());
            Assert.Equal(failDelete ? "not-attempted" : apply ? "completed" : "planned", outcomes[1].GetProperty("status").GetString());
            Assert.Equal(apply ? failDelete ? 1 : 2 : 0, navigator.DeleteCalls);
            Assert.Equal(apply && !failDelete ? 2 : 4, navigator.Lessons.Count);
            Assert.DoesNotContain(new Uri("https://registry.test/explicit-group"), navigator.ReadUris);
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CleanupDoesNotReadExplicitDestinationsOrAnotherTeachersLabs(bool wildcard, bool ownLabs)
    {
        await using var fixture = new Fixture(ownLecture: true, ownLabs);
        var navigator = fixture.Navigator(wildcard, combined: false);
        var diagnostics = new RegistrySyncDiagnostics();
        await fixture.Cleanup(navigator, diagnostics, actions =>
        {
            Assert.Fail("An ineligible destination must not produce cleanup actions.");
            return Task.FromResult(true);
        }, default);
        Assert.Equal(0, navigator.Reads);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public async Task CleanupRefusesToDeleteAColumnChangedAfterPlanning()
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined: false);
        navigator.AddMixedLessons();
        navigator.AllowDeletes = true;
        await fixture.Cleanup(navigator, new(), async actions =>
        {
            navigator.Lessons[1] = navigator.Lessons[1] with { Mark = "am" };
            var error = await Assert.ThrowsAsync<RegistryActionExecutionException>(() => actions[0].Execute(default));
            Assert.False(error.SubmissionStarted);
            Assert.Equal(0, navigator.DeleteCalls);
            return true;
        }, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitParentDestinationStillPlansOnlyThatParentsLabs(bool combined)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: false, combined);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(navigator, diagnostics, explicitApply: true);

        Assert.NotEmpty(plan);
        Assert.All(plan, action => Assert.Equal("create", action.Kind));
        Assert.All(plan, action => Assert.Equal("lab", action.Topic));
        Assert.All(plan, action => Assert.Equal("IA2403", action.Groups));
        Assert.All(plan, action => Assert.Equal(DayOfWeek.Tuesday, action.Date.DayOfWeek));
        Assert.DoesNotContain(diagnostics.Items, item => item.Kind == "unsafe-registry-lab-destination");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedLecturesRemainUsableWhenSeparateLabsBelongToAnotherTeacher(bool ownLecture)
    {
        await using var fixture = new Fixture(ownLecture, ownLabs: false);
        var navigator = fixture.Navigator(wildcard: true, combined: false);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(navigator, diagnostics, explicitApply: true);

        Assert.DoesNotContain(diagnostics.Items, item => item.Kind == "unsafe-registry-lab-destination");
        if (ownLecture)
        {
            Assert.NotEmpty(plan);
            Assert.All(plan, action => Assert.Equal("lecture", action.Topic));
        }
        else
        {
            Assert.Empty(plan);
            Assert.Contains(diagnostics.Items, item => item.Kind == "registry-assignment-mismatch");
            Assert.Equal(0, navigator.Reads);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly CourseId _course;
        private readonly LessonGroups _parents;
        private readonly GroupPartitionKey _partition = new(SubGroup.All, Specialization.DezvoltareaAplicatiilor);

        public Fixture(bool ownLecture, bool ownLabs)
        {
            var builder = new ScheduleBuilder();
            builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
            builder.EnableLookupModule();
            _course = builder.Course("Synthetic elective");
            _parents = [builder.Group("IA2403(ru)"), builder.Group("IA2404(ru)")];
            var teacher = builder.Teacher("Anton Curmanschii");
            var other = builder.Teacher("Another Lecturer");
            builder.RegularLesson(lesson =>
            {
                lesson.Course(_course);
                foreach (var group in _parents) lesson.Group(group);
                lesson.Specialization(_partition.Specialization);
                lesson.Type(LessonType.Curs);
                lesson.DayOfWeek(DayOfWeek.Tuesday);
                lesson.TimeSlot(TimeSlot.First);
                lesson.Teacher(ownLecture ? teacher : other);
            });
            foreach (var group in _parents)
            {
                builder.RegularLesson(lesson =>
                {
                    lesson.Course(_course);
                    lesson.Group(group);
                    lesson.Specialization(_partition.Specialization);
                    lesson.Type(LessonType.Lab);
                    lesson.DayOfWeek(group == _parents[0] ? DayOfWeek.Tuesday : DayOfWeek.Friday);
                    lesson.TimeSlot(new(1));
                    lesson.Teacher(ownLabs ? teacher : other);
                });
            }
            var schedule = builder.Build();
            builder.RefreshLookup();
            var services = new ServiceCollection();
            AppConfiguration.ConfigureServices(services);
            services.AddSingleton(builder);
            services.AddSingleton(schedule);
            _provider = AppConfiguration.BuildServiceProvider(services);
            AppConfiguration.ConfigureLayeredConfig(_provider);
            _scope = _provider.CreateMarkerScope(marker => marker.TeacherName = NameHelper.Parse("Curmanschii Anton"));
        }

        public Navigator Navigator(bool wildcard, bool combined, bool includeExplicitDestination = false)
        {
            var group = new GroupLink(
                new FoundGroups { Value = wildcard ? _parents : [_parents[0]], IsWildcard = wildcard, IsCombined = combined },
                _partition, new("https://registry.test/group"), new("https://registry.test/evaluation"));
            GroupLink[] additional = includeExplicitDestination
                ? [new(new FoundGroups { Value = [_parents[0]], IsWildcard = false }, _partition,
                    new("https://registry.test/explicit-group"), new("https://registry.test/explicit-evaluation"))]
                : [];
            return new(_course, group, additional);
        }

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(Navigator navigator, RegistrySyncDiagnostics diagnostics, bool explicitApply)
            => _scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>().Plan(new()
            {
                Navigator = navigator, Attendance = new([]), HasAttendanceSources = false,
                LessonTopics = new Topics(), Semester = Semester.Sem1, Diagnostics = diagnostics,
                LessonFilter = _scope.ServiceProvider.GetRequiredService<CurrentTeacherLessonFilterProvider>().Get(),
            }, explicitApply: explicitApply);

        public Task Cleanup(Navigator navigator, RegistrySyncDiagnostics diagnostics,
            Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned, CancellationToken token)
            => _scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>().PlanIntakeLabCleanup(
                navigator, Semester.Sem1, _scope.ServiceProvider.GetRequiredService<CurrentTeacherLessonFilterProvider>().Get(),
                diagnostics, onGroupPlanned, token);

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class Topics : ILessonTopics
    {
        public string? Get(in AttendanceLookupKey key) => key.LessonType == LessonType.Lab ? "lab" : "lecture";
    }

    private sealed class Navigator(CourseId course, GroupLink group, params GroupLink[] additionalGroups) : IRegistrySyncNavigator
    {
        public sealed record Lesson(int Id, string Date, string ShortDate, string Mode, string Topic, string Mark);
        public List<Lesson> Lessons { get; } = [];
        public bool AllowDeletes { get; set; }
        public bool FailDelete { get; set; }
        public int DeleteCalls { get; private set; }
        public void AddMixedLessons() => Lessons.AddRange([
            new(1, "01.09.2026 15:00", "01.09.26 15:00", "curs", "lecture", ""),
            new(2, "04.09.2026 15:00", "04.09.26 15:00", "laborator", "lab one", "a"),
            new(3, "08.09.2026 15:00", "08.09.26 15:00", "seminar", "seminar", "am"),
            new(4, "11.09.2026 15:00", "11.09.26 15:00", "laborator", "lab two", ""),
        ]);
        public GroupLink Group => group;
        public int Reads { get; private set; }
        public List<Uri> ReadUris { get; } = [];
        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester)
            => Task.FromResult<IEnumerable<CourseLink>>([new(course, new("https://registry.test/course"))]);
        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link)
            => Task.FromResult<IEnumerable<GroupLink>>([group, .. additionalGroups]);
        public async Task<IDocument> GetHtml(Uri uri)
        {
            Reads++;
            ReadUris.Add(uri);
            if (Lessons.Count != 0 && uri == group.Uri)
            {
                var rows = string.Join("", Lessons.Select(lesson => $"<tr><td><a href='https://registry.test/details/{lesson.Id}'>{lesson.Date}</a> {lesson.Mode}</td><td>1 {lesson.Topic}</td><td><a href='https://registry.test/edit/{lesson.Id}'>edit</a></td></tr>"));
                var headers = string.Join("", Lessons.Select(lesson => $"<th>{lesson.ShortDate}, {lesson.Mode}</th>"));
                var marks = string.Join("", Lessons.Select(lesson => $"<td>{lesson.Mark}</td>"));
                return await new HtmlParser().ParseDocumentAsync($"<table><tr><th>lesson</th></tr>{rows}</table><table><tr><th>NN</th><th>Name</th><th>Spec</th>{headers}</tr><tr><td>1</td><td>Synthetic Student</td><td>spec</td>{marks}</tr></table>");
            }
            if (uri.AbsolutePath.StartsWith("/details/", StringComparison.Ordinal))
                return await new HtmlParser().ParseDocumentAsync($"<input name='id' value='{uri.Segments[^1]}'>");
            return await new HtmlParser().ParseDocumentAsync(uri == group.Uri || additionalGroups.Any(item => item.Uri == uri)
                ? "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>"
                : "<html></html>");
        }
        public Task SubmitLesson(IDocument document) => throw new InvalidOperationException("Unexpected submission.");
        public Task SubmitDelete(IDocument document)
        {
            if (!AllowDeletes) throw new InvalidOperationException("Unexpected deletion.");
            DeleteCalls++;
            if (FailDelete) throw new IOException("Synthetic uncertain deletion.");
            var id = int.Parse(document.QuerySelector("input")!.GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture);
            Lessons.RemoveAll(lesson => lesson.Id == id);
            return Task.CompletedTask;
        }
    }
}
