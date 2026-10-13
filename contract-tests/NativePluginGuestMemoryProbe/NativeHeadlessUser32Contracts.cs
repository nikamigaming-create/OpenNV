using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeHeadlessUser32Contracts
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("OPENNV_HEADLESS_USER32_CONTRACT_SKIPPED actualWindows=absent");
            return;
        }
        var input = FalloutDirectInputState.CreateHeadless(new('4', 64), new(_ => null));
        var commands = new FalloutHeadlessInput(input); commands.Key(17, true); commands.Publish();
        var before = JsonSerializer.Serialize(input.Capture());
        var capability = new NativePluginHeadlessUser32(input);
        Require(capability.Window == 0 && capability.Thread == GetCurrentThreadId() && capability.Process == Environment.ProcessId &&
            capability.SourceIdentity == input.Source, "genuine product thread/input and absent HWND");
        var lease = capability.Retain();
        try
        {
            Require(input.NativeReaders == 1, "actual native input lifetime retention");
            Reject<InvalidOperationException>(input.Retire);
            var layout = capability.Execute(NativePluginWindowOperation.GetKeyboardLayout, 0, 0x1234, capability.Thread, new byte[4]);
            Require(layout.Result == NativePluginProductWindow.HandleBits(GetKeyboardLayout(0)) && layout.Result != 0 && layout.Bytes.Length == 0,
                "actual current-thread keyboard layout SDK result");
            var mapping = new byte[12]; Word(mapping, 0, 'A'); Word(mapping, 8, layout.Result);
            var mapped = capability.Execute(NativePluginWindowOperation.MapVirtualKey, 0, 0x1234, capability.Thread, mapping);
            Require(mapped.Result == MapVirtualKeyExA('A', 0, Pointer(layout.Result)) && mapped.Bytes.Length == 0,
                "layout-bound SDK mapping with no invented scan code");
            var conversion = Conversion(layout.Result, mapped.Result, preserve: true);
            var original = conversion.ToArray();
            var directKeys = new byte[256]; var directCharacters = Enumerable.Repeat((char)0x7b7b, 8).ToArray();
            var expected = ToUnicodeEx('A', mapped.Result, directKeys, directCharacters, directCharacters.Length, 4, Pointer(layout.Result));
            var converted = capability.Execute(NativePluginWindowOperation.ToUnicode, 0, 0x1234, capability.Thread, conversion);
            Require(converted.Result == unchecked((uint)expected) && converted.Bytes.AsSpan(280).SequenceEqual(
                MemoryMarshal.AsBytes(directCharacters.AsSpan())) && converted.Bytes.AsSpan(0, 280).SequenceEqual(original.AsSpan(0, 280)) &&
                conversion.AsSpan().SequenceEqual(original), "caller-supplied VK buffer and exact SDK Unicode output preserve input bytes");
            capability.RequireColdCapture();

            var keyboard = Enumerable.Repeat((byte)0xa5, 256).ToArray();
            Reject<NotSupportedException>(() => capability.Execute(NativePluginWindowOperation.GetKeyboardState, 0, 0, capability.Thread, keyboard),
                "no Win32 message-queue virtual-key state producer");
            Require(keyboard.All(value => value == 0xa5), "unowned keyboard state cannot overwrite a caller's output or invent zeros");
            Reject<NotSupportedException>(() => capability.Execute(NativePluginWindowOperation.GetForeground, 0, 0, capability.Thread, []),
                "headless play owns no HWND");
            Reject<NotSupportedException>(() => capability.Execute(NativePluginWindowOperation.SendInput, 0, 0, capability.Thread, []),
                "headless play owns no HWND");
            Reject<InvalidDataException>(() => capability.Execute(NativePluginWindowOperation.GetKeyboardLayout, 0, 0, capability.Thread, new byte[3]));
            Reject<InvalidDataException>(() => capability.Execute(NativePluginWindowOperation.ToUnicode, 0, 0, capability.Thread, conversion[..^1]));
            var unobserved = mapping.ToArray(); Word(unobserved, 8, layout.Result == 1 ? 2U : 1U);
            Reject<NotSupportedException>(() => capability.Execute(NativePluginWindowOperation.MapVirtualKey, 0, 0, capability.Thread, unobserved),
                "no actual source/thread SDK observation");
            Reject<InvalidDataException>(() => capability.Execute(NativePluginWindowOperation.MapVirtualKey, 99, 0, capability.Thread, mapping));
            Exception? foreignThread = null;
            var worker = new Thread(() => { try { capability.Require(); } catch (Exception error) { foreignThread = error; } });
            worker.Start(); worker.Join(); Require(foreignThread is InvalidOperationException, "real thread lifetime rejects cross-thread execution");
            Require(JsonSerializer.Serialize(input.Capture()) == before, "USER32 capabilities never manufacture gameplay command state");

            _ = capability.Execute(NativePluginWindowOperation.ToUnicode, 0, 0, capability.Thread,
                Conversion(layout.Result, mapped.Result, preserve: false));
            Reject<NotSupportedException>(capability.RequireColdCapture, "conversion buffer has no cold-state producer");
            capability.RetireBeforeChildExit(capability.Thread);
            Reject<InvalidOperationException>(capability.RetireAfterChildExit);
        }
        finally { lease.Dispose(); capability.RetireAfterChildExit(); input.Retire(); }
        Require(input.NativeReaders == 0, "native reader closes exactly once before input retirement");
        Reject<ObjectDisposedException>(capability.Require);
        Console.WriteLine("OPENNV_HEADLESS_USER32_CONTRACT_PASS HWND=NONE actualProductThread=true actualSDKLayoutAndConversion=true " +
            "headlessVkState=REFUSED windowOperations=REFUSED originalBuffers=true gameplayInputUnchanged=true coldAndRetirement=true");
    }
    private static byte[] Conversion(uint layout, uint scan, bool preserve)
    {
        var bytes = new byte[280 + 8 * sizeof(char)];
        Word(bytes, 0, 'A'); Word(bytes, 4, scan); Word(bytes, 12, 8); Word(bytes, 16, preserve ? 4U : 0U); Word(bytes, 20, layout);
        bytes.AsSpan(280).Fill(0x7b); return bytes;
    }
    private static void Word(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    private static nint Pointer(uint value) => new(unchecked((int)value));
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidDataException("Headless USER32 contract: " + reason); }
    private static void Reject<T>(Action action, string? expected = null) where T : Exception
    {
        try { action(); }
        catch (T error) when (expected is null || error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Headless USER32 contract lost refusal " + typeof(T).Name + ": " + expected);
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint MapVirtualKeyExA(uint code, uint kind, nint layout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int ToUnicodeEx(uint key, uint scan, byte[] state,
        [In, Out] char[] characters, int count, uint flags, nint layout);
}
