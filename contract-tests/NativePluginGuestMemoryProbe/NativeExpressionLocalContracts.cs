using System.Buffers.Binary;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;

internal static class NativeExpressionLocalContracts
{
    internal static void Run()
    {
        static NativeNvseExpressionValue NoLocal(byte type, ushort reference, ushort index)
            => throw new NotSupportedException("Authored source grammar has no native event-list.");
        var zero = FalloutNativePluginExpressionStream.Read(new byte[] { 0 }, 31, NoLocal);
        Require(zero.EndOffset == 31 && zero.Values.Count == 0, "Zero-argument external source cursor advanced.");
        Require(FalloutNativePluginExpressionStream.Read(new byte[] { 0 }, uint.MaxValue, NoLocal).EndOffset == uint.MaxValue,
            "Zero-argument external cursor overflowed its unchanged coordinate.");
        var bytes = Frame([[(byte)'b', 254], [(byte)'I', 0x34, 0x12], [(byte)'L', 0xff, 0xff, 0xff, 0xff],
            Double(3.25), [(byte)'S', 3, 0, (byte)'a', (byte)'b', (byte)'c'], [(byte)'S', 0, 0]]);
        var original = bytes.ToArray(); var values = FalloutNativePluginExpressionStream.Read(bytes, 11, NoLocal);
        Require(values.EndOffset == 11 + bytes.Length - 1 && values.Values.Count == 6 &&
            values.Values[0].Number == 254 && values.Values[1].Number == 0x1234 && values.Values[2].Number == uint.MaxValue &&
            values.Values[3].Number == 3.25 && values.Values[4].Text.AsSpan().SequenceEqual("abc"u8) &&
            values.Values[5].Text.Length == 0, "Source leaf extents/unsigned/double/string values drifted.");
        Require(bytes.AsSpan().SequenceEqual(original), "Source grammar changed its original input.");

        // This sentinel observes the actual encoded V tuple; it does not
        // publish a native cell or stand in for a Script/EventList object.
        for (byte type = 0; type < 5; ++type)
        {
            var observed = false;
            try
            {
                _ = FalloutNativePluginExpressionStream.Read(Frame([[(byte)'V', type, 0x34, 0x12, 8, 0]]), 0,
                    (actualType, reference, index) =>
                    {
                        Require(actualType == type && reference == 0x1234 && index == 8, "V tuple/identity drifted.");
                        observed = true; throw new LocalObserved();
                    });
                throw new InvalidOperationException("Source V did not reach its actual local factory.");
            }
            catch (LocalObserved) { Require(observed, "Variable tuple was not observed."); }
        }
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(Array.Empty<byte>(), 0, NoLocal));
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(new byte[] { 1, 1, 0 }, 0, NoLocal));
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(new byte[] { 1, 5, 0, (byte)'Z' }, 0, NoLocal));
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(Frame([[(byte)'Z', 0]]), 0, NoLocal));
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(new byte[] { 0, 0 }, 0, NoLocal));
        Reject<NotSupportedException>(() => FalloutNativePluginExpressionStream.Read(Frame([[(byte)'R', 1, 0]]), 0, NoLocal));
        Reject<NotSupportedException>(() => FalloutNativePluginExpressionStream.Read(Frame([[(byte)'b', 1, 0]]), 0, NoLocal));
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(Frame([[(byte)'S', 2, 0, (byte)'x']]), 0, NoLocal));
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(Frame([[(byte)'S', 1, 0, 0]]), 0, NoLocal));
        Reject<InvalidDataException>(() => FalloutNativePluginExpressionStream.Read(Frame([Double(double.NaN)]), 0, NoLocal));
        Reject<OverflowException>(() => FalloutNativePluginExpressionStream.Read(Frame([[(byte)'b', 1]]), uint.MaxValue, NoLocal));
        Console.WriteLine("OPENNV_NVSE_LOCAL_TOKEN_SOURCE_PASS nativeExecution=unexecuted nativeObjects=unexecuted");
    }

    private sealed class LocalObserved : Exception { }
    private static byte[] Double(double number)
    {
        var bytes = new byte[9]; bytes[0] = (byte)'Z';
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(1), BitConverter.DoubleToInt64Bits(number)); return bytes;
    }
    private static byte[] Frame(byte[][] arguments)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)arguments.Length));
        foreach (var argument in arguments) { writer.Write(checked((ushort)(argument.Length + 2))); writer.Write(argument); }
        return stream.ToArray();
    }
    private static void Require(bool condition, string failure)
    { if (!condition) throw new InvalidOperationException(failure); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected source refusal " + typeof(T).Name);
    }
}
