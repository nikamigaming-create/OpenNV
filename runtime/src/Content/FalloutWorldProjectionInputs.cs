using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutWorldProjectionInputs(FalloutPlayerWorldOrientationSource Source, string Stack,
    FalloutIniValue FarMinimum, FalloutIniValue FarMaximum, FalloutIniValue FarFraction, FalloutIniValue NearDistance)
{
    internal const string MinimumName = "fNoLODFarDistanceMin:Display";
    internal const string MaximumName = "fNoLODFarDistanceMax:Display";
    internal const string FractionName = "fNoLODFarDistancePct:Display";
    internal const string NearName = "fNearDistance:Display";
    internal static FalloutWorldProjectionInputs Read(FalloutPlayerWorldOrientationSource source, string stack, FalloutNumericIniSettings settings)
    {
        source.ReadConstructorFov(settings, stack);
        var result = new FalloutWorldProjectionInputs(source, stack,
            Setting(MinimumName, 0x44d48000), Setting(MaximumName, 0x46200000), Setting(FractionName, 0x3f800000), Setting(NearName, 0x40a00000));
        result.Validate(); return result;
        FalloutIniValue Setting(string name, uint initial) => settings.Find(FalloutIniCollection.Main, name) is { } value &&
            value.Declaration.Payload == initial ? value : throw new NotSupportedException("Source world projection has no exact Main setting: " + name);
    }
    internal uint ReadBaseFarBits()
    {
        Validate();
        var difference = FalloutPhysicalFloat32.Subtract((float)FarMaximum.Number!.Value, (float)FarMinimum.Number!.Value);
        var fraction = FalloutPhysicalFloat32.Subtract((float)FarFraction.Number!.Value, 0f);
        return BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Multiply(difference, fraction), (float)FarMinimum.Number!.Value));
    }
    internal void Validate()
    {
        Source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(Stack);
        Setting(FarMinimum, MinimumName, 0x44d48000); Setting(FarMaximum, MaximumName, 0x46200000);
        Setting(FarFraction, FractionName, 0x3f800000); Setting(NearDistance, NearName, 0x40a00000);
        static void Setting(FalloutIniValue value, string name, uint initial)
        {
            if (value.Declaration.Name != name || value.Declaration.Collection != FalloutIniCollection.Main ||
                value.Declaration.Payload != initial || value.Number is not { } number || !double.IsFinite(number) ||
                (double)(float)number != number || string.IsNullOrWhiteSpace(value.Origin))
                throw new InvalidDataException("Source world projection changed a typed effective Main clip setting: " + name);
        }
    }
}
