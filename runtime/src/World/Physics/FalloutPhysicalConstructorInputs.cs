using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutPhysicalEffectiveSettings(float DebrisMinExtent, float DebrisMaxVelocity)
{
    internal const string DebrisMinExtentSetting = "fDebrisMinExtent";
    internal const string DebrisMaxVelocitySetting = "fDebrisMaxVelocity";

    // The caller supplies the loaded stack's effective retained GMST reads.
    // The original defaults are not substitutes for winning or changed settings.
    internal static FalloutPhysicalEffectiveSettings Read(Func<string, float> readRetainedSetting)
    {
        ArgumentNullException.ThrowIfNull(readRetainedSetting);
        return new(readRetainedSetting(DebrisMinExtentSetting), readRetainedSetting(DebrisMaxVelocitySetting));
    }
}

internal sealed record FalloutRigidBodyConstructionInputs(FalloutNifRigidBody Source,
    float PenetrationDepth, float MaxLinearVelocity, float Restitution,
    byte InitialMotion, byte InitialQuality, byte? SavedMotion);

internal sealed record FalloutPlayerControllerConstructionInputs(FalloutPhysicalVector3 ReferencePosition,
    FalloutPhysicalVector3 ScaledPosition, FalloutPhysicalVector3 StandardCenter,
    FalloutPhysicalVector3 CapsulePositiveEndpoint, FalloutPhysicalVector3 CapsuleNegativeEndpoint,
    uint CapsuleRadiusBits, FalloutPhysicalVector3 RetainedOffset, uint StepBits, uint HeightBits,
    byte SelectedShapeSlot);

internal readonly record struct FalloutPhysicalVector4(FalloutPhysicalVector3 Position, uint W);

internal sealed record FalloutStandardControllerConvexInputs(IReadOnlyList<FalloutPhysicalVector4> Points,
    uint ConvexRadiusBits, uint SpecializedRadiusBits, uint KeepContactDistanceBits);

// This projection owns source constructor inputs, not physical bodies or worlds.
// Shape, motion, state factory, phantom/proxy and admission remain real consumers.
internal sealed class FalloutPhysicalConstructorInputs
{
    internal const string ReviewedEngineSha256 = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    internal const uint SourceToPhysicalBits = 0x3e124e47;
    internal const uint PhysicalToSourceBits = 0x40dff823;
    internal const uint PenetrationEpsilonBits = 0x34000000;
    internal const uint MaximumPenetrationBits = 0x3e4ccccd;
    internal const uint DefaultPenetrationBits = 0x3e19999a;
    internal const uint MinimumLinearVelocityBits = 0x447a0000;
    internal const uint RestitutionThresholdBits = 0x3ffeb852;
    internal const uint NormalizedRestitutionBits = 0x3ffd70a4;
    internal const uint StandardHalfWidthBits = 0x41b80000;
    internal const uint StandardHalfDepthBits = 0x418c0000;
    internal const uint StandardHalfHeightBits = 0x42800000;
    internal const uint InitialStepBits = 0x408dbbd5;
    internal const uint KeepDistanceBits = 0x3d4ccccd;
    internal const uint ContactToleranceBits = 0x3dcccccd;
    internal const uint RingDiagonalBits = 0x3f350481;
    internal const byte SelectedConvexShapeSlot = 1;
    private const byte FixedMotion = 7, KeyframedMotion = 6, DynamicMotion = 1;
    private const byte UnspecifiedQuality = 0, FixedQuality = 1, MovingQuality = 4;
    private const float MidpointFactor = .5f, StepRadiusFactor = .75f;
    private const float DiameterFactor = 2f, ReverseEndpointFactor = -1f;
    private const int StandardConvexPointCount = 18, LowerRingStart = 1, UpperRingStart = 9, TopPoint = 17;

    internal string EngineSha256 { get; }
    internal FalloutPhysicalEffectiveSettings Settings { get; }

    internal FalloutPhysicalConstructorInputs(string engineSha256, FalloutPhysicalEffectiveSettings settings)
    {
        if (!string.Equals(engineSha256, ReviewedEngineSha256, StringComparison.Ordinal))
            throw new NotSupportedException("Selected engine has no reviewed physical constructor input/transform contract.");
        ArgumentNullException.ThrowIfNull(settings);
        EngineSha256 = engineSha256; Settings = settings;
    }

    internal FalloutRigidBodyConstructionInputs ProjectRigidBody(FalloutNifRigidBody source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var depth = source.PenetrationDepth;
        if (depth <= Value(PenetrationEpsilonBits) || depth > Value(MaximumPenetrationBits))
            depth = Value(DefaultPenetrationBits);
        var maximumVelocity = source.MaxLinearVelocity;
        if (maximumVelocity < Value(MinimumLinearVelocityBits)) maximumVelocity = Value(MinimumLinearVelocityBits);
        var restitution = source.Restitution;
        if (restitution >= Value(RestitutionThresholdBits)) restitution = Value(NormalizedRestitutionBits);
        var motion = source.MotionSystem; var quality = source.QualityType;
        byte? savedMotion = null;
        if (source.Mass == 0f)
        {
            if (motion == FixedMotion) quality = FixedQuality;
        }
        else if (motion is KeyframedMotion or FixedMotion)
        {
            savedMotion = motion; motion = DynamicMotion; quality = MovingQuality;
        }
        else if (quality == UnspecifiedQuality) quality = MovingQuality;
        // Debris quality needs actual shape bounds and compressed current/saved
        // motion. It deliberately remains outside preconstruction projection.
        return new(source, depth, maximumVelocity, restitution, motion, quality, savedMotion);
    }

