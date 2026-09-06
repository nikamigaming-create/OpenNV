using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class FloatInitializerContracts
{
    internal static void Run()
    {
        // Synthetic addresses, identity and scalar deliberately differ from
        // the owned corpus. Both compiler layouts must retain the same binding.
        byte[] Build(bool framed)
        {
            var data = new byte[framed ? 28 : 25];
            if (framed) new byte[] { 0x55, 0x8b, 0xec, 0x51, 0xd9, 0x05 }.CopyTo(data, 0);
            else new byte[] { 0xd9, 0x05 }.CopyTo(data, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(framed ? 6 : 2), 0x790);
            (framed ? new byte[] { 0xd9, 0x1c, 0x24, 0x68 } : [0x51, 0xd9, 0x1c, 0x24, 0x68])
                .CopyTo(data, framed ? 10 : 6);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(framed ? 14 : 11), 0x450);
            data[framed ? 18 : 15] = 0xb9;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(framed ? 19 : 16), 0x860);
            data[framed ? 23 : 20] = 0xe8;
            return data;
        }
        IReadOnlyDictionary<string, float> Read(byte[] bytes, bool writable = true, float value = -0.03125f) =>
            FalloutExecutableStringTable.ReadFloatInitializers(bytes,
                address => address == 0x450 ? "fSyntheticRotation:Probe" : null,
                address => writable && address == 0x860,
                address => address == 0x790 ? value : throw new Exception("Wrong scalar association."));
        foreach (var framed in new[] { false, true })
        {
            var data = Build(framed);
            if (Read(data)["fSyntheticRotation:Probe"] != -0.03125f || Read(data, false).Count != 0)
                throw new Exception("Float initializer lost its compiler-owned association.");
            for (var count = 0; count < data.Length; ++count)
                if (Read(data[..count]).Count != 0) throw new Exception("Truncated float initializer admitted.");
            Reject(() => Read(data, value: float.NaN));
            data[framed ? 23 : 20] = 0xe9;
            if (Read(data).Count != 0) throw new Exception("Non-constructor tail admitted.");
        }
        Reject(() => Read(Build(false).Concat(Build(true)).ToArray()));
        foreach (var operation in new byte[] { 0xe8, 0xee })
        {
            byte[] unit = [0xd9, operation, 0x51, 0xd9, 0x1c, 0x24, 0xb9, 0x60, 8, 0, 0,
                0x68, 0x50, 4, 0, 0, 0xe8, 0, 0, 0, 0];
            if (Read(unit)["fSyntheticRotation:Probe"] != (operation == 0xe8 ? 1 : 0))
                throw new Exception("Encoded floating constant lost its value.");
            for (var count = 0; count < unit.Length; ++count)
                if (Read(unit[..count]).Count != 0) throw new Exception("Truncated constant initializer admitted.");
            Reject(() => Read(unit.Concat(Build(true)).ToArray()));
        }
        Console.WriteLine("Float default frame and optimized initializer contracts passed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Ambiguous or non-finite float default admitted.");
    }
}
