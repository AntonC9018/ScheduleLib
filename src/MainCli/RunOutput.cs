using System.Security.Cryptography;
using System.Text.Json;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record OwnedArtifact(string Name, string Sha256);

public sealed record OutputManifest(
    int SchemaVersion,
    string Command,
    string RunId,
    string Status,
    OwnedArtifact[] Artifacts);

/// <summary>A run owns only files named and fingerprinted by its manifest. Explicit
/// directories retain unrelated files. Hold this lease through artifact publication
/// and manifest completion; all export slices can reuse it.</summary>
public sealed class RunOutput : IAsyncDisposable, IDisposable
{
    private readonly LocalFileLock _lease;
    private readonly string _command;
    private readonly string _runId;
    private readonly Dictionary<string, string> _owned;

    public string DirectoryPath { get; }

    public string ManifestPath => Path.Combine(DirectoryPath, "schedulelib-manifest.json");

    public List<string> PublishedPaths { get; } = [];

    private RunOutput(
        string directory,
        LocalFileLock lease,
        string command,
        string runId,
        Dictionary<string, string> owned)
        => (DirectoryPath, _lease, _command, _runId, _owned) = (directory, lease, command, runId, owned);

    public static async Task<RunOutput> Create(
        string? output,
        string command,
        string runId,
        CancellationToken token,
        string? projectDirectory = null)
    {
        var directory = CliRuntime.ResolvePath(
            output
            ?? Path.Combine(
                projectDirectory ?? Environment.CurrentDirectory,
                "output",
                runId));
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (current.LinkTarget is not null)
            {
                throw new OutputDirectoryTraversesSymbolicLinkException(current.FullName);
            }
        }

        Directory.CreateDirectory(directory);
        var lease = await LocalFileLock.Acquire(Path.Combine(directory, ".schedulelib-output.lock"), token);
        try
        {
            var manifestPath = Path.Combine(directory, "schedulelib-manifest.json");
            var owned = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(manifestPath) || new FileInfo(manifestPath).LinkTarget is not null)
            {
                throw new OutputManifestCollisionException(manifestPath);
            }

            if (File.Exists(manifestPath))
            {
                OutputManifest? manifest;
                try
                {
                    manifest = JsonSerializer.Deserialize<OutputManifest>(
                        await File.ReadAllTextAsync(manifestPath, token));
                }
                catch (JsonException e)
                {
                    throw new InvalidOutputManifestException(manifestPath, e);
                }
                if (manifest is null)
                {
                    throw new OutputManifestDoesNotBelongToOperationException(manifestPath);
                }
                if (manifest.SchemaVersion != 1)
                {
                    throw new OutputManifestDoesNotBelongToOperationException(manifestPath);
                }
                if (manifest.Command != command)
                {
                    throw new OutputManifestDoesNotBelongToOperationException(manifestPath);
                }
                if (manifest.Artifacts is null)
                {
                    throw new OutputManifestDoesNotBelongToOperationException(manifestPath);
                }

                foreach (var artifact in manifest.Artifacts)
                {
                    ValidateName(artifact.Name);
                    if (!owned.TryAdd(artifact.Name, artifact.Sha256))
                    {
                        throw new DuplicateOutputManifestArtifactException(artifact.Name);
                    }
                }
            }
            return new(
                directory: directory,
                lease,
                command: command,
                runId: runId,
                owned);
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public async Task Publish(
        string name,
        Func<Stream, CancellationToken, Task> generate,
        CancellationToken token)
    {
        ValidateName(name);
        var destination = Path.Combine(DirectoryPath, name);
        if (Directory.Exists(destination) || new FileInfo(destination).LinkTarget is not null)
        {
            throw new OutputArtifactCollisionException(destination);
        }

        if (File.Exists(destination)
            && (!_owned.TryGetValue(key: name, value: out var hash) || hash != await Fingerprint(destination, token)))
        {
            throw new OutputArtifactOwnershipConflictException(destination);
        }

        await AtomicFile.Publish(
            destination,
            generate,
            token);
        PublishedPaths.Add(destination);
        _owned[name] = await Fingerprint(destination, CancellationToken.None);
    }

    public async Task Complete(string status, CancellationToken token)
    {
        var manifest = new OutputManifest(
            1,
            Command: _command,
            RunId: _runId,
            Status: status,
            _owned.Select(x => new OwnedArtifact(Name: x.Key, Sha256: x.Value))
                .ToArray());
        await AtomicFile.Publish(
            ManifestPath,
            (stream, ct) => JsonSerializer.SerializeAsync(
                stream,
                manifest,
                cancellationToken: ct),
            token);
        PublishedPaths.Add(ManifestPath);
    }

    private static void ValidateName(string name)
    {
        bool CheckCondition()
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            if (name != Path.GetFileName(name))
            {
                return true;
            }

            if (name is "." or "..")
            {
                return true;
            }

            if (name.Contains('\\'))
            {
                return true;
            }

            if (name.Contains('/'))
            {
                return true;
            }

            if (name is "schedulelib-manifest.json" or ".schedulelib-output.lock")
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            throw new InvalidOwnedArtifactNameException(name);
        }
    }

    private static async Task<string> Fingerprint(string path, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token));
    }

    public ValueTask DisposeAsync() => _lease.DisposeAsync();
    public void Dispose() => _lease.Dispose();

    internal sealed class OutputDirectoryTraversesSymbolicLinkException : IOException
    {
        internal OutputDirectoryTraversesSymbolicLinkException(string directoryPath)
            : base($"Output directory cannot traverse a symbolic link: {directoryPath}")
            => DirectoryPath = directoryPath;

        public string DirectoryPath { get; }
    }

    internal sealed class OutputManifestCollisionException : IOException
    {
        internal OutputManifestCollisionException(string manifestPath)
            : base($"Manifest collision: {manifestPath}")
            => ManifestPath = manifestPath;

        public string ManifestPath { get; }
    }

    internal sealed class InvalidOutputManifestException : IOException
    {
        internal InvalidOutputManifestException(string manifestPath, Exception innerException)
            : base($"Unrelated or invalid manifest: {manifestPath}", innerException)
            => ManifestPath = manifestPath;

        public string ManifestPath { get; }
    }

    internal sealed class OutputManifestDoesNotBelongToOperationException : IOException
    {
        internal OutputManifestDoesNotBelongToOperationException(string manifestPath)
            : base($"Output manifest belongs to another operation: {manifestPath}")
            => ManifestPath = manifestPath;

        public string ManifestPath { get; }
    }

    internal sealed class DuplicateOutputManifestArtifactException : IOException
    {
        internal DuplicateOutputManifestArtifactException(string artifactName)
            : base("Duplicate manifest artifact.")
            => ArtifactName = artifactName;

        public string ArtifactName { get; }
    }

    internal sealed class OutputArtifactCollisionException : IOException
    {
        internal OutputArtifactCollisionException(string destinationPath)
            : base($"Output collision: {destinationPath}")
            => DestinationPath = destinationPath;

        public string DestinationPath { get; }
    }

    internal sealed class OutputArtifactOwnershipConflictException : IOException
    {
        internal OutputArtifactOwnershipConflictException(string destinationPath)
            : base($"Output collision with an unrelated or modified file: {destinationPath}")
            => DestinationPath = destinationPath;

        public string DestinationPath { get; }
    }

    internal sealed class InvalidOwnedArtifactNameException : IOException
    {
        internal InvalidOwnedArtifactNameException(string? artifactName)
            : base($"Invalid owned artifact name: {artifactName}")
            => ArtifactName = artifactName;

        public string? ArtifactName { get; }
    }
}
