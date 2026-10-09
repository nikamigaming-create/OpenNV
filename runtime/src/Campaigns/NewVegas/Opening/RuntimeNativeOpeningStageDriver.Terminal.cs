using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private readonly Dictionary<FalloutFormKey, FalloutTerminalMenu> _terminalMenus = [];
    private IReadOnlyList<FalloutTerminalClosedSnapshot>? _restoreTerminalResults;

    internal string? TerminalExecutionFault => _terminalMenus.Values.Select(menu => menu.Error)
        .FirstOrDefault(error => error is not null);
    internal string? BlockingTerminalExecutionFault => _terminalMenus.Values.Select(menu => menu.BlockingError)
        .FirstOrDefault(error => error is not null);

    internal string? TerminalSaveBlocker => BlockingTerminalExecutionFault is not null ? "terminal-result" :
        _terminalMenus.Values.Any(menu => menu.Active) ? "terminal-menu" : null;

    private IReadOnlyList<FalloutTerminalClosedSnapshot> CaptureTerminalResults()
    {
        if (TerminalSaveBlocker is { } blocker) throw new NotSupportedException("Saving " + blocker + " requires its source continuation.");
        var stages = CaptureStageResults();
        return _terminalMenus.Values.OrderBy(menu => _pluginStack.RuntimeFormId(menu.Reference))
            .Select(menu => menu.CaptureClosed(stages)).ToArray();
    }

    private void RestoreTerminalResults()
    {
        var saved = _restoreTerminalResults ?? [];
        var stages = _stageResults!.CaptureResults();
        FalloutTerminalMenu.ValidateClosed(_pluginStack, saved, stages);
        var conditions = new FalloutTerminalConditions(_pluginStack, _quests, _scripts.References!,
            target => ReferencePresentation().GetOpenState(target));
        foreach (var item in saved)
            _terminalMenus.Add(item.Reference, FalloutTerminalMenu.RestoreClosed(_pluginStack, item, stages,
                RequireTerminalAdmission, conditions.Evaluate, ExecuteTerminalSelection));
        _restoreTerminalResults = null;
    }

    internal object TerminalState => _terminalMenus.Values
        .Where(menu => menu.Active || menu.Error is not null)
        .OrderBy(menu => _pluginStack.RuntimeFormId(menu.Reference)).Select(menu => new
        {
            reference = menu.Reference.ToString(),
            root = menu.RootPage.Record.FormKey.ToString(),
            page = menu.CurrentPage.Record.FormKey.ToString(),
            sourceHash = menu.CurrentPage.SourceHash,
            generation = menu.Generation,
            active = menu.Active,
            hasResult = menu.HasResult,
            canBack = menu.CanBack,
            displayText = menu.DisplayText,
            displayNote = menu.DisplayNote?.ToString(),
            visibleEntries = menu.VisibleEntries.Select(value => new
            {
                index = value.Entry.Index,
                selectable = value.Selectable,
                error = value.Error
            }).ToArray(),
            receipt = menu.LastReceipt is { } receipt ? new
            {
                reference = receipt.Selection.Reference.ToString(),
                page = receipt.Selection.Page.Record.FormKey.ToString(),
                entry = receipt.Selection.Entry.Index,
                fragment = receipt.Selection.Entry.Program.Identity,
                generation = receipt.Selection.Generation,
                state = receipt.State.ToString(),
                error = receipt.Error
            } : null,
            error = menu.Error,
            blockingError = menu.BlockingError,
            continuationSaving = menu.Active || menu.BlockingError is not null ? "unbound" : "closed-source-result"
        }).ToArray();

    internal FalloutTerminalMenu CreateTerminalMenu(FalloutFormKey reference)
    {
        if (_terminalMenus.TryGetValue(reference, out var previous) && previous.Error is { } failure)
            throw new InvalidOperationException("Terminal source selection stopped: " + failure);
        if (_terminalMenus.TryGetValue(reference, out var existing) && existing.Active) return existing;
        if (_terminalMenus.Values.Any(owner => owner.Active))
            throw new InvalidOperationException("Another terminal menu owns the player interaction.");
        var world = _scripts.References ?? throw new InvalidOperationException("Terminal has no world owner.");
        RequireTerminalReference(reference);
        if (!world.UnlockWithKey(reference, _inventory))
            throw new NotSupportedException("Locked terminal has no available password; hacking is unbound.");
        var conditions = new FalloutTerminalConditions(_pluginStack, _quests, world,
            target => ReferencePresentation().GetOpenState(target));
        var menu = new FalloutTerminalMenu(_pluginStack, reference, RequireTerminalAdmission,
            conditions.Evaluate, ExecuteTerminalSelection);
        _terminalMenus[reference] = menu;
        return menu;
    }

    private void RequireTerminalReference(FalloutFormKey reference)
    {
        var world = _scripts.References ?? throw new InvalidOperationException("Terminal has no world owner.");
        if (!world.IsResident(reference) || !world.CanActivate(reference))
            throw new InvalidOperationException("Terminal is not resident and available.");
        if (_pluginStack.GetEffective(reference).Signature != "REFR" ||
            _pluginStack.GetEffective(world.Get(reference).Base).Signature != "TERM")
            throw new InvalidDataException("Terminal activation has no placed TERM reference.");
    }

    private void RequireTerminalAdmission(FalloutFormKey reference)
    {
        RequireLevelUpMenuFree("Terminal menu");
        RequireTerminalReference(reference);
        if (_scripts.References!.GetLocked(reference) != 0)
            throw new InvalidOperationException("Terminal selection requires its shared access owner to be unlocked.");
    }

    private void ExecuteTerminalSelection(FalloutTerminalSelection selection)
    {
        RequireTerminalAdmission(selection.Reference);
        selection.Entry.RequireSelectionEffects();
        // Missing note presentation is detected before an authored source prefix
        // runs. The shared NOTE reader never substitutes text for other variants.
        if (selection.Entry.Note is { } note) _ = FalloutNote.Read(_pluginStack, note).RequireText();
        var scripts = _resultScripts ?? throw new InvalidOperationException("Terminal results have no shared script owner.");
        try { _terminalMenus[selection.Reference].BindResult(selection, scripts.ExecuteTerminalResult(selection)); }
        catch (Exception error)
        {
            if (_stageResults?.ClosedFailureFor(error) is { } stageFailure &&
                scripts.TerminalFailureFor(selection, error) is { } terminalFailure)
            {
                try
                {
                    _terminalMenus[selection.Reference].BindClosedStageFailure(selection, error,
                        terminalFailure.Statement, terminalFailure.StatementCount, stageFailure);
                }
                catch (Exception admission) when (admission is InvalidDataException or InvalidOperationException or
                    NotSupportedException or KeyNotFoundException or OverflowException)
                {
                    // Preserve the original source exception and consumed
                    // attempt. An unowned closure remains a visible blocker.
                    _terminalMenus[selection.Reference].ReportPresentationFailure(new InvalidOperationException(
                        "Terminal closed-result continuation refused: " + admission.Message, admission));
                }
            }
            throw;
        }
        if (selection.Entry.AddNote && selection.Entry.Note is { } added)
            ApplyReferenceEffect(new(FalloutReferenceEffectKind.AddNote, selection.Reference, added));
    }
}
