using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceScriptExtraInitializationInvocation(Guid Initialization,
    FalloutCombatActorIdentity Actor, Guid Process, long Epoch, Guid Constructor, int ThreadId);
internal enum FalloutReferenceScriptExtraWriter { ReferenceFlags, ActorTemplate, ExtraCopyOrLoad, ScriptCallback }
internal enum FalloutReferenceScriptExtraInitializationStep
{
    Entered, InitializedFlagRead, InitializedFlagStore, FlagPredicate, ThreadScope, ExistingExtra,
    Template, SelectedForm, Script, ScriptAttachment, EventListFactory, ThreadRestore, Returned, UnownedWriter
}
internal sealed record FalloutReferenceScriptExtraFormSource(FalloutFormKey Form, string Signature, uint Flags,
    string Sha256, FalloutFormKey? Script);
internal sealed record FalloutReferenceScriptExtraInitializationRead(FalloutReferenceScriptExtraInitializationStep Step,
    uint? Word = null, FalloutFormKey? Form = null, bool? Present = null);
internal sealed record FalloutReferenceScriptExtraInitializationCall(FalloutReferenceScriptExtraInitializerSource Source,
    FalloutReferenceScriptExtraInitializationInvocation Invocation, Guid Factory, Guid FieldsFactory,
    long Entered, long Changed, FalloutReferenceScriptExtraInitializationStep Step,
    uint FlagsBefore, uint FlagsAfter, FalloutFormKey? Template, FalloutPackageScriptExtra? Before,
    FalloutReferenceScriptExtraFormSource? Selected, IReadOnlyList<FalloutReferenceScriptExtraInitializationRead> Reads,
    FalloutSourceThreadKindScope? ThreadScope, IReadOnlyList<FalloutPackageCommandFieldStore> Stores,
    bool Returned, string? Failure, FalloutReferenceScriptExtraWriter? InvalidatedWriter = null, string? WriterBoundary = null)
{
    internal bool WholeReferenceInitializerReturned => false;
    internal bool WholePhysicalControllerReturned => false;
}
internal sealed record FalloutReferenceScriptExtraInitializerPublication(Guid Process, Guid Factory, Guid FieldsFactory,
    Guid Constructor, long Epoch, Guid ThreadProcess, Guid ThreadFactory, Guid? PreviousProcess = null,
    Guid? PreviousFactory = null);
internal sealed record FalloutReferenceScriptExtraInitializerSnapshot(string Schema,
    FalloutReferenceScriptExtraInitializerSource Source, string Stack, FalloutCombatActorIdentity Actor,
    Guid Process, Guid Factory, Guid FieldsFactory, Guid Constructor, long Epoch,
    uint ReferenceFlags, FalloutFormKey? Template, string? WriterBoundary,
    IReadOnlyList<FalloutReferenceScriptExtraInitializerPublication> Publications,
    IReadOnlyList<FalloutReferenceScriptExtraInitializationCall> Calls);

internal enum FalloutScriptExtraInitializationFieldOperationKind
{ Begin, Read, Attach, EventListPublication, Admit, UnownedWriter }
internal sealed record FalloutScriptExtraInitializationFieldOperation(
    FalloutReferenceScriptExtraInitializationInvocation Invocation, string Source, Guid InitializerFactory, Guid FieldsFactory,
    FalloutScriptExtraInitializationFieldOperationKind Kind, long Entered, long Changed,
    FalloutPackageScriptExtra? Before, FalloutPackageScriptExtra? After,
    IReadOnlyList<FalloutPackageCommandFieldStore> Stores, bool Returned, string? Failure,
    FalloutFormKey? Script = null, Guid? EventList = null, Guid? EventFactory = null,
    IReadOnlyList<FalloutPackageScriptEventRow?>? Rows = null, string? Boundary = null);
