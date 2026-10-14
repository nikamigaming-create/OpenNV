using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// This validates retained reference/package joins. It does not certify a whole
// constructor, native World reconstruction, playable save or rendered parity.
internal sealed class FalloutReferencePackageOwnerValidation
{
    internal const string ParentHistoryBoundary = "source-cold-World-ParentCELL-field-history-unbound";
    private sealed record PositionBoundary(FalloutReferencePositionValues Values,
        bool Position, bool Mode, bool PersistentCell);
    private sealed record FlagBoundary(uint Flags, bool Available);
    private readonly FalloutPluginStack _records;
    private readonly FalloutProcessCommonSnapshot _common;
    private readonly FalloutReferencePositionSnapshot _position;
    private readonly FalloutReferenceChangeSnapshot _changes;
    private readonly FalloutReferenceFormFileSnapshot _files;
    private readonly FalloutReferenceParentCellSnapshot? _parent;
    private readonly Dictionary<(Guid Process, Guid Factory, Guid Constructor, long Sequence), PositionBoundary> _positions = [];
    private readonly Dictionary<(Guid Process, Guid Factory, Guid Fields, Guid Constructor, long Sequence), FlagBoundary> _flags = [];
    private readonly Dictionary<Guid, FalloutReferenceScriptExtraInitializerPublication> _initializerPublications = [];
    private readonly Dictionary<Guid, FalloutReferenceScriptExtraInitializerPublication> _initializerByReference = [];
    private readonly Dictionary<Guid, FalloutSourcePackageAssignmentCall> _commands = [];
    private readonly Dictionary<Guid, FalloutReferencePositionCall> _positionCalls = [];
    private readonly Dictionary<(Guid Factory, long Changed), FalloutReferenceChangeCall> _changeCalls = [];
    private readonly Dictionary<(Guid Process, Guid Factory, long Changed), FalloutSourceThreadReferenceFlagsRead> _threadReads = [];
    private readonly Dictionary<Guid, List<FalloutReferenceChangeCall>> _packageChanges = [];
    private readonly Dictionary<Guid, List<FalloutReferenceFormFileCall>> _packageFiles = [];
    private FalloutReferenceParentCellValidation? _parentValidation;

