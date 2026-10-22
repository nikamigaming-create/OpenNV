using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedFactionArbitrationProbe
{
    internal static void Run(string game, string savePath, string candidateSelector, string victimSelector)
    {
        var original = File.ReadAllBytes(savePath);
        var saveHash = Convert.ToHexString(SHA256.HashData(original));
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(original) ??
            throw new InvalidDataException("Owned faction audit requires an actual campaign checkpoint.");
        Require(saved.Schema == FalloutNativeCampaignSave.ExpectedSchema && saved.SaveCompatibilityId == content.SaveCompatibilityId &&
            saved.References is not null && saved.ActorOverrides is not null && saved.FactionRelations is not null,
            "Owned faction audit requires the current checkpoint and complete override lanes from the selected source stack.");
        var candidate = Key(candidateSelector); var victim = Key(victimSelector);
        Require(candidate != victim, "Select distinct actual candidate and victim references.");
        var selected = new[] { candidate, victim }.Select(key =>
        {
            var source = records.GetEffective(key);
            Require(source.Signature is "ACHR" or "ACRE", "Owned faction pair must use actual placed actor references.");
            var snapshot = saved.References!.Single(value => value.Reference == key);
            Require(snapshot.Templates is not null, "Selected saved actor has no retained template selection.");
            return snapshot;
        }).ToArray();
        var pluginHashes = content.PluginSources.ToDictionary(value => value.AbsolutePath, value => FileHash(value.AbsolutePath));
        using var world = new FalloutReferenceWorld(records);
        world.Restore(selected);
        world.RestoreActorOverrides(saved.ActorOverrides);
        world.RestoreFactionRelations(saved.FactionRelations);
        foreach (var snapshot in selected)
            Require(JsonSerializer.Serialize(world.Get(snapshot.Reference).Templates?.Capture()) == JsonSerializer.Serialize(snapshot.Templates),
                "Faction fixture changed an actual saved template choice or its random state.");
        var before = State(world);
        var forward = Observe(records, world, candidate, victim);
        var reverse = Observe(records, world, victim, candidate);
        Require(forward.GetProperty("oldImplicitSharedAllyWouldConflict").GetBoolean() ||
            forward.GetProperty("mixedEnemyFriendly").GetBoolean(),
            "The selected saved pair did not reproduce an old shared-input or mixed-reaction refusal.");
        Require(State(world) == before, "Actor relation queries mutated the saved reference or override lanes.");

        // Reconstruct the selected real references and all actual saved override
        // lanes. This is a relation-owner fixture, not full campaign admission.
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(Copy(world.Capture()));
        cold.RestoreActorOverrides(Copy(world.CaptureActorOverrides()));
        cold.RestoreFactionRelations(Copy(world.CaptureFactionRelations()));
        var coldBefore = State(cold);
        Require(Observe(records, cold, candidate, victim).GetRawText() == forward.GetRawText() &&
            Observe(records, cold, victim, candidate).GetRawText() == reverse.GetRawText() && State(cold) == coldBefore,
            "Cold faction arbitration changed actual directed memberships, templates, source pairs or overlays.");
        Require(FileHash(savePath) == saveHash && pluginHashes.All(pair => FileHash(pair.Key) == pair.Value),
            "Owned faction proof changed its checkpoint or plugin inputs.");
        Console.WriteLine("OPENNV_OWNED_FACTION_ARBITRATION_PASS " + JsonSerializer.Serialize(new
        {
            source = content.SaveCompatibilityId,
            saveSha256 = saveHash,
            scopedSavedRelationFixture = true,
            candidateToVictim = true,
            candidate = Identity(selected[0]),
            victim = Identity(selected[1]),
            actorOverrideRows = saved.ActorOverrides!.Count,
            factionRelationRows = saved.FactionRelations!.Count,
            forward,
            reverse,
            cold = true,
            sourceAndSaveUnchanged = true,
            actualSpecificLoggedVictim = "unproven",
            fullCampaignAdmission = "unverified",
            assistanceEligibilityRangeAndLineOfSight = "unverified",
            sharedMembershipInput = "actual-directed-self-pairs-only;no-implicit-ally",
            playerEnemyFactionFlag = "unchanged-unqualified-boundary",
            ordinaryAssistanceAndRetailParity = "unverified",
        }));

        object Identity(FalloutReferenceSnapshot snapshot)
        {
            var reference = records.GetEffective(snapshot.Reference);
            var basis = records.GetEffective(snapshot.Base);
            var factionOwner = FalloutActorTemplateOwner.Resolve(records, basis, 4, world.Get(snapshot.Reference).Templates);
            return new
            {
                reference = snapshot.Reference.ToString(),
                referenceWinningPlugin = reference.Plugin.Name,
                referenceSha256 = Hash(reference.ReadData()),
                basis = snapshot.Base.ToString(),
                baseSha256 = Hash(basis.ReadData()),
                savedTemplates = snapshot.Templates,
                factionTemplateOwner = factionOwner.FormKey.ToString(),
                factionTemplateOwnerSha256 = Hash(factionOwner.ReadData()),
            };
        }
    }

    private static JsonElement Observe(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey fromReference, FalloutFormKey toReference)
    {
        var from = world.ActorFactions(fromReference).OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal).ToArray();
        var to = world.ActorFactions(toReference).OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal).ToArray();
        var shared = from.Where(pair => pair.Value >= 0).Select(pair => pair.Key)
            .Intersect(to.Where(pair => pair.Value >= 0).Select(pair => pair.Key)).ToArray();
        var reactions = new List<uint>();
        var overlays = world.CaptureFactionRelations().ToDictionary(row => (row.From, row.To));
        var pairs = new List<object>();
        foreach (var faction in from.Where(pair => pair.Value >= 0))
            foreach (var other in to.Where(pair => pair.Value >= 0))
            {
                var reaction = world.FactionCombatReaction(faction.Key, other.Key);
                Require(reaction <= 3, "Owned source reaction is invalid.");
                reactions.Add(reaction);
                var source = records.GetEffective(faction.Key);
                var declared = source.ReadSubrecords().Where(field => field.Signature == "XNAM").Select(field =>
                {
                    Require(field.Data.Length == 12, "Owned faction XNAM extent is invalid.");
                    var bytes = field.Data.Span;
                    return new
                    {
                        target = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(bytes)).ToString(),
                        disposition = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
                        combatReaction = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
                    };
                }).Where(row => row.target == other.Key.ToString()).ToArray();
                pairs.Add(new
                {
                    from = faction.Key.ToString(),
                    to = other.Key.ToString(),
                    fromRank = faction.Value,
                    toRank = other.Value,
                    effectiveReaction = reaction,
                    winningPlugin = source.Plugin.Name,
                    sourceSha256 = Hash(source.ReadData()),
                    declared,
                    savedOverlay = overlays.GetValueOrDefault((faction.Key, other.Key)),
                });
            }
        // Independent explicit contract table, not the production selection
        // expression. Rows/columns are neutral, enemy, ally, friend labels.
        uint[,] contract = { { 0, 1, 2, 3 }, { 1, 1, 2, 3 }, { 2, 2, 2, 2 }, { 3, 3, 2, 3 } };
        uint expected = 0;
        foreach (var reaction in reactions) expected = contract[expected, reaction];
        var actual = world.ActorRelation(fromReference, toReference);
        Require(actual == expected, "Actual owned actor relation disagrees with the qualified mixed-reaction table.");
        return JsonSerializer.SerializeToElement(new
        {
            from = fromReference.ToString(),
            to = toReference.ToString(),
            membershipsFrom = from.Select(pair => new { faction = pair.Key.ToString(), rank = pair.Value }),
            membershipsTo = to.Select(pair => new { faction = pair.Key.ToString(), rank = pair.Value }),
            sharedMemberships = shared.Select(key => key.ToString()),
            pairInputs = pairs,
            reactions,
            expected,
            actual,
            mixedEnemyFriendly = reactions.Contains(1) && reactions.Any(value => value is 2 or 3),
            oldImplicitSharedAllyWouldConflict = shared.Length != 0 && reactions.Contains(1),
        });
    }

    private static string State(FalloutReferenceWorld world) => JsonSerializer.Serialize(new
    { references = world.Capture(), overrides = world.CaptureActorOverrides(), relations = world.CaptureFactionRelations() });

    private static T[] Copy<T>(IReadOnlyList<T> values) => JsonSerializer.Deserialize<T[]>(JsonSerializer.Serialize(values))!;
    private static FalloutFormKey Key(string selector) => selector.Split(':') is [var plugin, var id] && plugin.Length != 0
        ? new(plugin, Convert.ToUInt32(id, 16)) : throw new ArgumentException("Use exact OwnerPlugin:hex actor selectors.");
    private static string Hash(ReadOnlyMemory<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes.Span));
    private static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
