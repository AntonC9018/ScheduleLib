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

public static class DriveSync
{
    public static string LockPath(string root, string account, string folderId) => Path.Combine(root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\n" + folderId))) + ".lock");

    public static async Task<DriveSyncResult> Run(IDriveSyncProvider provider, string folderName,
        IReadOnlyList<DriveArtifact> artifacts, bool apply, CancellationToken token, string? lockRoot = null)
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
        token.ThrowIfCancellationRequested();
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
                    outcomes.Add(new(action, "completed", action.FileId));
                }
                else
                {
                    await using var input = File.OpenRead(action.LocalPath!);
                    token.ThrowIfCancellationRequested();
                    attempted = true;
                    var id = action.Action == "create"
                        ? await provider.Create(folder.Id, action.Name, input, token)
                        : await provider.Update(action.FileId!, input, token);
                    outcomes.Add(new(action, "completed", id));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                outcomes.Add(new(action, attempted ? "uncertain" : "not-attempted", null));
                AddRemaining();
                return Result(130, ["Cancelled. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried."]);
            }
            catch (GooglePreSendTransportException)
            {
                outcomes.Add(new(action, "failed", null));
                AddRemaining();
                return Result(outcomes.Any(x => x.State == "completed") ? 6 : 5,
                    ["Google credential refresh transport failed; the action was not sent. Completed actions remain applied."]);
            }
            catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode is 400 or 401 or 403 or 404 or 409 or 412 or 422)
            {
                outcomes.Add(new(action, "failed", null));
                AddRemaining();
                return Result(outcomes.Any(x => x.State == "completed") ? 6 : (int)e.HttpStatusCode == 401 ? 4 : 5, ["Drive rejected the action. Completed actions remain applied; no action was retried."]);
            }
            catch (Exception)
            {
                outcomes.Add(new(action, attempted ? "uncertain" : "failed", null));
                AddRemaining();
                return Result(outcomes.Any(x => x.State is "completed" or "uncertain") ? 6 : 5,
                    ["Drive application failed. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried."]);
            }
        }
        return Result(token.IsCancellationRequested ? 130 : 0, token.IsCancellationRequested ? ["Cancelled after the reported actions completed."] : []);

        void AddRemaining()
        {
            foreach (var action in actions.Skip(outcomes.Count)) outcomes.Add(new(action, "not-attempted", null));
        }
        DriveSyncResult Result(int exit, string[] errors) => new(account.Id, account.Email, folder, actions, outcomes, exit, errors, warnings);
    }

    public static bool IsScheduleArtifact(string name) => name == Path.GetFileName(name)
        && new[] { ".xlsx", ".pdf", ".ics" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
}
