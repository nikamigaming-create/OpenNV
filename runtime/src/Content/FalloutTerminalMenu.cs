using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutTerminalVisibleEntry(FalloutTerminalEntry Entry, bool Selectable, string? Error);
internal sealed record FalloutTerminalSelection(FalloutFormKey Reference, FalloutTerminal Page,
    FalloutTerminalEntry Entry, long Generation);
internal enum FalloutTerminalSelectionState { Executing, Succeeded, Failed }
internal sealed record FalloutTerminalSelectionReceipt(FalloutTerminalSelection Selection,
    FalloutTerminalSelectionState State, string? Error = null);

// The source menu owns selection, not the native row or its visible position.
// Its host admits the real placed reference and applies all authoritative
// selection effects inside one retained attempt. Presentation cannot replay it.
internal sealed class FalloutTerminalMenu
{
    private readonly FalloutPluginStack _records;
    private readonly Action<FalloutFormKey> _requireAdmission;
    private readonly Func<FalloutFormKey, FalloutCondition, float> _evaluateCondition;
    private readonly Action<FalloutTerminalSelection> _executeSelection;
    private readonly string _referenceHash;
    private readonly Stack<FalloutTerminal> _parents = [];
    private IReadOnlyList<FalloutTerminalVisibleEntry> _visible = [];
    private FalloutTerminalEntry? _result;
    private bool _executing;
    private string? _presentationError;

    internal FalloutFormKey Reference { get; }
    internal FalloutTerminal RootPage { get; }
    internal FalloutTerminal CurrentPage { get; private set; }
    internal long Generation { get; private set; }
    internal bool Active { get; private set; }
    internal bool HasResult => _result is not null;
    internal bool CanBack => HasResult || _parents.Count != 0;
    internal string DisplayText { get; private set; } = "";
    internal FalloutFormKey? DisplayNote { get; private set; }
    internal IReadOnlyList<FalloutTerminalVisibleEntry> VisibleEntries => _visible;
    internal string? Error => _presentationError ??
        (LastReceipt is { State: FalloutTerminalSelectionState.Failed } receipt ? receipt.Error : null);
    internal FalloutTerminalSelectionReceipt? LastReceipt { get; private set; }

    internal FalloutTerminalMenu(FalloutPluginStack records, FalloutFormKey reference,
        Action<FalloutFormKey> requireAdmission,
        Func<FalloutFormKey, FalloutCondition, float> evaluateCondition,
        Action<FalloutTerminalSelection> executeSelection)
    {
        _records = records;
        _requireAdmission = requireAdmission ?? throw new ArgumentNullException(nameof(requireAdmission));
        _evaluateCondition = evaluateCondition ?? throw new ArgumentNullException(nameof(evaluateCondition));
        _executeSelection = executeSelection ?? throw new ArgumentNullException(nameof(executeSelection));
        Reference = reference;
        _requireAdmission(reference);
        var placed = records.GetEffective(reference);
        if (placed.Signature != "REFR") throw new InvalidDataException("Terminal menu requires a real placed REFR.");
        _referenceHash = ReferenceHash(placed);
        RootPage = CurrentPage = FalloutTerminal.Read(records, FalloutDialogueTopic.RequiredForm(placed, "NAME"));
        Active = true;
        Refresh();
    }

    internal void Refresh()
    {
        RequireReady(allowResult: true);
        RequireSourceScope();
        _visible = ReadVisibleEntries();
        Generation = checked(Generation + 1);
    }

    private IReadOnlyList<FalloutTerminalVisibleEntry> ReadVisibleEntries()
    {
        var visible = new List<FalloutTerminalVisibleEntry>();
        foreach (var entry in CurrentPage.Entries)
        {
            try
            {
                if (!Eligible(entry)) continue;
                entry.RequireSelectionEffects();
                if (entry.Note is { } note) _ = FalloutNote.Read(_records, note).RequireText();
                visible.Add(new(entry, true, null));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or
                InvalidOperationException or KeyNotFoundException or OverflowException)
            {
                // An unowned predicate is a blocked, observable choice. It
                // cannot disappear as if the source condition had been false.
                visible.Add(new(entry, false, error.Message));
            }
        }
        return visible.ToArray();
    }

