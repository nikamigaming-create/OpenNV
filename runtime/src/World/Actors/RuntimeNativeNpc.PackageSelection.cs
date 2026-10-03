using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private sealed record PackageSelection(FalloutPluginRecord? Record, FalloutScriptPackage? Declaration);
    private PackageSelection? _requestedSelection;
    private FalloutFormKey? _selectedSourcePackage;
    private bool _sourceSelectionKnown;
    private string? _selectionCaptureBlocker;

    private PackageSelection SelectSourcePackage()
    {
        var selected = FalloutAiPackages.Select(_aiStack!, Appearance.Npc, EvaluateAiCondition, _templates, _aiClock,
            evaluateRunOn: true, eligible: package => _aiWorld?.PackageEligible(Appearance.Reference!.Value, package,
                _aiClock, _packageEvents?.Active?.Form, _packageEvents?.Done == true) ??
                throw new NotSupportedException("NPC package eligibility has no reference state owner."));
        return new(selected, selected is null ? null : FalloutScriptPackage.Read(selected));
    }

    internal void EvaluatePackages(bool reset)
    {
        if (_aiStack is null || _questState is null)
            throw new NotSupportedException("Actor package commands require the live AI owner.");
        if (_aiReferenceState?.ScriptError is { } scriptError) throw new NotSupportedException(scriptError);
        if (_packageEvents?.Error is { } eventError) throw new NotSupportedException(eventError);
        // EVP evaluates source conditions now. Procedure binding/movement is
        // performed by the ordinary actor frame, as it is for creatures. A
        // later native procedure fault is not the result of this void command.
        // Retain the selected result so random predicates are not drawn twice.
        var selected = SelectSourcePackage();
        if (_aiReferenceState is not null) ClearBindingFailure();
        if (reset) { _aiError = null; _packageIdleError = null; }
        _requestedSelection = selected;
        _selectedSourcePackage = selected.Record?.FormKey; _sourceSelectionKnown = true;
        _aiQuestRevision = -1; _aiPollRemaining = 0;
        BlockSelectionCapture("Actor package selection awaits its native procedure continuation.");
    }

    private void BlockSelectionCapture(string message)
    {
        if (_aiReferenceState is not { } state || state.ProcedureCaptureBlocker is not null &&
            state.ProcedureCaptureBlocker != _selectionCaptureBlocker) return;
        state.ProcedureCaptureBlocker = _selectionCaptureBlocker = message;
    }

    private void ClearSelectionCapture()
    {
        if (_aiReferenceState is { } state && state.ProcedureCaptureBlocker == _selectionCaptureBlocker)
            state.ProcedureCaptureBlocker = null;
        _selectionCaptureBlocker = null;
    }
}
