using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CommandDotNet;
using ConvertDocToDocx;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Graph;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Curriculum.Download;
using File = System.IO.File;

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        category: "Reliability",
        checkId: "CA2000:Dispose objects before losing scope",
        Justification = "The returned Graph adapter owns the HttpProvider; the failure path disposes it.")]
    protected virtual async Task<ICurriculaProvider> CreateProvider(string profile, CancellationToken token)
    {
        var authentication = CreateAuthentication();
        var config = LoadMicrosoftConfig();
        // Fail before any Graph request; subsequent SDK requests may silently refresh under the same authorization contract.
        await authentication.Resolve(
            config,
            profile,
            token);
        var provider = new HttpProvider();
        try
        {
            var client = new GraphServiceClient(new DelegateAuthenticationProvider(async request =>
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        scheme: "Bearer",
                        parameter: await authentication.Resolve(
                            config,
                            profile,
                            token));
                }),
                provider);
            return new GraphCurriculaProvider(client, provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    protected virtual void EnsureLegacyCapability()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Legacy .doc conversion requires Windows and Microsoft Word. Supply DOCX sources on Linux. No downloaded originals were modified.");
        }
    }

    protected virtual Task<bool> ConvertLegacy(
        string input,
        string output,
        CancellationToken token)
    {
        return DocToDocxConversionHelper.TryConvertFile(
            inputPath: input,
            outputPath: output,
            token);
    }

    [Command(
        "download",
        Description =
            "Download curricula from the existing C#-configured owner/path (2024-2025). Requires provisioned Microsoft authorization; no browser consent. Legacy DOC conversion requires Windows and Word.")]
    public async Task<int> Download(
        SettingsArguments settings,
        OutputArguments destination,
        ResultArguments resultArgs,
        CancellationToken cancellationToken = default)
    {
        using var context = new CliCommandContext(
            "curricula download",
            resultArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cli = context.Output;
        var source = Source;
        RunOutput? output = null;
        var warnings = context.Warnings;
        warnings.Add(
            "Curricula source owner/path remain code-defined for 2024-2025; no current-year source discovery is performed.");
        var count = 0;
        var exit = CliExit.Unexpected;
        string[] errors = [];
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selected)
            {
                CliRuntime.ResolvePath(selected);
            }

            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is not { } profile)
            {
                throw new JsonException("Select a teacher with --profile. Use config profiles to list identities.");
            }

            using var provider = await CreateProvider(profile, cancellation.Token);
            IReadOnlyList<CurriculaFile> files;
            try
            {
                files = await provider.List(source, cancellation.Token);
            }
            catch (ServiceException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CurriculaSourceUnavailableException(
                    "The coded curricula owner/path was not found. Update C# source configuration if the existing 2024-2025 source is stale.");
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                ValidateComponent(file.Group);
                ValidateComponent(file.Name);
                var extension = Path.GetExtension(file.Name);
                if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureLegacyCapability();
                }

                if (!extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Skipping unsupported curriculum document: {file.Group}/{file.Name}");
                    continue;
                }
                var name = ArtifactName(file);
                if (!names.Add(name))
                {
                    throw new IOException("Curricula contain colliding output document names.");
                }
            }
            output = context.Artifacts = await RunOutput.Create(
                output: destination.Directory,
                command: context.Command,
                runId: context.RunId,
                cancellation.Token,
                projectDirectory: resolved.ProjectDirectory);
            foreach (var file in files)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file.Name);
                if (!extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    await output.Publish(
                        ArtifactName(file),
                        async (stream, ct) =>
                        {
                            await using var remote = await provider.Download(file, ct);
                            await remote.CopyToAsync(stream, ct);
                            ValidateDocx(stream);
                        },
                        cancellation.Token);
                    StreamNewLocal();
                }
                else
                {
                    // Publish the downloaded original first, then convert a separate staging copy.
                    var originalName = ArtifactName(file, ".doc");
                    await output.Publish(
                        originalName,
                        async (stream, ct) =>
                        {
                            await using var remote = await provider.Download(file, ct);
                            await remote.CopyToAsync(stream, ct);
                        },
                        cancellation.Token);
                    StreamNewLocal();
                    using var stagingOwner = OwnedConversionDirectory.Create(output.DirectoryPath);
                    var staging = stagingOwner.DirectoryPath;
                    {
                        var input = Path.Combine(staging, "source.doc");
                        var converted = Path.Combine(staging, "converted.docx");
                        File.Copy(sourceFileName: Path.Combine(output.DirectoryPath, originalName), destFileName: input);
                        if (!await ConvertLegacy(
                                input: input,
                                output: converted,
                                cancellation.Token))
                        {
                            throw new IOException("Legacy Word conversion failed.");
                        }

                        await output.Publish(
                            ArtifactName(file),
                            async (stream, ct) =>
                            {
                                await using var read = File.OpenRead(converted);
                                await read.CopyToAsync(stream, ct);
                                ValidateDocx(stream);
                            },
                            cancellation.Token);
                        StreamNewLocal();
                    }
                }
                count++;
                StreamNewLocal();
            }
            if (count == 0)
            {
                warnings.Add("No supported curriculum documents were found at the configured source.");
            }

            StreamNewLocal();
            await output.Complete("succeeded", cancellation.Token);
            StreamNewLocal();
            exit = CliExit.Success;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            exit = CliExit.Cancelled;
            errors = ["Cancelled."];
        }
        catch (AuthenticationRequiredException e)
        {
            exit = CliExit.AuthenticationRequired;
            errors = [e.Message];
        }
        catch (LocalOperationBusyException e)
        {
            exit = CliExit.Busy;
            errors = [e.Message];
        }
        catch (PlatformNotSupportedException e)
        {
            exit = CliExit.Unsupported;
            errors = [e.Message];
        }
        catch (CurriculaSourceUnavailableException e)
        {
            exit = CliExit.InvalidConfiguration;
            errors = [e.Message];
        }
        catch (JsonException e)
        {
            exit = CliExit.InvalidConfiguration;
            errors = [e.Message];
        }
        catch (DirectoryNotFoundException e)
        {
            exit = CliExit.InvalidConfiguration;
            errors = [e.Message];
        }
        catch (ArgumentException e)
        {
            exit = CliExit.InvalidArguments;
            errors = [e.Message];
        }
        // Graph's production HttpProvider wraps authentication middleware failures in ServiceException.
        // Recover only our known local errors; never expose SDK/wrapper messages or reclassify unknown failures.
        catch (ServiceException e) when (FindInnerException<AuthenticationRequiredException>(e) is { } authentication)
        {
            exit = CliExit.AuthenticationRequired;
            errors = [authentication.Message];
        }
        catch (ServiceException e) when (FindInnerException<LocalOperationBusyException>(e) is { } busy)
        {
            exit = CliExit.Busy;
            errors = [busy.Message];
        }
        catch (ServiceException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            exit = CliExit.AuthenticationRequired;
            errors = [new AuthenticationRequiredException(provider: "microsoft", teacher: settings.Profile ?? "TEACHER").Message];
        }
        catch (Exception e) when (e is IOException
                                      or UnauthorizedAccessException
                                      or ServiceException
                                      or HttpRequestException
                                      or OperationCanceledException
                                      or OpenXmlPackageException
                                      or FormatException)
        {
            exit = output?.PublishedPaths.Count > 0 ? CliExit.Partial : CliExit.IoFailure;
            errors = ["Curricula download or local publication failed."];
        }
        catch (Exception)
        {
            exit = CliExit.Unexpected;
            errors = ["Curricula download failed unexpectedly."];
        }
        finally
        {
            // Once artifacts are published, subsequent capability/authentication failures are partial output failures.
            exit = CliExit.AfterEffects(exit, effects: output?.PublishedPaths.Count > 0);
            if (exit != CliExit.Success
                && output?.PublishedPaths.Count > 0
                && !output.PublishedPaths.Contains(output.ManifestPath))
            {
                try
                {
                    await output.Complete("partial", CancellationToken.None);
                }
                catch (Exception)
                {
                    warnings.Add("Could not publish the partial output manifest.");
                }
                StreamNewLocal();
            }
        }
        StreamNewLocal();
        var paths = output?.PublishedPaths.ToArray() ?? [];
        var status = CliCommandContext.Status(exit, partial: paths.Length > 0);
        var data = new CurriculaDownloadResult(
            source,
            output?.DirectoryPath,
            count);
        var summary = new
        {
            source,
            outputDirectory = output?.DirectoryPath,
            documentCount = count,
            artifactCount = paths.Length,
        };
        return context.Finish(new CliCompletion<CurriculaDownloadResult>(
            exit,
            errors,
            data)
        {
            Summary = summary,
            Paths = paths,
            Status = status,
            WarningsFirst = true,
            RenderHuman = status =>
            {
                cli.WriteHuman($"Curricula source: {source.Owner}/{source.Path}");
                cli.WriteHuman(
                    $"{status}: {count} documents, {paths.Length} artifacts in {output?.DirectoryPath}.");
            },
        });

        void StreamNewLocal() => context.StreamArtifacts();
    }

    private static T? FindInnerException<T>(Exception exception) where T : Exception
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is T known)
            {
                return known;
            }
        }

        return null;
    }

    private static string ArtifactName(CurriculaFile file, string extension = ".docx") =>
        (string) $"{file.Group} - {Path.GetFileNameWithoutExtension(file.Name)}{extension}";

    private static void ValidateComponent(string value)
    {
        bool CheckCondition()
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (value is "." or "..")
            {
                return true;
            }

            if (value.Any(c => char.IsControl(c) || "/\\:<>\"|?*".Contains(c)))
            {
                return true;
            }

            if (value.EndsWith('.'))
            {
                return true;
            }

            if (value.EndsWith(' '))
            {
                return true;
            }

            if (Path.GetFileName(value) != value)
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            throw new IOException("Unsafe curricula group or document name in provider response.");
        }
    }

    private static void ValidateDocx(Stream stream)
    {
        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, false);
        if (document.MainDocumentPart?.Document.Body is null)
        {
            throw new InvalidDataException("Downloaded DOCX has no document body.");
        }
    }
}
