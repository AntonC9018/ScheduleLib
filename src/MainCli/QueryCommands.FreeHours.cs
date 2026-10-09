using System.Text.Json;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed class FreeHoursArguments : IArgumentModel
{
    [Option("group", Description = "Group name from the schedule, for example IA2301. Repeat it for several groups. Unknown or ambiguous names fail before execution.")]
    public string[] Groups { get; set; } = [];
}

public sealed record FreeHoursIntervalResult(string Start, string End);
public sealed record FreeHoursDayResult(string Day, FreeHoursIntervalResult[] Intervals);
public sealed record FreeHoursSectionResult(string Group, string Parity, string Mode, FreeHoursDayResult[] Days);
public sealed record FreeHoursResult(string[] Groups, FreeHoursSectionResult[] Sections);

public partial class QueryCommands
{
    [Command("free-hours", Description = "Free time slots of the supplied groups. Existing defaults are preserved and always apply: both parities (even and odd weeks), both occupancy modes, and every weekly period rather than only the latest one. The two modes are 'EveryLessonOccupies' - every lesson the group attends occupies its slot, whichever subgroup, specialization or alternative it addresses - and 'WholeGroupAndUnspecializedOptionalLessonsOccupy' - whole-group lessons and lessons with the legacy opțional subgroup marker and no specialization occupy their slot, regardless of alternative; other partitioned lessons leave their slot free. Days are the Monday-Friday slots of the configured lesson times, and consecutive free slots are merged into one interval. No teacher profile is required.")]
    public async Task<int> FreeHours(FreeHoursArguments groups, SourceArguments source, ResultArguments output,
        CancellationToken cancellationToken = default, SettingsArguments? settings = null)
    {
        var runId = Guid.NewGuid().ToString("N");
        var warnings = new List<string>();
        FreeHoursResult? data = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var requested = groups.Groups;
            if (requested.Length == 0) return Finish(2, ["At least one --group is required."]);
            if (requested.Any(string.IsNullOrWhiteSpace)) return Finish(2, ["Group names must not be empty."]);
            var names = requested.Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length != requested.Length) warnings.Add("Repeated group names were reported once.");
            using var resolvedSettings = await CliSettings.Load(settings ?? new(), cancellationToken: cancellation.Token);
            var services = CliRuntime.CreateServices(source, ConfigureServices, resolvedSettings.ProjectDirectory);
            resolvedSettings.ConfigureServices(services);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            var known = schedule.EnumerateGroups().Select(x => x.Item.Name).ToArray();
            var unknown = names.Where(name => !known.Contains(name, StringComparer.Ordinal)).ToArray();
            if (unknown.Length > 0) return Finish(2, [UnknownGroups(unknown, known)]);
            var ambiguous = names.Where(name => known.Count(x => x == name) > 1).ToArray();
            if (ambiguous.Length > 0) return Finish(2, [$"Group name(s) match more than one group: {string.Join(", ", ambiguous)}."]);
            warnings.Add("Free hours inspect every weekly period, not only the latest one.");
            var handler = scope.ServiceProvider.GetRequiredService<PrintFreeHoursOfGroupTaskHandler>();
            var sections = handler.Sections(names, cancellation.Token);
            var time = scope.ServiceProvider.GetRequiredService<LessonTimeConfig>();
            var result = new FreeHoursResult(names, [.. sections
                .Select(x => new FreeHoursSectionResult(x.Group, x.Parity.ToString(), x.Mode.ToString(),
                    [.. x.Days.Select(day => new FreeHoursDayResult(day.Day.ToString(),
                        [.. day.Intervals.Select(interval => new FreeHoursIntervalResult(
                            time.GetTimeSlotInterval(interval.Start).Start.ToString("HH:mm"),
                            time.GetTimeSlotInterval(interval.EndInclusive).End.ToString("HH:mm")))]))]))]);
            cancellation.Token.ThrowIfCancellationRequested();
            data = result;
            return Finish(0, []);
        }
        catch (OperationCanceledException) { return Finish(130, ["Cancelled."]); }
        catch (ArgumentException e) { return Finish(2, [e.Message]); }
        catch (PlatformNotSupportedException e) { return Finish(8, [e.Message]); }
        catch (InvalidScheduleSourceException e) { return Finish(3, [e.Message]); }
        catch (JsonException e) { return Finish(3, [e.Message]); }
        catch (ScheduleBuildException e) { return Finish(3, [e.Message]); }
        catch (FileNotFoundException e) { return Finish(3, [e.Message]); }
        catch (DirectoryNotFoundException e) { return Finish(3, [e.Message]); }
        catch (LocalOperationBusyException e) { return Finish(7, [e.Message]); }
        catch (UnauthorizedAccessException e) { return Finish(5, [e.Message]); }
        catch (IOException e) { return Finish(5, [e.Message]); }
        catch (Exception e) { return Finish(1, [e.Message]); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, string[] errors)
        {
            foreach (var error in errors) Console.Error.WriteLine(error);
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            if (output.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<FreeHoursResult?>(1, "query free-hours", runId,
                    exit == 0 ? "succeeded" : "failed", exit, [], [], warnings.ToArray(), errors, data),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (data is not null)
                foreach (var section in data.Sections)
                {
                    Console.WriteLine($"{section.Group}, {section.Parity} week, {Describe(section.Mode)}");
                    foreach (var day in section.Days)
                        Console.WriteLine($"  {day.Day}: {string.Join(", ", day.Intervals.Select(x => $"{x.Start}-{x.End}"))}");
                }
            return exit;
        }

        static string Describe(string mode) => mode == nameof(FreeHoursOccupancyMode.WholeGroupAndUnspecializedOptionalLessonsOccupy)
            ? "whole-group lessons and unspecialized optional lessons occupy their slot"
            : "every lesson occupies its slot";

        static string UnknownGroups(string[] unknown, string[] known) =>
            string.Join(" ", unknown.Select(name =>
            {
                var suggestion = known.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                return $"Unknown group '{name}'." + (suggestion is null ? "" : $" Did you mean '{suggestion}'?");
            }));
    }
}
