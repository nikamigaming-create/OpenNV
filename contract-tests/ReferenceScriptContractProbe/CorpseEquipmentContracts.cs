using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class CorpseEquipmentContracts
{
    internal static void Run()
    {
        var directory = Path.Combine("local", "corpse-equipment-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            WriteSource(Path.Combine(directory, "Corpse.esm"));
            using var records = FalloutPluginStack.Load(directory, ["Corpse.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
            var state = world.Get(Key(0x900));
            state.Inventory = Inventory(records, Key(0x50), ammunition: true);
            var inventoryBefore = JsonSerializer.Serialize(state.Inventory.Capture());
            state.Animation.Change("meshes/fixture/mtidle.kf", new string('a', 64)); state.Animation.Advance(8.125);
            state.PackageBindingFailure = new(Key(0x20), Hash(records, 0x20), "Original source Travel procedure is unbound.",
                [2, 3, 4], [1, 0, 0, 0, 1, 0, 0, 0, 1], false, 17, 2.125, new(4, 10, 2, 7.5f),
                new(2, "POCA", Key(0x22), Hash(records, 0x22)));
            state.ProcedureCaptureBlocker = state.PackageBindingFailure.Error;
            state.AttackRandom.Restore(9);
            var weapon = FalloutWeaponPresentation.Read(records, Key(0x50), false);
            var handling = new FalloutWeaponHandling(state.Inventory.Contents, nativeNpc: true);
            handling.CompleteReload(weapon);
            var actualHandling = handling.Capture();
            Require(handling.Loaded(weapon.Form) == 12 && JsonSerializer.Serialize(state.Inventory.Capture()) == inventoryBefore,
                "Preparing a magazine changed actual item totals or condition.");
            state.Engagement = new(Key(0x902), "attack", .375, false, "meshes/fixture/1hpattack.kf", new string('b', 64),
                [2, 3, 4], [0, 0, 0, 1], actualHandling, state.AttackRandom.State);
            Require(world.DamageActor(state.Reference, Key(0x902), 0, 125, 1, 1).Died,
                "Synthetic contract did not enter authoritative death.");
            state.Ragdoll = new(new string('c', 64), [new(7, Pose(), [.125f, 0, -.25f], [0, .5f, 0], false)]);
            state.CorpseEquipment = Equipment(records, actualHandling);
            NativeBindingContracts(state.CorpseEquipment);
            var complete = Roundtrip(world.Capture());
            FalloutReferenceSnapshot.Validate(complete);
            var corpse = complete.Single(snapshot => snapshot.Reference == state.Reference);
            Require(JsonSerializer.Serialize(state.Inventory.Capture()) == inventoryBefore && corpse.DeathCount == 1 &&
                corpse.Injury is { DeathEventPending: true, DeathInventoryGranted: true } &&
                corpse.PackageBindingFailure!.Error == state.ProcedureCaptureBlocker,
                "Corpse capture changed inventory, pending source death or the original procedure fault.");

            world.HitEvents.Mark(state.Reference, Key(0x902), weapon.Form, FalloutReferenceHitKind.Melee);
            Reject(() => world.Capture(), "pending reference hit events");
            var pending = world.HitEvents.SnapshotPending(state.Reference);
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Synthetic source hit cannot invent host effects.")));
            _ = scripts.DispatchFrame(state.Reference, pending.Events, 0);
            world.HitEvents.Consume(pending);
            Require(JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(complete),
                "Consuming the actual source hit admission changed corpse history or replayed an effect.");
            state.CanCaptureCorpseEquipment = _ => false;
            state.CorpseEquipmentCaptureBlocker = "Unowned source drop/continuation layout.";
            Reject(() => state.Capture(), "Unowned source drop/continuation layout");
            state.CanCaptureCorpseEquipment = _ => true;
            state.CaptureCorpseEquipment = () => state.CorpseEquipment.Copy();
            state.CorpseEquipmentCaptureBlocker = null;
            Require(JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(complete),
                "Independent native capture binding changed the retained receipt.");
            state.CanCaptureCorpseEquipment = null; state.CaptureCorpseEquipment = null;
            world.UnloadCell(cell.Cell.FormKey);
            Require(JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(complete),
                "Retiring a source-bound corpse changed its admitted equipment receipt.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(complete);
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(complete),
                "Cold restoration changed raw poses, weapon condition, magazines, random streams or source faults.");
            var coldInventory = cold.Get(state.Reference).Inventory!.Contents;
            var coldHandling = new FalloutWeaponHandling(coldInventory, nativeNpc: true);
            coldHandling.Restore(corpse.CorpseEquipment!.WeaponHandling!, key => FalloutWeaponPresentation.Read(records, key, false));
            Require(FalloutActorCorpseEquipment.SameHandling(actualHandling, coldHandling.Capture()) &&
                JsonSerializer.Serialize(cold.Get(state.Reference).Inventory!.Capture()) == inventoryBefore,
                "Cold native-NPC handling reloaded ammunition, changed condition or drew new random state.");
            foreach (var changed in Corruptions(corpse, records)) AtomicReject(records, Replace(complete, changed));
            foreach (var changed in new[]
            {
                corpse with { Inventory = corpse.Inventory! with { Contents = corpse.Inventory!.Contents with
                    { Inventory = new(corpse.Inventory.Contents.Inventory.Items.Select(item => item.FormKey == Key(0x51)
                        ? item with { Count = 5 } : item).ToArray(), null) } } },
                corpse with { Inventory = corpse.Inventory! with { Contents = corpse.Inventory!.Contents with { EquippedRuntimeFormIds = [] } } }
            }) Reject(() => FalloutReferenceSnapshot.Validate([changed]));
            VirtualMagazineContracts(records);
            LegacySchemaBoundary(corpse, directory);
            Console.WriteLine("OPENNV_CORPSE_EQUIPMENT_CONTRACT_PASS current=true cold=true retirementReceipt=true rawGraph=true " +
                "itemAmmoConditionConservation=true virtualAmmo=true pendingHitRefused=true originalTravelFault=true " +
                "sourceDriftAtomic=true legacyFutureRefused=true synthetic=true nativeAndRetail=unverified");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static FalloutActorCorpseEquipment Equipment(FalloutPluginStack records, FalloutWeaponHandlingSnapshot handling)
    {
        var source = new FalloutCorpseWeaponSource(Key(0x50), Hash(records, 0x50));
        var attachment = new FalloutCorpseWeaponAttachment("combat", source, "meshes/fixture/weapon.nif", new string('d', 64),
            true, [new(0, -1, "Weapon", null, "", "Godot.Node3D", Pose(), true),
                new(1, 0, null, 4, "Slide", "Godot.Node3D", Pose(.125f), false),
                new(2, -1, "Pack", 8, "PackMesh", "Godot.MeshInstance3D", Pose(.25f), true, 1)],
            [new(0, "Pack", true)]);
        var residual = new FalloutActorResidualPose("meshes/fixture/skeleton.nif", new string('c', 64),
            [new(1, "Weapon", [.125f, -.25f, .5f], [0, 0, 0, 1], [1, 1, 1]),
                new(2, "Pack", [0, 1, 0], [0, 0, 0, 1], [1, 1, 1])]);
        return new(Pose(), new(true, true, true, false, false, true, 7), residual,
            [attachment, attachment with { Owner = "package", PrimaryVisible = false,
            Nodes = attachment.Nodes.Select(node => node with { Visible = false }).ToArray(),
            BodyAttachments = [new(0, "Pack", false)] }], source, handling,
            RouteRetirement: new(true, "Retained route refusal.", "Retained coarse refusal.", 3,
                Key(0x901), true, 1.25, "Original door wait fault."));
    }

    private static void NativeBindingContracts(FalloutActorCorpseEquipment saved)
    {
        saved.Validate();
        var attachment = saved.Attachments[0];
        attachment.ValidateBinding(attachment with
        {
            PrimaryVisible = false,
            Nodes = attachment.Nodes.Select(node => node with { Transform = Pose(.75f), Visible = !node.Visible }).ToArray()
        });
        (attachment with { Nodes = attachment.Nodes.Select(node => node with { Transform = new float[12] }).ToArray() })
            .ValidateBinding(attachment);
        foreach (var invalid in new[]
        {
            attachment with { ModelSha256 = new string('0', 64) },
            attachment with { ModelResource = "meshes/fixture/another.nif" },
            attachment with { Nodes = attachment.Nodes.Take(2).ToArray() },
            attachment with { Nodes = [attachment.Nodes[0], attachment.Nodes[1] with { Parent = -1, BoneParent = "Weapon" }, attachment.Nodes[2]] },
            attachment with { Nodes = [attachment.Nodes[0], attachment.Nodes[1] with { SourceBlock = 9 }, attachment.Nodes[2]] },
            attachment with { Nodes = [attachment.Nodes[0], attachment.Nodes[1] with { NativeType = "Godot.RigidBody3D" }, attachment.Nodes[2]] },
            attachment with { Nodes = [attachment.Nodes[0], attachment.Nodes[1] with { SourceName = "OtherSlide" }, attachment.Nodes[2]] },
            attachment with { BodyAttachments = [new(0, "OtherPack", true)] },
            attachment with { Nodes = [attachment.Nodes[0] with { Transform = [float.NaN, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0] }, .. attachment.Nodes.Skip(1)] }
        }) Reject(() => invalid.ValidateBinding(attachment));
        var coverage = new (string, FalloutNifTransformComponents)[] { ("Root", FalloutNifTransformComponents.All),
            ("Weapon", FalloutNifTransformComponents.None), ("Pack", FalloutNifTransformComponents.None) };
        saved.ResidualPose.ValidateBinding(saved.ResidualPose.SkeletonResource, saved.ResidualPose.SkeletonSha256, coverage);
        Reject(() => (saved.ResidualPose with { Bones = saved.ResidualPose.Bones.Take(1).ToArray() })
            .ValidateBinding(saved.ResidualPose.SkeletonResource, saved.ResidualPose.SkeletonSha256, coverage));
        var copy = saved.Copy();
        Require(JsonSerializer.Serialize(copy) == JsonSerializer.Serialize(saved) &&
            !ReferenceEquals(copy.RootPose, saved.RootPose) && !ReferenceEquals(copy.Attachments[0].Nodes[0].Transform, attachment.Nodes[0].Transform),
            "Corpse receipt cloning retained mutable native pose storage.");
    }

    private static IEnumerable<FalloutReferenceSnapshot> Corruptions(FalloutReferenceSnapshot corpse, FalloutPluginStack records)
    {
        var equipment = corpse.CorpseEquipment!;
        yield return corpse with { CorpseEquipment = null };
        yield return corpse with { CorpseEquipment = equipment with { RootPose = new float[12] } };
        yield return corpse with { CorpseEquipment = equipment with { Attachments = [] } };
        yield return corpse with { CorpseEquipment = equipment with { Activity = null! } };
        yield return corpse with { CorpseEquipment = equipment with { Attachments = [equipment.Attachments[0], equipment.Attachments[0]] } };
        yield return corpse with
        {
            CorpseEquipment = equipment with
            {
                Attachments = [equipment.Attachments[0] with
            { Source = equipment.Attachments[0].Source with { Sha256 = new string('0', 64) } }]
            }
        };
        yield return corpse with
        {
            CorpseEquipment = equipment with
            {
                Attachments = [equipment.Attachments[0] with
            { ModelResource = "meshes/fixture/not-the-winning-model.nif" }]
            }
        };
        yield return corpse with { CorpseEquipment = equipment with { WeaponHandling = equipment.WeaponHandling! with { ShotRandomState = null } } };
        yield return corpse with
        {
            CorpseEquipment = equipment with
            {
                WeaponHandling = equipment.WeaponHandling! with
                { Magazines = [equipment.WeaponHandling!.Magazines[0] with { Loaded = 13 }] }
            }
        };
        yield return corpse with { CorpseEquipment = equipment with { HandlingWeapon = new(Key(0x52), Hash(records, 0x52)) } };
        yield return corpse with { Inventory = null };
        yield return corpse with { Ragdoll = null };
        yield return corpse with { KnockedDown = true };
        yield return corpse with { Injury = corpse.Injury! with { Dead = false } };
        yield return corpse with
        {
            Inventory = corpse.Inventory! with
            {
                Contents = corpse.Inventory!.Contents with
                {
                    Inventory = new(corpse.Inventory.Contents.Inventory.Items.Where(item => item.FormKey != Key(0x50)).ToArray(), null),
                    EquippedRuntimeFormIds = []
                }
            }
        };
        yield return corpse with
        {
            Inventory = corpse.Inventory! with
            {
                Contents = corpse.Inventory!.Contents with
                { Inventory = new(corpse.Inventory.Contents.Inventory.Items.Select(item => item.FormKey == Key(0x51) ? item with { Count = 5 } : item).ToArray(), null) }
            }
        };
        yield return corpse with { Engagement = corpse.Engagement! with { WeaponHandling = equipment.WeaponHandling! with { Drawn = false } } };
        yield return corpse with { CorpseEquipment = equipment with { RouteRetirement = equipment.RouteRetirement! with { Door = corpse.Reference } } };
    }

    private static void VirtualMagazineContracts(FalloutPluginStack records)
    {
        var inventory = Inventory(records, Key(0x52), ammunition: false);
        var before = JsonSerializer.Serialize(inventory.Capture());
        var weapon = FalloutWeaponPresentation.Read(records, Key(0x52), false);
        var handling = new FalloutWeaponHandling(inventory.Contents, nativeNpc: true); handling.CompleteReload(weapon);
        var virtualMagazine = handling.Capture();
        Require(!virtualMagazine.Magazines.Single().UsesInventoryAmmo && virtualMagazine.Magazines.Single().Loaded == 12,
            "Source native-NPC ammo exemption did not remain a virtual magazine.");
        var corpse = Equipment(records, virtualMagazine) with
        { Attachments = [], HandlingWeapon = new(Key(0x52), Hash(records, 0x52)), EmbeddedMuzzleBone = "ProjectileNode" };
        corpse.ValidateSource(records, inventory.Contents);
        Require(JsonSerializer.Serialize(inventory.Capture()) == before && inventory.Contents.Item(Key(0x51)) is null,
            "Cold virtual magazine manufactured carried ammunition or changed item condition.");
        Reject(() => (corpse with
        {
            WeaponHandling = virtualMagazine with
            { Magazines = [virtualMagazine.Magazines.Single() with { UsesInventoryAmmo = true }] }
        }).ValidateSource(records, inventory.Contents));
        var consuming = Inventory(records, Key(0x50), ammunition: true);
        var alteredPolicy = Equipment(records, new(true, [new(Key(0x50), Key(0x51), 12, false)], 17, 19));
        Reject(() => alteredPolicy.ValidateSource(records, consuming.Contents));
    }

    private static void LegacySchemaBoundary(FalloutReferenceSnapshot corpse, string directory)
    {
        var legacy = new FalloutNativeCampaignState(FalloutNativeCampaignSave.ActivationRelaySchema, "synthetic", Key(0x800),
            "Fixture", 1, "Player", null!, null!, [], [], [], [], [true, true, true, true, true, true, true],
            [0, 0, 0], [0, 0, 0], References: [corpse]);
        var path = Path.Combine(directory, "must-not-write.json");
        Reject(() => FalloutNativeCampaignSave.Write(path, legacy), "future corpse equipment");
        Require(!File.Exists(path), "Future corpse fields partially wrote a legacy-labelled save.");
    }

    private static FalloutReferenceInventory Inventory(FalloutPluginStack records, FalloutFormKey weapon, bool ammunition)
    {
        var inventory = new FalloutReferenceInventory();
        var items = new List<FalloutCampaignItem> { new(weapon, records.RuntimeFormId(weapon), "FixtureGun", "WEAP", 1, 0, 1,
            Variants: [new(1, .42f)]) };
        if (ammunition) items.Add(new(Key(0x51), records.RuntimeFormId(Key(0x51)), "FixtureAmmo", "AMMO", 17, 0, 0));
        inventory.Contents.Restore(new(items, null), [records.RuntimeFormId(weapon)], 17);
        return inventory;
    }
    private static void AtomicReject(FalloutPluginStack records, FalloutReferenceSnapshot[] candidate)
    {
        using var world = new FalloutReferenceWorld(records); Reject(() => world.Restore(candidate));
        Require(world.InstanceCount == 0, "Invalid corpse state partially published a world.");
    }
    private static FalloutReferenceSnapshot[] Roundtrip(IReadOnlyList<FalloutReferenceSnapshot> snapshots) =>
        JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!;
    private static FalloutReferenceSnapshot[] Replace(IReadOnlyList<FalloutReferenceSnapshot> snapshots, FalloutReferenceSnapshot changed) =>
        snapshots.Select(snapshot => snapshot.Reference == changed.Reference ? changed : snapshot).ToArray();
    private static FalloutFormKey Key(uint id) => new("Corpse.esm", id);
    private static string Hash(FalloutPluginStack records, uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
    private static float[] Pose(float x = 2) => [1, 0, 0, 0, 1, 0, 0, 0, 1, x, 3, 4];
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action, string? text = null)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
        { if (text is null || error.Message.Contains(text, StringComparison.Ordinal)) return; throw; }
        throw new InvalidDataException("Unowned or corrupt corpse continuation was admitted.");
    }
    private static void WriteSource(string path)
    {
        var header = new byte[12]; Float(header, 0, 1.34f);
        var data = new byte[11]; UInt(data, 0, 100);
        var acbs = new byte[24]; UInt(acbs, 0, 0x10); BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(8), 1);
        var pkdt = new byte[12]; pkdt[4] = 6;
        var part = new byte[84]; Float(part, 0, 1); part[4] = 1; part[6] = 100; Float(part, 24, 1); Float(part, 40, 1); Float(part, 80, 1);
        var weapon = new byte[204]; UInt(weapon, 0, 3); Float(weapon, 4, 1); weapon[13] = 255; weapon[14] = 1;
        weapon[41] = 32; UInt(weapon, 56, 2); Float(weapon, 60, 1); Float(weapon, 48, 4096);
        var virtualWeapon = (byte[])weapon.Clone(); UInt(virtualWeapon, 56, 0); virtualWeapon[12] = 0x20;
        var economics = new byte[15]; UInt(economics, 4, 100); economics[12] = 16; economics[14] = 12;
        var references = Join(Reference("ACHR", 0x900, 0x10), Reference("ACHR", 0x902, 0x11), Reference("REFR", 0x901, 0x40));
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); references.CopyTo(group, 24);
        byte[] Npc(uint id) => Record("NPC_", id, Field("ACBS", acbs), Field("DATA", data), Field("NAM4", BitConverter.GetBytes(6u)));
        byte[] Weapon(uint id, byte[] dnam) => Record("WEAP", id, [Field("EDID", Text("FixtureGun")),
            .. id == 0x52 ? Array.Empty<byte[]>() : new[] { Field("MODL", Text("fixture/weapon.nif")) },
            Field("DATA", economics), Field("DNAM", dnam), Field("ETYP", BitConverter.GetBytes(1)), Field("NAM0", BitConverter.GetBytes(0x51u))]);
        File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", header)), Npc(0x10), Npc(0x11),
            Record("BPTD", 0x1d, Field("BPNN", Text("Root")), Field("BPNT", Text("Root")), Field("BPND", part)),
            Setting(0x61, "fAVDNPCHealthEnduranceOffset", -1), Setting(0x62, "fAVDNPCHealthEnduranceMult", 5.25f),
            Setting(0x63, "fAVDNPCHealthLevelMult", 5), Record("PACK", 0x20, Field("PKDT", pkdt)),
            Record("PACK", 0x22, Field("PKDT", pkdt)),
            Weapon(0x50, weapon), Weapon(0x52, virtualWeapon), Record("AMMO", 0x51, Field("EDID", Text("FixtureAmmo")), Field("DATA", new byte[13])),
            Record("DOOR", 0x40), Record("CELL", 0x800, Field("DATA", [1])), group));
    }
    private static byte[] Reference(string kind, uint id, uint item) => Record(kind, id, Field("NAME", BitConverter.GetBytes(item)), Field("DATA", new byte[24]));
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Float(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string kind, uint id, params byte[][] fields)
    {
        if (kind == "PACK") fields = [Field("EDID", Text("FixturePackage" + id)), .. fields];
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(kind).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)payload.Length); UInt(bytes, 12, id); payload.CopyTo(bytes, 24); return bytes;
    }
}