    internal FalloutPlayerControllerConstructionInputs ProjectPlayer(FalloutPhysicalVector3 referencePosition,
        FalloutPhysicalVector3 currentStandardCenter)
    {
        var radius = FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Add(
            Value(StandardHalfWidthBits), Value(StandardHalfDepthBits)), MidpointFactor);
        var endpoint = FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Subtract(Value(StandardHalfHeightBits), radius),
            currentStandardCenter.ZValue);
        var units = Value(SourceToPhysicalBits);
        var physicalRadius = FalloutPhysicalFloat32.Multiply(radius, units);
        var positive = FalloutPhysicalVector3.FromValues(
            FalloutPhysicalFloat32.Multiply(currentStandardCenter.XValue, units),
            FalloutPhysicalFloat32.Multiply(currentStandardCenter.YValue, units),
            FalloutPhysicalFloat32.Multiply(endpoint, units));
        // The source negates the endpoint after adding center Z. It does not
        // subtract the extent from center Z; the two Z endpoints remain mirrored.
        var negative = positive with
        {
            Z = Bits(FalloutPhysicalFloat32.Multiply(
                FalloutPhysicalFloat32.Multiply(endpoint, ReverseEndpointFactor), units))
        };
        var step = Value(InitialStepBits);
        var minimumStep = FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Multiply(radius, StepRadiusFactor), units);
        if (minimumStep > step) step = minimumStep;
        var offsetZ = FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Multiply(
            FalloutPhysicalFloat32.Add(endpoint, radius), units), FalloutPhysicalFloat32.Multiply(DiameterFactor, Value(KeepDistanceBits)));
        var offset = positive with { Z = Bits(offsetZ) };
        var height = FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Multiply(DiameterFactor, Value(StandardHalfHeightBits)), units);
        return new(referencePosition, ScaleReferencePosition(referencePosition), currentStandardCenter,
            positive, negative, Bits(physicalRadius), offset, Bits(step), Bits(height), SelectedConvexShapeSlot);
    }

    internal FalloutPhysicalVector3 ScaleReferencePosition(FalloutPhysicalVector3 source) =>
        FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Multiply(source.XValue, Value(SourceToPhysicalBits)),
            FalloutPhysicalFloat32.Multiply(source.YValue, Value(SourceToPhysicalBits)),
            FalloutPhysicalFloat32.Multiply(source.ZValue, Value(SourceToPhysicalBits)));

    // These are the original ordered inputs to the hull factory. They are not
    // canonical hull vertices, planes, a physical shape or a backend margin.
    internal FalloutStandardControllerConvexInputs ProjectStandardConvex(FalloutPlayerControllerConstructionInputs player,
        uint currentKeepContactDistanceBits)
    {
        ArgumentNullException.ThrowIfNull(player);
        var expected = ProjectPlayer(player.ReferencePosition, player.StandardCenter);
        if (player != expected)
            throw new InvalidDataException("Standard convex inputs changed their actual selected Player constructor projection.");
        var radius = Value(player.CapsuleRadiusBits);
        var lowerZ = FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Add(Value(player.StepBits),
            Value(ContactToleranceBits)), player.CapsuleNegativeEndpoint.ZValue);
        var halfRadius = FalloutPhysicalFloat32.Multiply(radius, MidpointFactor);
        var upperZ = FalloutPhysicalFloat32.Add(player.CapsulePositiveEndpoint.ZValue, halfRadius);
        var diagonal = Value(RingDiagonalBits);
        float[] cosines = [1, diagonal, 0, -diagonal, -1, -diagonal, 0, diagonal];
        float[] sines = [0, diagonal, 1, diagonal, 0, -diagonal, -1, -diagonal];
        var points = new FalloutPhysicalVector4[StandardConvexPointCount];
        points[0] = Point(player.CapsuleNegativeEndpoint.XValue, player.CapsuleNegativeEndpoint.YValue,
            FalloutPhysicalFloat32.Subtract(player.CapsuleNegativeEndpoint.ZValue, radius));
        for (var index = 0; index < cosines.Length; index++)
        {
            var x = FalloutPhysicalFloat32.Multiply(radius, cosines[index]);
            var y = FalloutPhysicalFloat32.Multiply(radius, sines[index]);
            points[index + LowerRingStart] = Point(FalloutPhysicalFloat32.Add(x, player.CapsuleNegativeEndpoint.XValue),
                FalloutPhysicalFloat32.Add(y, player.CapsuleNegativeEndpoint.YValue), lowerZ);
            points[index + UpperRingStart] = Point(x, y, upperZ);
        }
        points[TopPoint] = Point(player.CapsulePositiveEndpoint.XValue, player.CapsulePositiveEndpoint.YValue,
            FalloutPhysicalFloat32.Add(upperZ, halfRadius));
        var keepContact = Value(currentKeepContactDistanceBits) == 0f
            ? player.CapsuleRadiusBits : currentKeepContactDistanceBits;
        return new(Array.AsReadOnly(points), KeepDistanceBits, ContactToleranceBits, keepContact);

        FalloutPhysicalVector4 Point(float x, float y, float z) =>
            new(FalloutPhysicalVector3.FromValues(x, y, z), player.CapsuleRadiusBits);
    }

    private static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
}
