using System.Security.Cryptography;
using System.Text;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record DriveAccount(string Id, string? Email);
public sealed record DriveDestination(string Id, string Name);
public sealed record DriveRemoteFile(string Id, string Name);
public sealed record DriveArtifact(string Name, string Path);
public sealed record DriveAction(string Action, string Name, string? FileId, string? LocalPath);
public sealed record DriveOutcome(DriveAction Action, string State, string? ResultFileId);
public sealed record DriveSyncResult(string Account, string? AccountEmail, DriveDestination Folder, IReadOnlyList<DriveAction> Actions,
    IReadOnlyList<DriveOutcome> Outcomes, int ExitCode, string[] Errors, string[] Warnings);

/// <summary>Mutations must execute once and surface rejected or lost responses.</summary>
public interface IDriveSyncProvider : IDisposable
{
    Task<DriveAccount> GetAccount(CancellationToken token);
    Task<DriveDestination> FindFolder(string name, CancellationToken token);
    Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string folderId, CancellationToken token);
    Task Delete(string fileId, CancellationToken token);
    Task<string> Create(string folderId, string name, Stream input, CancellationToken token);
    Task<string> Update(string fileId, Stream input, CancellationToken token);
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
    public static string LockPath(string root, string account, string folderId) => Path.Combine(root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\n" + folderId))) + ".lock");

    public static async Task<DriveSyncResult> Run(IDriveSyncProvider provider, string folderName,
        IReadOnlyList<DriveArtifact> artifacts, bool apply, CancellationToken token, string? lockRoot = null,
        Action<(string Id, string Status, object? Item, string Human)>? onOutcome = null)
    {
        var account = await provider.GetAccount(token);
        var folder = await provider.FindFolder(folderName, token);
        await using var lease = apply ? await LocalFileLock.Acquire(LockPath(lockRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLib", "remote-locks", "drive"), account.Id, folder.Id), token) : null;
        // Folder identity is resolved before locking; reread it under the lease and refuse a changed destination.
        if (apply && (await provider.FindFolder(folderName, token)).Id != folder.Id)
            throw new IOException("The Drive folder changed while acquiring its lock; rerun to resolve the current destination.");
        var remote = await provider.ListFiles(folder.Id, token);
        var local = new Dictionary<string, DriveArtifact>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts.Where(x => IsScheduleArtifact(x.Name))) local.TryAdd(artifact.Name, artifact);
        var cloudNames = remote.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actions = new List<DriveAction>();
        // Preserve legacy matching: every duplicate remote name is updated, unmatched remote files are deleted.
        actions.AddRange(remote.Where(x => !local.ContainsKey(x.Name)).Select(x => new DriveAction("delete", x.Name, x.Id, null)));
        actions.AddRange(local.Values.Where(x => !cloudNames.Contains(x.Name)).Select(x => new DriveAction("create", x.Name, null, x.Path)));
        actions.AddRange(remote.Where(x => local.ContainsKey(x.Name)).Select(x => new DriveAction("update", x.Name, x.Id, local[x.Name].Path)));
        var warnings = new[] { "All unmatched remote files are deleted, including unrelated files in the configured folder. Matching is case-insensitive; every matching remote file is updated. No rollback is provided. Local locks coordinate only this machine." };
        var outcomes = new List<DriveOutcome>();
        // Stable plan-order ids shared by preview and apply and by each action's
        // planned record and its later terminal transition.
        string PlanId(int index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        void Report(string id, DriveOutcome outcome, string human)
        {
            try
            {
                onOutcome?.Invoke((Id: id, Status: outcome.State, Item: outcome, Human: human));
            }
            catch (CliOutputStreamingException ex) { throw StreamingFailure(ex); }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw StreamingFailure(new CliOutputStreamingException("Drive output streaming failed.", ex)); }
        }
        DriveStreamingException StreamingFailure(Exception inner) => new(Result(
            outcomes.Any(x => x.State == "completed") ? 6 : 1,
            ["Output streaming failed. Completed actions remain applied; inspect Drive before another apply. No action was retried."]), inner);
        void ReportPlanned(int index, DriveAction action)
        {
            var planned = new DriveOutcome(action, "planned", null);
            Report(PlanId(index), planned, $"{action.Action} {action.Name}: {planned.State}");
        }
        void AddOutcome(DriveOutcome outcome)
        {
            // The completed remote outcome is recorded before rendering so a
            // rendering failure cannot change it or trigger another mutation.
            // Reports below run outside the mutation try/catch blocks (or inside
            // catch blocks, which never re-catch their own throws), so a
            // rendering failure is never misclassified as provider uncertainty.
            outcomes.Add(outcome);
            var index = outcomes.Count - 1;
            Report(PlanId(index), outcome, $"{outcome.Action.Action} {outcome.Action.Name}: {outcome.State}; id {outcome.ResultFileId}");
        }
        token.ThrowIfCancellationRequested();
        // The full plan streams before any mutation in both modes; the final
        // summary keeps counts only and never repeats these records.
        for (var index = 0; index < actions.Count; index++) ReportPlanned(index, actions[index]);
        if (!apply) return Result(0, []);
        foreach (var action in actions)
        {
            var attempted = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (action.Action == "delete")
                {
                    attempted = true;
                    await provider.Delete(action.FileId!, token);
                    AddOutcome(new(action, "completed", action.FileId));
                }
                else
                {
                    await using var input = File.OpenRead(action.LocalPath!);
                    token.ThrowIfCancellationRequested();
                    attempted = true;
                    var id = action.Action == "create"
                        ? await provider.Create(folder.Id, action.Name, input, token)
                        : await provider.Update(action.FileId!, input, token);
                    AddOutcome(new(action, "completed", id));
                }
            }
            catch (CliOutputStreamingException)
            {
                // Rendering already recorded the completed remote outcome above;
                // it must not be reclassified as uncertain/failed or retried.
                throw;
            }
            catch (Exception error) when (token.IsCancellationRequested
                && error is OperationCanceledException or GooglePreSendTransportException)
            {
                AddOutcome(new(action, attempted ? "uncertain" : "not-attempted", null));
                AddRemaining();
                return Result(130, ["Cancelled. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried."]);
            }
            catch (GooglePreSendTransportException)
            {
                AddOutcome(new(action, "failed", null));
                AddRemaining();
                return Result(outcomes.Any(x => x.State == "completed") ? 6 : 5,
                    ["Google credential refresh transport failed; the action was not sent. Completed actions remain applied."]);
            }
            catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode is 400 or 401 or 403 or 404 or 409 or 412 or 422)
            {
                AddOutcome(new(action, "failed", null));
                AddRemaining();
                return Result(outcomes.Any(x => x.State == "completed") ? 6 : (int)e.HttpStatusCode == 401 ? 4 : 5, ["Drive rejected the action. Completed actions remain applied; no action was retried."]);
            }
            catch (Exception)
            {
                AddOutcome(new(action, attempted ? "uncertain" : "failed", null));
                AddRemaining();
                return Result(outcomes.Any(x => x.State is "completed" or "uncertain") ? 6 : 5,
                    ["Drive application failed. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried."]);
            }
        }
        return Result(token.IsCancellationRequested ? 130 : 0, token.IsCancellationRequested ? ["Cancelled after the reported actions completed."] : []);

        void AddRemaining()
        {
            foreach (var action in actions.Skip(outcomes.Count)) AddOutcome(new(action, "not-attempted", null));
        }
        DriveSyncResult Result(int exit, string[] errors) => new(account.Id, account.Email, folder, actions, outcomes, exit, errors, warnings);
    }

    public static bool IsScheduleArtifact(string name) => name == Path.GetFileName(name)
        && new[] { ".xlsx", ".pdf", ".ics" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
}
