namespace OpenNV.Runtime.Content;

internal sealed record FalloutTerminalClosedStageFailure(int Statement, int StatementCount,
    FalloutQuestStageDriverFailure StageFailure);
internal sealed record FalloutTerminalSavedReceipt(FalloutFormKey Page, string PageHash, int Entry,
    string FragmentHash, long Generation, FalloutTerminalSelectionState State, string? Error,
    FalloutTerminalClosedStageFailure? ClosedStageFailure, FalloutScriptResultReceipt? ResultReceipt = null);
internal sealed record FalloutTerminalClosedSnapshot(FalloutFormKey Reference, string ReferenceHash,
    FalloutFormKey Root, string RootHash, FalloutFormKey Page, string PageHash, long Generation,
    FalloutTerminalSavedReceipt? Receipt);

internal sealed partial class FalloutTerminalMenu
{
    private Exception? _closedResultException;
    private FalloutTerminalClosedStageFailure? _closedStageFailure;

    internal string? BlockingError => _presentationError ??
        (LastReceipt is { State: FalloutTerminalSelectionState.Failed } && _closedStageFailure is null ? Error : null);

    internal void BindClosedStageFailure(FalloutTerminalSelection selection, Exception error,
        int statement, int statementCount, FalloutQuestStageDriverFailure failure)
    {
        if (!_executing || LastReceipt is not { State: FalloutTerminalSelectionState.Executing } receipt ||
            !ReferenceEquals(receipt.Selection, selection) || error.Message != failure.Error)
            throw new InvalidOperationException("Closed terminal stage failure has no current source attempt.");
        var closure = new FalloutTerminalClosedStageFailure(statement, statementCount, failure);
        ValidateStatement(_records, Reference, selection.Entry, closure);
        _closedResultException = error;
        _closedStageFailure = closure;
    }

    internal FalloutTerminalClosedSnapshot CaptureClosed(IReadOnlyList<FalloutQuestStageResultSnapshot> stageResults)
    {
        RequireSaveable();
        ValidateWinningScope(_records, Reference, _referenceHash, RootPage.Record.FormKey, RootPage.SourceHash,
            CurrentPage.Record.FormKey, CurrentPage.SourceHash);
        var receipt = LastReceipt;
        if (_closedStageFailure is { } failure) FalloutQuestStages.ValidateDriverFailure(stageResults, failure.StageFailure);
        var saved = new FalloutTerminalClosedSnapshot(Reference, _referenceHash, RootPage.Record.FormKey, RootPage.SourceHash,
            CurrentPage.Record.FormKey, CurrentPage.SourceHash, Generation,
            receipt is null ? null : new(receipt.Selection.Page.Record.FormKey, receipt.Selection.Page.SourceHash,
                receipt.Selection.Entry.Index, receipt.Selection.Entry.Program.Identity, receipt.Selection.Generation,
                receipt.State, receipt.Error, _closedStageFailure, receipt.ResultReceipt));
        ValidateClosed(_records, [saved], stageResults);
        return saved;
    }

    private FalloutTerminalMenu(FalloutPluginStack records, FalloutTerminalClosedSnapshot saved,
        Action<FalloutFormKey> requireAdmission, Func<FalloutFormKey, FalloutCondition, float> evaluateCondition,
        Action<FalloutTerminalSelection> executeSelection)
    {
        _records = records; _requireAdmission = requireAdmission; _evaluateCondition = evaluateCondition;
        _executeSelection = executeSelection; Reference = saved.Reference; _referenceHash = saved.ReferenceHash;
        RootPage = FalloutTerminal.Read(records, saved.Root); CurrentPage = FalloutTerminal.Read(records, saved.Page);
        Generation = saved.Generation; Active = false;
        if (saved.Receipt is { } receipt)
        {
            var page = FalloutTerminal.Read(records, receipt.Page);
            LastReceipt = new(new(Reference, page, page.Entries.Single(entry => entry.Index == receipt.Entry), receipt.Generation),
                receipt.State, receipt.Error, receipt.ResultReceipt);
            _closedStageFailure = receipt.ClosedStageFailure;
        }
    }

    internal static FalloutTerminalMenu RestoreClosed(FalloutPluginStack records, FalloutTerminalClosedSnapshot saved,
        IReadOnlyList<FalloutQuestStageResultSnapshot> stageResults, Action<FalloutFormKey> requireAdmission,
        Func<FalloutFormKey, FalloutCondition, float> evaluateCondition, Action<FalloutTerminalSelection> executeSelection)
    {
        ValidateClosed(records, [saved], stageResults);
        // A closed owner restores no visible rows and executes no predicate or result.
        return new(records, saved, requireAdmission, evaluateCondition, executeSelection);
    }

    internal static void ValidateClosed(FalloutPluginStack records, IReadOnlyList<FalloutTerminalClosedSnapshot> saved,
        IReadOnlyList<FalloutQuestStageResultSnapshot> stageResults)
    {
        ValidateClosedShape(saved);
        foreach (var item in saved)
        {
            ValidateWinningScope(records, item.Reference, item.ReferenceHash, item.Root, item.RootHash, item.Page, item.PageHash);
            if (item.Receipt is not { } receipt) continue;
            var page = FalloutTerminal.Read(records, receipt.Page);
            var entry = page.Entries.SingleOrDefault(entry => entry.Index == receipt.Entry);
            if (entry is null || page.SourceHash != receipt.PageHash || entry.Program.Identity != receipt.FragmentHash)
                throw new InvalidDataException("Saved terminal result differs from its winning page/fragment.");
            if (receipt.State == FalloutTerminalSelectionState.Succeeded)
                (receipt.ResultReceipt ?? throw new InvalidDataException("Saved terminal selection has no completed result owner."))
                    .Require(entry.Program.Scope, item.Reference);
            else if (receipt.ResultReceipt is not null)
                throw new InvalidDataException("A failed terminal selection contains a completed result owner.");
            RequireReachablePage(records, item.Root, receipt.Page);
            if (receipt.ClosedStageFailure is { } failure)
            {
                ValidateStatement(records, item.Reference, entry, failure);
                FalloutQuestStages.ValidateDriverFailure(stageResults, failure.StageFailure);
            }
        }
    }