    internal FalloutReferencePackageOwnerValidation(FalloutPluginStack records, FalloutProcessCommonSnapshot common,
        FalloutReferencePositionSnapshot position, FalloutReferenceChangeSnapshot changes,
        FalloutSourceThreadReferenceFlagsSnapshot threadFlags, FalloutReferenceFormFileSnapshot formFiles,
        FalloutReferenceParentCellSnapshot? parent, FalloutActorProcessRuntimeSnapshot? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(common);
        _records = records; _common = common; _position = position; _changes = changes; _files = formFiles; _parent = parent;
        if (parent is not null)
        {
            if (runtime is null) throw new NotSupportedException("source-cold-reference-World-process-history-unbound");
            FalloutActorProcessRuntimeState.Validate(runtime);
            if (parent.Process != runtime.CapturedProcess || runtime.Stack != common.Stack || runtime.Contract != common.Contract ||
                runtime.Player != position.Actor.Reference || !runtime.Actors.Any(value => value.Source == position.Actor))
                throw new InvalidDataException("ParentCELL history changed its independent actual Runtime/World process association.");
        }
        FalloutActorProcessCommonState.Validate(common);
        FalloutSourceReferencePositionState.Validate(position, position.Source, common.Stack, position.Actor);
        ValidateOwnedSources(records, position, formFiles);
        FalloutSourceReferenceChangeManager.Validate(changes, position.Source, common.Stack);
        FalloutPlayerThreadContextState.ValidateReferenceChangeFlags(threadFlags, position.Source);
        FalloutReferenceFormFileAssociationState.Validate(formFiles, formFiles.Source, common.Stack, position.Actor);
        if (position.Source.Fields.Controls.Main.EngineSha256 != common.ExecutableSha256 ||
            formFiles.Source.Reference != position.Source || position.Retired || changes.Retired || formFiles.Retired ||
            common.CommandFields is null || common.PackageCommands is null || common.Publications is null)
            throw new InvalidDataException("Reference aggregate changed source owners, original files or current retirement.");
        var lineage = common.Publications.Select(value => value.Process).ToArray();
        foreach (var field in common.CommandFields)
        {
            FalloutActorPackageCommandFields.Validate(field, field.Source, common.Stack, field.Actor, lineage);
            if (field.Actor != position.Actor || field.Source.Identity != position.Source.Fields.Identity ||
                field.ScriptInitialization is not { } initialization)
                throw new NotSupportedException("source-cold-package-reference-initializer-history-unbound");
            BuildFlagIndex(initialization, field, threadFlags);
        }
        foreach (var command in common.PackageCommands)
        {
            FalloutSourcePackageAssignment.Validate(command, common);
            if (!_commands.TryAdd(command.Invocation.SourceCommand, command))
                throw new InvalidDataException("Reference aggregate duplicated an original source package command.");
        }
        BuildPositionIndex();
        foreach (var call in threadFlags.Calls.Where(value => value.Enabled is null && value.WriterBoundary is null && value.Returned))
        {
            var read = new FalloutSourceThreadReferenceFlagsRead(call.Source, call.Process, call.Factory,
                call.Invocation, call.ConsumerFactory, call.Thread, call.Changed, 1, call.After);
            _threadReads.Add((call.Process, call.Factory, call.Changed), read);
        }
        foreach (var call in changes.Calls)
        {
            _changeCalls.Add((call.Factory, call.Changed), call);
            RequireReferenceCaller(call.Invocation);
            if (call.ReferenceFlags is { } flags) RequireFlags(flags, call.Invocation.CommonProcess, call.Invocation.Epoch);
            if (call.ThreadFlags is { } read &&
                (!_threadReads.TryGetValue((read.Process, read.Factory, read.Sequence), out var actual) || actual != read))
                throw new InvalidDataException("Reference change thread read has no identical retained actual getter call.");
            if (call.Invocation.PackageCommand is { } command) Add(_packageChanges, command, call);
        }
        foreach (var call in formFiles.Calls)
        {
            RequireReferenceCaller(call.Invocation);
            if (call.Invocation.PackageCommand is { } command) Add(_packageFiles, command, call);
        }
        foreach (var call in position.Calls)
            if (call.Change is { } child &&
                (!_changeCalls.TryGetValue((child.Factory, child.Changed), out var actual) || !SameChange(actual, child)))
                throw new InvalidDataException("Reference position mask2 receipt has no identical retained manager child.");
        ValidatePackageBorrows();
    }

    internal static void ValidateOwnedSources(FalloutPluginStack records, FalloutReferencePositionSnapshot position,
        FalloutReferenceFormFileSnapshot formFiles)
    {
        FalloutSourceReferencePositionState.ValidateOwnedSources(position, records);
        if (!records.Plugins.Select(value => new FalloutReferenceFormFileDependency(value.Plugin.Name, value.Sha256)).SequenceEqual(formFiles.Files))
            throw new InvalidDataException("Reference form-file history changed the actual original selected plugin dependencies.");
    }

    private static void Add<T>(Dictionary<Guid, List<T>> index, Guid key, T value)
    { if (!index.TryGetValue(key, out var values)) index.Add(key, values = []); values.Add(value); }

