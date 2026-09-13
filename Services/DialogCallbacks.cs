using MacExplorer.Models;

namespace MacExplorer.Services;

public sealed class DialogCallbacks
{
    public Func<string, string, string, string, Task<bool>> Confirm { get; set; } =
        (_, _, _, _) => Task.FromResult(true);

    public Func<string, Task<ConflictDecision>> Conflict { get; set; } =
        _ => Task.FromResult(ConflictDecision.KeepBoth);

    public Func<string, string, Task> Error { get; set; } =
        (_, _) => Task.CompletedTask;
}
