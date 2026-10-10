using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunUtilityStartupEnvelopes()
    {
        var fixture = new StartupArgumentTransport();
        var sourceHash = Convert.ToHexString(SHA256.HashData(fixture.Code));
        var declaration = fixture.Read();
        Require(declaration.Option == "FixtureSwitch" && declaration.ReceiverByteField == 23 &&
            FalloutAdvancementRuntimeReceipt.Digest(declaration.DeclarationSha256),
            "Source option lost its complete import/caller/pure-byte association.");
        using (var world = new Fixture())
        {
            var source = FalloutPlatformStartupArgumentSource.Read(world.Owner.MainUtilitySource,
                declaration.Option, declaration.DeclarationSha256);
            Require(source.Matches("-fIxTuReSwItCh") && !source.Matches("FixtureSwitch") &&
                !source.Matches("-Fixture") && !source.Matches("-FixtureSwitchExtra") && !source.Matches(null),
                "The original source whole ASCII suffix contract changed.");
            Reject(() => source.Matches("-F\u0131xtureSwitch"));
        }

        // Different receiver registers and a full displacement remain pure
        // transport. E8 and POP/RET-looking bytes inside that displacement do
        // not become an instruction, another method or an effect.
        var wide = StartupPureSetter(2, 0, 0x00c35de8, true);
        var decoded = FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(wide);
        Require(decoded.ReceiverByteField == 0x00c35de8 && decoded.Bytes == wide.Length,
            "Source byte setter read its displacement as another instruction.");
        var reserved = StartupPureSetter(0, 1, 19, false, true);
        Require(FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(reserved).ReceiverByteField == 19,
            "Equivalent four-byte reserved frame lost the actual receiver.");

        var changed = fixture.Code.ToArray();
        // The exact selected setter is unknown, rather than the unrelated
        // indirect-call/IMUL methods preceding and following the true caller.
        fixture.SetSetter([0x55, 0x8b, 0xec, 0x51, 0xff, 0xd0, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0]);
        Reject(() => fixture.Read()); fixture.Restore(changed);
        fixture.SetSetter([0x55, 0x8b, 0xec, 0x51, 0x6b, 0xc0, 7, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0]);
        Reject(() => fixture.Read()); fixture.Restore(changed);
        var extraCall = StartupPureSetter(0, 1, 23, false).ToList();
        extraCall.InsertRange(16, [0xe8, 0, 0, 0, 0]); fixture.SetSetter(extraCall.ToArray());
        Reject(() => fixture.Read()); fixture.Restore(changed);
        var bad = StartupPureSetter(0, 1, 23, false); bad[6] = 0xf8;
        Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(bad));
        bad = StartupPureSetter(0, 1, 23, false); bad[9] = 0xf8;
        Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(bad));
        bad = StartupPureSetter(0, 1, 23, false); bad[12] = 12;
        Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(bad));
        bad = StartupPureSetter(0, 1, 23, false); bad[14] = 0x4a;
        Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(bad));
        bad = StartupPureSetter(0, 1, 23, false); bad[20] = 8;
        Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(bad));
        Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(StartupPureSetter(1, 1, 23, false)));
        Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(StartupPureSetter(0, 4, 23, false)));
        var complete = StartupPureSetter(0, 1, 23, false);
        for (var bytes = 0; bytes < complete.Length; bytes++)
        {
            var count = bytes;
            Reject(() => FalloutExecutableStringTable.ReadMainUtilityPureByteSetter(complete.AsSpan(0, count)));
        }

        fixture.Imports[StartupArgumentTransport.CommandLineImport] = "UnownedCommandLine";
        Reject(() => fixture.Read()); fixture.Imports[StartupArgumentTransport.CommandLineImport] = "GetCommandLineW";
        fixture.Imports[StartupArgumentTransport.ArgumentsImport] = "UnownedArguments";
        Reject(() => fixture.Read()); fixture.Imports[StartupArgumentTransport.ArgumentsImport] = "CommandLineToArgvW";
        fixture.Code[StartupArgumentTransport.ImportPair + 6] = 0x51;
        Reject(() => fixture.Read()); fixture.Restore(changed);
        fixture.Code[StartupArgumentTransport.OptionPush + 20] = 15;
        Reject(() => fixture.Read()); fixture.Restore(changed);
        fixture.Code[StartupArgumentTransport.OptionPush + 7] = 4;
        Reject(() => fixture.Read()); fixture.Restore(changed);
        fixture.Tokens[StartupArgumentTransport.Option] = "F\u0131xtureSwitch";
        Reject(() => fixture.Read()); fixture.Tokens[StartupArgumentTransport.Option] = "FixtureSwitch";
        fixture.Tokens.Remove(StartupArgumentTransport.Option);
        Reject(() => fixture.Read()); fixture.Tokens[StartupArgumentTransport.Option] = "FixtureSwitch";
        fixture.AddOption(152, StartupArgumentTransport.Option);
        Reject(() => fixture.Read()); fixture.Restore(changed);
        StartupWriteRelative(fixture.Code, StartupArgumentTransport.OptionPush + 30, fixture.Code.Length + 1);
        Reject(() => fixture.Read()); fixture.Restore(changed);
        Require(Convert.ToHexString(SHA256.HashData(fixture.Code)) == sourceHash,
            "Ordinary declaration reads modified their original authored byte input.");
        Console.WriteLine("OPENNV_MAIN_UTILITY_STARTUP_ENVELOPE_PASS unrelatedMethodsRejectedBeforeAdmission=true " +
            "selectedPureThiscall=true sourceOptionImports=true selectedUnknownRefused=true " +
            "completeByteTransport=true instructionOperandsPreserved=true unchangedInput=true originalExecution=UNEXECUTED");
    }

    private static byte[] StartupPureSetter(int receiver, int argument, int field, bool wide, bool reserve = false)
    {
        var result = new List<byte> { 0x55, 0x8b, 0xec };
        result.AddRange(reserve ? new byte[] { 0x83, 0xec, 4 } : new byte[] { 0x51 });
        result.AddRange([0x89, 0x4d, 0xfc, 0x8b, (byte)(0x45 | receiver << 3), 0xfc,
            0x8a, (byte)(0x45 | argument << 3), 8, 0x88, (byte)((wide ? 0x80 : 0x40) | argument << 3 | receiver)]);
        if (wide) StartupWord(result, unchecked((uint)field)); else result.Add(unchecked((byte)field));
        result.AddRange([0x8b, 0xe5, 0x5d, 0xc2, 4, 0]); return result.ToArray();
    }
    private static void StartupWord(List<byte> output, uint value)
    {
        var data = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value); output.AddRange(data);
    }
    private static void StartupWriteRelative(byte[] code, int at, int destination)
    {
        code[at] = 0xe8; BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(at + 1), checked(destination - at - 5));
    }
    private sealed class StartupArgumentTransport
    {
        internal const uint CommandLineImport = 0x2400, ArgumentsImport = 0x2410, Option = 0x2800;
        internal const int ImportPair = 72, OptionPush = 104, Setter = 512;
        internal byte[] Code { get; } = Enumerable.Repeat((byte)0x90, 1024).ToArray();
        internal Dictionary<uint, string> Imports { get; } = new()
        { [CommandLineImport] = "GetCommandLineW", [ArgumentsImport] = "CommandLineToArgvW" };
        internal Dictionary<uint, string> Tokens { get; } = new() { [Option] = "FixtureSwitch" };
        internal StartupArgumentTransport()
        {
            // Unrelated prefix+ret4 candidates are deliberately effectful and
            // contain compiler families absent in the old heuristic scanner.
            AddUnrelated(16, 256, [0x55, 0x8b, 0xec, 0x51, 0xff, 0xd0, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0]);
            Code[64] = 0x55; Code[65] = 0x8b; Code[66] = 0xec;
            Code[ImportPair] = 0xff; Code[ImportPair + 1] = 0x15;
            BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(ImportPair + 2), CommandLineImport);
            Code[ImportPair + 6] = 0x50; Code[ImportPair + 7] = 0xff; Code[ImportPair + 8] = 0x15;
            BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(ImportPair + 9), ArgumentsImport);
            AddOption(OptionPush, Option); SetSetter(StartupPureSetter(0, 1, 23, false));
            AddUnrelated(208, 320, [0x55, 0x8b, 0xec, 0x51, 0x6b, 0xc0, 9, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0]);
        }
        internal FalloutMainUtilityStartupArgumentDeclaration Read() =>
            FalloutExecutableStringTable.ReadMainUtilityStartupArgumentDeclaration(Code,
                address => Tokens.GetValueOrDefault(address), address => Imports.GetValueOrDefault(address));
        internal void Restore(byte[] saved) => saved.CopyTo(Code, 0);
        internal void SetSetter(byte[] body)
        {
            Array.Fill(Code, (byte)0x90, Setter, 64); body.CopyTo(Code, Setter);
        }
        internal void AddOption(int push, uint option)
        {
            Code[push] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(push + 1), option);
            new byte[] { 0x8b, 0x4d, 0xf8, 0x51 }.CopyTo(Code, push + 5);
            StartupWriteRelative(Code, push + 9, 900);
            new byte[] { 0x83, 0xc4, 8, 0x85, 0xc0, 0x75, 14, 0x6a, 0 }.CopyTo(Code, push + 14);
            StartupWriteRelative(Code, push + 23, 896);
            Code[push + 28] = 0x8b; Code[push + 29] = 0xc8;
            StartupWriteRelative(Code, push + 30, Setter);
        }
        private void AddUnrelated(int call, int method, byte[] bytes)
        {
            Code[call] = 0x6a; Code[call + 1] = 0;
            StartupWriteRelative(Code, call + 2, 896); Code[call + 7] = 0x8b; Code[call + 8] = 0xc8;
            StartupWriteRelative(Code, call + 9, method); bytes.CopyTo(Code, method);
        }
    }
}
