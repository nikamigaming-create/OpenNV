using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativePlayerPackage
{
    private readonly Action<FalloutPackageEvent, Action<FalloutScriptResultReceipt>>? _executeResult;
    private readonly Dictionary<string, FalloutPlayerPackageResultSnapshot> _results = new(StringComparer.Ordinal);
    private readonly HashSet<(FalloutFormKey Package, string Kind)> _executingResults = [];
    private ulong _assignmentRevision;
    private string? _exitAction;
    private string? _failure;

    private void RequireHealthy()
    {
        if (_failure is not null) throw new NotSupportedException("Player package lifecycle failed: " + _failure);
    }

    private void DispatchResult(FalloutScriptPackage package, string kind)
    {
        if (package.EventPrograms.GetValueOrDefault(kind) is not { } program) return;
        var key = (package.Form, kind);
        if (!_executingResults.Add(key))
            throw new NotSupportedException("Player package recursively reentered the same source lifecycle event.");
        var observed = false;
        try
        {
            (_executeResult ?? throw new NotSupportedException("Player package result has no shared execution owner."))(
                program, receipt =>
                {
                    if (observed) throw new InvalidOperationException("Player package result committed twice.");
                    receipt.Require(program.Scope, _stack.RuntimeFormKey(0x14));
                    observed = true;
                    // A source result may replace its own assignment. The retired
                    // caller's receipt must not overwrite the new package's state.
                    if (_package?.Form != package.Form) return;
                    var executions = checked((_results.GetValueOrDefault(kind)?.Executions ?? 0) + 1);
                    _results[kind] = new(kind, executions, receipt);
                });
            if (!observed) throw new InvalidOperationException("Player package result returned without its real execution receipt.");
        }
        finally { _executingResults.Remove(key); }
    }

    private IReadOnlyList<FalloutPlayerPackageResultSnapshot> RestoreResults(FalloutScriptPackage package,
        IReadOnlyList<FalloutPlayerPackageResultSnapshot> saved)
    {
        var result = new List<FalloutPlayerPackageResultSnapshot>();
        foreach (var row in saved)
        {
            var program = package.EventPrograms.GetValueOrDefault(row.Kind) ??
                throw new InvalidDataException("Saved player result has no matching original package event.");
            row.Receipt.Require(program.Scope, _stack.RuntimeFormKey(0x14));
            result.Add(row);
        }
        return result;
    }

    private void RequireSavedEvent(FalloutScriptPackage package, string? kind,
        IReadOnlyList<FalloutPlayerPackageResultSnapshot> results)
    {
        if (kind is null || !package.EventPrograms.ContainsKey(kind)) return;
        var receipt = results.SingleOrDefault(result => result.Kind == kind)?.Receipt ??
            throw new InvalidDataException("Saved player package phase lacks its already committed result.");
        receipt.Require(package.EventPrograms[kind].Scope, _stack.RuntimeFormKey(0x14));
    }

    private void BeginExit(string action)
    {
        var package = _package ?? throw new InvalidOperationException("Player package exit has no active assignment.");
        PrepareEvent(package, "POEA");
        var revision = _assignmentRevision;
        _pendingPackage = null; _pendingPackageHash = null;
        _exitAction = action; _complete = false; _wait = 0;
        DispatchResult(package, "POEA");
        if (_assignmentRevision != revision) return;
        if (package.Events.GetValueOrDefault("POEA") is { } animation) Start(animation, "POEA");
        else FinishExit();
    }

    private void FinishExit()
    {
        var action = _exitAction ?? throw new InvalidDataException("Player package end phase lost its reached action.");
        _exitAction = null; _eventKind = null;
        _animation = null; _playback = null; _idle = null; _elapsed = 0; _wait = 0;
        _player.ReleaseSourceCamera();
        if (action == "complete") { _complete = true; return; }
        if (action != "remove") throw new InvalidDataException("Player package end action is invalid.");
        checked { ++_assignmentRevision; }
        _package = null; _packageHash = null; _cursor = 0; _complete = false;
        _pendingPackage = null; _pendingPackageHash = null; _results.Clear();
        _session.PublishPlayerPackage(null);
    }
}
