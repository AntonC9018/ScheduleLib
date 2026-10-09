using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CommandDotNet;
using ConvertDocToDocx;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Graph;
using File = System.IO.File;
using Directory = System.IO.Directory;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Curriculum.Download;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public CurriculaCommands Curricula { get; set; } = new();
}

public sealed record CurriculaDownloadResult(CurriculaSource Source, string? OutputDirectory, int DocumentCount);

[Command("curricula")]
public class CurriculaCommands
{
    protected virtual CurriculaSource Source => CurriculaDownloadTasks.ConfiguredSource;
    protected virtual MicrosoftAuthConfig LoadMicrosoftConfig() => MicrosoftCliConfiguration.Load();
    protected virtual MicrosoftAuthentication CreateAuthentication() => new();
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned Graph adapter owns the HttpProvider; the failure path disposes it.")]
    protected virtual async Task<ICurriculaProvider> CreateProvider(string profile, CancellationToken token)
    {
        var authentication = CreateAuthentication();
        var config = LoadMicrosoftConfig();
        // Fail before any Graph request; subsequent SDK requests may silently refresh under the same authorization contract.
        await authentication.Resolve(config, profile, token);
        var provider = new HttpProvider();
        try
        {
            var client = new GraphServiceClient(new DelegateAuthenticationProvider(async request =>
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await authentication.Resolve(config, profile, token));
            }), provider);
            return new GraphCurriculaProvider(client, provider);
        }
        catch { provider.Dispose(); throw; }
    }
    protected virtual void EnsureLegacyCapability()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Legacy .doc conversion requires Windows and Microsoft Word. Supply DOCX sources on Linux. No downloaded originals were modified.");
    }
    protected virtual Task<bool> ConvertLegacy(string input, string output, CancellationToken token) =>
        DocToDocxConversionHelper.TryConvertFile(input, output, token);

    [Command("download", Description = "Download curricula from the existing C#-configured owner/path (2024-2025). Requires provisioned Microsoft authorization; no browser consent. Legacy DOC conversion requires Windows and Word.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "RunOutput is retained for partial manifest reporting and unconditionally disposed in the nested finally block.")]
    public async Task<int> Download(SettingsArguments settings, OutputArguments destination, ResultArguments result, CancellationToken cancellationToken = default)
    {
        const string command = "curricula download";
        var runId = Guid.NewGuid().ToString("N");
        var source = Source;
        RunOutput? output = null;
        var warnings = new List<string> { "Curricula source owner/path remain code-defined for 2024-2025; no current-year source discovery is performed." };
        var count = 0;
        var exit = 1;
        string[] errors = [];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selected) CliRuntime.ResolvePath(selected);
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is not { } profile) throw new JsonException("Select a teacher with --profile. Use config profiles to list identities.");
            using var provider = await CreateProvider(profile, cancellation.Token);
            IReadOnlyList<CurriculaFile> files;
            try { files = await provider.List(source, cancellation.Token); }
            catch (ServiceException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CurriculaSourceUnavailableException("The coded curricula owner/path was not found. Update C# source configuration if the existing 2024-2025 source is stale.");
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                ValidateComponent(file.Group);
                ValidateComponent(file.Name);
                var extension = Path.GetExtension(file.Name);
                if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)) EnsureLegacyCapability();
                if (!extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Skipping unsupported curriculum document: {file.Group}/{file.Name}");
                    continue;
                }
                var name = ArtifactName(file);
                if (!names.Add(name)) throw new IOException("Curricula contain colliding output document names.");
            }
            output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token, resolved.ProjectDirectory);
            foreach (var file in files)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file.Name);
                if (!extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)) continue;
                if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                    await output.Publish(ArtifactName(file), async (stream, ct) =>
                    {
                        await using var remote = await provider.Download(file, ct);
                        await remote.CopyToAsync(stream, ct);
                        ValidateDocx(stream);
                    }, cancellation.Token);
                else
                {
                    // Publish the downloaded original first, then convert a separate staging copy.
                    var originalName = ArtifactName(file, ".doc");
                    await output.Publish(originalName, async (stream, ct) =>
                    {
                        await using var remote = await provider.Download(file, ct);
                        await remote.CopyToAsync(stream, ct);
                    }, cancellation.Token);
                    using var stagingOwner = OwnedConversionDirectory.Create(output.DirectoryPath);
                    var staging = stagingOwner.DirectoryPath;
                    {
                        var input = Path.Combine(staging, "source.doc");
                        var converted = Path.Combine(staging, "converted.docx");
                        File.Copy(Path.Combine(output.DirectoryPath, originalName), input);
                        if (!await ConvertLegacy(input, converted, cancellation.Token)) throw new IOException("Legacy Word conversion failed.");
                        await output.Publish(ArtifactName(file), async (stream, ct) =>
                        {
                            await using var read = File.OpenRead(converted);
                            await read.CopyToAsync(stream, ct);
                            ValidateDocx(stream);
                        }, cancellation.Token);
                    }
                }
                count++;
            }
            if (count == 0) warnings.Add("No supported curriculum documents were found at the configured source.");
            await output.Complete("succeeded", cancellation.Token);
            exit = 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { exit = 130; errors = ["Cancelled."]; }
        catch (AuthenticationRequiredException e) { exit = 4; errors = [e.Message]; }
        catch (LocalOperationBusyException e) { exit = 7; errors = [e.Message]; }
        catch (PlatformNotSupportedException e) { exit = 8; errors = [e.Message]; }
        catch (CurriculaSourceUnavailableException e) { exit = 3; errors = [e.Message]; }
        catch (JsonException e) { exit = 3; errors = [e.Message]; }
        catch (DirectoryNotFoundException e) { exit = 3; errors = [e.Message]; }
        catch (ArgumentException e) { exit = 2; errors = [e.Message]; }
        // Graph's production HttpProvider wraps authentication middleware failures in ServiceException.
        // Recover only our known local errors; never expose SDK/wrapper messages or reclassify unknown failures.
        catch (ServiceException e) when (FindInnerException<AuthenticationRequiredException>(e) is { } authentication)
        {
            exit = 4; errors = [authentication.Message];
        }
        catch (ServiceException e) when (FindInnerException<LocalOperationBusyException>(e) is { } busy)
        {
            exit = 7; errors = [busy.Message];
        }
        catch (ServiceException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            exit = 4; errors = [new AuthenticationRequiredException("microsoft", settings.Profile ?? "TEACHER").Message];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ServiceException or HttpRequestException
            or OperationCanceledException or OpenXmlPackageException or FormatException)
        {
            exit = output?.PublishedPaths.Count > 0 ? 6 : 5;
            errors = ["Curricula download or local publication failed."];
        }
        catch (Exception) { exit = 1; errors = ["Curricula download failed unexpectedly."]; }
        finally
        {
            Console.CancelKeyPress -= cancel;
            // Once artifacts are published, subsequent capability/authentication failures are partial output failures.
            if (exit != 0 && exit != 130 && output?.PublishedPaths.Count > 0) exit = 6;
            try
            {
                if (exit != 0 && output?.PublishedPaths.Count > 0 && !output.PublishedPaths.Contains(output.ManifestPath))
                {
                    try { await output.Complete("partial", CancellationToken.None); }
                    catch (Exception) { warnings.Add("Could not publish the partial output manifest."); }
                }
            }
            finally { output?.Dispose(); }
        }
        foreach (var warning in warnings) Console.Error.WriteLine(warning);
        foreach (var error in errors) Console.Error.WriteLine(error);
        var paths = output?.PublishedPaths.ToArray() ?? [];
        if (result.Json)
            Console.WriteLine(JsonSerializer.Serialize(new CommandResult<CurriculaDownloadResult>(1, command, runId,
                exit == 0 ? "succeeded" : exit == 130 ? "cancelled" : paths.Length > 0 ? "partial" : "failed", exit, paths, [], warnings.ToArray(), errors,
                new(source, output?.DirectoryPath, count)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        else
        {
            Console.WriteLine($"Curricula source: {source.Owner}/{source.Path}");
            foreach (var path in paths) Console.WriteLine(path);
        }
        return exit;
    }

    private static T? FindInnerException<T>(Exception exception) where T : Exception
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is T known) return known;
        return null;
    }

    private static string ArtifactName(CurriculaFile file, string extension = ".docx") => file.Group + " - " + Path.GetFileNameWithoutExtension(file.Name) + extension;
    private static void ValidateComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Any(c => char.IsControl(c) || "/\\:<>\"|?*".Contains(c)) || value.EndsWith('.') || value.EndsWith(' ') || Path.GetFileName(value) != value)
            throw new IOException("Unsafe curricula group or document name in provider response.");
    }
    private static void ValidateDocx(Stream stream)
    {
        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, false);
        if (document.MainDocumentPart?.Document.Body is null) throw new InvalidDataException("Downloaded DOCX has no document body.");
    }
}