    private void BuildFlagIndex(FalloutReferenceScriptExtraInitializerSnapshot initialization,
        FalloutPackageCommandFieldSnapshot fields, FalloutSourceThreadReferenceFlagsSnapshot thread)
    {
        var fieldPublications = fields.Publications.ToDictionary(value => value.Process);
        var threadPublications = thread.Publications.ToDictionary(value => value.Process);
        foreach (var publication in initialization.Publications)
        {
            if (!fieldPublications.TryGetValue(publication.Process, out var field) || field.Factory != publication.FieldsFactory ||
                field.Constructor != publication.Constructor || field.Epoch != publication.Epoch ||
                !threadPublications.TryGetValue(publication.ThreadProcess, out var currentThread) || currentThread.Factory != publication.ThreadFactory ||
                !_initializerPublications.TryAdd(publication.Process, publication) ||
                !_initializerByReference.TryAdd(publication.ThreadProcess, publication))
                throw new InvalidDataException("Reference flags changed their actual Fields/Common/current-thread publications.");
        }
        var events = initialization.Calls.Select(value => (Process: value.Invocation.Process, value.Changed, value.FlagsAfter,
                Boundary: value.InvalidatedWriter is FalloutReferenceScriptExtraWriter.ReferenceFlags or FalloutReferenceScriptExtraWriter.ScriptCallback ? value.WriterBoundary : null))
            .Concat((initialization.ReferenceFlagWrites ?? []).Select(value =>
                (Process: value.Invocation.CommonProcess, value.Changed, FlagsAfter: value.After, Boundary: (string?)null)))
            .OrderBy(value => value.Changed).ToArray();
        var grouped = events.GroupBy(value => value.Process).ToDictionary(value => value.Key, value => value.ToArray());
        uint flags = FalloutReferenceScriptExtraInitializerSource.ConstructorFlags; long sequence = 0; var available = true;
        foreach (var publication in initialization.Publications)
        {
            sequence = publication.PublishedSequence!.Value;
            Store();
            foreach (var value in grouped.GetValueOrDefault(publication.Process) ?? [])
            { sequence = value.Changed; flags = value.FlagsAfter; if (value.Boundary is not null) available = false; Store(); }
            void Store() => _flags.Add((publication.Process, publication.Factory, publication.FieldsFactory,
                publication.Constructor, sequence), new(flags, available));
        }
    }

    private void BuildPositionIndex()
    {
        var groups = _position.Calls.GroupBy(value => value.Invocation.ReferenceProcess).ToDictionary(value => value.Key, value => value.ToArray());
        var values = new FalloutReferencePositionValues(new(0, 0, 0), 0, FalloutReferencePositionSource.InitialScale, null, false);
        long sequence = 0; var xyz = false; var mode = false; var persistent = false;
        var filePublications = _files.Publications.ToDictionary(value => value.Process);
        var changeProcesses = _changes.Publications.Select(value => value.Process).ToHashSet();
        foreach (var publication in _position.Publications)
        {
            if (!_initializerByReference.TryGetValue(publication.Process, out var initializer) || initializer.Constructor != publication.Constructor)
                throw new InvalidDataException("Reference publication has no exact current Script/Common constructor association.");
            if (!filePublications.TryGetValue(publication.Process, out var file) || file.Constructor != publication.Constructor ||
                !changeProcesses.Contains(publication.Process))
                throw new InvalidDataException("Reference/file/change owners changed their independent actual process associations.");
            sequence = publication.PublishedSequence!.Value;
            Store();
            foreach (var call in groups.GetValueOrDefault(publication.Process) ?? [])
            {
                _positionCalls.Add(call.Invocation.Call, call);
                RequireReferenceCaller(call.Invocation);
                if (call.Flags is { } flags) RequireFlags(flags, call.Invocation.CommonProcess, call.Invocation.Epoch);
                values = call.After; sequence = call.Changed;
                if (call.Stores.Any(value => value.Step == FalloutReferencePositionStep.PositionZ)) xyz = true;
                if (call.Stores.Any(value => value.Step == FalloutReferencePositionStep.PositionMode)) mode = true;
                if (call.Stores.Any(value => value.Step == FalloutReferencePositionStep.PersistentCell)) persistent = true;
                if (call.Prefix is { Returned: true }) { mode = true; persistent = true; }
                switch (call.InvalidatedLane)
                {
                    case FalloutReferencePositionLane.Position: xyz = false; break;
                    case FalloutReferencePositionLane.PositionMode: mode = false; break;
                    case FalloutReferencePositionLane.PersistentCell: persistent = false; break;
                }
                Store();
            }
            void Store() => _positions.Add((publication.Process, publication.Factory, publication.Constructor, sequence), new(values, xyz, mode, persistent));
        }
    }

    private FalloutReferenceScriptExtraInitializerPublication RequireReferenceCaller(FalloutReferencePositionInvocation invocation)
    {
        if (!_initializerPublications.TryGetValue(invocation.CommonProcess, out var publication) || publication.Epoch != invocation.Epoch ||
            publication.ThreadProcess != invocation.ReferenceProcess || publication.Constructor != invocation.ReferenceConstructor ||
            invocation.Actor != _position.Actor)
            throw new InvalidDataException("Reference child changed its actual Common, reference process or constructor lifetime.");
        if (invocation.PackageCommand is { } command && (!_commands.TryGetValue(command, out var parent) ||
                parent.Invocation.Actor != invocation.Actor || parent.Invocation.Process != invocation.CommonProcess ||
                parent.Invocation.Epoch != invocation.Epoch || parent.Invocation.ThreadId != invocation.ThreadId))
            throw new InvalidDataException("Reference child has no matching original entered source package command.");
        return publication;
    }

