using System.Text.Json;
using Anton.LayeredData.Retrieval;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public DriveCommands Drive { get; set; } = new();
}

[Command("drive", Description = "Generate and preview or apply the existing schedule bundle using provisioned Google authorization.")]
public class DriveCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) => AppConfiguration.ConfigureServices(services);
    protected virtual Task<IDriveSyncProvider> Connect(IServiceProvider services, BuiltGoogleDriveConfig config, CancellationToken token)
        => GoogleDriveSyncProvider.Connect(services, config, token);

    [Command("publish", Description = "Generate all-teacher XLSX, free-room XLSX, group/partition/teacher PDF and ICS files. Workbook/PDF/ICS use the latest period; free rooms use all weekly periods. Preview actual Drive account/folder and all creates/updates/deletes by default. --apply recomputes under a local account/folder lock. Matching names are always updated; unmatched remote files are deleted. Requires a teacher --profile and provisioned auth login google; no consent is opened.")]
    public async Task<int> Publish(SourceArguments source, SettingsArguments settings, OutputArguments destination,
        ResultArguments result, ApplyArguments apply, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        RunOutput? output = null;
        RunOutput? outputLease = null;
        DriveSyncResult? data = null;
        var warnings = new List<string>();
        var bundleGenerated = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            string? outputDirectory;
            try
            {
                outputDirectory = destination.Directory is { } path ? CliRuntime.ResolvePath(path) : null;
                if (source.DataDirectory is { } sourcePath) CliRuntime.ResolvePath(sourcePath);
                if (source.CacheDirectory is { } cachePath) CliRuntime.ResolvePath(cachePath);
            }
            catch (ArgumentException) { return await Finish(2, ["Invalid directory argument. Supply a nonempty valid path."]); }
            catch (NotSupportedException) { return await Finish(2, ["Invalid directory argument. Supply a supported path."]); }
            catch (PathTooLongException) { return await Finish(2, ["Directory argument is too long."]); }
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is null) return await Finish(3, ["A teacher --profile is required. Use config profiles to list identities."]);
            var configured = resolved.Get(GoogleDriveConfig.Key);
            if (configured?.Credentials is null || string.IsNullOrWhiteSpace(configured.DriveFolderName))
                return await Finish(3, ["Google Drive credentials and folder name are required for this profile."]);
            var services = CliRuntime.CreateServices(source, ConfigureServices, resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            await using var container = AppConfiguration.BuildServiceProvider(services);
            await container.InitializeSchedule(cancellation.Token);
            await using var scope = container.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleDriveConfig>>().Get();
            if (config is null) return await Finish(3, ["Google Drive configuration is missing for this profile."]);
            using var provider = await Connect(scope.ServiceProvider, config, cancellation.Token);
            outputLease = await RunOutput.Create(outputDirectory, "drive publish", runId, cancellation.Token, resolved.ProjectDirectory);
            output = outputLease;
            var artifacts = await DriveBundle.Generate(scope.ServiceProvider, output, warnings, cancellation.Token);
            await output.Complete("generated", cancellation.Token);
            bundleGenerated = true;
            data = await DriveSync.Run(provider, config.DriveFolderName, artifacts, apply.Apply, cancellation.Token);
            return await Finish(data.ExitCode, data.ExitCode == 4
                ? [new AuthenticationRequiredException("google", resolved.Profile).Message] : data.Errors);
        }
        catch (AuthenticationRequiredException e) { return await Finish(4, [e.Message]); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return await Finish(130, ["Cancelled."]); }
        catch (OperationCanceledException) { return await Finish(5, ["Drive request timed out or was interrupted before remote application."]); }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException) { return await Finish(4, [new AuthenticationRequiredException("google", settings.Profile ?? "").Message]); }
        catch (HttpRequestException) { return await Finish(5, ["Drive remote state request failed. Check the connection and retry."]); }
        catch (LocalOperationBusyException) { return await Finish(7, ["Another operation owns this output or actual Drive account/folder."]); }
        catch (JsonException e) { return await Finish(3, [e.Message]); }
        catch (ArgumentException e) { return await Finish(3, [e.Message]); }
        catch (DirectoryNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (FileNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (InvalidScheduleSourceException e) { return await Finish(3, [e.Message]); }
        catch (ScheduleBuildException e) { return await Finish(3, [e.Message]); }
        catch (PlatformNotSupportedException e) { return await Finish(8, [e.Message]); }
        catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode == 401) { return await Finish(4, [new AuthenticationRequiredException("google", settings.Profile ?? "").Message]); }
        catch (Google.GoogleApiException) { return await Finish(5, ["Drive rejected the remote state request."]); }
        catch (UnauthorizedAccessException) { return await Finish(!bundleGenerated && output?.PublishedPaths.Count > 0 ? 6 : 5, ["Local Drive bundle or lock state is inaccessible."]); }
        catch (IOException e) { return await Finish(!bundleGenerated && output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (Exception) { return await Finish(!bundleGenerated && output?.PublishedPaths.Count > 0 ? 6 : 1, ["Drive publication failed before remote application."]); }
        finally
        {
            Console.CancelKeyPress -= cancel;
            outputLease?.Dispose();
        }

        async Task<int> Finish(int exit, string[] errors)
        {
            if (output is not null && output.PublishedPaths.Count > 0 && !output.PublishedPaths.Contains(output.ManifestPath))
            {
                try { await output.Complete("partial", CancellationToken.None); }
                catch (Exception) { warnings.Add("Could not publish partial bundle manifest."); }
            }
            var allWarnings = warnings.Concat(data?.Warnings ?? []).ToArray();
            foreach (var error in errors) Console.Error.WriteLine(error);
            foreach (var warning in allWarnings) Console.Error.WriteLine(warning);
            var actions = data?.Actions.Select(x => $"{x.Action} {x.Name}; id {x.FileId}").ToArray() ?? [];
            var status = exit == 130 ? "cancelled" : exit == 0 ? apply.Apply ? "applied" : "preview" : exit == 6 ? "partial" : "failed";
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<DriveSyncResult?>(1, "drive publish", runId, status, exit,
                    output?.PublishedPaths.ToArray() ?? [], actions, allWarnings, errors, data), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
            {
                if (data is not null) Console.WriteLine($"{status}: account {data.AccountEmail} ({data.Account}); folder {data.Folder.Name} ({data.Folder.Id})");
                foreach (var action in actions) Console.WriteLine(action);
                foreach (var outcome in data?.Outcomes ?? []) Console.WriteLine($"{outcome.Action.Action} {outcome.Action.Name}: {outcome.State}; id {outcome.ResultFileId}");
                foreach (var path in output?.PublishedPaths ?? []) Console.WriteLine(path);
            }
            return exit;
        }
    }
}
