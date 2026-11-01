using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCompassMarker(FalloutFormKey Reference, string Kind, float Heading,
    float Distance, bool Discovered, bool Hostile = false);

internal sealed record FalloutCompassFrame(float? HeadingRadians, float? StripOffset,
    IReadOnlyList<FalloutCompassMarker> Markers, IReadOnlyList<string> Unbound);

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutReferencePlacement> _compassSourcePlacements = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutQuestTarget>> _compassQuestTargets = [];
    private readonly Dictionary<FalloutFormKey, float> _compassNorthRotations = [];
    private readonly Dictionary<FalloutFormKey, FalloutFormKey?> _compassNorthMarkers = [];
    private readonly Dictionary<FalloutFormKey, string> _compassNorthFailures = [];

    // This is a projection of shared world/quest state. Drawing never discovers
    // locations, awards experience, changes objectives or admits actor targets.
    internal FalloutCompassFrame Compass(FalloutReferencePlacement player, float unitsToMetres,
        float stripWidth, FalloutQuestState quests, Func<FalloutFormKey, FalloutCondition, float> evaluate,
        Func<FalloutFormKey, bool?>? sight = null)
    {
        player.Validate();
        if (!float.IsFinite(stripWidth) || stripWidth <= 70)
            throw new InvalidDataException("Source compass strip has no positive heading band.");
        var cell = FalloutCellSceneReader.ReadDefinition(records, player.Cell);
        var unbound = new List<string> { "player-placed-marker", "distance-blink" };
        var world = cell.Worldspace;
        var north = world is null ? CompassNorthRotation(player.Cell, unbound) : 0;
        if (north is null) return new(null, null, [], unbound);
        var heading = player.RotationRadians[2];
        var phase = (heading + north.Value) / (2 * MathF.PI) - 0.15f;
        phase -= MathF.Floor(phase);
        var halfBand = (int)(stripWidth / 2) - 35;
        var markers = new List<FalloutCompassMarker>();
        AddActorTicks();
        var maximumDistance = unchecked((int)records.NumericSettings.IntegerBits("iMapMarkerVisibleDistance"));
        if (maximumDistance < 0) throw new InvalidDataException("Source compass marker distance is negative.");
        foreach (var source in FalloutWorldMap.SourceMarkers(records))
        {
            if (world is null) continue;
            var reference = source.Marker.Reference;
            FalloutReferencePlacement placement;
            if (_instances.TryGetValue(reference, out var instance) &&
                (instance.Placement is not null || instance.QuerySpatialPlacement is not null ||
                    instance.PackageMotion?.Position is not null || instance.Engagement?.Position is not null ||
                    instance.CaptureEngagement is not null || instance.SandboxAdmissionFailure is not null))
                placement = SpatialPlacement(reference, player, unitsToMetres);
            else
            {
                if (source.World != world) continue;
                if (!_compassSourcePlacements.TryGetValue(reference, out placement!))
                {
                    var record = records.GetEffective(reference);
                    var data = record.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
                    if (data.Length != 24) throw new InvalidDataException("Compass marker has no source transform.");
                    placement = new(FalloutCellSceneReader.ParentCell(record) ??
                        throw new InvalidDataException("Compass marker has no CELL."),
                        [BinaryPrimitives.ReadSingleLittleEndian(data.Span), BinaryPrimitives.ReadSingleLittleEndian(data.Span[4..]),
                            BinaryPrimitives.ReadSingleLittleEndian(data.Span[8..])], [0, 0, 0]);
                    placement.Validate();
                    _compassSourcePlacements.Add(reference, placement);
                }
            }
            if (!SameSpace(placement)) continue;
            var distance = DistanceTo(placement, false);
            if (distance > maximumDistance || !IsEnabled(reference)) continue;
            Add(reference, "landmark", placement, distance, MapMarkerState(source.Marker).CanTravel, false);
        }
        if (quests.ActiveQuest is { } quest && !quests.IsCompleted(quest))
        {
            try
            {
                if (!_compassQuestTargets.TryGetValue(quest, out var targets))
                    _compassQuestTargets.Add(quest, targets = FalloutQuestTargets.Read(records, quest));
                foreach (var target in targets)
                {
                    if (!quests.ObjectiveDisplayed(quest, target.Objective) || quests.Objective(quest, target.Objective).Completed) continue;
                    try
                    {
                        if (!FalloutCondition.AllPass(target.Conditions, condition => evaluate(target.Reference, condition), evaluateRunOn: true)) continue;
                        var placement = SpatialPlacement(target.Reference, player, unitsToMetres);
                        var portal = !SameSpace(placement);
                        if (portal)
                        {
                            placement = CompassQuestPortal(target, player, placement.Cell, unitsToMetres);
                            if (!SameSpace(placement)) throw new InvalidDataException("Quest portal is outside the player's source coordinate region.");
                        }
                        Add(target.Reference, "quest", placement, portal ?
                            DistanceTo(placement, horizontal: false, squared: true) : DistanceTo(placement, true), false, true);
                    }
                    catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
                    { unbound.Add($"quest-target:{target.Reference}:{error.Message}"); }
                }
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
            { unbound.Add($"quest:{quest}:{error.Message}"); }
        }
        return new(heading, phase, markers, unbound);

        bool SameSpace(FalloutReferencePlacement placement) => placement.Cell == player.Cell ||
            world is not null && FalloutCellSceneReader.ReadDefinition(records, placement.Cell).Worldspace == world;
        float DistanceTo(FalloutReferencePlacement placement, bool horizontal, bool squared = false)
        {
            var x = (double)placement.Position[0] - player.Position[0];
            var y = (double)placement.Position[1] - player.Position[1];
            var z = horizontal ? 0 : (double)placement.Position[2] - player.Position[2];
            // Native quest teleport-link HUD distance is squared XYZ, summed in
            // double precision and rounded once to Float32. Gameplay range
            // gates and other marker lanes retain their existing linear units.
            var distance = x * x + y * y + z * z;
            return (float)(squared ? distance : Math.Sqrt(distance));
        }
        void Add(FalloutFormKey reference, string kind, FalloutReferencePlacement placement, float distance, bool discovered, bool clamp)
        {
            var angle = FalloutHeadingAngle.Between(player.Position, placement.Position, heading) * 1.45f;
            if (!clamp && MathF.Abs(angle) >= 70) return;
            angle = Math.Clamp(angle, -70, 70);
            markers.Add(new(reference, kind, (angle + 70) / 140 * (2 * halfBand), distance, discovered));
        }

        void AddActorTicks()
        {
            // Compass detection is independent of collision LOS. Native sound
            // and visual contributions are nonnegative. Source perception,
            // target skill and levels can establish a minimum remaining score
            // without inventing light/noise samples.
            // This proves only an admission boolean, never an exact score.
            try
            {
                float Setting(string name) => FalloutGameSettingFloats.Read(records, name);
                var playerReference = records.RuntimeFormKey(0x14);
                var owner = records.GetEffective(records.RuntimeFormKey(0x1d));
                if (QueryNativeCondition(playerReference,
                    new FalloutCondition(owner, 0, 0, (ushort)FalloutConditionFunction.GetActorValue, (uint)FalloutActorValueId.Health, 0, 0, 0)) <= 0) return;
                var perception = QueryNativeCondition(playerReference,
                    new FalloutCondition(owner, 0, 0, (ushort)FalloutConditionFunction.GetActorValue, (uint)FalloutActorValueId.Perception, 0, 0, 0));
                var playerLevelValue = QueryNativeCondition(playerReference,
                    new FalloutCondition(owner, 0, 0, (ushort)FalloutConditionFunction.GetLevel, 0, 0, 0, 0));
                if (!float.IsFinite(playerLevelValue) || playerLevelValue is < 1 or > ushort.MaxValue || playerLevelValue != MathF.Truncate(playerLevelValue))
                    throw new NotSupportedException("Detection observer level has no source integer owner.");
                var playerLevel = (int)playerLevelValue;
                int Integer(string name) => unchecked((int)records.NumericSettings.IntegerBits(name));
                var maximum = Setting("fSneakMaxDistance") * (world is null ? 1 : Setting("fSneakExteriorDistanceMult"));
                var exponent = Setting("fSneakDistanceAttenuationExponent");
                var improvedDistance = Math.Max(1, Setting("fImprovedDetectModifier"));
                var skillMultiplier = Setting("fSneakSkillMult");
                var perceptionMinimum = Setting("fSneakPerceptionSkillMin");
                var perceptionMaximum = Setting("fSneakPerceptionSkillMax");
                var inCombat = QueryNativeCondition(playerReference,
                    new FalloutCondition(owner, 0, 0, (ushort)FalloutConditionFunction.IsInCombat, 0, 0, 0, 0));
                var sitting = QueryNativeCondition(playerReference,
                    new FalloutCondition(owner, 0, 0, (ushort)FalloutConditionFunction.GetSitting, 0, 0, 0, 0));
                var activityMinimum = 1 + (inCombat != 0 ? Setting("fSneakAlertMod") + Math.Min(0, Setting("fSneakCombatMod")) : 0) +
                    (sitting == 9 ? Setting("fSneakSleepBonus") : 0) +
                    Math.Min(0, Math.Min(Setting("fSneakAmbushTargetMod"), Setting("fSneakAmbushNonTargetMod")));
                var perceptionSkill = perceptionMinimum + (perceptionMaximum - perceptionMinimum) * MathF.Truncate(perception) / 10;
                var baseline = Setting("fSneakBaseValue");
                var threshold = Setting("fPlayerDetectActorValue");
                var cap = unchecked((int)records.NumericSettings.IntegerBits("iHUDMaxCompassNPCTicks"));
                if (!float.IsFinite(perception) || perception < 0 || !float.IsFinite(maximum) || maximum <= 0 ||
                    !float.IsFinite(exponent) || exponent < 0 || !float.IsFinite(improvedDistance) || !float.IsFinite(skillMultiplier) || skillMultiplier < 0 ||
                    !float.IsFinite(perceptionSkill) || perceptionSkill < 0 || !float.IsFinite(activityMinimum) || activityMinimum < 0 ||
                    !float.IsFinite(baseline) || !float.IsFinite(threshold) || cap < 0)
                    throw new NotSupportedException("Source detection signs do not admit the nonnegative contribution proof.");
                var candidates = new List<(FalloutReferenceInstance Actor, FalloutReferencePlacement Placement, float Distance)>();
                foreach (var actor in _instances.Values.ToArray())
                {
                    if (actor.Reference == playerReference || actor.QueryNativeCondition is null || actor.QuerySpatialPlacement is null ||
                        records.GetEffective(actor.Base).Signature is not ("NPC_" or "CREA") || !IsEnabled(actor.Reference)) continue;
                    var placement = SpatialPlacement(actor.Reference, player, unitsToMetres);
                    if (!SameSpace(placement) || Health(actor.Reference).Current <= 0 || IsDead(actor.Reference)) continue;
                    var distance = DistanceTo(placement, false);
                    if (distance <= maximum) candidates.Add((actor, placement, distance));
                }
                // Native caps its retained target list before drawing. Its
                // history/tie ordering cannot be reconstructed from a subset.
                if (candidates.Count > cap) throw new NotSupportedException("Actor tick capacity requires retained detection-list ordering.");
                foreach (var (actor, placement, distance) in candidates)
                {
                    try
                    {
                        var basis = records.GetEffective(actor.Base);
                        var effects = FalloutActorTemplateOwner.Resolve(records, basis, 8, actor.Templates);
                        foreach (var field in effects.ReadSubrecords().Where(field => field.Signature == "SPLO"))
                        {
                            if (field.Data.Length != 4) throw new InvalidDataException("Actor ability identity extent is invalid.");
                            var spell = effects.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                            if (!_perkAbilities.TryGetConstantModifiers(spell, out var modifiers) || _perkAbilities.Scripts(spell).Count != 0 ||
                                modifiers.Any(modifier => modifier.ActorValue is 42 or 48 or 49))
                                throw new NotSupportedException("Actor visibility ability requires its active effect owner.");
                        }
                        if (AcquiredPerks(actor.Reference).Count != 0 ||
                            actor.Ingestibles is { Effects.Count: > 0 } or { Failure: not null } ||
                            actor.ActorValues.Keys.Any(name => name.Equals("invisibility", StringComparison.OrdinalIgnoreCase) || name.Equals("chameleon", StringComparison.OrdinalIgnoreCase)))
                            throw new NotSupportedException("Visibility effects require their active spell/equipment/perk owner.");
                        var equipment = RequireCompassEquipmentVisibility(actor);
                        var sneaking = QueryNativeCondition(actor.Reference,
                            new FalloutCondition(basis, 0, 0, (ushort)FalloutConditionFunction.IsSneaking, 0, 0, 0, 0));
                        if (sneaking != 0) throw new NotSupportedException("Stealth target requires complete detection inputs.");
                        var moving = QueryNativeCondition(actor.Reference,
                            new FalloutCondition(basis, 0, 0, (ushort)FalloutConditionFunction.IsMoving, 0, 0, 0, 0));
                        var skillUpper = 0d;
                        if (moving != 0)
                        {
                            var stats = FalloutActorTemplateOwner.Resolve(records, basis, 2, actor.Templates);
                            var acbs = stats.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
                            var actorLevel = FalloutActorLevel.Resolve(acbs, actor.Templates?.Level);
                            var skill = ActorValue(actor.Reference, "sneak");
                            if (!float.IsFinite(skill) || skill < int.MinValue || skill >= int.MaxValue)
                                throw new NotSupportedException("Detection target skill has no finite source integer value.");
                            var armorMinimum = Math.Min(0, Math.Min(Integer("iHeavyArmorStealthPenalty"), Integer("iMediumArmorStealthPenalty")));
                            double SourceInteger(double value) => value is < int.MinValue or > int.MaxValue
                                ? throw new NotSupportedException("Detection target skill/level arithmetic exceeds its source integer range.") : value;
                            var levelBonus = SourceInteger((actorLevel - (double)playerLevel) * Integer("iSneakLevelBonus"));
                            var startPenalty = SourceInteger((double)actorLevel * Integer("iSneakStartBonusLevelPenatly"));
                            var startBonus = SourceInteger(Integer("iSneakStartBonus") - startPenalty);
                            skillUpper = SourceInteger(SourceInteger(SourceInteger(Math.Truncate(skill) + levelBonus) + Math.Max(0, startBonus)) - armorMinimum);
                        }
                        var attenuation = MathF.Pow(Math.Max(0, (maximum - distance * improvedDistance) / maximum), exponent);
                        var minimumScore = baseline + skillMultiplier * (perceptionSkill * activityMinimum * attenuation - skillUpper);
                        if (moving != 0 && Math.Floor(minimumScore) < threshold)
                        {
                            // Native movement sound includes a source boot base
                            // even at zero equipment weight. Its action level
                            // is selected from the owned silent/normal/loud
                            // settings; all omitted terms must be nonnegative.
                            var bootBase = Setting("fSneakBootWeightBase");
                            var bootWeight = Setting("fSneakBootWeightMult");
                            var sounds = Setting("fSneakSoundsMult");
                            var running = Setting("fSneakRunningMult");
                            var unseen = Setting("fSneakSoundLosMult");
                            var action = Setting("fSneakActionMult");
                            var actionLevels = new[] { Integer("iSoundLevelSilent"), Integer("iSoundLevelNormal"), Integer("iSoundLevelLoud") };
                            if (bootBase < 0 || bootWeight < 0 || sounds < 0 || running < 0 || unseen < 0 || action < 0 ||
                                actionLevels.Any(level => level < 0) || Math.Truncate(action) * actionLevels.Max() > int.MaxValue ||
                                equipment.Contents.Items.Any(item => item.RecordType == "ARMO" && (item.Weight is null || item.Weight < 0)))
                                throw new NotSupportedException("Movement sound does not admit a nonnegative source-weight/action proof.");
                            var soundAttenuation = Math.Min(1, unseen);
                            var cone = Setting("fDetectionViewCone");
                            if (cone is < 0 or > 360) throw new NotSupportedException("Detection view cone is outside its source angular range.");
                            var angle = MathF.Abs(FalloutHeadingAngle.Between(player.Position, placement.Position, heading));
                            if (angle < cone / 2 && sight?.Invoke(actor.Reference) == true) soundAttenuation = 1;
                            minimumScore += Math.Max(0, attenuation * soundAttenuation * sounds * bootBase * Math.Min(1, running));
                        }
                        var lowerBound = Math.Floor(minimumScore);
                        if (!double.IsFinite(lowerBound) || lowerBound < threshold)
                            throw new NotSupportedException("Detection admission requires light/noise/history inputs.");
                        var hostile = actor.Engagement?.Target == playerReference ||
                            ActorThreat(actor.Reference).Hostile(ActorRelation(actor.Reference, playerReference));
                        var count = markers.Count;
                        Add(actor.Reference, "actor", placement, distance, false, false);
                        if (markers.Count != count) markers[^1] = markers[^1] with { Hostile = hostile };
                    }
                    catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
                    { unbound.Add($"actor-detection:{actor.Reference}:{error.Message}"); }
                }
                unbound.Add("actor-detection:nonnegative-admission-proof;exact-score-light-noise-history-and-enhanced-sensors-unbound");
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
            { unbound.Add($"actor-detection:{error.Message}"); }
        }
    }

    // Compass reads the existing equipment publication. It must not choose or
    // equip clothing/weapons as a side effect of drawing a detection marker.
    private FalloutReferenceInventory RequireCompassEquipmentVisibility(FalloutReferenceInstance actor)
    {
        var inventory = actor.Inventory ??
            throw new NotSupportedException("Detection target equipment has no inventory owner.");
        var equipped = inventory.Contents.Equipped.Select(records.RuntimeFormKey).ToHashSet();
        var weaponResolved = inventory.WeaponSelectionResolved || equipped.Any(key => records.GetEffective(key).Signature == "WEAP");
        foreach (var item in inventory.Contents.Items)
        {
            var source = records.GetEffective(item.FormKey);
            var fields = source.ReadSubrecords().Where(field => field.Signature == "EITM").ToArray();
            if (fields.Length == 0) continue;
            if (fields.Length != 1 || fields[0].Data.Length != 4)
                throw new InvalidDataException("Detection equipment enchantment identity is invalid.");
            var effect = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
            if (effect is null) continue;
            if (source.Signature == "ARMO")
            {
                if (!inventory.InitialArmorResolved)
                    throw new NotSupportedException("Detection target worn armor selection is unresolved.");
            }
            else if (source.Signature == "WEAP")
            {
                if (inventory.Unequipped.Contains(item.FormKey))
                {
                    if (equipped.Contains(item.FormKey))
                        throw new InvalidDataException("Detection equipment has conflicting equipped/unequipped state.");
                    continue;
                }
                if (!weaponResolved)
                    throw new NotSupportedException("Detection target enchanted weapon selection is unresolved.");
            }
            else
                throw new NotSupportedException("Detection inventory enchantment requires its source activation owner.");
            // A carried enchantment does not prove an active worn effect.
            if (!equipped.Contains(item.FormKey)) continue;
            if (!_perkAbilities.TryGetConstantModifiers(effect.Value, out var modifiers) ||
                _perkAbilities.Scripts(effect.Value).Count != 0)
                throw new NotSupportedException("Detection equipment effect requires its active effect owner.");
            foreach (var modifier in modifiers)
            {
                var value = (FalloutActorValueId)modifier.ActorValue;
                if (value is FalloutActorValueId.Sneak or FalloutActorValueId.Invisibility or FalloutActorValueId.Chameleon)
                    throw new NotSupportedException("Detection equipment modifies a source stealth input.");
                // These target AVs are absent from the qualified detection
                // expression. Conditions/amounts cannot make them stealth terms.
                // Other attribute, state or skill-dependency paths stay unknown.
                if (value is not
                    (FalloutActorValueId.Perception or FalloutActorValueId.Barter or FalloutActorValueId.BigGuns or
                    FalloutActorValueId.EnergyWeapons or FalloutActorValueId.Explosives or FalloutActorValueId.Lockpick or
                    FalloutActorValueId.Medicine or FalloutActorValueId.MeleeWeapons or FalloutActorValueId.Repair or
                    FalloutActorValueId.Science or FalloutActorValueId.SmallGuns or FalloutActorValueId.Speech or
                    FalloutActorValueId.Survival or FalloutActorValueId.Unarmed))
                    throw new NotSupportedException($"Detection equipment AV {modifier.ActorValue} impact is unqualified.");
            }
        }
        return inventory;
    }

    // CELL initialization derives north from its engine NorthMarker reference,
    // with the opposite sign to the placed clockwise Z angle. No marker means
    // the native extra-data getter returns zero; lighting rotation is unrelated.
    private float? CompassNorthRotation(FalloutFormKey cell, List<string> unbound)
    {
        if (_compassNorthFailures.TryGetValue(cell, out var failure))
        {
            unbound.Add(failure);
            return null;
        }
        if (_compassNorthRotations.TryGetValue(cell, out var cached))
            return Current(cached, _compassNorthMarkers[cell]);
        var markerBase = records.RuntimeFormKey(3);
        var markers = records.EffectiveCellChildren(cell, new HashSet<string> { "REFR" })
            .Where(record => FalloutDialogueTopic.RequiredForm(record, "NAME") == markerBase).ToArray();
        if (markers.Length > 1)
        {
            failure = $"interior-multiple-north-marker-order:{cell}";
            _compassNorthFailures.Add(cell, failure);
            unbound.Add(failure);
            return null;
        }
        var rotation = 0f;
        if (markers.Length == 1)
        {
            var data = markers[0].ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            if (data.Length != 24) throw new InvalidDataException("NorthMarker requires a source transform.");
            rotation = -BinaryPrimitives.ReadSingleLittleEndian(data.Span[20..]);
            if (!float.IsFinite(rotation)) throw new InvalidDataException("NorthMarker rotation is not finite.");
        }
        FalloutFormKey? marker = markers.Length == 1 ? markers[0].FormKey : null;
        _compassNorthRotations.Add(cell, rotation);
        _compassNorthMarkers.Add(cell, marker);
        return Current(rotation, marker);

        float? Current(float sourceRotation, FalloutFormKey? sourceMarker)
        {
            if (sourceMarker is { } reference && _instances.TryGetValue(reference, out var retained) && retained.Placement is not null)
            {
                unbound.Add($"interior-runtime-north-marker-transform:{reference}");
                return null;
            }
            return sourceRotation;
        }
    }
}
