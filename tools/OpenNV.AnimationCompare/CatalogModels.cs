namespace OpenNV.AnimationCompare;

internal sealed record CatalogFailure(string Lane, string Identity, string ErrorType, string Reason, string? Domain = null);
internal sealed record CatalogFailureGroup(string Lane, string? Domain, string ErrorType, long Count,
    IReadOnlyList<string> ExampleIdentities, IReadOnlyList<string> ExampleReasons);
internal sealed record CatalogReference(string Form, uint RuntimeFormId, string? Cell, uint? CellRuntimeFormId,
    bool SourceDisabled, string State = "authored-placement; current runtime state unobserved", string? BaseForm = null);
internal sealed record CatalogDeclaration(string Field, int Occurrence, string Value, string Role);
internal sealed record CatalogRecord(string Form, uint RuntimeFormId, string Signature, string Plugin,
    string PayloadSha256, bool Deleted, string? EditorId, IReadOnlyList<CatalogDeclaration> Declarations,
    IReadOnlyList<CatalogFailure> Failures);
internal sealed record CatalogNode(int Block, string Type, string Name, IReadOnlyList<int> Parents,
    IReadOnlyList<int> Children, int Controller, int SkinInstance);
internal sealed record CatalogChannel(string NodeName, string PropertyType, string ControllerType,
    string Variable1, string Variable2, int Interpolator, int Controller, byte Priority);
internal sealed record CatalogSequence(int Block, string Name, string TargetName, float StartTime,
    float StopTime, float Frequency, uint CycleType, int Manager, int TextKeys,
    IReadOnlyList<CatalogChannel> Channels, IReadOnlyList<CatalogTextKey>? Events = null);
internal sealed record CatalogTextKey(float Time, string Value);
internal sealed record CatalogController(int Block, string Type, string Sha256, object? Time,
    IReadOnlyList<int>? Sequences = null, int? ObjectPalette = null);
internal sealed record CatalogResource(string Path, string Kind, string? SourceIdentity, string? Sha256,
    long Bytes, string DecodeStatus, string? AnimationBindingSha256,
    IReadOnlyList<CatalogNode> Nodes, IReadOnlyList<CatalogSequence> Sequences,
    IReadOnlyList<CatalogController> Controllers, IReadOnlyList<CatalogFailure> Failures, IReadOnlyList<int>? Roots = null);
internal sealed record CatalogRig(string Id, string Family, string SkeletonPath,
    IReadOnlyList<string> ModelPaths, IReadOnlyList<string> SourceForms,
    IReadOnlyList<string> AnimationDirectories, IReadOnlyList<string> DeclaredAnimations,
    string? BindingSha256, IReadOnlyList<CatalogFailure> Failures, IReadOnlyList<CatalogRigVariant>? Variants = null);
internal sealed record CatalogRigVariant(string SourceForm, IReadOnlyList<string> ModelPaths);
internal sealed record CatalogCase(string Id, string Family, string? SourceForm, uint? RuntimeFormId,
    string? RigId, string? RigHash, string ResourcePath, string? SourceHash, int? SequenceBlock,
    string Action, string Perspective, string Role, string Admission,
    IReadOnlyList<string> Blockers, IReadOnlyList<string> RequiredObservables,
    IReadOnlyList<CatalogReference> ReferenceCandidates,
    string Coverage = "not-run", string ExecutionAdmission = "unbound; runner must qualify action and restore owners",
    IReadOnlyList<CatalogReference>? EntryReferenceCandidates = null, byte? WeaponMods = null,
    IReadOnlyList<CatalogBindingResource>? BindingResources = null, string? SourceRecordHash = null,
    string? ReferenceSetId = null, string? EntryReferenceSetId = null,
    IReadOnlyList<string>? EquivalentRigIds = null, IReadOnlyList<string>? RelatedRigIds = null,
    string? ActionBindingHash = null, string? EquivalentRigSetId = null, string? RelatedRigSetId = null,
    string? SourceOwnerSetId = null, IReadOnlyList<string>? ExecutionBlockers = null);
internal sealed record CatalogReferenceSet(string Id, IReadOnlyList<CatalogReference> References);
internal sealed record CatalogRigSet(string Id, string Role, IReadOnlyList<string> RigIds);
internal sealed record CatalogSourceOwner(string Form, uint RuntimeFormId, string Signature, string? RecordHash, string ReferenceSetId);
internal sealed record CatalogSourceOwnerSet(string Id, IReadOnlyList<CatalogSourceOwner> Owners);
internal sealed record CatalogWeaponModifier(int Slot, string Item, uint Effect, float Value, float Secondary,
    string RuntimeAdmission);
internal sealed record CatalogWeaponVariant(string SourceForm, uint RuntimeFormId, byte Mask, string Perspective,
    string? SourceRecordHash, string? ModelPath, string? ModelHash, string AnimationGroup, byte AttackAnimation,
    byte ReloadAnimation, IReadOnlyList<CatalogWeaponModifier> Modifiers, IReadOnlyList<string> ExecutionBlockers);
internal sealed record CatalogBindingResource(string Role, string Path, string? SourceIdentity, string? Sha256);
internal sealed record CatalogManifest(string Schema, string OutputDirectory, string Game,
    string SaveCompatibilityId, string CompletionStatus, long WinningRecords, long DeletedRecords,
    long Resources, long Rigs, long Cases, long Failures, IReadOnlyDictionary<string, string> Files,
    string Acceptance = "source catalog only; no runtime, animation, pixel or performance pass",
    IReadOnlyList<CatalogPlugin>? Plugins = null, string? SourceStackHash = null,
    string SourceStackHashScope = "canonical ordered plugin names and SHA-256 values; shared rig-control identity",
    string? CatalogContentHash = null,
    string CatalogContentHashScope = "selected plugin order, settings and catalog NIF/KF bytes; texture/audio bytes require their independent evidence lanes");
internal sealed record CatalogPlugin(string Name, string Path, string Sha256, long Bytes);
