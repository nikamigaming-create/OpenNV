using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class TerminalHackStateContracts
{
    internal static void Run(FalloutPluginStack records, FalloutCellScene cell)
    {
        var target = new FalloutFormKey("Access.esm", 0xa1);
        var peer = new FalloutFormKey("Access.esm", 0xa2);
        using var fresh = new FalloutReferenceWorld(records);
        fresh.LoadCell(cell);
        var initial = fresh.TerminalHackResult(target);
        Require(initial.RawState == 0 && !initial.Hacked && !fresh.TerminalHackLockedOut(target),
            "Fresh TERM result did not use the source absent-extra zero.");
        var originalLock = fresh.GetLocked(target);
        var saved = fresh.Get(target).Capture();
        for (var raw = 0; raw <= byte.MaxValue; ++raw)
        {
            var snapshot = saved with { TerminalHackState = initial with { RawState = (byte)raw } };
            using var flags = new FalloutReferenceWorld(records);
            flags.Restore([snapshot]);
            var calls = 0;
            var locked = flags.TerminalHackLockedOut(target, () => { ++calls; return 0; });
            Require(locked == ((raw & 0x7f) != 0) && calls == ((raw & 0x7f) == 1 ? 1 : 0) &&
                flags.TerminalHackResult(target).Hacked == ((raw & 0x80) != 0),
                "Result flag, masked lockout or perk-call cardinality changed.");
            flags.SetTerminalHacked(target, true);
            Require(flags.TerminalHackResult(target).RawState == (byte)(raw | 0x80), "Success flag lost failure bits.");
            flags.SetTerminalHacked(target, false);
            Require(flags.TerminalHackResult(target).RawState == (byte)(raw & 0x7f), "Clearing success lost failure bits.");
            flags.SetTerminalHackFailure(target, false);
            Require(flags.TerminalHackResult(target).RawState == 0, "Failure reset did not clear the entire byte.");
            using var increment = new FalloutReferenceWorld(records);
            increment.Restore([snapshot]);
            increment.SetTerminalHackFailure(target, true);
            Require(increment.TerminalHackResult(target).RawState == unchecked((byte)(raw + 1)),
                "Failure increment saturated/masked instead of advancing the entire byte.");
        }
        fresh.SetTerminalHackFailure(target, true);
        Reject(() => fresh.TerminalHackLockedOut(target));
        Require(!fresh.TerminalHackLockedOut(target, () => 1) && !fresh.TerminalHackLockedOut(target, () => -2),
            "Nonzero perk result failed to permit the first retry.");
        Reject(() => fresh.TerminalHackLockedOut(target, () => float.NaN));
        Require(fresh.TerminalHackResult(target).RawState == 1 && fresh.TerminalHackResult(peer).RawState == 0,
            "Lockout query or another reference mutated result history.");
        fresh.SetTerminalHacked(target, true);
        Require(fresh.TerminalHackResult(target).RawState == 0x81 && fresh.GetLocked(target) == originalLock &&
            fresh.Get(target).LockState == saved.LockState && fresh.Get(target).Unlocked == saved.Unlocked,
            "Hacking flags silently performed generic Unlock or changed lock provenance.");
        var captured = fresh.Capture();
        var coldSnapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(captured))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(coldSnapshots);
        Require(cold.TerminalHackResult(target).RawState == 0x81 &&
            JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(captured),
            "Known result history or unrelated reference state changed across cold JSON restoration.");

        var legacySnapshot = saved with { TerminalHackState = null };
        using var legacy = new FalloutReferenceWorld(records);
        legacy.Restore([legacySnapshot]);
        Reject(() => legacy.TerminalHackResult(target));
        Reject(() => legacy.SetTerminalHacked(target, true));
        Require(legacy.Get(target).Capture().TerminalHackState is null && legacy.GetLocked(target) == originalLock,
            "Missing historical state became a fabricated zero or changed existing lock behavior.");
        var unknownJson = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(legacy.Capture()))!;
        using var unknownCold = new FalloutReferenceWorld(records);
        unknownCold.Restore(unknownJson);
        Reject(() => unknownCold.SetTerminalHackFailure(target, true));
        Require(unknownCold.Get(target).Capture().TerminalHackState is null, "Unknown history was lost on recapture.");

        foreach (var invalid in new[]
        {
            saved with { TerminalHackState = initial with { ReferenceSha256 = "invalid" } },
            saved with { TerminalHackState = initial with { ReferenceSha256 = new('0', 64) } },
            saved with { Reference = new("Access.esm", 0x94), Base = new("Access.esm", 3), TerminalHackState = initial }
        })
        {
            using var refused = new FalloutReferenceWorld(records);
            Reject(() => refused.Restore([invalid]));
            Require(refused.InstanceCount == 0, "Invalid result state partially committed a restored world.");
        }
        Reject(() => fresh.SetTerminalHacked(new("Access.esm", 0x94), true));

        var previous = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(new
        {
            Schema = FalloutNativeCampaignSave.BeforeTerminalHackStateSchema,
            References = new[] { legacySnapshot }
        }))!;
        var upgraded = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous);
        Require(upgraded.Schema == FalloutNativeCampaignSave.ExpectedSchema &&
            ReferenceEquals(upgraded.References, previous.References) && upgraded.References![0].TerminalHackState is null,
            "v56 header admission invented terminal history or replaced reference snapshots.");
        Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous with { References = [saved] }));
        var current = previous with { Schema = FalloutNativeCampaignSave.ExpectedSchema, References = [saved] };
        Require(ReferenceEquals(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(current), current),
            "Current schema admission replaced known result state.");
        Console.WriteLine("TERMINAL_HACK_STATE_PASS rawBytes=256 flagBits=true fullByteIncrement=true maskedLockout=true " +
            "perkRequired=true sourceBound=true cold=true legacyUnknown=true atomicRefusal=true headerAdmission=true " +
            "genericLockUnchanged=true nativeAdmissionAndFullCampaignMigration=unverified");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unqualified terminal result state was admitted.");
    }
}
