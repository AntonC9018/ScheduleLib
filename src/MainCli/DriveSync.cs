using System.Security.Cryptography;
using System.Text;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record DriveAccount(string Id, string? Email);

public sealed record DriveDestination(string Id, string Name);

public sealed record DriveRemoteFile(string Id, string Name);

public sealed record DriveArtifact(string Name, string Path);

public sealed record DriveAction(
    string Action,
    string Name,
    string? FileId,
    string? LocalPath);

public sealed record DriveOutcome(DriveAction Action, string State, string? ResultFileId);

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
        global::System.Text.Encoding encoding = Encoding.UTF8;
        string text = $"{account}\n{folderId}";
        string s = (string) text;
        byte[] source = encoding.GetBytes(s);
        byte[] inArray = SHA256.HashData(source);
        string toHexString = Convert.ToHexString(inArray);
        string text2 = $"{toHexString}.lock";
        string path2 = (string) text2;
        string combine = Path.Combine(
        path1, path2);
        return combine;
    }

    public static async Task<DriveSyncResult> Run(
        IDriveSyncProvider provider,
        string folderName,
        IReadOnlyList<DriveArtifact> artifacts,
        bool apply,
        CancellationToken token,
        string? lockRoot = null,
        Action<(string Id, string Status, object? Item, string Human)>? onOutcome = null)
    {
        var account = await provider.GetAccount(token);
        var folder = await provider.FindFolder(folderName, token);
        string CalculatePath1()
        {
            string path1 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return path1;
        }

        await using var lease = apply
                    ? await LocalFileLock.Acquire(
                        LockPath(
                            root: lockRoot
                            ?? Path.Combine(
                                CalculatePath1(),
                                "ScheduleLib",
                                "remote-locks",
                                "drive"),
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
        var local = new Dictionary<string, DriveArtifact>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts.Where(x => IsScheduleArtifact(x.Name)))
        {
            local.TryAdd(artifact.Name, artifact);
        }

        var cloudNames = remote.Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actions = new List<DriveAction>();
        // Preserve legacy matching: every duplicate remote name is updated, unmatched remote files are deleted.
        actions.AddRange(
            remote.Where(x => !local.ContainsKey(x.Name))
                .Select(x => new DriveAction(
                Action: "delete",
                Name: x.Name,
                FileId: x.Id,
                LocalPath: null)));
        actions.AddRange(
            local.Values.Where(x => !cloudNames.Contains(x.Name))
                .Select(x => new DriveAction(
                Action: "create",
                Name: x.Name,
                FileId: null,
                LocalPath: x.Path)));
        actions.AddRange(
            remote.Where(x => local.ContainsKey(x.Name))
                .Select(x => new DriveAction(
                Action: "update",
                Name: x.Name,
                FileId: x.Id,
                LocalPath: local[x.Name].Path)));
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
                onOutcome?.Invoke((Id: id, Status: outcome.State, Item: outcome, Human: human));
            }
            catch (CliOutputStreamingException ex)
            {
                throw StreamingFailure(ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                global::ScheduleLib.Cli.GeneralCliOutputStreamingException inner = new GeneralCliOutputStreamingException("Drive output streaming failed.", ex);
                global::System.Exception inner2 = inner;
                throw StreamingFailure(inner2);
            }
        }

        DriveStreamingException StreamingFailure(Exception inner)
        {
            int exit;
            if (outcomes.Any(x => x.State == "completed"))
            {
                exit = 6;
            }
            else
            {
                exit = 1;
            }

            string[] errors = [
                                    "Output streaming failed. Completed actions remain applied; inspect Drive before another apply. No action was retried.",
                    ];
            global::ScheduleLib.Cli.DriveSyncResult partialResult = Result(
            exit, errors);
            global::System.Exception inner2 = inner;
            global::ScheduleLib.Cli.DriveStreamingException driveStreamingException = new(
            partialResult, inner2);
            return driveStreamingException;
        }

        void ReportPlanned(int index, DriveAction action)
        {
            var planned = new DriveOutcome(
                action,
                State: "planned",
                ResultFileId: null);
            string id2 = PlanId(index);
            global::ScheduleLib.Cli.DriveOutcome outcome = planned;
            string human2 = $"{action.Action} {action.Name}: {planned.State}";
            Report(
                id: id2, outcome, human: human2);
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
            string id2 = PlanId(index);
            global::ScheduleLib.Cli.DriveOutcome outcome2 = outcome;
            string human2 = $"{outcome.Action.Action} {outcome.Action.Name}: {outcome.State}; id {outcome.ResultFileId}";
            Report(
                id: id2, outcome2, human: human2);
        }

        token.ThrowIfCancellationRequested();
        // The full plan streams before any mutation in both modes; the final
        // summary keeps counts only and never repeats these records.
        for (var index = 0; index < actions.Count; index++)
        {
            ReportPlanned(index, actions[index]);
        }

        if (!apply)
        {
            return Result(0, []);
        }

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
                    global::ScheduleLib.Cli.DriveOutcome outcome3 = new(
                        action,
                        State: "completed",
                        ResultFileId: action.FileId);
                    AddOutcome(outcome3);
                }
                else
                {
                    await using var input = File.OpenRead(action.LocalPath!);
                    token.ThrowIfCancellationRequested();
                    attempted = true;
                    var id = action.Action == "create"
                        ? await provider.Create(
                            folderId: folder.Id,
                            name: action.Name,
                            input,
                            token)
                        : await provider.Update(
                            action.FileId!,
                            input,
                            token);
                    global::ScheduleLib.Cli.DriveOutcome outcome4 = new(
                        action,
                        State: "completed",
                        ResultFileId: id);
                    AddOutcome(outcome4);
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
                global::ScheduleLib.Cli.DriveAction action2 = action;
                string state;
                if (attempted)
                {
                    state = "uncertain";
                }
                else
                {
                    state = "not-attempted";
                }

                string? resultFileId = null;
                global::ScheduleLib.Cli.DriveOutcome outcome7 = new(
                    action2, State: state, ResultFileId: resultFileId);
                AddOutcome(outcome7);
                AddRemaining();
                return Result(
                    130,
                    [
                        "Cancelled. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried.",
                    ]);
            }
            catch (GooglePreSendTransportException)
            {
                global::ScheduleLib.Cli.DriveOutcome outcome5 = new(
                    action,
                    State: "failed",
                    ResultFileId: null);
                AddOutcome(outcome5);
                AddRemaining();
                int exit2;
                if (outcomes.Any(x => x.State == "completed"))
                {
                    exit2 = 6;
                }
                else
                {
                    exit2 = 5;
                }

                string[] errors2 = [
                                        "Google credential refresh transport failed; the action was not sent. Completed actions remain applied.",
                    ];
                global::ScheduleLib.Cli.DriveSyncResult result = Result(
                exit2, errors2);
                return result;
            }
            catch (Google.GoogleApiException e) when ((int) e.HttpStatusCode is 400
                                                      or 401
                                                      or 403
                                                      or 404
                                                      or 409
                                                      or 412
                                                      or 422)
            {
                global::ScheduleLib.Cli.DriveOutcome outcome6 = new(
                    action,
                    State: "failed",
                    ResultFileId: null);
                AddOutcome(outcome6);
                AddRemaining();
                int exit3;
                if (outcomes.Any(x => x.State == "completed"))
                {
                    exit3 = 6;
                }
                else
                {
                    int int32 = (int) e.HttpStatusCode;
                    int int322;
                    if (int32 == 401)
                    {
                        int322 = 4;
                    }
                    else
                    {
                        int322 = 5;
                    }

                    exit3 = int322;
                }

                string[] errors3 = ["Drive rejected the action. Completed actions remain applied; no action was retried."];
                global::ScheduleLib.Cli.DriveSyncResult result2 = Result(
                exit3, errors3);
                return result2;
            }
            catch (Exception)
            {
                global::ScheduleLib.Cli.DriveAction action3 = action;
                string state2;
                if (attempted)
                {
                    state2 = "uncertain";
                }
                else
                {
                    state2 = "failed";
                }

                string? resultFileId2 = null;
                global::ScheduleLib.Cli.DriveOutcome outcome8 = new(
                    action3, State: state2, ResultFileId: resultFileId2);
                AddOutcome(outcome8);
                AddRemaining();
                int exit4;
                if (outcomes.Any(x => x.State is "completed" or "uncertain"))
                {
                    exit4 = 6;
                }
                else
                {
                    exit4 = 5;
                }

                string[] errors4 = [
                                        "Drive application failed. An uncertain action may have completed remotely; inspect Drive before another apply. No action was retried.",
                    ];
                global::ScheduleLib.Cli.DriveSyncResult result3 = Result(
                exit4, errors4);
                return result3;
            }
        }
        int exit5;
        if (token.IsCancellationRequested)
        {
            exit5 = 130;
        }
        else
        {
            exit5 = 0;
        }

        string[] errors5;
        if (token.IsCancellationRequested)
        {
            errors5 = ["Cancelled after the reported actions completed."];
        }
        else
        {
            errors5 = [];
        }

        global::ScheduleLib.Cli.DriveSyncResult result4 = Result(
        exit5, errors5);
        return result4;

        void AddRemaining()
        {
            foreach (var action in actions.Skip(outcomes.Count))
            {
                global::ScheduleLib.Cli.DriveOutcome outcome = new(
                    action,
                    State: "not-attempted",
                    ResultFileId: null);
                AddOutcome(outcome);
            }
        }

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
