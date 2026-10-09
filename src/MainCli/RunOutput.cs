using System.Security.Cryptography;
using System.Text.Json;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record OwnedArtifact(string Name, string Sha256);
public sealed record OutputManifest(int SchemaVersion, string Command, string RunId, string Status, OwnedArtifact[] Artifacts);

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

    private RunOutput(string directory, LocalFileLock lease, string command, string runId, Dictionary<string, string> owned)
        => (DirectoryPath, _lease, _command, _runId, _owned) = (directory, lease, command, runId, owned);

    public static async Task<RunOutput> Create(string? output, string command, string runId, CancellationToken token, string? projectDirectory = null)
    {
        var directory = CliRuntime.ResolvePath(output ?? Path.Combine(projectDirectory ?? Environment.CurrentDirectory, "output", runId));
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.LinkTarget is not null) throw new IOException($"Output directory cannot traverse a symbolic link: {current.FullName}");
        Directory.CreateDirectory(directory);
        var lease = await LocalFileLock.Acquire(Path.Combine(directory, ".schedulelib-output.lock"), token);
        try
        {
            var manifestPath = Path.Combine(directory, "schedulelib-manifest.json");
            var owned = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(manifestPath) || new FileInfo(manifestPath).LinkTarget is not null)
                throw new IOException($"Manifest collision: {manifestPath}");
            if (File.Exists(manifestPath))
            {
                OutputManifest? manifest;
                try { manifest = JsonSerializer.Deserialize<OutputManifest>(await File.ReadAllTextAsync(manifestPath, token)); }
                catch (JsonException e) { throw new IOException($"Unrelated or invalid manifest: {manifestPath}", e); }
                if (manifest is null || manifest.SchemaVersion != 1 || manifest.Command != command || manifest.Artifacts is null)
                    throw new IOException($"Output manifest belongs to another operation: {manifestPath}");
                foreach (var artifact in manifest.Artifacts)
                {
                    ValidateName(artifact.Name);
                    if (!owned.TryAdd(artifact.Name, artifact.Sha256)) throw new IOException("Duplicate manifest artifact.");
                }
            }
            return new(directory, lease, command, runId, owned);
        }
        catch { await lease.DisposeAsync(); throw; }
    }

    public async Task Publish(string name, Func<Stream, CancellationToken, Task> generate, CancellationToken token)
    {
        ValidateName(name);
        var destination = Path.Combine(DirectoryPath, name);
        if (Directory.Exists(destination) || new FileInfo(destination).LinkTarget is not null)
            throw new IOException($"Output collision: {destination}");
        if (File.Exists(destination) && (!_owned.TryGetValue(name, out var hash) || hash != await Fingerprint(destination, token)))
            throw new IOException($"Output collision with an unrelated or modified file: {destination}");
        await AtomicFile.Publish(destination, generate, token);
        PublishedPaths.Add(destination);
        _owned[name] = await Fingerprint(destination, CancellationToken.None);
    }

    public async Task Complete(string status, CancellationToken token)
    {
        var manifest = new OutputManifest(1, _command, _runId, status, _owned.Select(x => new OwnedArtifact(x.Key, x.Value)).ToArray());
        await AtomicFile.Publish(ManifestPath, (stream, ct) => JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: ct), token);
        PublishedPaths.Add(ManifestPath);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or ".." || name.Contains('\\') || name.Contains('/')
            || name is "schedulelib-manifest.json" or ".schedulelib-output.lock")
            throw new IOException($"Invalid owned artifact name: {name}");
    }

    private static async Task<string> Fingerprint(string path, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token));
    }

    public ValueTask DisposeAsync() => _lease.DisposeAsync();
    public void Dispose() => _lease.Dispose();
}
