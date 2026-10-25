using Anton.LayeredData.Retrieval;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public DriveCommands Drive { get; set; } = new();
}

[Command(
    "drive",
    Description = "Generate and preview or apply the existing schedule bundle using provisioned Google authorization.")]
public class DriveCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) =>
        AppConfiguration.ConfigureServices(services);

    protected virtual Task<IDriveSyncProvider> Connect(
        IServiceProvider services,
        BuiltGoogleDriveConfig config,
        CancellationToken token)
    {
        return GoogleDriveSyncProvider.Connect(
                services,
                config,
                token);
    }

    [Command(
        "publish",
        Description =
            "Generate all-teacher XLSX, free-room XLSX, group/partition/teacher PDF and ICS files. Workbook/PDF/ICS use the latest period; free rooms use all weekly periods. Preview actual Drive account/folder and all creates/updates/deletes by default. --apply recomputes under a local account/folder lock. Matching names are always updated; unmatched remote files are deleted. Requires a teacher --profile and provisioned auth login google; no consent is opened.")]
    public async Task<int> Publish(
        SourceArguments source,
        SettingsArguments settings,
        OutputArguments destination,
        ResultArguments resultArgs,
        ApplyArguments apply,
        CancellationToken cancellationToken = default)
    {
        using var context = new CliCommandContext(
            "drive publish",
            resultArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cli = context.Output;
        RunOutput? output = null;
        RunOutput? outputLease = null;
        DriveSyncResult? data = null;
        var warnings = context.Warnings;
        var bundleGenerated = false;
        var streamedArtifacts = new HashSet<string>(StringComparer.Ordinal);
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            string? outputDirectory;
            try
            {
                outputDirectory = destination.Directory is { } path ? CliRuntime.ResolvePath(path) : null;
                if (source.DataDirectory is { } sourcePath)
                {
                    CliRuntime.ResolvePath(sourcePath);
                }

                if (source.CacheDirectory is { } cachePath)
                {
                    CliRuntime.ResolvePath(cachePath);
                }
            }
            catch (ArgumentException)
            {
                return await Finish(
                    CliExit.InvalidArguments,
                    ["Invalid directory argument. Supply a nonempty valid path."]);
            }
            catch (NotSupportedException)
            {
                return await Finish(CliExit.InvalidArguments, ["Invalid directory argument. Supply a supported path."]);
            }
            catch (PathTooLongException)
            {
                return await Finish(CliExit.InvalidArguments, ["Directory argument is too long."]);
            }
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is null)
            {
                return await Finish(
                    CliExit.InvalidConfiguration,
                    ["A teacher --profile is required. Use config profiles to list identities."]);
            }

            var configured = resolved.Get(GoogleDriveConfig.Key);
            if (configured?.Credentials is null || string.IsNullOrWhiteSpace(configured.DriveFolderName))
            {
                return await Finish(
                    CliExit.InvalidConfiguration,
                    ["Google Drive credentials and folder name are required for this profile."]);
            }

            var services = context.CreateServices(
                source,
                resolved,
                ConfigureServices);
            await using var container = AppConfiguration.BuildServiceProvider(services);
            await container.InitializeSchedule(cancellation.Token);
            await using var scope = container.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleDriveConfig>>()
                .Get();
            if (config is null)
            {
                return await Finish(
                    CliExit.InvalidConfiguration,
                    ["Google Drive configuration is missing for this profile."]);
            }

            using var provider = await Connect(
                scope.ServiceProvider,
                config,
                cancellation.Token);
            outputLease = await RunOutput.Create(
                output: outputDirectory,
                command: "drive publish",
                runId: context.RunId,
                cancellation.Token,
                projectDirectory: resolved.ProjectDirectory);
            output = outputLease;
            var artifacts = await DriveBundle.Generate(
                scope.ServiceProvider,
                output,
                warnings,
                cancellation.Token);
            StreamNewArtifacts();
            await output.Complete("generated", cancellation.Token);
            StreamNewArtifacts();
            bundleGenerated = true;
            data = await DriveSync.Run(
                provider,
                config.DriveFolderName,
                artifacts,
                apply.Apply,
                cancellation.Token,
                onOutcome: context.StreamUpdate);
            // Outcomes already streamed via callback; ensure any trailing not-attempted are visible.
            return await Finish(
                data.ExitCode,
                data.ExitCode == CliExit.AuthenticationRequired
                    ? [new AuthenticationRequiredException(provider: "google", teacher: resolved.Profile).Message]
                    : data.Errors);
        }
        catch (DriveStreamingException ex)
        {
            data = ex.PartialResult;
            return await Finish(data.ExitCode, data.Errors);
        }
        catch (Exception error)
        {
            var messages = new GoogleFailureMessages
            {
                Timeout = "Drive request timed out or was interrupted before remote application.",
                Network = "Drive remote state request failed. Check the connection and retry.",
                Busy = "Another operation owns this output or actual Drive account/folder.",
                Access = "Local Drive bundle or lock state is inaccessible.",
                Rejected = "Drive rejected the remote state request.",
                Unexpected = "Drive publication failed before remote application.",
                Io = "",
                IncludeIoDetails = true,
            };
            var failure = CliRemoteFailure.ClassifyGoogle(
                error,
                context,
                settings.Profile,
                messages,
                partialBundle: !bundleGenerated && output?.PublishedPaths.Count > 0);
            return await Finish(failure.ExitCode, failure.Errors);
        }

        finally
        {
            outputLease?.Dispose();
        }

        void StreamNewArtifacts()
        {
            if (output is null)
            {
                return;
            }

            foreach (var path in output.PublishedPaths)
            {
                if (streamedArtifacts.Add(path))
                {
                    cli.WriteHuman(path);
                    global::ScheduleLib.Cli.CliOutput cliOutput = cli;
                    string text = $"artifact:{path}";
                    string id2 = (string) text;
                    string status2 = "completed";
                    object item = new { path };
                    cliOutput.EmitItem(
                        id: id2, status: status2, item);
                }
            }
        }

        async Task<int> Finish(int exit, string[] errors)
        {
            StreamNewArtifacts();
            if (output is not null)
            {
                if (output.PublishedPaths.Count > 0)
                {
                    if (!output.PublishedPaths.Contains(output.ManifestPath))
                    {
                        try
                        {
                            await output.Complete("partial", CancellationToken.None);
                        }
                        catch (Exception)
                        {
                            warnings.Add("Could not publish partial bundle manifest.");
                        }
                        StreamNewArtifacts();
                    }
                }
            }
            var allWarnings = warnings.Concat(data?.Warnings ?? [])
                .ToArray();
            var actions = data?.Actions.Select(x => $"{x.Action} {x.Name}; id {x.FileId}")
                .ToArray() ?? [];
            var success = "preview";
            if (apply.Apply)
            {
                success = "applied";
            }
            var status = CliCommandContext.Status(exit, success);
            object summary = new
            {
                outputDirectory = output?.DirectoryPath,
                artifactCount = output?.PublishedPaths.Count ?? 0,
            };
            if (data is not null)
            {
                summary = new
                {
                    outputDirectory = output?.DirectoryPath,
                    artifactCount = output?.PublishedPaths.Count ?? 0,
                    data.Account,
                    data.AccountEmail,
                    folder = new
                    {
                        data.Folder.Id,
                        data.Folder.Name,
                    },
                    actionCount = data.Actions.Count,
                    outcomeCount = data.Outcomes.Count,
                };
            }
            var paths = output?.PublishedPaths.ToArray() ?? [];
            return context.Finish(new CliCompletion<DriveSyncResult?>(
                exit,
                errors,
                data)
            {
                Summary = summary,
                Paths = paths,
                Actions = actions,
                Warnings = allWarnings,
                Status = status,
                RenderHuman = status =>
                {
                    if (data is not null)
                    {
                        global::ScheduleLib.Cli.CliOutput cliOutput = cli;
                        string line = $"{status}: account {data.AccountEmail} ({data.Account}); folder {data.Folder.Name} ({data.Folder.Id})";
                        cliOutput.WriteHuman(
                            line);
                    }
                },
            });
        }
    }
}
