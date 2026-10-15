namespace OpenNV.Runtime.Content;

internal sealed record FalloutLoadedAnimationGroup(byte Index, string Name, bool Multiple, int Category,
    IReadOnlyList<int> EventSlots);
internal sealed record FalloutLoadedAnimationSource(FalloutPlayerReturnTailSource Tail,
    string Contract, IReadOnlyList<string?> NodeNames, IReadOnlyList<string> MovementPrefixes,
    IReadOnlyList<string> WeaponPrefixes, IReadOnlyList<FalloutLoadedAnimationGroup> Groups,
    string ResourceRootName, IReadOnlyList<string> ResourceNodeNames)
{
    internal const int GroupCount = 183;
    internal const int ManagerSequenceCapacity = 10;
    internal const int ManagerNameBuckets = 37;
    internal const int PaletteNameBuckets = 79;
    internal const byte InitialIdlePriority = 100;
    internal const uint BindThreadKind = 51;
    internal const uint PlayerLoadThreadKind = 52;
    internal const uint AnimationReferenceFlag = 0x10000000;
    internal const uint ReferenceAnimationInhibitedFlag = 0x20;
    internal const uint NoTime = 0xff7fffff;
    internal const ushort InitialGroupWord = ushort.MaxValue;
    internal const uint ClearRootFlag = 2;
    internal const string FirstSkeletonSetting = "sFirstPersonSkeleton";
    private const string Rules = "source-FO3-loaded-Player-AnimData/v1;" +
        "Player-first-constructor-store-before-inherited-Reference-Common-animation-constructor-and-setter;" +
        "actual-null-reset-before-new-current-pointer-never-native-model-as-pointer;" +
        "nullable-current-tier-uses-same-Common-tier-field-not-class-metadata;" +
        "bind-kind-fifty-one-saves-actual-prior-kind-inside-Player-load-kind-fifty-two;" +
        "actor-before-root-retain-before-eight-source-node-lookups-before-root-flag-clear;" +
        "real-manager-ten-sequence-slots-thirty-seven-name-buckets-and-seventy-nine-palette-buckets;" +
        "owned-KF-sequences-retain-source-controlled-links-and-group-identity;" +
        "single-group-registration-replaces-current-sequence-multiple-group-retains-source-order;" +
        "idle-group-zero-activation-at-priority-one-hundred-before-zero-root-update-before-immediate-deactivation;" +
        "nonnull-root-updater-keeps-actual-group-slots-time-accumulation-and-existing-source-leaves;" +
        "unknown-writers-retain-failed-prefix-same-fields-and-source-lifetimes-no-renderer-authority";
    internal string Tables { get; } = FalloutAdvancementRuntimeReceipt.Hash(System.Text.Json.JsonSerializer.Serialize(
        new { NodeNames, MovementPrefixes, WeaponPrefixes, Groups, ResourceRootName, ResourceNodeNames }));
    internal string Identity { get; } = FalloutAdvancementRuntimeReceipt.Hash(Tail.Identity + "\0" + Contract + "\0" +
        System.Text.Json.JsonSerializer.Serialize(new { NodeNames, MovementPrefixes, WeaponPrefixes, Groups, ResourceRootName, ResourceNodeNames }));
    internal static string ContractDigest => FalloutAdvancementRuntimeReceipt.Hash(Rules);
    internal void Validate()
    {
        Tail.Validate();
        if (Contract != ContractDigest || NodeNames is not { Count: 8 } || MovementPrefixes is not { Count: 4 } ||
            WeaponPrefixes is not { Count: 12 } || Groups is not { Count: GroupCount } ||
            MovementPrefixes[0] != "" || WeaponPrefixes[0] != "" || NodeNames.Any(value => value is { Length: 0 }) ||
            MovementPrefixes.Any(value => value is null) || WeaponPrefixes.Any(value => value is null) ||
            string.IsNullOrWhiteSpace(ResourceRootName) || ResourceNodeNames is not { Count: 5 } || ResourceNodeNames.Any(string.IsNullOrWhiteSpace) ||
            Groups.Where((group, index) => group.Index != index || string.IsNullOrWhiteSpace(group.Name) ||
                group.Category is < 0 or > 9 || group.EventSlots is not { Count: 5 }).Any() || Groups[0].Multiple || Groups[0].Name != "Idle")
            throw new InvalidDataException("Loaded animation source lost its actual selected constructors, names and group table.");
    }
}
