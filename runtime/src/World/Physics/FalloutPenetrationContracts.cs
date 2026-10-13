namespace OpenNV.Runtime.World.Physics;

// These are live Actor objects. Original NIF declarations and presentation
// handles cannot stand in for current memberships, world residency or callbacks.
internal interface IFalloutPenetrationModel
{
    string Key { get; }
    IReadOnlyList<IFalloutPenetrationScene?> Roots { get; }
}

internal interface IFalloutPenetrationScene
{
    string Key { get; }
    IReadOnlyList<IFalloutPenetrationScene?> Children { get; }
    IFalloutPenetrationBody? Body { get; }
}

internal interface IFalloutPenetrationConstraint
{
    string Key { get; }
    int? DataType { get; }
    IFalloutPenetrationBody First { get; }
    IFalloutPenetrationBody Second { get; }
}

internal interface IFalloutPenetrationBody
{
    string Key { get; }
    string Name { get; }
    bool IsWeaponObject { get; }
    byte Motion { get; }
    uint CollisionFilter { get; }
    string? WorldKey { get; }
    // These are the source master/slave arrays, not endpoint-derived lists.
    IReadOnlyList<IFalloutPenetrationConstraint> IncomingConstraints { get; }
    IReadOnlyList<IFalloutPenetrationConstraint> OutgoingConstraints { get; }
    // The physical wrapper supplies its actual constructed linked-list order.
    IReadOnlyList<IFalloutPenetrationConstraint?> LinkedConstraints { get; }
    FalloutCharacterControllerProxyTransform ReadTransform();
    FalloutPhysicalVector3 ReadCenterOfMass();
    FalloutPhysicalVector3 ReadLinearVelocity();
    FalloutPhysicalVector3 ReadAngularVelocity();
    uint ReadInverseMassBits();
    FalloutPenetrationTerminalShape ReadTerminalShape();
    FalloutPenetrationBounds ReadBounds();
    void Activate();
    void PublishCollisionFilter(uint filter);
    void ApplyPointImpulse(FalloutPhysicalVector3 impulse, FalloutPhysicalVector3 point);
}

internal readonly record struct FalloutPenetrationTerminalShape(byte Kind,
    FalloutPhysicalVector3 EndpointA, FalloutPhysicalVector3 EndpointB, uint RadiusBits);
internal readonly record struct FalloutPenetrationBounds(FalloutPhysicalVector3 Minimum,
    FalloutPhysicalVector3 Maximum);
internal readonly record struct FalloutPenetrationSegment(FalloutPhysicalVector3 Start,
    FalloutPhysicalVector3 End);
internal readonly record struct FalloutPenetrationRayHit(bool RigidCollidable, uint CollisionFilter,
    uint FractionBits, FalloutPhysicalVector3 Normal);
internal readonly record struct FalloutPenetrationContact(int Bone, int Parent,
    FalloutPhysicalVector3 Point, FalloutPhysicalVector3 Direction);
internal readonly record struct FalloutPenetrationNormalizedVector(uint LengthBits,
    FalloutPhysicalVector3 Direction);

internal interface IFalloutPenetrationQuery : IDisposable
{
    string? WorldKey { get; }
    bool ReverseQueryFlag { get; }
    void BindWorld(string? world);
    void PublishBounds(FalloutPenetrationBounds bounds);
    void SetReverseQueryFlag(bool value);
    // All hits retain actual collector order. A nearest-hit result is insufficient.
    IReadOnlyList<FalloutPenetrationRayHit> CastAll(FalloutPenetrationSegment segment, uint filter);
    FalloutPenetrationQuerySnapshot Capture();
    void Restore(FalloutPenetrationQuerySnapshot saved);
}

internal sealed record FalloutPenetrationQuerySnapshot(string? WorldKey, bool ReverseQueryFlag,
    FalloutPenetrationBounds Bounds, uint CollisionFilter);

internal interface IFalloutPenetrationSystem : IDisposable
{
    IReadOnlyList<IFalloutPenetrationBody> Bodies { get; }
    IReadOnlyList<int> BoneToBodyIndices { get; }
    IFalloutPenetrationQuery Query { get; }
    IReadOnlyList<FalloutPhysicalVector3> SampleBonePositions(FalloutPenetrationSkeleton skeleton);
}

internal sealed record FalloutPenetrationConnectedGraph(IReadOnlyList<IFalloutPenetrationBody> Bodies,
    IReadOnlyList<IFalloutPenetrationConstraint> Constraints, IReadOnlyList<int> Parents,
    IReadOnlyList<int> Depths);
internal sealed record FalloutPenetrationOrderedGraph(IReadOnlyList<IFalloutPenetrationBody> Bodies,
    IReadOnlyList<IFalloutPenetrationConstraint> Constraints, IReadOnlyList<int> Parents);
internal sealed record FalloutPenetrationSystemConstruction(IFalloutPenetrationBody Root,
    FalloutPenetrationOrderedGraph Graph, FalloutPenetrationSkeleton Skeleton, ushort CollisionGroup,
    FalloutPenetrationBounds Bounds, uint PhantomFilter);

internal interface IFalloutPenetrationBackend
{
    // Equal-depth source ordering is unstable and has a distinct current owner.
    FalloutPenetrationOrderedGraph OrderBySourceDepth(FalloutPenetrationConnectedGraph graph);
    FalloutCharacterControllerProxyTransform MakeRelativeTransform(
        FalloutCharacterControllerProxyTransform child, FalloutCharacterControllerProxyTransform parent);
    string TerminalBoneName(string bodyName);
    FalloutPenetrationNormalizedVector Normalize(FalloutPhysicalVector3 vector);
    uint DotBits(FalloutPhysicalVector3 left, FalloutPhysicalVector3 right);
    FalloutPhysicalVector3 InitialImpulseDirection();
    IFalloutPenetrationSystem Construct(FalloutPenetrationSystemConstruction construction);
}

internal interface IFalloutPenetrationClock
{
    long ReadCounterTicks();
    uint ReadTicksPerMillisecondBits();
    long ConvertDurationTicks(uint scaledFloat32Bits);
}

internal sealed record FalloutPenetrationEffectiveSettings(uint DesiredVelocityBits,
    uint ImpulseLimitBits, uint DetectionSecondsBits)
{
    internal const string DesiredVelocitySetting = "fDesiredVel:RagdollAnim";
    internal const string ImpulseLimitSetting = "fImpulseLimit:RagdollAnim";
    internal const string DetectionSecondsSetting = "fDetectionUpdateTimeSec:RagdollAnim";

    internal static FalloutPenetrationEffectiveSettings Read(Func<string, float> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return new(BitConverter.SingleToUInt32Bits(read(DesiredVelocitySetting)),
            BitConverter.SingleToUInt32Bits(read(ImpulseLimitSetting)),
            BitConverter.SingleToUInt32Bits(read(DetectionSecondsSetting)));
    }
}
