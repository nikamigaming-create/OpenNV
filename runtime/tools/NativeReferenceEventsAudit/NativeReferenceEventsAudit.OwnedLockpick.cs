using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    // Controlled owned-state exercise. Skill100 and a solved position are
    // explicit prerequisites, not ordinary input, exploration or campaign proof.
    private static void OwnedLockpickSession(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var geometry = NativeLockpickMenu.ReadGeometry(new(1920, 1080));
        var smaller = NativeLockpickMenu.ReadGeometry(new(1280, 720));
        var settings = FalloutInstallationSettings.Read(content);
        var expectedWidth = (1920f / 1080 * 960) - 4 * settings.Number("Interface", "iSafeZoneXWide");
        Require(geometry.Width == expectedWidth && geometry.Position == geometry.Width / 2 &&
            MathF.Abs(geometry.MouseScale - 8f / 9) < .00001f && MathF.Abs(smaller.MouseScale - 4f / 3) < .00001f,
            "Owned lockpick source meter/arrow geometry or relative-coordinate scale changed.");
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var refused = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in records.EffectiveRecords("REFR"))
        {
            var fields = record.ReadSubrecords().ToArray();
            var raw = fields.SingleOrDefault(field => field.Signature == "XLOC").Data;
            if (raw.Length is not (12 or 20) || raw.Span[0] > 100 || (raw.Span[8] & 4) != 0 ||
                record.IsDeleted || (record.Flags & 0x800) != 0 || fields.Any(field => field.Signature is "XTEL" or "XESP")) continue;
            var name = fields.SingleOrDefault(field => field.Signature == "NAME").Data;
            if (name.Length != 4) continue;
            var basis = records.GetEffective(record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(name.Span)));
            if (basis.Signature is not ("DOOR" or "CONT") || selected.Contains(basis.Signature) ||
                FalloutScriptLocals.AttachedScript(records, record) is not null || FalloutCellSceneReader.ParentCell(record) is not { } cellKey) continue;
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, cellKey);
            world.LoadCell(cell with { References = cell.References.Where(value => value.FormKey == record.FormKey).ToArray() });
            FalloutReferenceLockState state;
            try { state = world.RequirePickLock(record.FormKey); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
            { refused[error.Message] = refused.GetValueOrDefault(error.Message) + 1; continue; }
            if (state.FailedPickAttempts != 0 || state.PickRewardCount != 0) continue;
            var admission = FalloutLockpickSession.Admission(records, state.Level, 100);
            var manual = FalloutLockpickSession.ManualSettings(records, admission.Class, 100);
            var vitals = new FalloutPlayerVitals(records, records.RuntimeFormKey(7), new(5, 5, 5, 5, 5, 5, 5));
            var experience = new FalloutPlayerExperience(records, vitals, () => []);
            var initialXp = vitals.State.ExperiencePoints;
            var random = new FalloutSharedUnsignedRandom(() => 123456u);
            var session = FalloutLockpickSession.CreateManual(() => admission.Chance, random.NextBounded,
                () => world.CompletePickLock(record.FormKey, state, () => experience.Reward(admission.Reward)),
                () => world.FailForceLock(record.FormKey, state),
                geometry.Width, geometry.Position, manual.Length, manual.Concentric, manual.Health, manual.BreakSeconds,
                () => throw new InvalidOperationException("Aligned owned test must not consume a pin."), () => { }, new());
            session.AdvanceManual(1, session.Target - session.Position, true);
            session.AdvanceManual(1001, 0, true);
            Require(session.Finished && session.Won && world.GetLocked(record.FormKey) == 0 &&
                vitals.State.ExperiencePoints == initialXp + admission.Reward && random.Observation.Draws >= 1,
                "Owned manual session failed authoritative unlock/source reward.");
            var snapshot = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshot);
            Require(cold.GetLocked(record.FormKey) == 0 && cold.Get(record.FormKey).LockState!.PickRewardCount == 1,
                "Owned canonical cold continuation lost the completed lock receipt.");
            GD.Print($"OPENNV_OWNED_LOCKPICK_CASE type={basis.Signature} reference={record.FormKey} sourceSha256={Convert.ToHexString(SHA256.HashData(record.ReadData()))} " +
                $"class={admission.Class} width={geometry.Width} length={manual.Length} concentric={manual.Concentric} reward={admission.Reward} coldCanonical=true");
            selected.Add(basis.Signature);
            if (selected.Count == 2) break;
        }
        Require(selected.Count == 2, "Owned audit did not find both admitted DOOR and CONT declarations.");
        GD.Print($"OPENNV_OWNED_LOCKPICK_PASS sourceGeometry=true door=true container=true sourceSettings=true reward=true canonicalCold=true " +
            $"refusedCandidates={refused.Values.Sum()} ordinaryActivationAndCampaignAndRetailParity=unverified");
    }
}
