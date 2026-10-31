using System.Security.Cryptography;
using System.Text;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record DriveAccount(string Id, string? Email);

public sealed record DriveDestination(string Id, string Name);

public sealed record DriveRemoteFile(string Id, string Name);

public sealed record DriveArtifact(string Name, string Path);

[System.Text.Json.Serialization.JsonConverter(typeof(DriveActionKindJsonConverter))]
public enum DriveActionKind
{
    Delete,
    Create,
    Update,
}

public static class DriveActionKindWireNames
{
    public static string ToWire(this DriveActionKind kind)
    {
        return kind switch
        {
            DriveActionKind.Delete => "delete",
            DriveActionKind.Create => "create",
            DriveActionKind.Update => "update",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}

public sealed class DriveActionKindJsonConverter : WireNameJsonConverter<DriveActionKind>
{
    protected override string ToWire(DriveActionKind value) => value.ToWire();
}

/// <summary>
/// One planned Drive mutation: a closed set of <see cref="DriveDeleteAction"/>,
/// <see cref="DriveCreateAction"/> and <see cref="DriveUpdateAction"/>. The nullable
/// <see cref="FileId"/>/<see cref="LocalPath"/> members keep the existing wire shape;
/// mutations use the non-nullable members of the concrete action.
/// </summary>
public abstract record DriveAction
{
    private protected DriveAction(string name)
        => Name = name;

    public abstract DriveActionKind Action { get; }

    public string Name { get; }

    public abstract string? FileId { get; }

    public abstract string? LocalPath { get; }
}

public sealed record DriveDeleteAction : DriveAction
{
    public DriveDeleteAction(string name, string remoteFileId) : base(name)
        => RemoteFileId = remoteFileId;

    [System.Text.Json.Serialization.JsonIgnore]
    public string RemoteFileId { get; }

    public override DriveActionKind Action => DriveActionKind.Delete;

    public override string? FileId => RemoteFileId;

    public override string? LocalPath => null;
}

public sealed record DriveCreateAction : DriveAction
{
    public DriveCreateAction(string name, string sourcePath) : base(name)
        => SourcePath = sourcePath;

    [System.Text.Json.Serialization.JsonIgnore]
    public string SourcePath { get; }

    public override DriveActionKind Action => DriveActionKind.Create;

    public override string? FileId => null;

    public override string? LocalPath => SourcePath;
}

public sealed record DriveUpdateAction : DriveAction
{
    public DriveUpdateAction(string name, string remoteFileId, string sourcePath) : base(name)
    {
        RemoteFileId = remoteFileId;
        SourcePath = sourcePath;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string RemoteFileId { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string SourcePath { get; }

    public override DriveActionKind Action => DriveActionKind.Update;

    public override string? FileId => RemoteFileId;

    public override string? LocalPath => SourcePath;
}

public sealed record DriveOutcome(DriveAction Action, ActionState State, string? ResultFileId);

public sealed record DriveSyncResult(
    string Account,
    string? AccountEmail,
    DriveDestination Folder,
    IReadOnlyList<DriveAction> Actions,
    IReadOnlyList<DriveOutcome> Outcomes,
    int ExitCode,
    string[] Errors,
    string[] Warnings);

/// <summary>Mutations must execute once and surface rejected or lost responses.</summary>
public interface IDriveSyncProvider : IDisposable
{
    Task<DriveAccount> GetAccount(CancellationToken token);
    Task<DriveDestination> FindFolder(string name, CancellationToken token);
    Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string folderId, CancellationToken token);
    Task Delete(string fileId, CancellationToken token);

    Task<string> Create(
        string folderId,
        string name,
        Stream input,
        CancellationToken token);

    Task<string> Update(
        string fileId,
        Stream input,
        CancellationToken token);
}

/// <summary>
/// A rendering/output failure after a completed remote mutation. The remote
/// outcome already recorded in <see cref="PartialResult"/> must not be retried
/// or reclassified; callers stop further mutations and report it as-is.
/// </summary>
public sealed class DriveStreamingException(DriveSyncResult PartialResult, Exception? Inner = null)
    : CliOutputStreamingException("Drive output streaming failed.", Inner)
{
    public DriveSyncResult PartialResult { get; } = PartialResult;
}

public static class DriveSync
{
    public static string LockPath(
        string root,
        string account,
        string folderId)
    {
        string path1 = root;
        var encoding = Encoding.UTF8;
        string text = $"{account}\n{folderId}";
        string s = text;
        byte[] source = encoding.GetBytes(s);
        byte[] inArray = SHA256.HashData(source);
        string toHexString = Convert.ToHexString(inArray);
        string text2 = $"{toHexString}.lock";
        string path2 = text2;
        string combine = Path.Combine(
            path1,
            path2);
        return combine;
    }

    public static async Task<DriveSyncResult> Run(
        IDriveSyncProvider provider,
        string folderName,
        IReadOnlyList<DriveArtifact> artifacts,
        bool apply,
        CancellationToken token,
        string? lockRoot = null,
        Action<StreamedUpdate>? onOutcome = null)
    {
        var account = await provider.GetAccount(token);
        var folder = await provider.FindFolder(folderName, token);

        await using var lease = apply
            ? await LocalFileLock.Acquire(
                LockPath(
                    root: lockRoot ?? DefaultLockRoot(),
                    account: account.Id,
                    folderId: folder.Id),
                token)
            : null;
        // Folder identity is resolved before locking; reread it under the lease and refuse a changed destination.
        if (apply && (await provider.FindFolder(folderName, token)).Id != folder.Id)
        {
            throw new IOException(
                "The Drive folder changed while acquiring its lock; rerun to resolve the current destination.");
        }

        var remote = await provider.ListFiles(folder.Id, token);
        var actions = PlanActions(remote, artifacts);
        var warnings = new[]
        {
            "All unmatched remote files are deleted, including unrelated files in the configured folder. Matching is case-insensitive; every matching remote file is updated. No rollback is provided. Local locks coordinate only this machine.",
        };
        var outcomes = new List<DriveOutcome>();

        // Stable plan-order ids shared by preview and apply and by each action's
        // planned record and its later terminal transition.
        string PlanId(int index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        void Report(
            string id,
            DriveOutcome outcome,
            string human)
        {
            try
            {
                if (onOutcome is not null)
                {
                    var update = new StreamedUpdate(
                        Id: id,
                        Status: outcome.State,
                        outcome,
                        Human: human);
                    onOutcome.Invoke(update);
                }
            }
            catch (CliOutputStreamingException ex)
            {
                throw StreamingFailure(ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw StreamingFailure(
                    new GeneralCliOutputStreamingException("Drive output streaming failed.", ex));
            }
        }

        DriveStreamingException StreamingFailure(Exception inner)
        {
            var partialResult = Result(
                RemoteSyncOutcomes.StreamingFailureExit(outcomes.Select(x => x.State)),
                [
                    "Output streaming failed. Completed actions remain applied; inspect Drive before another apply. No action was retried.",
                ]);
            return new DriveStreamingException(partialResult, inner);
        }

        void ReportPlanned(int index, DriveAction action)
        {
            var planned = new DriveOutcome(
                action,
                State: ActionState.Planned,
                ResultFileId: null);
            Report(
                id: PlanId(index),
                planned,
                human: $"{action.Action.ToWire()} {action.Name}: {planned.State.ToWire()}");
        }

        void AddOutcome(DriveOutcome outcome)
        {
            // The completed remote outcome is recorded before rendering so a
            // rendering failure cannot change it or trigger another mutation.
            // Reports below run outside the mutation try/catch blocks (or inside
            // catch blocks, which never re-catch their own throws), so a
            // rendering failure is never misclassified as provider uncertainty.
            outcomes.Add(outcome);
            Report(
                id: PlanId(outcomes.Count - 1),
                outcome,
                human:
                $"{outcome.Action.Action.ToWire()} {outcome.Action.Name}: {outcome.State.ToWire()}; id {outcome.ResultFileId}");
        }

        void AddTerminal(DriveAction action, ActionState state)
        {
            AddOutcome(new DriveOutcome(action, state, ResultFileId: null));
            foreach (var remaining in actions.Skip(outcomes.Count))
            {
                AddOutcome(new DriveOutcome(remaining, ActionState.NotAttempted, ResultFileId: null));
            }
        }

        bool AnyCompleted() => RemoteSyncOutcomes.AnyCompleted(outcomes.Select(x => x.State));

        token.ThrowIfCancellationRequested();
        // The full plan streams before any mutation in both modes; the final
        // summary keeps counts only and never repeats these records.
        for (var index = 0; index < actions.Count; index++)
        {
            ReportPlanned(index, actions[index]);
        }

        if (!apply)
        {
            return Result(CliExit.Success, []);
        }

        foreach (var action in actions)
        {
            var attempted = false;
            try
            {
                token.ThrowIfCancellationRequested();
                string resultFileId;
                switch (action)
                {
                    case DriveDeleteAction delete:
                    {
                        attempted = true;
                        await provider.Delete(delete.RemoteFileId, token);
                        resultFileId = delete.RemoteFileId;
                        break;
                    }
                    case DriveCreateAction create:
                    {
                        await using var input = File.OpenRead(create.SourcePath);
                        token.ThrowIfCancellationRequested();
                        attempted = true;
                        resultFileId = await provider.Create(
                            folderId: folder.Id,
                            name: create.Name,
                            input,
                            token);
                        break;
                    }
                    case DriveUpdateAction update:
                    {
                        await using var input = File.OpenRead(update.SourcePath);
                        token.ThrowIfCancellationRequested();
                        attempted = true;
                        resultFileId = await provider.Update(
                            update.RemoteFileId,
                            input,
                            token);
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unknown Drive action {action.GetType().Name}.");
                }

                AddOutcome(new DriveOutcome(action, ActionState.Completed, resultFileId));
            }
            catch (CliOutputStreamingException)
            {
                // Rendering already recorded the completed remote outcome above;
                // it must not be reclassified as uncertain/failed or retried.
                throw;
            }
            catch (Exception error) when (RemoteSyncOutcomes.IsCancellationTransportError(token, error))
            {
                AddTerminal(action, RemoteSyncOutcomes.InterruptedState(attempted, ActionState.NotAttempted));
                return Result(
                    CliExit.Cancelled,
                    [
                        "Cancelled. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried.",
                    ]);
            }
            catch (GooglePreSendTransportException)
            {
                AddTerminal(action, ActionState.Failed);
                return Result(
                    CliExit.AfterEffects(CliExit.IoFailure, effects: AnyCompleted()),
                    [RemoteSyncOutcomes.CredentialRefreshTransportError]);
            }
            catch (Google.GoogleApiException e)
                when (RemoteSyncOutcomes.IsUnauthorized(e) || RemoteSyncOutcomes.IsRejected(e))
            {
                AddTerminal(action, ActionState.Failed);
                var failureExit = RemoteSyncOutcomes.IsUnauthorized(e)
                    ? CliExit.AuthenticationRequired
                    : CliExit.IoFailure;
                return Result(
                    CliExit.AfterEffects(failureExit, effects: AnyCompleted()),
                    ["Drive rejected the action. Completed actions remain applied; no action was retried."]);
            }
            catch (Exception)
            {
                AddTerminal(action, RemoteSyncOutcomes.InterruptedState(attempted, ActionState.Failed));
                var effects = outcomes.Any(x => x.State is ActionState.Completed or ActionState.Uncertain);
                return Result(
                    CliExit.AfterEffects(CliExit.IoFailure, effects),
                    [
                        "Drive application failed. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried.",
                    ]);
            }
        }

        if (token.IsCancellationRequested)
        {
            return Result(CliExit.Cancelled, ["Cancelled after the reported actions completed."]);
        }

        return Result(CliExit.Success, []);

        DriveSyncResult Result(int exit, string[] errors)
        {
            return new(
                Account: account.Id,
                AccountEmail: account.Email,
                folder,
                actions,
                outcomes,
                exit,
                Errors: errors,
                Warnings: warnings);
        }
    }

    private static string DefaultLockRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScheduleLib",
            "remote-locks",
            "drive");
    }

    /// <summary>
    /// Preserves legacy matching: every duplicate remote name is updated and unmatched
    /// remote files are deleted. Deletes come first, then creates, then updates.
    /// </summary>
    private static List<DriveAction> PlanActions(
        IReadOnlyList<DriveRemoteFile> remote,
        IReadOnlyList<DriveArtifact> artifacts)
    {
        var local = new Dictionary<string, DriveArtifact>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts.Where(x => IsScheduleArtifact(x.Name)))
        {
            local.TryAdd(artifact.Name, artifact);
        }

        var cloudNames = remote.Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actions = new List<DriveAction>();
        actions.AddRange(
            remote.Where(x => !local.ContainsKey(x.Name))
                .Select(x => new DriveDeleteAction(name: x.Name, remoteFileId: x.Id)));
        actions.AddRange(
            local.Values.Where(x => !cloudNames.Contains(x.Name))
                .Select(x => new DriveCreateAction(name: x.Name, sourcePath: x.Path)));
        actions.AddRange(
            remote.Where(x => local.ContainsKey(x.Name))
                .Select(x => new DriveUpdateAction(
                    name: x.Name,
                    remoteFileId: x.Id,
                    sourcePath: local[x.Name].Path)));
        return actions;
    }

    public static bool IsScheduleArtifact(string name)
    {
        string CalculateValue()
        {
            string value = Path.GetExtension(name);
            return value;
        }

        return name == Path.GetFileName(name)
            && new[]
            {
                ".xlsx",
                ".pdf",
                ".ics",
            }.Contains(CalculateValue(), StringComparer.OrdinalIgnoreCase);
    }
}
