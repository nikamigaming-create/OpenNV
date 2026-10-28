using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class PackageDataContracts
{
    private static void CheckSandboxActivitySelection()
    {
        var preferences = new FalloutSandboxPreferences();
        var ordinary = preferences.ActivityWeights(null, false, false, false);
        Require(ordinary.SequenceEqual([128, 0, 113, 128, 128, 128]), "Sandbox ambient weights include unscheduled sleep or lose food context.");
        var food = preferences.ActivityWeights(null, true, true, true);
        Require(food.SequenceEqual([128, 0, 148, 128, 128, 0]), "Sandbox food/furniture or dialogue suppression changed unrelated activities.");
        foreach (var scheduled in new[] { FalloutSandboxActivity.Sleep, FalloutSandboxActivity.Eat })
        {
            var weights = preferences.ActivityWeights(scheduled, false, false, true);
            Require(weights.Sum() == 10 && weights[(int)scheduled] == 10, "Sandbox scheduled activity did not override ambient preference.");
        }
        preferences.Selected(FalloutSandboxActivity.Eat);
        var restored = new FalloutSandboxPreferences(preferences.Capture());
        var suppressedFood = restored.ActivityWeights(null, false, false, false);
        Require(suppressedFood[2] == -14 && FalloutSandboxPreferences.CandidateWeight(1, suppressedFood[2]) == 0 &&
            restored.Weight(FalloutSandboxActivity.Wander) == 129, "Sandbox continuation lost preference or clamped before candidate selection.");
        Require(FalloutSandboxPreferences.Choose([0, 3, 0, 2], 3) == 3,
            "Sandbox selection crossed a zero-weight candidate or the wrong ticket boundary.");
        var exclusions = new FalloutSandboxActivities(31);
        Require(Enum.GetValues<FalloutSandboxActivity>().Where(exclusions.Allows).SequenceEqual([FalloutSandboxActivity.Wander]),
            "Sandbox exclusion mask disabled wandering or admitted a forbidden activity.");
        var actorData = new byte[20]; actorData[2] = 255;
        Require(FalloutActorThreat.Decode(new("Base.esm", 0x700), actorData).Energy == 255,
            "Actor energy was read from the wrong AIDT byte or silently clamped.");
        Console.WriteLine("OPENNV_SANDBOX_ACTIVITY_CONTRACT_PASS context=true preferenceContinuation=true exclusions=true energy=true gameplay=unverified");
    }

    private static void CheckSandboxDailyWindows(FalloutNumericGameSettings settings)
    {
        var sleep = new FalloutSandboxDailyWindow(0, 40, 23, 8);
        Require(sleep.Contains(40, 23) && sleep.Contains(41, 6) && !sleep.Contains(40, 22) && !sleep.Contains(41, 7),
            "Sandbox sleep window lost midnight rollover or its exclusive end.");
        var restored = System.Text.Json.JsonSerializer.Deserialize<FalloutSandboxDailyWindow>(System.Text.Json.JsonSerializer.Serialize(sleep))!;
        Require(FalloutSandboxDailyWindow.Refresh(restored, 41, 6, _ => throw new InvalidOperationException("Unexpected redraw.")) == sleep,
            "Sandbox cold continuation redrew an unfinished daily window.");
        var draws = new List<int>();
        var next = FalloutSandboxDailyWindow.Refresh(restored, 41, 7, slot =>
        {
            draws.Add(slot);
            return slot == 0 ? (0f, 8f) : (7f, 2f);
        });
        Require(next.Slot == 1 && draws.SequenceEqual([0, 1]), "Expired sleep restarted on the same day or skipped the active meal.");
        Require(next.EligibleActivity(41, 8, false, true) is null &&
            next.EligibleActivity(41, 8, true, false) == FalloutSandboxActivity.Eat,
            "Daily meal eligibility ignored actual food availability.");
        var future = FalloutSandboxDailyWindow.Refresh(null, 41, 10,
            slot => slot switch { 0 => (23f, 8f), 1 => (7f, 1f), 2 => (12f, 1f), _ => (18f, 1f) });
        Require(future is { Slot: 2, Day: 41, StartHour: 12 }, "Daily selection did not choose the nearest future window.");
        var bounds = new List<(float, float)>();
        var wrapped = FalloutSandboxDailyWindow.Refresh(null, 41, 1, settings, (low, high) =>
        {
            bounds.Add((low, high));
            return high;
        });
        Require(wrapped is { Slot: 0, Day: 41, StartHour: 1, DurationHours: 10 } &&
            bounds.SequenceEqual([(19f, 25f), (6f, 10f)]), "Daily source settings lost a start range crossing midnight.");
        Console.WriteLine("OPENNV_SANDBOX_DAILY_CONTRACT_PASS midnight=true coldWindow=true expiredWindow=true nearestFuture=true sourceSettings=true gameplay=unverified");
    }

    internal static void Run()
    {
        CheckSandboxActivitySelection();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-package-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var packages = new List<byte[]>();
            var activitySettings = new (string Name, float Value)[]
            {
                ("fSandboxDurationBase", 10), ("fSandboxDurationMultWandering", 2),
                ("fSandboxEnergyMult", .01f), ("fSandboxEnergyMultWandering", .5f),
                ("fSandboxDurationRangeMult", .25f)
            };
            packages.AddRange(activitySettings.Select((setting, index) => Record("GMST", 0xb30u + (uint)index,
                Field("EDID", Encoding.UTF8.GetBytes(setting.Name + "\0")), Field("DATA", BitConverter.GetBytes(setting.Value)))));
            foreach (var (id, flags) in new[] { (0xb00u, 0u), (0xb01u, 1u), (0xb02u, 0x400000u) })
            {
                var effect = new byte[72];
                BinaryPrimitives.WriteUInt32LittleEndian(effect, flags);
                packages.Add(Record("MGEF", id, Field("DATA", effect)));
            }
            for (uint index = 0; index < 5; index++)
            {
                var header = new byte[20]; header[4] = index == 0 ? (byte)0 : (byte)2;
                var effects = index switch
                {
                    1 => new[] { Field("EFID", BitConverter.GetBytes(0xb01u)) },
                    2 => new[] { Field("EFID", BitConverter.GetBytes(0xb02u)) },
                    3 => new[] { Field("EFID", BitConverter.GetBytes(0xb01u)), Field("EFID", BitConverter.GetBytes(0xb00u)) },
                    _ => Array.Empty<byte[]>()
                };
                packages.Add(Record("ALCH", 0xb10u + index, new[] { Field("ENIT", header) }.Concat(effects).ToArray()));
            }
            packages.Add(Record("INGR", 0xb20, Field("EDID", "FindFoodIngredient\0"u8.ToArray()),
                Field("DATA", BitConverter.GetBytes(1.5f)), Field("ENIT", [7, 0, 0, 0, 2, 0, 0, 0])));
            packages.Add(Record("INGR", 0xb21, Field("ENIT", new byte[8])));
            packages.Add(Record("ALCH", 0xb22, Field("ENIT", new byte[8])));
            var sandboxSettings = new (string Name, float Value)[]
            {
                ("SleepStartMin", 19), ("SleepStartMax", 1), ("SleepDurationMin", 6), ("SleepDurationMax", 10),
                ("BreakfastMin", 6), ("BreakfastMax", 9), ("LunchMin", 11), ("LunchMax", 14),
                ("DinnerMin", 17), ("DinnerMax", 20), ("MealDurationMin", .5f), ("MealDurationMax", 2)
            };
            packages.AddRange(sandboxSettings.Select((setting, index) => Record("GMST", 0x600u + (uint)index,
                Field("EDID", Encoding.UTF8.GetBytes("fSandbox" + setting.Name + "\0")), Field("DATA", BitConverter.GetBytes(setting.Value)))));
            packages.Add(Record("GMST", 0x620, Field("EDID", "fAISocialTimerForConversationsMin\0"u8.ToArray()), Field("DATA", BitConverter.GetBytes(20f))));
            packages.Add(Record("GMST", 0x621, Field("EDID", "fAISocialTimerForConversationsMax\0"u8.ToArray()), Field("DATA", BitConverter.GetBytes(10f))));
            packages.Add(Record("GMST", 0x622, Field("EDID", "fAItalktosameNPCtimer\0"u8.ToArray()), Field("DATA", BitConverter.GetBytes(10f))));
            foreach (var extent in new[] { 8, 12 })
            {
                var first = extent == 8 ? 0x400u : 0x420u;
                packages.AddRange(new[]
                {
                    Package(first, extent, 0, 0, Location(0), Target(2, 11)),
                    Package(first + 1, extent, 1, 0, Target(0, 0x900, 100)),
                    Package(first + 2, extent, 2, 2, Location(0), Target(0, 0x900), Field("PKE2", BitConverter.GetBytes(100u))),
                    Package(first + 3, extent, 6, 0, Location(3)),
                    Package(first + 4, extent, 13, 0, Location(0), Field("PKPT", [0, 0])),
                    Package(first + 5, extent, 14, 0x10001000, Location(3)),
                    Package(first + 6, extent, 15, 0, Target(0, 0x900, 100), Field("PKDD", Dialogue())),
                    Record("PACK", first + 7, Field("PKDT", Data(extent, 17, 0x89abcdef, 0x1234, 0x5678))),
                });
            }
            var lengths = new[] { 0, 1, 4, 5, 6, 7, 9, 10, 11, 13, 16 };
            packages.AddRange(lengths.Select((size, index) => Record("PACK", 0x500u + (uint)index, Field("PKDT", new byte[size]))));
            packages.Add(Record("PACK", 0x510));
            packages.Add(Record("PACK", 0x511, Field("PKDT", Data(8, 6)), Field("PKDT", Data(12, 6))));
            packages.Add(Package(0x480, 12, 6, 0, Location(3), Field("CTDA", Condition())));
            packages.Add(Package(0x481, 8, 6, 0, Location(3)));
            packages.Add(Package(0x482, 8, 6, 0, Location(3), behavior: 1));
            packages.Add(Package(0x483, 12, 6, 0, Location(3), specific: 1));
            packages.Add(Record("PACK", 0x484, Field("EDID", "NoSchedule\0"u8.ToArray()), Field("PKDT", Data(8, 6)), Location(3)));
            packages.Add(Package(0x485, 12, 12, 0, Location(3), specific: 4));
            packages.Add(Package(0x486, 12, 12, 0, Location(3)));
            packages.Add(Package(0x487, 12, 0, 0, Location(2),
                Field("PTDT", Join(BitConverter.GetBytes(2), BitConverter.GetBytes(18u), BitConverter.GetBytes(2), BitConverter.GetBytes(0x12345678u)))));
            packages.Add(Package(0x488, 12, 0, 0, Location(2), Target(2, 18, 2), Target(2, 18, 2)));
            packages.Add(Package(0x489, 12, 12, 0, Location(1, 0x800)));
            packages.Add(Package(0x48a, 12, 12, 0, Location(4, 0x700)));
            packages.Add(Package(0x48b, 12, 12, 0, Location(2, 0x87654321)));
            packages.Add(Package(0x48c, 12, 12, 0, Location(1, 0x800), Location(1, 0x800)));
            packages.Add(Package(0x48d, 12, 12, 0, Field("PLDT", new byte[11])));
            packages.Add(Package(0x48e, 12, 0, 0x00080000, Location(2, radius: 10), Target(2, 18, 2), specific: 0x0200));
            for (byte kind = 18; kind <= 38; kind++)
                packages.Add(Package(0xa00u + kind, 12, kind, 0));
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 0x700, Field("ACBS", new byte[24]), Field("AIDT", new byte[20]),
                    Field("PKID", BitConverter.GetBytes(0x480u)), Field("PKID", BitConverter.GetBytes(0x481u))),
                Record("CREA", 0x710, Field("ACBS", new byte[24]), Field("AIDT", new byte[20])),
                Record("CREA", 0x711, Field("ACBS", new byte[24]), Field("VTCK", new byte[4])),
                Record("CREA", 0x712, Field("ACBS", new byte[24]), Field("VTCK", BitConverter.GetBytes(0x715u))),
                Record("CREA", 0x713, Field("ACBS", new byte[24]), Field("VTCK", BitConverter.GetBytes(0x700u))),
                Record("CREA", 0x714, Field("ACBS", new byte[24]), Field("VTCK", new byte[3])),
                Record("CONT", 0x716, Field("CNTO", Join(BitConverter.GetBytes(0xb20u), BitConverter.GetBytes(2)))),
                Record("VTYP", 0x715),
                Record("STAT", 0x3b), Record("CELL", 0x800, Field("DATA", [1])), References(), Join(packages.ToArray())));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm"]);
            CheckSandboxDailyWindows(new FalloutNumericGameSettings(records));
            var activity = FalloutSandboxActivityProgress.Select(records.NumericSettings, 100,
                FalloutSandboxActivity.Wander, 10.25f, new FalloutSandboxPreferences(), () => .5f);
            var coldActivity = System.Text.Json.JsonSerializer.Deserialize<FalloutSandboxActivityProgress>(
                System.Text.Json.JsonSerializer.Serialize(activity))!;
            var sandboxMotion = new FalloutActorPackageMotion(new("Base.esm", 0x486), new string('a', 64),
                "idle", new string('b', 64), 0, false, [0, 0, 0], [0, 0, 0, 1],
                Wander: new(new("Base.esm", 0x800), new string('c', 64), [1, 2, 3], false), Sandbox: activity);
            var copiedMotion = sandboxMotion.Copy();
            sandboxMotion.Position[0] = 9; sandboxMotion.Wander!.Destination[0] = 9; activity.Preferences[0] = 9;
            Require(copiedMotion.Position[0] == 0 && copiedMotion.Wander!.Destination[0] == 1 &&
                copiedMotion.Sandbox!.Preferences[0] == 129, "Sandbox motion copy aliases live pose, destination or preferences.");
            activity.Preferences[0] = 129;
            Require(activity.Duration == 30 && !coldActivity.Expired(10.75f) && coldActivity.Expired(10.8f) &&
                coldActivity.Preferences[3] == 1 && coldActivity.Preferences[5] == 129 &&
                FalloutSandboxActivityProgress.Elapsed(10.75f, 11.25f) == 31 &&
                FalloutSandboxActivityProgress.Elapsed(23.5f, .5f) == -77,
                "Sandbox activity lost its source clock, strict expiry, duration or cold preference history.");
            Reject(() => coldActivity.Expired(float.NaN));
            var dialogueActivity = FalloutSandboxActivityProgress.Select(records.NumericSettings, 100,
                FalloutSandboxActivity.Dialogue, 10.25f, new FalloutSandboxPreferences(activity.Preferences), () => .5f,
                new("Base.esm", 0x901));
            var coldDialogue = System.Text.Json.JsonSerializer.Deserialize<FalloutSandboxActivityProgress>(
                System.Text.Json.JsonSerializer.Serialize(dialogueActivity))!;
            coldDialogue.Validate();
            Require(coldDialogue.Target == new FalloutFormKey("Base.esm", 0x901) && coldDialogue.Preferences[5] == 1 &&
                coldDialogue.Preferences[3] == 2, "Sandbox dialogue continuation lost its selected listener or category history.");
            Reject(() => (coldDialogue with { Target = null }).Validate());
            Reject(() => (coldActivity with { Target = coldDialogue.Target }).Validate());
            Console.WriteLine("OPENNV_SANDBOX_ACTIVITY_CLOCK_PASS gameHour=true strictExpiry=true sourceMinuteComponents=true coldPreferences=true gameplay=unverified");
            var social = FalloutConversationSuppression.Sample(records.NumericSettings, () => .25f);
            Require(social == 12.5f && FalloutConversationSuppression.Advance(social, 13) == -.5f,
                "Conversation suppression lost reversed setting bounds or expired through-zero continuation.");
            var preferences = new FalloutSandboxPreferences();
            Require(preferences.ActivityWeights(null, false, false, social > 0)[5] == 0 &&
                preferences.ActivityWeights(null, false, false, FalloutConversationSuppression.Advance(social, 13) > 0)[5] > 0,
                "Conversation suppression did not release the activity after expiration.");
            FalloutFormKey Key(uint id) => new("Base.esm", id);
            Require(!FalloutActorFood.IsFood(records, Key(0xb10)) &&
                !FalloutActorFood.IsFood(records, Key(0xb11)) && !FalloutActorFood.IsFood(records, Key(0xb12)) &&
                FalloutActorFood.IsFood(records, Key(0xb13)) && FalloutActorFood.IsFood(records, Key(0xb14)) &&
                FalloutActorFood.IsFood(records, Key(0xb20)) && !FalloutActorFood.IsFood(records, Key(0xb21)) &&
                !FalloutActorFood.IsFood(records, Key(0x700)), "Actor food classification lost food flags, mixed effects or ingredient semantics.");
            Reject(() => FalloutActorFood.IsFood(records, Key(0xb22)));
            Require(!FalloutDialogueSpeaker.HasConversationVoice(records, Key(0x710)) &&
                !FalloutDialogueSpeaker.HasConversationVoice(records, Key(0x711)) &&
                FalloutDialogueSpeaker.HasConversationVoice(records, Key(0x712)) &&
                FalloutDialogueSpeaker.HasConversationVoice(records, Key(0x700)),
                "Ambient voice admission confused voiceless creatures, voiced creatures or NPCs.");
            Reject(() => FalloutDialogueSpeaker.HasConversationVoice(records, Key(0x713)));
            Reject(() => FalloutDialogueSpeaker.HasConversationVoice(records, Key(0x714)));
            var hashes = records.EffectiveRecords("PACK").ToDictionary(record => record.FormKey,
                record => Convert.ToHexString(SHA256.HashData(record.ReadData())));
            var foodTarget = FalloutPackageTarget.Read(records.GetEffective(Key(0x487)));
            Require(foodTarget is { Type: 2, RawTarget: 18, Form: null, CountOrDistance: 2, AuxiliaryBits: 0x12345678 },
                "Generic package target lost object category, count or auxiliary source bytes.");
            Reject(() => FalloutPackageTarget.Read(records.GetEffective(Key(0x488))));
            Reject(() => FalloutFindFurniturePackage.Read(records.GetEffective(Key(0x487))));
            var find = FalloutFindPackage.Read(records.GetEffective(Key(0x48e)));
            Require(find.AllowFalls && find.AllowKilling && find.Target is { Type: 2, RawTarget: 18, CountOrDistance: 2 },
                "Find declaration lost its independent flags or source target.");
            Reject(() => FalloutFindPackage.Read(records.GetEffective(Key(0x488))));
            Reject(() => FalloutFindPackage.Read(records.GetEffective(Key(0x481))));
            using (var findWorld = new FalloutReferenceWorld(records))
            {
                Reject(() => find.Location(records, findWorld, Key(0x900)));
                findWorld.SetActorPackageStartLocation(Key(0x900), new(Key(0x800), [10, 20, 30]));
                var point = find.Location(records, findWorld, Key(0x900));
                point.Position[0] = 999;
                Require(find.Location(records, findWorld, Key(0x900)).Position.SequenceEqual([10f, 20f, 30f]),
                    "Find location exposed or recaptured its authoritative actor start point.");
                using var coldFind = new FalloutReferenceWorld(records);
                coldFind.Restore(findWorld.Capture());
                Require(find.Location(records, coldFind, Key(0x900)).Position.SequenceEqual([10f, 20f, 30f]),
                    "Find current location did not survive cold restoration.");
            }
            using (var sourceWorld = new FalloutReferenceWorld(records))
            {
                sourceWorld.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
                sourceWorld.SetActorPackageStartLocation(Key(0x900), new(Key(0x800), [1, 2, 3]));
                var before = sourceWorld.FindFoodSources(Key(0x900), find, 1);
                Require(!before.PlayerInspected && !before.NonresidentSourcesInspected &&
                    before.Sources.Count == 1 && before.Sources[0] is { Inventory: false, Count: 3 } &&
                    before.Sources[0].Reference == Key(0x903) && before.UninitializedInventories.Contains(Key(0x901)) &&
                    before.UninitializedInventories.Contains(Key(0x904)) && sourceWorld.Get(Key(0x904)).Inventory is null,
                    "Find source collection lost a placed stack or silently rolled an uninitialized inventory.");
                var container = sourceWorld.Inventory(Key(0x904), 1).Contents;
                Require(container.Item(Key(0xb20)) is { Count: 2, Value: 7, Weight: 1.5f },
                    "Ingredient inventory lost its source count, value or weight.");
                var revision = container.Revision;
                var after = sourceWorld.FindFoodSources(Key(0x900), find, 1);
                Require(after.Sources.Any(value => value.Reference == Key(0x904) && value.Inventory &&
                    value.Item == Key(0xb20) && value.Count == 2 && value.InventoryRevision == revision) &&
                    !after.UninitializedInventories.Contains(Key(0x904)) && container.Revision == revision,
                    "Find source collection changed inventory or lost an actual retained food stack.");
                var playerInventory = new FalloutPlayerInventory(7);
                sourceWorld.Take(Key(0x903), playerInventory, 1, null);
                Require(sourceWorld.Get(Key(0x903)).Taken && playerInventory.Item(Key(0xb20)) is { Count: 3, Value: 7, Weight: 1.5f } &&
                    FalloutInventoryItemCounts.Count(records, playerInventory, Key(0xb20)) == 3,
                    "Placed ingredient pickup or script item count lost its inventory stack.");
                Require(sourceWorld.FindFoodSources(Key(0x900), find, 1).Sources.All(value => value.Reference != Key(0x903)),
                    "Find source collection retained a taken placed stack.");
            }
            var cellLocation = FalloutScriptPackage.Read(records.GetEffective(Key(0x489)));
            var objectLocation = FalloutScriptPackage.Read(records.GetEffective(Key(0x48a)));
            var currentLocation = FalloutScriptPackage.Read(records.GetEffective(Key(0x48b)));
            Require(cellLocation.LocationDeclaration is { Type: 1, RawLocation: 0x800, Radius: 0 } &&
                cellLocation.LocationDeclaration.Form == Key(0x800) && cellLocation.LocationReference is null &&
                objectLocation.LocationDeclaration?.Form == Key(0x700) && objectLocation.LocationReference is null &&
                currentLocation.LocationDeclaration is { Type: 2, RawLocation: 0x87654321, Form: null },
                "Package location lost its type-specific form, raw value or reference-only legacy behavior.");
            Reject(() => FalloutScriptPackage.Read(records.GetEffective(Key(0x48c))));
            Reject(() => FalloutScriptPackage.Read(records.GetEffective(Key(0x48d))));
            Console.WriteLine("OPENNV_PACKAGE_LOCATION_CONTRACT_PASS cell=true object=true rawCurrent=true duplicatesRejected=true malformedRejected=true execution=unverified");
            foreach (var extent in new[] { 8, 12 })
            {
                var first = extent == 8 ? 0x400u : 0x420u;
                FalloutPluginRecord Source(uint offset) => records.GetEffective(Key(first + offset));
                var data = FalloutPackageData.Read(Source(7));
                Require(data.Flags == 0x89abcdef && data.Procedure == 17 && data.BehaviorFlags == 0x1234 &&
                    data.SourceExtent == extent && data.SpecificFlags == (extent == 8 ? null : (ushort?)0x5678),
                    "Package data lost common fields, source extent or absent type-specific flags.");
                Require(FalloutScriptPackage.Read(Source(3)) is { Procedure: 6, LocationType: 3, Flags: 0 },
                    "Generic package declaration rejected a supported source extent.");
                Require(FalloutFindFurniturePackage.Read(Source(0)).TargetType == 2 &&
                    FalloutFollowPackage.Read(Source(1)).Distance == 100 &&
                    FalloutEscortPackage.Read(Source(2)).Distance == 100 &&
                    FalloutEditorTravelPackage.Read(Source(3)).Radius == 0 &&
                    FalloutTravelPackage.Read(Source(3)).LocationType == 3 &&
                    FalloutPatrolRoute.Read(records, Source(4), Key(0x900)).Points.Single().Reference == Key(0x902) &&
                    !FalloutGuardPackage.Read(Source(5)).WarnAndAttack &&
                    FalloutDialoguePackage.Read(Source(6)).ActivationDistance == 100,
                    "A procedure reader retained its obsolete package-data extent restriction.");
            }
            foreach (var id in lengths.Select((_, index) => 0x500u + (uint)index).Append(0x510u).Append(0x511u).Append(0x700u))
                Reject(() => FalloutPackageData.Read(records.GetEffective(Key(id))));
            Reject(() => FalloutEditorTravelPackage.Read(records.GetEffective(Key(0x482))));
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x482))));
            Reject(() => FalloutEditorTravelPackage.Read(records.GetEffective(Key(0x483))));
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x483))));
            Require(FalloutPackageData.Read(records.GetEffective(Key(0x484))).SourceExtent == 8,
                "Package data admission substituted schedule validation for its source extent.");
            Reject(() => FalloutPackageSchedule.Read(records.GetEffective(Key(0x484))));
            using var world = new FalloutReferenceWorld(records);
            var currentSandbox = FalloutSandboxPackage.Read(records.GetEffective(Key(0x48b)));
            Reject(() => currentSandbox.Location(records, world, Key(0x901)));
            Reject(() => world.CaptureActorPackageStartLocation(Key(0x901), 1));
            world.Get(Key(0x901)).QuerySpatialPlacement = () => new(Key(0x800), [10, 20, 30], [0, 0, 0]);
            world.CaptureActorPackageStartLocation(Key(0x901), 1);
            world.Get(Key(0x901)).QuerySpatialPlacement = null;
            var sandboxCenter = currentSandbox.Location(records, world, Key(0x901));
            sandboxCenter.Location[0] = 999;
            world.SetPlacement(Key(0x901), new(Key(0x800), [40, 50, 60], [0, 0, 0]));
            Require(currentSandbox.Location(records, world, Key(0x901)).Location.SequenceEqual([10f, 20f, 30f]),
                "Current-location Sandbox recentered after movement or exposed its retained start.");
            using (var coldSandboxLocation = new FalloutReferenceWorld(records))
            {
                coldSandboxLocation.Restore(world.Capture());
                Require(currentSandbox.Location(records, coldSandboxLocation, Key(0x901)).Location.SequenceEqual([10f, 20f, 30f]),
                    "Current-location Sandbox lost its actor start on cold restore.");
            }
            foreach (var reference in new[] { Key(0x900), Key(0x901) })
            {
                var participant = world.Get(reference);
                Require(!world.BlocksAmbientConversation(reference), "Available actor was rejected by preliminary conversation gates.");
                participant.Engagement = new(Key(0x14));
                Require(world.BlocksAmbientConversation(reference), "Combat actor remained an ambient conversation candidate.");
                participant.Engagement = null;
                participant.KnockedDown = true;
                Require(world.BlocksAmbientConversation(reference), "Knocked-down actor remained an ambient conversation candidate.");
                participant.KnockedDown = false;
                participant.Enabled = false;
                Require(world.BlocksAmbientConversation(reference), "Disabled actor remained an ambient conversation candidate.");
                participant.Enabled = true;
                Require(!world.BlocksAmbientConversation(reference), "Recovered actor retained a stale conversation rejection.");
                participant.Injury = new(true, null, new Dictionary<byte, float>());
                Require(world.BlocksAmbientConversation(reference), "Dead actor remained an ambient conversation candidate.");
                participant.Injury = null;
                FalloutFormKey? targetPackage = Key(0x485);
                var sitting = 0;
                participant.QuerySitting = () => sitting;
                participant.QueryCurrentPackage = () => targetPackage;
                Require(world.BlocksAmbientConversationTarget(reference), "Target Sandbox dialogue exclusion was ignored.");
                targetPackage = Key(0x486);
                Require(!world.BlocksAmbientConversationTarget(reference), "Target retained an obsolete package exclusion.");
                targetPackage = Key(0x481);
                Require(!world.BlocksAmbientConversationTarget(reference), "Non-Sandbox target inherited Sandbox exclusions.");
                foreach (var transition in new[] { 1, 2, 4 })
                {
                    sitting = transition;
                    Require(world.BlocksAmbientConversationTarget(reference), "Furniture transition admitted an ambient listener.");
                }
                sitting = 3;
                Require(!world.BlocksAmbientConversationTarget(reference), "Fully seated actor was rejected as a furniture transition.");
                var restrictedKinds = new byte[] { 18, 19, 20, 21, 22, 23, 24, 25, 27, 28, 29, 31, 32, 33, 34, 35, 36 };
                for (byte kind = 18; kind <= 38; kind++)
                {
                    targetPackage = Key(0xa00u + kind);
                    Require(world.BlocksAmbientConversationTarget(reference) == restrictedKinds.Contains(kind),
                        "Listener package restriction differs from the source kind table.");
                }
                participant.QuerySitting = null;
                Reject(() => world.BlocksAmbientConversationTarget(reference));
                participant.QueryCurrentPackage = null;
            }
            Reject(() => world.BlocksAmbientConversation(Key(0x902)));
            Console.WriteLine("OPENNV_AMBIENT_PARTICIPANT_GATES_PASS npc=true creature=true combat=true knockdown=true disabled=true dead=true recovered=true currentTargetExclusion=true completeAdmission=unverified");
            Require(!world.BlocksAmbientConversationRelationship(Key(0x900), Key(0x901)),
                "Unaggressive neutral actors were treated as hostile conversation partners.");
            world.ChangeActorValue(Key(0x901), "aggression", "setav", 2);
            Require(world.BlocksAmbientConversationRelationship(Key(0x900), Key(0x901)) &&
                world.BlocksAmbientConversationRelationship(Key(0x901), Key(0x900)),
                "Conversation hostility ignored the reverse direction, live aggression or zero-confidence actor.");
            world.ChangeActorValue(Key(0x901), "aggression", "setav", 0);
            Require(!world.BlocksAmbientConversationRelationship(Key(0x900), Key(0x901)),
                "Conversation hostility retained obsolete aggression.");
            world.Get(Key(0x901)).PlayerTeammate = true;
            Reject(() => world.BlocksAmbientConversationRelationship(Key(0x900), Key(0x901)));
            world.Get(Key(0x901)).PlayerTeammate = false;
            Console.WriteLine("OPENNV_CONVERSATION_HOSTILITY_PASS directional=true liveAggression=true confidenceIndependent=true recovered=true teammateUnboundVisible=true");
            Require(!world.IsRecentConversationPartner(Key(0x900), Key(0x901), 100), "Empty conversation history consumed a populated-history interval.");
            world.RememberConversationAttempt(Key(0x900), Key(0x901));
            Require(world.Get(Key(0x900)).ConversationHistory!.Partners.SequenceEqual([Key(0x901)]) &&
                world.Get(Key(0x901)).ConversationHistory!.Partners.SequenceEqual([Key(0x900)]),
                "Conversation attempt did not retain reciprocal participants.");
            Require(world.IsRecentConversationPartner(Key(0x900), Key(0x901), 4), "Recent conversation partner was admitted before expiry.");
            using (var coldHistory = new FalloutReferenceWorld(records))
            {
                coldHistory.Restore([world.Get(Key(0x900)).Capture()]);
                Require(coldHistory.Get(Key(0x900)).ConversationHistory!.RemainingSeconds == 6 &&
                    !coldHistory.IsRecentConversationPartner(Key(0x900), Key(0x901), 6),
                    "Cold conversation history redrew or failed to expire its retained interval.");
            }
            Require(!world.IsRecentConversationPartner(Key(0x900), Key(0x901), 6) &&
                world.Get(Key(0x900)).ConversationHistory!.Partners.Count == 0,
                "Conversation history did not clear at its boundary.");
            records.NumericSettings.Set("fAItalktosameNPCtimer", 3);
            _ = world.IsRecentConversationPartner(Key(0x900), Key(0x901), 100);
            Require(world.Get(Key(0x900)).ConversationHistory!.RemainingSeconds == 3,
                "Empty conversation history did not refresh from current settings.");
            Console.WriteLine("OPENNV_CONVERSATION_HISTORY_PASS reciprocalAttempt=true candidateClock=true cold=true expirationBoundary=true liveSettingRefresh=true playback=unverified");
            world.Get(Key(0x900)).ConversationSuppressionSeconds = -.5f;
            var socialSnapshot = world.Get(Key(0x900)).Capture();
            using (var coldSocial = new FalloutReferenceWorld(records))
            {
                coldSocial.Restore([socialSnapshot]);
                Require(coldSocial.Get(Key(0x900)).ConversationSuppressionSeconds == -.5f,
                    "Cold actor reconstruction discarded or redrew an expired conversation timer.");
            }
            using (var invalidSocial = new FalloutReferenceWorld(records))
            {
                Reject(() => invalidSocial.Restore([socialSnapshot with { ConversationSuppressionSeconds = float.NaN }]));
                Require(invalidSocial.InstanceCount == 0, "Invalid conversation timer partially restored the world.");
            }
            records.NumericSettings.Set("fAISocialTimerForConversationsMin", 7);
            records.NumericSettings.Set("fAISocialTimerForConversationsMax", 7);
            Require(FalloutConversationSuppression.Sample(records.NumericSettings,
                () => throw new InvalidOperationException("Equal conversation bounds consumed randomness.")) == 7,
                "Fixed conversation timer ignored live settings.");
            Console.WriteLine("OPENNV_CONVERSATION_SUPPRESSION_PASS reversedBounds=true expiredNegative=true weightRelease=true cold=true invalidAtomic=true fixedNoDraw=true");
            var queries = 0; var eligible = new List<FalloutFormKey>();
            var selected = FalloutAiPackages.Select(records, Key(0x700), _ => { queries++; return 0; },
                eligible: record => { eligible.Add(record.FormKey); return world.PackageEligible(Key(0x900), record, null, null, false); });
            Require(selected?.FormKey == Key(0x481) && queries == 1 && eligible.SequenceEqual([Key(0x480), Key(0x481)]),
                "Priority selection invented a schedule or skipped the shorter package declaration.");
            Require(hashes.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(records.GetEffective(pair.Key).ReadData()))),
                "Package data admission changed source bytes.");
            Console.WriteLine("OPENNV_PACKAGE_DATA_CONTRACT_PASS extents=8,12 absentSpecificFlags=true procedures=9 malformedRejected=true unsupportedFlagsRejected=true priority=true authoredSchedule=true sourceUnchanged=true");
        }
        finally { File.Delete(Path.Combine(directory, "Base.esm")); Directory.Delete(directory); }
    }

    private static byte[] Data(int extent, byte procedure, uint flags = 0, ushort behavior = 0, ushort specific = 0)
    {
        var data = new byte[extent]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags); data[4] = procedure; data[5] = 0xcd;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), behavior);
        if (extent == 12) { BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), specific); data[10] = data[11] = 0xcd; }
        return data;
    }
    private static byte[] Package(uint id, int extent, byte procedure, uint flags, byte[]? location = null,
        byte[]? target = null, byte[]? extra = null, ushort behavior = 0, ushort specific = 0) => Record("PACK", id,
        Field("EDID", Encoding.ASCII.GetBytes("SyntheticPackage" + id + '\0')), Field("PKDT", Data(extent, procedure, flags, behavior, specific)),
        Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]), location ?? [], target ?? [], extra ?? []);
    private static byte[] Location(int type, uint raw = 0, int radius = 0)
    {
        var data = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(data, type);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), type == 0 && raw == 0 ? 0x902 : raw);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), radius); return Field("PLDT", data);
    }
    private static byte[] Target(int type, uint form, int distance = 0)
    {
        var data = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(data, type);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), form); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), distance);
        return Field("PTDT", data);
    }
    private static byte[] Dialogue() { var data = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(data, 45); return data; }
    private static byte[] Condition()
    {
        var data = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 14); return data;
    }
    private static byte[] References()
    {
        var pose = new float[] { 1, 2, 3, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray();
        var references = Join(Record("ACHR", 0x900, Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", pose)),
            Record("ACRE", 0x901, Field("NAME", BitConverter.GetBytes(0x710u)), Field("DATA", pose)),
            Record("REFR", 0x902, Field("NAME", BitConverter.GetBytes(0x3bu)), Field("DATA", pose)),
            Record("REFR", 0x903, Field("NAME", BitConverter.GetBytes(0xb20u)), Field("DATA", pose), Field("XCNT", BitConverter.GetBytes(3))),
            Record("REFR", 0x904, Field("NAME", BitConverter.GetBytes(0x716u)), Field("DATA", pose)));
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        references.CopyTo(group, 24); return group;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unowned package declaration was accepted.");
    }
}
