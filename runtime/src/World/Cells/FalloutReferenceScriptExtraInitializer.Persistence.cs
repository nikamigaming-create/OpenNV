using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    internal static void Validate(FalloutReferenceScriptExtraInitializerSnapshot saved,
        FalloutReferenceScriptExtraInitializerSource source, string stack, FalloutCombatActorIdentity actor)
    {
        ArgumentNullException.ThrowIfNull(saved); source.Validate(); actor.Validate();
        if (saved.Schema != Schema || saved.Source is null || saved.Source.Identity != source.Identity || saved.Stack != stack ||
            saved.Actor != actor || !actor.EnginePlayer || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.FieldsFactory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Epoch < 1 || saved.Template is not null ||
            saved.Publications is not { Count: > 0 } || saved.Calls is null || saved.WriterBoundary is { Length: 0 })
            throw new InvalidDataException("Script initializer cold state changed its exact source, Player or schema.");
        saved.Source.Validate();
        var processes = new HashSet<Guid>(); var factories = new HashSet<Guid>(); var fieldFactories = new HashSet<Guid>();
        var constructors = new HashSet<Guid>(); var threadProcesses = new HashSet<Guid>(); var threadFactories = new HashSet<Guid>();
        var publications = new Dictionary<Guid, (int Ordinal, FalloutReferenceScriptExtraInitializerPublication Value)>();
        FalloutReferenceScriptExtraInitializerPublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication is null || publication.Process == Guid.Empty || publication.Factory == Guid.Empty || publication.FieldsFactory == Guid.Empty ||
                publication.Constructor == Guid.Empty || publication.ThreadProcess == Guid.Empty || publication.ThreadFactory == Guid.Empty ||
                publication.Epoch != saved.Epoch || !processes.Add(publication.Process) || !factories.Add(publication.Factory) ||
                !fieldFactories.Add(publication.FieldsFactory) || !constructors.Add(publication.Constructor) ||
                !threadProcesses.Add(publication.ThreadProcess) || !threadFactories.Add(publication.ThreadFactory) ||
                (previous is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != previous.Process || publication.PreviousFactory != previous.Factory))
                throw new InvalidDataException("Script initializer lost fresh, independent reference and current-thread factory lineage.");
            publications.Add(publication.Process, (publications.Count, publication)); previous = publication;
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory || previous.FieldsFactory != saved.FieldsFactory ||
            previous.Constructor != saved.Constructor)
            throw new InvalidDataException("Script initializer snapshot omitted its current actual publication.");
        var threadSource = FalloutPlayerThreadContextSource.Reviewed(source.Fields.Controls.Main);
        var ids = new HashSet<Guid>(); var threads = new Dictionary<Guid, int>();
        uint flags = FalloutReferenceScriptExtraInitializerSource.ConstructorFlags; long sequence = 0; var lastPublication = -1;
        string? boundary = null; var priorReturned = false; var failed = false;
        foreach (var call in saved.Calls)
        {
            if (call is null || call.Source is null || call.Source.Identity != source.Identity || call.Invocation is not { } invocation ||
                invocation.Initialization == Guid.Empty || !ids.Add(invocation.Initialization) || invocation.Actor != actor || invocation.Epoch != saved.Epoch ||
                invocation.ThreadId < 1 || !publications.TryGetValue(invocation.Process, out var publication) ||
                invocation.Constructor != publication.Value.Constructor || call.Factory != publication.Value.Factory ||
                call.FieldsFactory != publication.Value.FieldsFactory || call.Entered != ++sequence || call.Changed != ++sequence ||
                !Enum.IsDefined(call.Step) || call.Reads is null || call.Stores is null || call.FlagsBefore != flags || call.Template is not null ||
                call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } || publication.Ordinal < lastPublication ||
                threads.TryGetValue(invocation.Process, out var thread) && thread != invocation.ThreadId)
                throw new InvalidDataException("Script initializer history omitted or replaced an original caller or failed prefix.");
            call.Source.Validate(); lastPublication = publication.Ordinal; threads.TryAdd(invocation.Process, invocation.ThreadId);
            if (call.InvalidatedWriter is { } writer)
            {
                if (!Enum.IsDefined(writer) || string.IsNullOrWhiteSpace(call.WriterBoundary) ||
                    call.Step != FalloutReferenceScriptExtraInitializationStep.UnownedWriter || call.Reads.Count != 0 || call.Stores.Count != 0 ||
                    call.ThreadScope is not null || call.Selected is not null || call.Before is not null || call.FlagsAfter != flags)
                    throw new InvalidDataException("Script Extra observation invented a writer return or unrelated state.");
                boundary = call.WriterBoundary;
            }
            else
            {
                if (call.WriterBoundary is not null || call.Step == FalloutReferenceScriptExtraInitializationStep.UnownedWriter)
                    throw new InvalidDataException("Script initializer omitted its actual unowned writer kind.");
                ValidateCall(call, threadSource, publication.Value, priorReturned, failed || boundary is not null);
                priorReturned |= call.Returned;
            }
            flags = call.FlagsAfter; failed |= !call.Returned;
        }
        if (flags != saved.ReferenceFlags || boundary != saved.WriterBoundary)
            throw new InvalidDataException("Script initializer current flags or writer refusal disagree with their ordered source history.");
    }

    private static void ValidateCall(FalloutReferenceScriptExtraInitializationCall call, FalloutPlayerThreadContextSource threadSource,
        FalloutReferenceScriptExtraInitializerPublication publication, bool priorReturned, bool unavailable)
    {
        var initialized = (call.FlagsBefore & FalloutReferenceScriptExtraInitializerSource.InitializedFlag) != 0;
        var stored = call.Reads.Any(value => value.Step == FalloutReferenceScriptExtraInitializationStep.InitializedFlagStore);
        if (call.FlagsAfter != (stored ? call.FlagsBefore | FalloutReferenceScriptExtraInitializerSource.InitializedFlag : call.FlagsBefore) ||
            initialized && stored || call.Returned && (unavailable || initialized && !priorReturned) ||
            call.Returned && call.Step != FalloutReferenceScriptExtraInitializationStep.Returned ||
            !call.Returned && call.Step == FalloutReferenceScriptExtraInitializationStep.Returned && call.ThreadScope is { Returned: false })
            throw new InvalidDataException("Script initializer fabricated flag8 authority or promoted an unreturned predecessor.");
        var expected = new List<FalloutReferenceScriptExtraInitializationRead>
        { new(FalloutReferenceScriptExtraInitializationStep.InitializedFlagRead, call.FlagsBefore) };
        var scriptBranch = !initialized && (call.FlagsAfter & FalloutReferenceScriptExtraInitializerSource.ScriptSuppressionFlag) == 0;
        if (!initialized)
        {
            expected.Add(new(FalloutReferenceScriptExtraInitializationStep.InitializedFlagStore,
                call.FlagsBefore | FalloutReferenceScriptExtraInitializerSource.InitializedFlag));
            expected.Add(new(FalloutReferenceScriptExtraInitializationStep.FlagPredicate,
                call.FlagsBefore | FalloutReferenceScriptExtraInitializerSource.InitializedFlag));
            if (scriptBranch)
            {
                expected.Add(new(FalloutReferenceScriptExtraInitializationStep.ThreadScope,
                    FalloutReferenceScriptExtraInitializerSource.ScriptThreadKind));
                expected.Add(new(FalloutReferenceScriptExtraInitializationStep.ExistingExtra, Form: call.Before?.Script, Present: call.Before is not null));
                if (call.Before?.Script is null)
                {
                    expected.Add(new(FalloutReferenceScriptExtraInitializationStep.Template, Present: false));
                    if (call.Selected is { } selected)
                    {
                        if (selected.Form != call.Invocation.Actor.Base || selected.Signature != call.Invocation.Actor.BaseSignature ||
                            selected.Flags != call.Invocation.Actor.BaseFlags || selected.Sha256 != call.Invocation.Actor.BaseSha256)
                            throw new InvalidDataException("Script initializer selected another winning base or record header as reference flags.");
                        ValidateForm(selected.Script);
                        expected.Add(new(FalloutReferenceScriptExtraInitializationStep.SelectedForm, Form: selected.Form));
                        expected.Add(new(FalloutReferenceScriptExtraInitializationStep.Script, Form: selected.Script, Present: selected.Script is not null));
                        if (selected.Script is { } script)
                            expected.Add(new(FalloutReferenceScriptExtraInitializationStep.ScriptAttachment, Form: script));
                    }
                }
                if (call.ThreadScope is { } scope)
                    expected.Add(new(FalloutReferenceScriptExtraInitializationStep.ThreadRestore, scope.BeforeKind));
            }
        }
        if (call.Reads.Count > expected.Count || !call.Reads.SequenceEqual(expected.Take(call.Reads.Count)) ||
            call.Returned && !call.Reads.SequenceEqual(expected) || call.Returned && !initialized && !stored ||
            call.Returned && scriptBranch && call.ThreadScope is not { Returned: true, Failure: null } ||
            call.Returned && scriptBranch && call.Before?.Script is null && call.Selected is null ||
            call.Returned && call.Selected?.Script is not null ||
            call.ThreadScope is not null && (!scriptBranch || !stored) ||
            call.Reads.Any(value => value.Step == FalloutReferenceScriptExtraInitializationStep.ThreadRestore) && call.ThreadScope is not { Returned: true })
            throw new InvalidDataException("Script initializer lost source predicate, existing-Extra, template, Script or restoration order.");
        if (call.ThreadScope is { } entered)
        {
            FalloutPlayerThreadContextState.ValidateScriptInitializationScopes([entered], threadSource);
            if (entered.Process != publication.ThreadProcess || entered.Factory != publication.ThreadFactory ||
                entered.OwnerFactory != call.Factory || entered.Invocation != call.Invocation.Initialization ||
                entered.Thread != call.Invocation.ThreadId || !entered.Returned && entered.Failure != call.Failure)
                throw new InvalidDataException("Script initializer borrowed a menu, historical or unrelated current-thread scope.");
        }
        var expectedStores = call.Selected?.Script is not null && call.Before?.Script is null ?
            call.Before is null ? new[] { FalloutPackageCommandField.ScriptConstruction, FalloutPackageCommandField.ScriptForm,
                FalloutPackageCommandField.ScriptConstructorEventList, FalloutPackageCommandField.ScriptPresence } :
                new[] { FalloutPackageCommandField.ScriptForm } : [];
        if (call.Stores.Count > expectedStores.Length || !call.Stores.Select(value => value.Field).SequenceEqual(expectedStores.Take(call.Stores.Count)) ||
            call.Stores.Where(value => value.Field == FalloutPackageCommandField.ScriptForm).Any(value => value.Form != call.Selected?.Script) ||
            call.Stores.Zip(call.Stores.Skip(1)).Any(pair => pair.First.Changed >= pair.Second.Changed) ||
            call.Step == FalloutReferenceScriptExtraInitializationStep.EventListFactory &&
            (call.Selected?.Script is null || call.Stores.Count != expectedStores.Length || call.Failure != EventListBoundary))
            throw new InvalidDataException("Script initializer lost the attachment-before-list-factory failed prefix.");
    }

    private static void ValidateForm(FalloutFormKey? form)
    {
        if (form is { } key && (string.IsNullOrWhiteSpace(key.OwnerPlugin) || key.ObjectId is 0 or > FalloutFormKey.ObjectIdMask))
            throw new InvalidDataException("Script initializer retained an unowned form identity.");
    }
    internal static FalloutReferenceScriptExtraInitializationCall CloneCall(FalloutReferenceScriptExtraInitializationCall call) => call with
    {
        Before = call.Before is { } extra ? extra with { Rows = Array.AsReadOnly(extra.Rows.ToArray()) } : null,
        Reads = Array.AsReadOnly(call.Reads.ToArray()),
        Stores = Array.AsReadOnly(call.Stores.ToArray())
    };
    internal static FalloutReferenceScriptExtraInitializerSnapshot CloneSnapshot(FalloutReferenceScriptExtraInitializerSnapshot snapshot) => snapshot with
    { Publications = Array.AsReadOnly(snapshot.Publications.ToArray()), Calls = Array.AsReadOnly(snapshot.Calls.Select(CloneCall).ToArray()) };
    private static bool EquivalentSnapshot(FalloutReferenceScriptExtraInitializerSnapshot first, FalloutReferenceScriptExtraInitializerSnapshot second) =>
        JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
}
