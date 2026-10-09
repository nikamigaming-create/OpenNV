using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class InterfaceActivationFrameContracts
{
    internal static void Run()
    {
        using var fixture = new Fixture();
        using var owner = fixture.Owner();
        Require(owner.Observe().State == FalloutAdvancementActivityState.Unowned,
            "Interface construction fabricated a living source getter.");
        var session = owner.AttachSession();
        Require(owner.Observe().State == FalloutAdvancementActivityState.Satisfied,
            "Actual interface constructor's false flag did not survive its native session lease.");
        Reject(() => owner.EnterContainerFactory(Guid.NewGuid(), fixture.Reference));
        Require(owner.Capture().FactorySequence == 0, "Foreign session consumed a source factory prefix.");
        var actual = fixture.Activation(fixture.Reference, 41);
        owner.UnlockedContainerActivation(session, actual);
        Require(owner.Observe().State == FalloutAdvancementActivityState.Held &&
            owner.SaveBlocker == "unlocked-container-activation-continuation",
            "A real unlocked-container activation skipped its pending interface transition.");
        Reject(() => owner.Capture());
        owner.EnterContainerFactory(session, fixture.Reference);
        var clear = owner.Capture();
        Require(!clear.Pending && clear.FactorySequence == 1 && clear.LastFactoryReference == fixture.Reference,
            "The actual factory entry did not retire its exact source activation before later presentation.");
        using var cold = fixture.Owner(JsonSerializer.Deserialize<FalloutInterfaceActivationFrameSnapshot>(
            JsonSerializer.Serialize(clear))!);
        Reject(() => cold.EnterContainerFactory(session, fixture.Reference));
        Require(cold.Observe().State == FalloutAdvancementActivityState.Unowned,
            "A cold flag admitted the previous process's native session lease.");
        cold.AttachSession();
        Require(cold.Observe().State == FalloutAdvancementActivityState.Satisfied,
            "Cold source initialization lost the actual false-state continuation.");
        Reject(() => fixture.Owner(clear with { Pending = true }));
        Reject(() => fixture.Owner(clear with { Contract = new('f', 64) }));

        // Independent readers admit the actual winning override and adjusted
        // master identity. Raw payload/source-state substitutions must fail.
        using var drift = fixture.Owner(); var driftSession = drift.AttachSession();
        Reject(() => drift.UnlockedContainerActivation(driftSession, actual with { BaseSha256 = new('0', 64) }));
        Require(drift.Observe().State == FalloutAdvancementActivityState.Unowned,
            "A changed winning container declaration still reported a clean frame.");
        using var foreign = fixture.Owner(); var foreignSession = foreign.AttachSession();
        Reject(() => foreign.UnlockedContainerActivation(foreignSession,
            actual with { Base = new("Other.esm", actual.Base.ObjectId) }));
        using var wrongType = fixture.Owner(); var wrongTypeSession = wrongType.AttachSession();
        Reject(() => wrongType.UnlockedContainerActivation(wrongTypeSession, fixture.Activation(fixture.Activator, 42)));
        using var disabled = fixture.Owner(); var disabledSession = disabled.AttachSession();
        Reject(() => disabled.UnlockedContainerActivation(disabledSession, fixture.Activation(fixture.Disabled, 43)));
        using var mismatchedFactory = fixture.Owner(); var mismatchedSession = mismatchedFactory.AttachSession();
        mismatchedFactory.UnlockedContainerActivation(mismatchedSession, actual);
        Reject(() => mismatchedFactory.EnterContainerFactory(mismatchedSession, fixture.Unrelated));
        Require(mismatchedFactory.SaveBlocker == "interface-activation-frame-failure" &&
            mismatchedFactory.Observe().State == FalloutAdvancementActivityState.Unowned,
            "An unrelated factory erased the actual source owner instead of retaining its failure.");
        using var reused = fixture.Owner(); var reusedSession = reused.AttachSession();
        reused.UnlockedContainerActivation(reusedSession, actual); reused.EnterContainerFactory(reusedSession, fixture.Reference);
        Reject(() => reused.UnlockedContainerActivation(reusedSession, actual));
        using var zero = fixture.Owner(); var zeroSession = zero.AttachSession();
        Reject(() => zero.UnlockedContainerActivation(zeroSession, actual with { InputSequence = 0 }));
        owner.Dispose();
        Require(owner.Observe().State == FalloutAdvancementActivityState.Unowned,
            "Disposed actual interface still supplied advancement admission.");
        Console.WriteLine("OPENNV_INTERFACE_ACTIVATION_FRAME_CONTRACT_PASS constructorFalse=true sourceWinner=true " +
            "masterAdjusted=true exactUnlock=true factoryPrefixClear=true foreignSessionRefused=true " +
            "foreignBaseRefused=true wrongTypeRefused=true disabledRefused=true duplicateInputRefused=true " +
            "unrelatedFactoryFailureRetained=true coldPendingRefused=true nativeLockpick=unowned");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("opennv-interface-frame-");
        internal FalloutPluginStack Records { get; }
        private readonly FalloutReferenceWorld _world;
        private readonly FalloutAdvancementFrameDeclaration _source = new(new('a', 64), new('b', 64), false, 1, false, 1008);
        internal FalloutFormKey Reference => Key(0x900);
        internal FalloutFormKey Unrelated => Key(0x901);
        internal FalloutFormKey Activator => Key(0x902);
        internal FalloutFormKey Disabled => Key(0x903);
        internal Fixture()
        {
            var disabled = ReferenceRecord(0x903, 1); UInt(disabled, 8, 0x800);
            File.WriteAllBytes(Path.Combine(_directory.FullName, "Frame.esm"), Join(Header(),
                Record("CONT", 1, Field("FULL", Text("Initial authored box"))), Record("ACTI", 2),
                Record("CELL", 0x800, Field("DATA", [1])), Group(0x800,
                    ReferenceRecord(0x900, 1), ReferenceRecord(0x901, 1), ReferenceRecord(0x902, 2), disabled)));
            File.WriteAllBytes(Path.Combine(_directory.FullName, "Other.esm"), Join(Header(), Record("CONT", 1)));
            File.WriteAllBytes(Path.Combine(_directory.FullName, "FramePatch.esp"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12]), Field("MAST", Text("Frame.esm")), Field("DATA", new byte[8])),
                Record("CONT", 1, Field("FULL", Text("Winning authored box"))),
                Group(0x800, ReferenceRecord(0x900, 1))));
            Records = FalloutPluginStack.Load(_directory.FullName, ["Frame.esm", "Other.esm", "FramePatch.esp"]);
            _world = new(Records);
            Require(Records.GetEffective(Key(1)).Plugin.Name == "FramePatch.esp",
                "Authored current-winner fixture failed to exercise the original master namespace.");
        }
        internal FalloutInterfaceActivationFrame Owner(FalloutInterfaceActivationFrameSnapshot? restore = null) =>
            new(_source, Records, _world, restore);
        internal FalloutUnlockedContainerActivation Activation(FalloutFormKey reference, ulong input)
        {
            var basis = _world.Get(reference).Base;
            return new(input, reference, basis, Records.RuntimeFormKey(0x14),
                Hash(Records.GetEffective(reference)), Hash(Records.GetEffective(basis)));
        }
        public void Dispose() { _world.Dispose(); Records.Dispose(); _directory.Delete(true); }
        private static FalloutFormKey Key(uint id) => new("Frame.esm", id);
    }

    private static string Hash(FalloutPluginRecord record)
    {
        // The independently assembled receipt includes the source header and
        // ordered master namespace, not only a plausible container payload.
        var context = Encoding.UTF8.GetBytes(record.Signature + "\0" + record.FormKey + "\0" + record.Flags + "\0" +
            string.Concat(record.Plugin.Masters.Append(record.Plugin.Name).Select(name => name.ToUpperInvariant() + "\0")));
        return Convert.ToHexString(SHA256.HashData(record.ReadData().Concat(context).ToArray())).ToLowerInvariant();
    }
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned or mismatched original interface transition was admitted.");
    }
    private static byte[] Header() => Record("TES4", 0, Field("HEDR", new byte[12]));
    private static byte[] ReferenceRecord(uint id, uint basis) => Record("REFR", id,
        Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]));
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var payload = Join(records); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        UInt(bytes, 4, checked((uint)bytes.Length)); UInt(bytes, 8, cell); UInt(bytes, 12, 6); payload.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(value => value).ToArray();
    private static byte[] Field(string tag, byte[] payload)
    {
        var bytes = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length)); payload.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string tag, uint id, params byte[][] fields)
    {
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(bytes, 0);
        UInt(bytes, 4, checked((uint)payload.Length)); UInt(bytes, 12, id); payload.CopyTo(bytes, 24); return bytes;
    }
}