    internal FalloutTerminalSelection Select(int sourceIndex, long generation)
    {
        RequireReady();
        if (generation != Generation) throw new InvalidOperationException("Terminal selection has a stale menu generation.");
        RequireSourceScope();
        var visible = _visible.SingleOrDefault(value => value.Entry.Index == sourceIndex)
            ?? throw new InvalidOperationException("Terminal selection is not an available source entry.");
        if (!visible.Selectable) throw new NotSupportedException(visible.Error ?? "Terminal condition is unbound.");
        if (!Eligible(visible.Entry)) throw new InvalidOperationException("Terminal selection no longer meets its source conditions.");
        visible.Entry.RequireSelectionEffects();
        var selected = new FalloutTerminalSelection(Reference, CurrentPage, visible.Entry, Generation);
        var nextGeneration = checked(Generation + 1);
        // Validation and source execution failures retain the same receipt.
        // No row rebuild, duplicate input or close/reopen may retry its prefix.
        LastReceipt = new(selected, FalloutTerminalSelectionState.Executing);
        _executing = true;
        Generation = nextGeneration;
        try
        {
            var submenu = selected.Entry.Submenu is { } form ? FalloutTerminal.Read(_records, form) : null;
            _executeSelection(selected);
            DisplayText = selected.Entry.ResultText;
            DisplayNote = selected.Entry.Note;
            if (submenu is not null)
            {
                _parents.Push(CurrentPage);
                CurrentPage = submenu;
                _result = null;
                RequireSourceScope();
                _visible = ReadVisibleEntries();
            }
            else _result = selected.Entry;
            LastReceipt = new(selected, FalloutTerminalSelectionState.Succeeded);
        }
        catch (Exception error)
        {
            LastReceipt = new(selected, FalloutTerminalSelectionState.Failed, error.Message);
            throw;
        }
        finally { _executing = false; }
        return selected;
    }

    internal bool Back()
    {
        RequireReady(allowResult: true);
        RequireSourceScope();
        if (_result is { } result)
        {
            _result = null;
            DisplayText = "";
            DisplayNote = null;
            if (result.ForceRedraw) Refresh();
            else Generation = checked(Generation + 1);
            return true;
        }
        if (_parents.Count == 0) return false;
        CurrentPage = _parents.Pop();
        DisplayText = "";
        DisplayNote = null;
        Refresh();
        return true;
    }

    internal void Close()
    {
        if (_executing) throw new InvalidOperationException("Terminal source selection is still executing.");
        Active = false;
        _result = null;
        _visible = [];
        _parents.Clear();
        DisplayText = "";
        DisplayNote = null;
        // In particular, a failed receipt is not acknowledged by closing UI.
        Generation = checked(Generation + 1);
    }

    internal void RequireSaveable()
    {
        if (Error is { } error) throw new NotSupportedException("Saving a failed terminal result requires continuation state: " + error);
        if (Active || _executing) throw new NotSupportedException("Saving an active terminal menu requires continuation state.");
    }

    internal void ReportPresentationFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _presentationError ??= error.Message;
    }

    private bool Eligible(FalloutTerminalEntry entry) => FalloutCondition.AllPass(entry.Conditions,
        condition => _evaluateCondition(Reference, condition), evaluateRunOn: true);

    private void RequireReady(bool allowResult = false)
    {
        if (Error is { } error) throw new InvalidOperationException("Terminal source selection stopped: " + error);
        if (!Active || _executing || !allowResult && HasResult)
            throw new InvalidOperationException("Terminal menu is not ready for this operation.");
    }

    private void RequireSourceScope()
    {
        _requireAdmission(Reference);
        var placed = _records.GetEffective(Reference);
        if (placed.Signature != "REFR" || ReferenceHash(placed) != _referenceHash ||
            FalloutDialogueTopic.RequiredForm(placed, "NAME") != RootPage.Record.FormKey ||
            FalloutTerminal.Hash(_records.GetEffective(RootPage.Record.FormKey)) != RootPage.SourceHash ||
            FalloutTerminal.Hash(_records.GetEffective(CurrentPage.Record.FormKey)) != CurrentPage.SourceHash)
            throw new InvalidDataException("Terminal reference/menu source scope changed during its session.");
    }

    private static string ReferenceHash(FalloutPluginRecord record)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(record.ReadData());
        hash.AppendData(Encoding.UTF8.GetBytes(record.FormKey + "\0"));
        foreach (var plugin in record.Plugin.Masters.Append(record.Plugin.Name))
            hash.AppendData(Encoding.UTF8.GetBytes(plugin.ToUpperInvariant() + "\0"));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