    internal static void ValidateClosedShape(IReadOnlyList<FalloutTerminalClosedSnapshot> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        static bool Key(FalloutFormKey key) => !string.IsNullOrWhiteSpace(key.OwnerPlugin) && key.ObjectId != 0;
        static bool Hash(string hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);
        var seen = new HashSet<FalloutFormKey>();
        foreach (var item in saved)
        {
            if (item is null || !Key(item.Reference) || !seen.Add(item.Reference) || !Key(item.Root) || !Key(item.Page) ||
                !Hash(item.ReferenceHash) || !Hash(item.RootHash) || !Hash(item.PageHash) || item.Generation <= 0)
                throw new InvalidDataException("Saved closed terminal scope is malformed or duplicated.");
            if (item.Receipt is not { } receipt) continue;
            if (!Key(receipt.Page) || !Hash(receipt.PageHash) || !Hash(receipt.FragmentHash) || receipt.Entry < 0 ||
                receipt.Generation <= 0 || receipt.Generation >= item.Generation ||
                receipt.State is not (FalloutTerminalSelectionState.Succeeded or FalloutTerminalSelectionState.Failed) ||
                (receipt.State == FalloutTerminalSelectionState.Succeeded) != (receipt.Error is null) ||
                receipt.State == FalloutTerminalSelectionState.Failed && (string.IsNullOrWhiteSpace(receipt.Error) ||
                    item.Page != receipt.Page || item.PageHash != receipt.PageHash ||
                    receipt.ClosedStageFailure is null || receipt.ClosedStageFailure.StageFailure is null ||
                    receipt.ClosedStageFailure.StageFailure.Error != receipt.Error) ||
                receipt.State == FalloutTerminalSelectionState.Succeeded && receipt.ClosedStageFailure is not null)
                throw new InvalidDataException("Saved terminal result has no closed consumed source receipt.");
            if (receipt.ClosedStageFailure is { } failure && (failure.Statement < 0 || failure.StatementCount <= failure.Statement ||
                !Key(failure.StageFailure.Quest) || failure.StageFailure.Stage < 0 || string.IsNullOrWhiteSpace(failure.StageFailure.Error)))
                throw new InvalidDataException("Saved terminal stage cause has no consumed source instruction.");
        }
    }

    private static void ValidateStatement(FalloutPluginStack records, FalloutFormKey reference,
        FalloutTerminalEntry entry, FalloutTerminalClosedStageFailure failure)
    {
        entry.Program.RequireSourceExecution();
        var program = FalloutGameModeProgram.Read("begin Result\n" + entry.Program.Source + "\nend", "Result");
        var site = program.CommandSites("setstage").SingleOrDefault(site => site.Statement == failure.Statement);
        if (failure.Statement < 0 || failure.Statement >= program.StatementCount || failure.StatementCount != program.StatementCount ||
            site.Arguments is null || site.Arguments.Count != 2)
            throw new InvalidDataException("Closed terminal failure has no exact source SetStage instruction.");
        var bindings = new FalloutScriptBindings(records, records.GetEffective(reference), entry.Program.Terminal, entry.Program.Fields);
        // This receipt owns a closed fixed-operand invocation. Dynamic operands
        // require their own retained evaluated values, never fresh evaluation.
        if (bindings.Form(site.Arguments[0]).FormKey != failure.StageFailure.Quest ||
            !short.TryParse(site.Arguments[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var stage) || stage != failure.StageFailure.Stage)
            throw new InvalidDataException("Closed terminal stage cause differs from its source operands.");
    }

    private static void RequireReachablePage(FalloutPluginStack records, FalloutFormKey root, FalloutFormKey target)
    {
        var seen = new HashSet<FalloutFormKey>();
        var pending = new Queue<FalloutFormKey>(); pending.Enqueue(root);
        while (pending.TryDequeue(out var current))
        {
            if (!seen.Add(current)) continue;
            if (current == target) return;
            foreach (var entry in FalloutTerminal.Read(records, current).Entries)
                if (entry.Submenu is { } submenu) pending.Enqueue(submenu);
        }
        throw new InvalidDataException("Saved terminal page is outside its authored menu graph.");
    }

    private static void ValidateWinningScope(FalloutPluginStack records, FalloutFormKey reference, string referenceHash,
        FalloutFormKey root, string rootHash, FalloutFormKey page, string pageHash)
    {
        var placed = records.GetEffective(reference);
        if (placed.Signature != "REFR" || placed.IsDeleted || ReferenceHash(placed) != referenceHash ||
            FalloutDialogueTopic.RequiredForm(placed, "NAME") != root ||
            FalloutTerminal.Read(records, root).SourceHash != rootHash || FalloutTerminal.Read(records, page).SourceHash != pageHash)
            throw new InvalidDataException("Saved terminal differs from its winning placed reference/menu scope.");
        if (records.GetEffective(root).IsDeleted || records.GetEffective(page).IsDeleted)
            throw new InvalidDataException("Saved terminal page is deleted.");
        RequireReachablePage(records, root, page);
    }
}