    private FlagBoundary RequireFlags(FalloutReferenceFlagRead read, Guid commonProcess, long epoch)
    {
        if (!_initializerPublications.TryGetValue(commonProcess, out var publication) || read.Process != commonProcess || read.Epoch != epoch ||
            read.Actor != _position.Actor || read.Stack != _common.Stack || read.Source !=
                FalloutReferenceScriptExtraInitializerSource.Reviewed(_position.Source.Fields).Identity ||
            read.Factory != publication.Factory || read.FieldsFactory != publication.FieldsFactory || read.Constructor != publication.Constructor ||
            !_flags.TryGetValue((read.Process, read.Factory, read.FieldsFactory, read.Constructor, read.Sequence), out var boundary) ||
            !boundary.Available || boundary.Flags != read.Flags)
            throw new InvalidDataException("Reference flag borrow has no identical retained merged initializer writer boundary.");
        return boundary;
    }

    private PositionBoundary RequirePositionBoundary(Guid process, Guid factory, Guid constructor, long sequence,
        FalloutPackageCommandFieldInvocation invocation)
    {
        if (!_initializerPublications.TryGetValue(invocation.Process, out var common) || common.ThreadProcess != process ||
            common.Constructor != constructor || common.Epoch != invocation.Epoch ||
            !_positions.TryGetValue((process, factory, constructor, sequence), out var boundary))
            throw new InvalidDataException("Package reference borrow has no identical retained actual field publication/writer boundary.");
        return boundary;
    }

