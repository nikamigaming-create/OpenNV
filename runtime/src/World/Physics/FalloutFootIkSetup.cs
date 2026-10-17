using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutFootIkSettingRead(string Setting, uint Value, string Origin);
internal sealed record FalloutFootIkSetup(Guid? Skeleton, int Hip, int Knee, int Ankle,
    FalloutPhysicalVector3 KneeAxis, FalloutPhysicalVector3 FootEnd,
    FalloutPhysicalVector3 WorldUp, FalloutPhysicalVector3 ModelUp,
    uint GroundHeight, uint PlantedHeight, uint RaisedHeight, uint MaximumHeight, uint MinimumHeight,
    uint CosineMaximumKnee, uint CosineMinimumKnee, uint RayDistanceUp, uint RayDistanceDown, bool UseFootEnd)
{
    internal static FalloutFootIkSetup Construct() => new(null, -1, -1, -1, default, default, default, default,
        0, 0, 0, 0x3f333333, 0xbdcccccd, 0xbf800000, 0x3f800000, 0x3f000000, 0x3f4ccccd, false);
}

internal static class FalloutFootIkMath
{
    internal const uint RadiansToDegrees = 0x42652ee0, DegreesToRadians = 0x3c8efa35;
    internal static uint MaximumKneeCosine(uint upper) => Bits((float)Math.Cos(Multiply(
        Subtract(Subtract(90, Multiply(Value(upper), Value(RadiansToDegrees))), 10), Value(DegreesToRadians)))) ^ 0x80000000;
    internal static uint MinimumKneeCosine(uint lower) => Bits((float)Math.Cos(Multiply(
        Subtract(Add(Multiply(Value(lower), Value(RadiansToDegrees)), 90), 20), Value(DegreesToRadians))));
    internal static FalloutPhysicalVector3 Normalize(FalloutPhysicalVector3 value)
    {
        var squared = Squared(value);
        var reciprocal = squared == 0 ? 0 : Divide(1, (float)Math.Sqrt(squared));
        return FalloutPhysicalVector3.FromValues(Multiply(value.XValue, reciprocal), Multiply(value.YValue, reciprocal), Multiply(value.ZValue, reciprocal));
    }
    internal static FalloutPhysicalVector3 FootEnd(FalloutPhysicalVector3 first, FalloutPhysicalVector3 second)
    {
        var delta = FalloutPhysicalVector3.FromValues(Subtract(first.XValue, second.XValue), Subtract(first.YValue, second.YValue), Subtract(first.ZValue, second.ZValue));
        return (float)Math.Sqrt(Squared(delta)) < .5f ? default : delta;
    }
    internal static float Squared(FalloutPhysicalVector3 value) => Add(Add(Multiply(value.YValue, value.YValue), Multiply(value.XValue, value.XValue)), Multiply(value.ZValue, value.ZValue));
    internal static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
    internal static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    internal static float Add(float a, float b) => FalloutPhysicalFloat32.Add(a, b);
    internal static float Subtract(float a, float b) => FalloutPhysicalFloat32.Subtract(a, b);
    internal static float Multiply(float a, float b) => FalloutPhysicalFloat32.Multiply(a, b);
    internal static float Divide(float a, float b) => FalloutPhysicalFloat32.Divide(a, b);
}

internal sealed partial class FalloutLivingActorModel
{
    private void CalibrateFootIkLeg(FalloutLivingJointParameters knee, FalloutLivingJointParameters ankle,
        Func<string, FalloutIniValue> readSetting, Action check)
    {
        var state = _footIk ?? throw new InvalidOperationException("Foot calibration has no actual entered setup.");
        var maximumCosine = FalloutFootIkMath.MaximumKneeCosine(knee.Angular[0].Upper);
        var minimumCosine = FalloutFootIkMath.MinimumKneeCosine(knee.Angular[0].Lower);
        var ankleOffset = FalloutFootIkMath.Add(ankle.First.Pivot.XValue, ReadFootIkSetting("fAnkleOffset:FootIK", readSetting, check));
        var calf = state.Shapes[1].Parameters; var foot = state.Shapes[2].Parameters;
        if (calf is not { Kind: FalloutLivingPrimitiveShapeKind.Capsule, First: { } calfFirst, Second: { } calfSecond } ||
            foot is not { Kind: FalloutLivingPrimitiveShapeKind.Capsule, First: { } footFirst, Second: { } footSecond })
            throw new NotSupportedException("source-foot-IK-non-capsule-current-geometry-calibration-unowned");
        Set(setup => setup with { KneeAxis = knee.First.Axis });
        Set(setup => setup with { KneeAxis = FalloutFootIkMath.Normalize(setup.KneeAxis) });
        Set(setup => setup with { FootEnd = FalloutFootIkMath.FootEnd(footFirst, footSecond) });
        var minimumHeight = FalloutFootIkMath.Bits(ReadFootIkSetting("fMaxStepVertError:FootIK", readSetting, check)) ^ 0x80000000;
        Set(setup => setup with { MinimumHeight = minimumHeight });
        var maximumHeight = FalloutFootIkMath.Add(FalloutFootIkMath.Add(FalloutFootIkMath.Subtract(calfFirst.XValue, calfSecond.XValue),
            FalloutFootIkMath.Add(footFirst.XValue, footFirst.XValue)), ankleOffset);
        var raisedHeight = FalloutFootIkMath.Subtract(maximumHeight, FalloutFootIkMath.Add(ankleOffset, ankleOffset));
        Set(setup => setup with { MaximumHeight = FalloutFootIkMath.Bits(maximumHeight) });
        Set(setup => setup with { CosineMaximumKnee = maximumCosine });
        Set(setup => setup with { PlantedHeight = footFirst.X });
        Set(setup => setup with { RaisedHeight = FalloutFootIkMath.Bits(raisedHeight) });
        Set(setup => setup with { CosineMinimumKnee = minimumCosine });

        void Set(Func<FalloutFootIkSetup, FalloutFootIkSetup> store)
        {
            check(); _footIk = _footIk! with { Setup = store(_footIk.Setup ?? throw new InvalidDataException("Foot calibration lost its original setup fields.")) }; check();
        }
    }

    private float ReadFootIkSetting(string name, Func<string, FalloutIniValue> readSetting, Action check)
    {
        check(); var value = readSetting(name); check();
        if (value.Declaration.Collection != FalloutIniCollection.Main || value.Declaration.Name != name || value.Declaration.Kind != 'f' ||
            value.Number is not { } number || !double.IsFinite(number) || !float.IsFinite((float)number))
            throw new InvalidDataException("Foot IK selected a missing, foreign or nonfloat current Main INI setting.");
        _footIk = _footIk! with { Settings = [.. _footIk.Settings ?? [], new(name, FalloutFootIkMath.Bits((float)number), value.Origin)] };
        return (float)number;
    }
}
