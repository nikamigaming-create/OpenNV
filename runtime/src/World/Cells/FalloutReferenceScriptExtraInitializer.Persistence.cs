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
        var flagWrites = saved.ReferenceFlagWrites ?? [];
        var flagWriteIndex = 0; var animationWrites = saved.AnimationFlagWrites ?? []; var animationIndex = 0;
        foreach (var call in saved.Calls)
        {
            AdvanceFlagsBefore(call.Entered);
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
        AdvanceFlagsBefore(long.MaxValue);
        if (flags != saved.ReferenceFlags || boundary != saved.WriterBoundary)
            throw new InvalidDataException("Script initializer current flags or writer refusal disagree with their ordered source history.");
        var publicationCalls = saved.Calls.Select(value => (Process: value.Invocation.Process, value.Entered, value.Changed))
            .Concat(flagWrites.Select(value => (Process: value.Invocation.CommonProcess, value.Entered, value.Changed)))
            .Concat(animationWrites.Select(value => (Process: value.Invocation.CommonProcess, value.Entered, value.Changed)))
            .OrderBy(value => value.Entered).ToArray();
        FalloutReferencePublicationSequence.Validate(saved.Publications.Select(value => (value.Process, value.PublishedSequence)), publicationCalls, sequence);

        void AdvanceFlagsBefore(long bound)
        {
            while (flagWriteIndex < flagWrites.Count || animationIndex < animationWrites.Count)
            {
                var normal = flagWriteIndex < flagWrites.Count ? flagWrites[flagWriteIndex].Entered : long.MaxValue;
                var animation = animationIndex < animationWrites.Count ? animationWrites[animationIndex].Entered : long.MaxValue;
                if (Math.Min(normal, animation) >= bound) break;
                if (normal < animation) FlagWrite(flagWrites[flagWriteIndex++]);
                else AnimationFlag(animationWrites[animationIndex++]);
            }
        }
        void AnimationFlag(FalloutReferenceAnimationFlagWrite write)
        {
            var invocation = write.Invocation;
            if (write.Source != source.Identity || write.LoadedSource.Length != 64 || write.Call == Guid.Empty || !ids.Add(write.Call) ||
                invocation.Call == Guid.Empty || invocation.Actor != actor || invocation.Epoch != saved.Epoch || invocation.ThreadId < 1 ||
                invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad ||
                !publications.TryGetValue(invocation.CommonProcess, out var publication) ||
                invocation.ReferenceProcess != publication.Value.ThreadProcess || invocation.ReferenceConstructor != publication.Value.Constructor ||
                write.Factory != publication.Value.Factory || write.FieldsFactory != publication.Value.FieldsFactory ||
                publication.Ordinal < lastPublication || write.Entered != ++sequence || write.Changed != ++sequence || write.Before != flags ||
                write.Returned != (write.Failure is null) || write.Failure is { Length: 0 } ||
                threads.TryGetValue(invocation.CommonProcess, out var thread) && thread != invocation.ThreadId ||
                write.Enabled && (write.Construction is not { Returned: true, Role: FalloutLoadedAnimationRole.Common } construction ||
                    construction.Invocation != invocation))
                throw new InvalidDataException("Animation flag store changed its actual source word, loader or constructor.");
            var after = write.Enabled ? flags | FalloutLoadedAnimationSource.AnimationReferenceFlag : flags & ~FalloutLoadedAnimationSource.AnimationReferenceFlag;
            if (write.Returned ? write.After != after : write.After != flags && write.After != after)
                throw new InvalidDataException("Animation flag store fabricated a bit or lost a failed prefix.");
            flags = write.After; lastPublication = publication.Ordinal;
            threads.TryAdd(invocation.CommonProcess, invocation.ThreadId); failed |= !write.Returned;
        }

        void FlagWrite(FalloutReferenceInitializedFlagWrite write)
        {
            var invocation = write.Invocation;
            if (write.Source != source.Identity || invocation.Call == Guid.Empty || !ids.Add(invocation.Call) ||
                invocation.Actor != actor || invocation.Epoch != saved.Epoch || invocation.ThreadId < 1 ||
                !publications.TryGetValue(invocation.CommonProcess, out var publication) ||
                invocation.ReferenceProcess != publication.Value.ThreadProcess ||
                invocation.ReferenceConstructor != publication.Value.Constructor || write.Factory != publication.Value.Factory ||
                write.FieldsFactory != publication.Value.FieldsFactory || publication.Ordinal < lastPublication ||
                write.Entered != ++sequence || write.Changed != ++sequence || write.Before != flags ||
                write.Returned != (write.Failure is null) || write.Failure is { Length: 0 } ||
                threads.TryGetValue(invocation.CommonProcess, out var thread) && thread != invocation.ThreadId)
                throw new InvalidDataException("Reference initialized writer changed its actual ordered flag owner.");
            if (write.Mask == FalloutReferenceBaseAssignmentInputs.DestructibleReferenceFlag)
            {
                if (write.BaseAssignment is not { } assignment || write.Initialized != assignment.Destructible)
                    throw new InvalidDataException("Reference Base flag store omitted its actual selected component inputs.");
                assignment.Validate(FalloutReferencePositionSource.Reviewed(source.Fields), actor);
            }
            else if (write.Mask != FalloutReferenceScriptExtraInitializerSource.InitializedFlag || write.BaseAssignment is not null)
                throw new InvalidDataException("Reference scalar writer borrowed another flag mask or source child.");
            var after = write.Initialized ? flags | write.Mask : flags & ~write.Mask;
            if (write.Returned ? write.After != after : write.After != flags && write.After != after)
                throw new InvalidDataException("Reference initialized writer invented a flag store or lost a failed prefix.");
            flags = write.After; lastPublication = publication.Ordinal;
            threads.TryAdd(invocation.CommonProcess, invocation.ThreadId); failed |= !write.Returned;
        }
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
    {
        Publications = Array.AsReadOnly(snapshot.Publications.ToArray()),
        Calls = Array.AsReadOnly(snapshot.Calls.Select(CloneCall).ToArray()),
        ReferenceFlagWrites = snapshot.ReferenceFlagWrites is { } writes ? Array.AsReadOnly(writes.ToArray()) : null,
        AnimationFlagWrites = snapshot.AnimationFlagWrites is { } animations ? Array.AsReadOnly(animations.ToArray()) : null
    };
    private static bool EquivalentSnapshot(FalloutReferenceScriptExtraInitializerSnapshot first, FalloutReferenceScriptExtraInitializerSnapshot second) =>
        JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
}