    private void ValidatePackageBorrows()
    {
        var enteredChanges = new HashSet<(Guid Factory, long Changed)>();
        foreach (var fields in _common.CommandFields!)
            foreach (var operation in fields.Operations)
            {
                if (operation.SavedPositionInitialization is { } initialization)
                {
                    if (!_commands.TryGetValue(operation.Invocation.SourceCommand, out var admissionCommand) || admissionCommand.Invocation != operation.Invocation ||
                        !_positionCalls.TryGetValue(initialization.Prefix.Invocation.Call, out var actual) || actual.Prefix is null ||
                        !SamePrefix(actual.Prefix, initialization.Prefix))
                        throw new InvalidDataException("Saved18 admission has no identical actual prior reference prefix and entered package parent.");
                }
                if (operation.SavedPositionCall is not { } saved) continue;
                if (!_commands.TryGetValue(saved.Invocation.SourceCommand, out var command) || command.Invocation != saved.Invocation ||
                    (saved.Returned ? !command.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.SavedPosition) :
                        command.FailedAt != FalloutSourcePackageAssignmentStep.SavedPosition || !command.ConsumerEntered))
                    throw new InvalidDataException("Saved18 receipt changed its actual source parent's entered/returned child.");
                if (saved.PositionRead is { } xyz)
                {
                    var boundary = RequirePositionBoundary(xyz.Process, xyz.Factory, xyz.Constructor, xyz.Sequence, saved.Invocation);
                    if (!boundary.Position || !boundary.Mode || boundary.Values.PositionMode != 0 ||
                        xyz.Mode != boundary.Values.PositionMode || xyz.Position != boundary.Values.Position)
                        throw new InvalidDataException("Saved18 XYZ borrowed stale/unavailable mode or a value absent from reference history.");
                }
                else if (saved.Position is not null)
                    throw new NotSupportedException("source-cold-Saved18-current-reference-position-history-unbound");
                if (saved.ParentRead is { } parent) RequireParent(parent, saved.Invocation);
                else if (saved.ParentCellReturned)
                    throw new NotSupportedException(ParentHistoryBoundary);
                if (saved.WorldspaceRead is { } space) ValidateWorldspace(space, saved.Invocation);
                else if (saved.WorldspaceReturned)
                    throw new NotSupportedException("source-cold-Saved18-current-Worldspace-history-unbound");
                if (!saved.ChangeEntered) continue;
                var children = (_packageChanges.GetValueOrDefault(saved.Invocation.SourceCommand) ?? [])
                    .Where(value => value.Kind == "reference-change" && value.Mask == 0x800).ToArray();
                if (children.Length != 1)
                    throw new NotSupportedException("source-cold-Saved18-mask800-child-entry-history-unbound");
                var child = children[0];
                if (saved.Returned && !child.Returned || !enteredChanges.Add((child.Factory, child.Changed)))
                    throw new InvalidDataException("Saved18 returned an unreturned mask800 child or duplicated its actual caller.");
            }
        foreach (var child in _packageChanges.Values.SelectMany(value => value).Where(value => value.Mask == 0x800))
            if (!enteredChanges.Contains((child.Factory, child.Changed)))
                throw new InvalidDataException("Package mask800 child has no actual entered Saved18 field operation.");
    }

    private void RequireParent(FalloutReferenceParentCellRead read, FalloutPackageCommandFieldInvocation invocation)
    {
        if (_parent is null) throw new NotSupportedException(ParentHistoryBoundary);
        if (_parent.Retired) throw new InvalidDataException("Current ParentCELL history is retired.");
        _parentValidation ??= new(_parent);
        _parentValidation.ValidateRead(read);
        if (!_initializerPublications.TryGetValue(invocation.Process, out var publication) || read.Constructor != publication.Constructor)
            throw new InvalidDataException("ParentCELL borrow changed the actual package scene constructor lifetime.");
    }

    private void ValidateWorldspace(FalloutReferenceWorldspaceRead read, FalloutPackageCommandFieldInvocation invocation)
    {
        RequireParent(read.Parent, invocation);
        var boundary = RequirePositionBoundary(read.Process, read.Factory, read.Constructor, read.Sequence, invocation);
        var cell = read.Parent.Cell;
        if (cell is null)
        {
            var flags = read.Flags ?? throw new InvalidDataException("Null ParentCELL omitted its actual persistent-reference predicate.");
            RequireFlags(flags, invocation.Process, invocation.Epoch);
            var files = (_packageFiles.GetValueOrDefault(invocation.SourceCommand) ?? []).Where(value => value.Writer is null).ToArray();
            if (files.Length != 1 || files[0] is not { Returned: true, Read: { Count: 0, File: null } } ||
                files[0].Invocation.CommonProcess != invocation.Process || files[0].Invocation.ReferenceProcess != read.Process ||
                files[0].Invocation.ReferenceConstructor != read.Constructor)
                throw new NotSupportedException("source-cold-Worldspace-current-form-file-predicate-history-unbound");
            var selected = (flags.Flags & (FalloutReferenceScriptExtraInitializerSource.PlayerPersistenceFlag | 0x4000)) != 0;
            if (read.PersistentLookup != selected || selected && (!boundary.PersistentCell ||
                    read.PersistentPresent != boundary.Values.PersistentCellPresent || read.PersistentCell != boundary.Values.PersistentCell))
                throw new InvalidDataException("Worldspace fallback changed its current flag predicate or actual Extra12 field history.");
            if (selected) cell = boundary.Values.PersistentCellPresent ? boundary.Values.PersistentCell : null;
        }
        else if (read.Flags is not null || read.PersistentLookup)
            throw new InvalidDataException("Current raw ParentCELL did not short circuit persistent-reference children.");
        var expected = cell is { } selectedCell ? FalloutCellSceneReader.ReadDefinition(_records, selectedCell).Worldspace : null;
        if (expected != read.Worldspace)
            throw new InvalidDataException("Worldspace borrow changed the selected actual winning CELL/WRLD association.");
    }

    private static bool SamePrefix(FalloutReferencePositionPrefix left, FalloutReferencePositionPrefix right) =>
        (left with { Dependencies = null }) == (right with { Dependencies = null }) &&
        (left.Dependencies ?? []).SequenceEqual(right.Dependencies ?? []);
    private static bool SameChange(FalloutReferenceChangeCall left, FalloutReferenceChangeCall right)
    {
        var empty = Array.Empty<FalloutReferenceChangeStore>();
        return (left with { Stores = empty }) == (right with { Stores = empty }) && left.Stores.SequenceEqual(right.Stores);
    }
}
