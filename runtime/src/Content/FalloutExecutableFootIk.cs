using System.Buffers.Binary;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireFootIkInitialization(byte[] code, Image image, int ragdollCaller)
    {
        var initialize = Call(ragdollCaller + 0x4e5);
        var defaults = Call(initialize + 0x64); var calibrate = Call(initialize + 0x73);
        Need(defaults, [0x83, 0xc8, 0xff], "written minus-one setup bone indices");
        Need(defaults + 9, [0x66, 0x89, 0x41, 4, 0x66, 0x89, 0x41, 6, 0x66, 0x89, 0x41, 8], "three independent initial bone stores");
        var initial = FalloutFootIkSetup.Construct();
        foreach (var (at, field, value) in new (int, byte, uint)[]
        {
            (0x3f, 0x50, initial.GroundHeight), (0x46, 0x54, initial.PlantedHeight), (0x51, 0x58, initial.RaisedHeight),
            (0x58, 0x5c, initial.MaximumHeight), (0x5f, 0x60, initial.MinimumHeight), (0x66, 0x64, initial.CosineMaximumKnee),
            (0x6d, 0x68, initial.CosineMinimumKnee), (0x74, 0x6c, initial.RayDistanceUp), (0x7b, 0x70, initial.RayDistanceDown)
        })
        {
            Need(defaults + at, [0xc7, 0x41, field], "actual independent foot setup scalar constructor");
            if (Word(code, defaults + at + 3) != value) throw new NotSupportedException("Foot setup changed an original scalar default.");
        }
        Need(calibrate + 0x2a6, [0xf3, 0x0f, 0x10, 0x86, 0xc8, 0, 0, 0], "current knee hinge maximum field");
        Need(calibrate + 0x2ea, [0xf3, 0x0f, 0x10, 0x86, 0xc4, 0, 0, 0], "independent current knee hinge minimum field");
        Constant(calibrate + 0x2ae, FalloutFootIkMath.RadiansToDegrees, "actual radians-to-degrees operand");
        Constant(calibrate + 0x2ca, FalloutFootIkMath.DegreesToRadians, "actual degrees-to-radians operand");
        if (Call(calibrate + 0x2d5) != Call(calibrate + 0x315))
            throw new NotSupportedException("Foot knee limits changed their shared original cosine service.");
        Setting(calibrate + 0x3b7, "fAnkleOffset:FootIK");
        Setting(calibrate + 0x572, "fMaxStepVertError:FootIK");
        Need(calibrate + 0x3fe, [0xf3, 0x0f, 0x10, 0x45, 0xd8], "actual knee-axis copy before normalization");
        Need(calibrate + 0x52c, [0xf3, 0x0f, 0x10, 5], "source half-unit foot-end threshold");
        if (Word(image.Read(Word(code, calibrate + 0x530), 4), 0) != 0x3f000000)
            throw new NotSupportedException("Foot IK changed its source half-unit foot-end boundary.");
        Need(calibrate + 0x58b, [0xf3, 0x0f, 0x5c, 0x48, 0x20], "current calf endpoint extent");
        Need(calibrate + 0x595, [0xf3, 0x0f, 0x58, 0xc0], "foot first endpoint doubled before maximum height");
        Need(calibrate + 0x5a6, [0xf3, 0x0f, 0x58, 0xc0], "independent ankle-offset doubling for raised height");
        Setting(initialize + 0xb6, "fOriginalGroundHeightMS:FootIK");
        Need(initialize + 0x8c, [0x0f, 0x28, 5], "actual common world/model up vector");
        var up = image.Read(Word(code, initialize + 0x8f), 16);
        if (!Enumerable.Range(0, 4).Select(index => Word(up, index * 4)).SequenceEqual(new uint[] { 0, 0, 0x3f800000, 0 }))
            throw new NotSupportedException("Foot solver changed its current world/model up fields.");
        FloatOperand(initialize + 0x237, 0x40e00000, "current foot radius plus seven ray distance");
        FloatOperand(initialize + 0x23f, 0x40800000, "independent current foot radius plus four ray distance");
        var solver = Call(initialize + 0x353);
        Need(solver + 0xc, [0xb9, 0x20, 0, 0, 0], "actual setup-copy denominator");
        Need(solver + 0x23, [0xf3, 0xa5], "actual independent solver setup copy");
        Need(solver + 0x25, [0xc7, 0x82, 0x90, 0, 0, 0, 0, 0, 0, 0], "first solver accumulator constructor zero");
        Need(solver + 0x2f, [0xc7, 0x82, 0x94, 0, 0, 0, 0, 0, 0, 0], "independent second solver accumulator zero");
        Need(solver + 0x39, [0x0f, 0x10, 0x40, 0x30], "initial solver ground normal consumes its own setup up vector");
        Need(solver + 0x51, [0xc6, 0x82, 0xb4, 0, 0, 0, 0], "independent solver ground byte initially false");
        Need(initialize + 0x45e, [0x88, 0x86, 0x1f, 2, 0, 0], "actual foot-availability store after constructed solver pointers");
        Need(ragdollCaller + 0x4f0, [0x80, 0xb9, 0x1f, 2, 0, 0, 0], "actual independent current foot-availability read");
        Need(ragdollCaller + 0x4fa, [0x88, 0x81, 0x20, 2, 0, 0], "actual foot-enable byte store");
        Need(ragdollCaller + 0x506, [0x80, 0xb9, 0x1c, 2, 0, 0, 0], "independent current arm-availability read");
        Need(ragdollCaller + 0x510, [0x88, 0x81, 0x1d, 2, 0, 0], "actual arm-enable byte store after foot enable");

        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Foot IK source lost an actual direct child.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Foot IK changed its " + role + "."); }
        void Constant(int at, uint expected, string role)
        {
            Need(at, [0xf3, 0x0f, 0x59], role);
            if (Word(image.Read(Word(code, at + 4), 4), 0) != expected) throw new NotSupportedException("Foot IK changed its " + role + ".");
        }
        void Setting(int at, string name)
        {
            Need(at, [0xf3, 0x0f], "current Main INI float operation");
            if (image.Literal(Word(image.Read(Word(code, at + 4) + 4, 4), 0)) != name)
                throw new NotSupportedException("Foot IK changed its actual current INI setting identity.");
        }
        void FloatOperand(int at, uint value, string role)
        {
            Need(at, [0xf3, 0x0f, 0x58], role);
            if (Word(image.Read(Word(code, at + 4), 4), 0) != value) throw new NotSupportedException("Foot IK changed its " + role + ".");
        }
    }
}
