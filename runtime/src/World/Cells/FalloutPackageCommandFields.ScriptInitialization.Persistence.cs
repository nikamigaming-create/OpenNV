using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    private static IReadOnlyDictionary<Guid, FalloutReferenceScriptExtraInitializationCall> ValidateScriptInitializationHistory(
        FalloutPackageCommandFieldSnapshot saved, FalloutPackageCommandFieldsSource source, string stack, FalloutCombatActorIdentity actor)
    {
        var operations = saved.ScriptInitializationOperations ?? [];
        if (saved.Operations.Any(value => value is null) || operations.Any(value => value is null))
            throw new InvalidDataException("Script/command field history contains a missing original operation.");
        if (saved.ScriptInitialization is not { } initialization)
        {
            if (operations.Count != 0) throw new InvalidDataException("Script field operations omitted their actual initializer source history.");
            return new Dictionary<Guid, FalloutReferenceScriptExtraInitializationCall>();
        }
        var initializerSource = FalloutReferenceScriptExtraInitializerSource.Reviewed(source);
        FalloutReferenceScriptExtraInitializer.Validate(initialization, initializerSource, stack, actor);
        var fields = saved.Publications.ToDictionary(value => value.Process);
        foreach (var publication in initialization.Publications)
            if (!fields.TryGetValue(publication.Process, out var field) || publication.FieldsFactory != field.Factory ||
                publication.Constructor != field.Constructor || publication.Epoch != field.Epoch)
                throw new InvalidDataException("Script initializer history selected another current or cold reference field publication.");
        var calls = initialization.Calls.ToDictionary(value => value.Invocation.Initialization);
        var grouped = operations.GroupBy(value => value.Invocation.Initialization).ToDictionary(group => group.Key, group => group.ToArray());
        if (grouped.Keys.Any(key => !calls.ContainsKey(key)))
            throw new InvalidDataException("Script field operation fabricated an initializer invocation.");
        foreach (var call in initialization.Calls)
        {
            var selected = grouped.GetValueOrDefault(call.Invocation.Initialization) ?? [];
            var expected = new List<FalloutScriptExtraInitializationFieldOperationKind>();
            if (call.InvalidatedWriter is not null) expected.Add(FalloutScriptExtraInitializationFieldOperationKind.UnownedWriter);
            else
            {
                expected.Add(FalloutScriptExtraInitializationFieldOperationKind.Begin);
                if ((call.FlagsBefore & FalloutReferenceScriptExtraInitializerSource.InitializedFlag) == 0 &&
                    (call.FlagsAfter & FalloutReferenceScriptExtraInitializerSource.ScriptSuppressionFlag) == 0)
                {
                    expected.Add(FalloutScriptExtraInitializationFieldOperationKind.Read);
                    if (call.Selected?.Script is not null && call.Before?.Script is null)
                        expected.Add(FalloutScriptExtraInitializationFieldOperationKind.Attach);
                }
                expected.Add(FalloutScriptExtraInitializationFieldOperationKind.Admit);
            }
            if (selected.Length > expected.Count || !selected.Select(value => value.Kind).SequenceEqual(expected.Take(selected.Length)) ||
                call.Returned && selected.Length != expected.Count || selected.Any(value => !value.Returned) &&
                (call.Returned || selected.Take(selected.Length - 1).Any(value => !value.Returned)) ||
                !call.Stores.SequenceEqual(selected.SelectMany(value => value.Stores)))
                throw new InvalidDataException("Script field history lost independent read/attachment/admission receipts or a committed failure prefix.");
            var read = selected.SingleOrDefault(value => value.Kind == FalloutScriptExtraInitializationFieldOperationKind.Read);
            if (read is { Returned: true } && !EquivalentExtra(read.Before, call.Before) ||
                call.Reads.Any(value => value.Step == FalloutReferenceScriptExtraInitializationStep.ExistingExtra) && read is not { Returned: true } ||
                selected.Any(value => value.Kind == FalloutScriptExtraInitializationFieldOperationKind.Admit && value.Returned) && !call.Returned)
                throw new InvalidDataException("Script initializer borrowed existing membership or admitted an unreturned source branch.");
        }
        return calls;
    }

    private static void ValidateScriptInitializationOperation(FalloutScriptExtraInitializationFieldOperation operation,
        IReadOnlyDictionary<Guid, FalloutReferenceScriptExtraInitializationCall> calls, ref FalloutPackageCommandFieldValues values,
        IDictionary<FalloutPackageCommandFieldLane, FalloutPackageCommandFieldAvailability> available, ref long sequence,
        IDictionary<Guid, int> sourceThreads, IReadOnlyDictionary<Guid, int> publicationOrder, ref int lastPublication)
    {
        if (operation.Invocation is not { } invocation || !calls.TryGetValue(invocation.Initialization, out var call) ||
            operation.Invocation != call.Invocation || operation.Source != call.Source.Identity || operation.InitializerFactory != call.Factory ||
            operation.FieldsFactory != call.FieldsFactory || !Enum.IsDefined(operation.Kind) || operation.Entered != ++sequence ||
            operation.Stores is null || operation.Returned != (operation.Failure is null) || operation.Failure is { Length: 0 } ||
            !EquivalentExtra(operation.Before, values.ScriptExtra))
            throw new InvalidDataException("Script field operation changed its exact narrow capability or entered canonical field state.");
        var currentPublication = publicationOrder[invocation.Process];
        if (currentPublication < lastPublication || sourceThreads.TryGetValue(invocation.Process, out var thread) && thread != invocation.ThreadId)
            throw new InvalidDataException("Script field history returned to an old reference process or changed its current source thread.");
        lastPublication = currentPublication; sourceThreads.TryAdd(invocation.Process, invocation.ThreadId);
        FalloutPackageCommandField[] expected = operation.Kind switch
        {
            FalloutScriptExtraInitializationFieldOperationKind.Attach => operation.Before is null ?
                [FalloutPackageCommandField.ScriptConstruction, FalloutPackageCommandField.ScriptForm,
                    FalloutPackageCommandField.ScriptConstructorEventList, FalloutPackageCommandField.ScriptPresence] :
                [FalloutPackageCommandField.ScriptForm],
            FalloutScriptExtraInitializationFieldOperationKind.EventListPublication =>
                throw new InvalidDataException("Script list publication has no returned independent event-list/local-variable factory."),
            _ => []
        };
        if (operation.Stores.Count > expected.Length || !operation.Stores.Select(value => value.Field).SequenceEqual(expected.Take(operation.Stores.Count)) ||
            operation.Returned && operation.Stores.Count != expected.Length || operation.EventList is not null || operation.EventFactory is not null ||
            operation.Rows is not null || operation.Kind != FalloutScriptExtraInitializationFieldOperationKind.Attach && operation.Script is not null ||
            operation.Kind == FalloutScriptExtraInitializationFieldOperationKind.Attach &&
            (operation.Returned || operation.Stores.Count != 0) && (operation.Script is null || operation.Script != call.Selected?.Script) ||
            operation.Stores.Where(value => value.Field == FalloutPackageCommandField.ScriptForm).Any(value => value.Form != operation.Script))
            throw new InvalidDataException("Script initializer field receipt borrowed an unrelated store or event-list publication.");
        foreach (var store in operation.Stores)
        {
            if (store is null || store.Changed != ++sequence)
                throw new InvalidDataException("Script field stores omitted or reordered their exact committed prefix.");
            values = ApplyStore(values, store, null, null);
        }
        if (operation.Changed != ++sequence || !EquivalentExtra(operation.After, values.ScriptExtra))
            throw new InvalidDataException("Script field operation erased its original attached Extra or changed the completed prefix extent.");
        if (operation.Kind == FalloutScriptExtraInitializationFieldOperationKind.Begin)
        {
            if (operation.Boundary is not null && operation.Boundary != FalloutReferenceScriptExtraInitializer.EnteredBoundary ||
                operation.Returned && operation.Boundary is null)
                throw new InvalidDataException("Script initialization began without its independent unavailable lane.");
            if (operation.Boundary is { } boundary)
                available[FalloutPackageCommandFieldLane.ScriptExtra] = new(FalloutPackageCommandFieldLane.ScriptExtra, false, boundary);
        }
        else if (operation.Kind == FalloutScriptExtraInitializationFieldOperationKind.UnownedWriter)
        {
            if (operation.Boundary is { } boundary)
            {
                if (boundary != call.WriterBoundary || string.IsNullOrWhiteSpace(boundary) || call.InvalidatedWriter is null)
                    throw new InvalidDataException("Unowned Script field observation hid its exact source writer.");
                available[FalloutPackageCommandFieldLane.ScriptExtra] = new(FalloutPackageCommandFieldLane.ScriptExtra, false, boundary);
            }
            else if (operation.Returned) throw new InvalidDataException("Returned Script writer observation did not invalidate its current lane.");
        }
        else if (operation.Boundary is not null)
            throw new InvalidDataException("Script field receipt borrowed another initialization/writer boundary.");
        if (operation.Kind == FalloutScriptExtraInitializationFieldOperationKind.Admit && operation.Returned)
        {
            if (!call.Returned || call.Step != FalloutReferenceScriptExtraInitializationStep.Returned || call.InvalidatedWriter is not null)
                throw new InvalidDataException("Script lane admission promoted an unreturned reference initializer.");
            available[FalloutPackageCommandFieldLane.ScriptExtra] = new(FalloutPackageCommandFieldLane.ScriptExtra, true, null);
        }
    }
}
