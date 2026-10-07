using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class RaceMenuDevicesProbe
{
    internal static void Run()
    {
        NativeRaceMenuModelContracts.Run();
        const uint pluginBase = 1000, nativeBase = 4000, nativeHandler = 4020, buffer = 2500, copy = 2100, protect = 2200;
        var code = Enumerable.Repeat((byte)0x90, 600).ToArray();
        var native = Enumerable.Repeat((byte)0x90, 128).ToArray();
        var strings = new Dictionary<uint, string> { [10] = "Terminals/alternate.nif", [11] = "Terminals/initial.nif", [12] = "ShowRaceMenu" };
        var imports = new Dictionary<uint, string> { [copy] = "strcpy_s", [protect] = "VirtualProtect" };
        string initial = strings[11];
        bool validBuffer = true;
        void Write(int at, params byte[] bytes) => bytes.CopyTo(code, at);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
        void Selector(int at, uint name)
        {
            Write(at, 0x55, 0x8b, 0xec, 0x68); U32(at + 4, name); code[at + 8] = 0x68; U32(at + 9, 260);
            code[at + 13] = 0x68; U32(at + 14, buffer); Write(at + 18, 0xff, 0x15); U32(at + 20, copy);
            Write(at + 24, 0xff, 0x75, 0x24, 0xb8); U32(at + 28, nativeHandler);
            for (var index = 0; index < 7; ++index) Write(at + 32 + index * 3, 0xff, 0x75, (byte)(0x20 - index * 4));
            Write(at + 53, 0xff, 0xd0, 0x83, 0xc4, 0x2c, 0xb0, 1, 0x5d, 0xc3);
        }
        Selector(50, 10); Selector(200, 11);
        Write(300, 0x6a, 3, 0xff, 0x57, 0x18, 0x68); U32(306, 12); code[310] = 0xa3; U32(311, 2800);
        Write(315, 0xff, 0x50, 0x10, 0x83, 0xc4, 0x14, 0xbe, 0xeb, 0, 0, 0, 0x8b, 0xd6, 0xb9);
        U32(329, 5000); Write(333, 0xc7, 0x40, 0x18); U32(336, pluginBase + 200);
        code[400] = 0xba; U32(401, buffer); code[405] = 0xb9; U32(406, nativeBase + 43); code[410] = 0xe8; U32(411, 450 - 415);
        Write(450, 0x55, 0x8b, 0xec, 0x51, 0x53, 0x57, 0x8d, 0x45, 0xfc, 0x8b, 0xd9, 0x50, 0x6a, 0x40,
            0x6a, 4, 0x53, 0x8b, 0xfa, 0xff, 0x15); U32(471, protect);
        Write(475, 0x8d, 0x45, 0xfc, 0x89, 0x3b, 0x50, 0xff, 0x75, 0xfc, 0x6a, 4, 0x53, 0xff, 0x15); U32(489, protect);
        Write(493, 0x5f, 0x5b, 0xc9, 0xc3);
        new byte[] { 0x6a, 0, 0x68 }.CopyTo(native, 40); BinaryPrimitives.WriteUInt32LittleEndian(native.AsSpan(43), 11);
        new byte[] { 0x8b, 0x8d }.CopyTo(native, 47); new byte[] { 0x81, 0xc1 }.CopyTo(native, 53); native[59] = 0xe8;
        FalloutRaceMenuDevices Read() => FalloutExecutableStringTable.ReadTtwRaceMenuAssociations(code, pluginBase, 50,
            pointer => strings.GetValueOrDefault(pointer), (pointer, count) => pointer == buffer && count == 260 && validBuffer || pointer == 2800 && count == 4,
            (pointer, count) => pointer == buffer && count == 260 ? initial : throw new InvalidDataException("Foreign buffer."),
            pointer => imports.GetValueOrDefault(pointer), native, nativeBase, nativeHandler, pointer => strings.GetValueOrDefault(pointer));
        var devices = Read();
        Require(devices.ModelFor("TTW_ShowGeneProjector") == "meshes/terminals/alternate.nif" &&
            devices.ModelFor("ShowRaceMenu") == "meshes/terminals/initial.nif", "Source selectors substituted model identities.");
        Require(devices.ModelFor("ShowRaceMenu") == devices.DefaultModel, "Gene selection did not preserve the next normal menu's reset.");
        Reject(() => devices.ModelFor("other"));
        validBuffer = false; Reject(() => Read()); validBuffer = true;
        imports[copy] = "foreign"; Reject(() => Read()); imports[copy] = "strcpy_s";
        imports[protect] = "foreign"; Reject(() => Read()); imports[protect] = "VirtualProtect";
        initial = strings[10]; Reject(() => Read()); initial = strings[11];
        strings[10] = "../escaped.nif"; Reject(() => Read()); strings[10] = "Terminals/alternate.nif";
        U32(50 + 28, nativeHandler + 1); Reject(() => Read()); U32(50 + 28, nativeHandler);
        U32(200 + 14, buffer + 1); Reject(() => Read()); U32(200 + 14, buffer);
        code[50 + 52] = 9; Reject(() => Read()); code[50 + 52] = 8;
        code[333] = 0x90; Reject(() => Read()); code[333] = 0xc7;
        U32(406, nativeBase + 1); Reject(() => Read()); U32(406, nativeBase + 43);
        native[59] = 0x90; Reject(() => Read()); native[59] = 0xe8;
        strings[11] = "Terminals/different.nif"; Reject(() => Read()); strings[11] = initial;
        code.AsSpan(300, 40).CopyTo(code.AsSpan(540)); Reject(() => Read());
        Console.WriteLine("OPENNV_RACE_MENU_DEVICES_PASS ownedSelectors=true sharedBuffer=true nativeForward=true defaultReset=true originalConsumer=true invalidRejected=true");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; } throw new InvalidDataException("Invalid race-menu declaration was admitted."); }
}
