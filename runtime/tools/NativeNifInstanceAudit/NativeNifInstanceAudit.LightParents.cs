using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private sealed record AuditedPlacedLight(FalloutPlacedReference Reference, FalloutBaseObjectDefinition Base, FalloutPlacedLight Source);
    private sealed record SourceLightEnableDomain(FalloutFormKey Root, bool Opposite, bool EnginePlayer,
        bool InitialRootEnabled, IReadOnlyList<object> Chain);

    private void ExerciseOwnedLightParents(string game, string mod, string modRoot, string output, string[] arguments)
    {
        var split = Array.IndexOf(arguments, "--dependencies");
        if (split < 1) throw new ArgumentException("Owned light audit needs source CELL keys then --dependencies.");
        var setup = new FalloutModStackSelection([new(mod, modRoot, arguments[(split + 1)..])]).Resolve(game);
        RuntimeLiveContentSource.Configure(setup.BaseInstallation.InstallRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var cells = arguments[..split].Select(value =>
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0) throw new ArgumentException("Owned light audit CELL keys require plugin:hex-id.");
            return FalloutCellSceneReader.Read(records, new(value[..separator], Convert.ToUInt32(value[(separator + 1)..], 16)));
        }).ToArray();
        var hour = FalloutGlobalState.Read(records).Get(FalloutGameTimeBindings.Read(records).Hour);
        var skies = cells.ToDictionary(cell => cell.Cell.FormKey, cell =>
        {
            var sky = new FalloutSkyLightingState(records, FalloutGameSettingFloats.Read(records, "fDaytimeColorExtension"));
            sky.EnterCell(cell.Cell); return sky;
        });
        var cohort = new List<AuditedPlacedLight>(); var unbound = new List<object>(); var unparented = 0; var denominator = 0;
        foreach (var cell in cells)
        {
            foreach (var reference in cell.References.Where(reference => cell.BaseObjects[reference.Base].Light is not null))
            {
                denominator++;
                var basis = cell.BaseObjects[reference.Base];
                try
                {
                    var light = FalloutPlacedLightResolver.Resolve(reference, basis, records,
                        region => skies[reference.Cell].RegionEmittance(region, hour));
                    if (reference.EnableParent is null) unparented++;
                    else cohort.Add(new(reference, basis, light));
                }
                catch (NotSupportedException error)
                {
                    unbound.Add(new
                    {
                        reference = reference.FormKey.ToString(),
                        basis = basis.FormKey.ToString(),
                        reference.Scale,
                        model = basis.ModelPath,
                        flags = basis.Light!.Flags,
                        error = error.Message
                    });
                }
            }
        }
        if (cohort.Count == 0) throw new InvalidDataException("Selected source cells have no admitted parent-controlled static lights.");
        var references = cohort.Select(light => light.Reference).ToArray();
        foreach (var cell in cells)
            world.LoadCell(cell with { References = references.Where(reference => reference.Cell == cell.Cell.FormKey).ToArray() });
        var enginePlayer = records.RuntimeFormKey(0x14);
        SourceLightEnableDomain SourceDomain(FalloutFormKey key)
        {
            var visited = new HashSet<FalloutFormKey>(); var chain = new List<object>(); var opposite = false;
            while (key != enginePlayer)
            {
                if (!visited.Add(key)) throw new InvalidDataException("Owned light parent graph contains a cycle.");
                var record = records.GetEffective(key);
                var fields = record.ReadSubrecords().Where(field => field.Signature == "XESP").ToArray();
                if (fields.Length > 1 || fields.Length == 1 && fields[0].Data.Length != 8)
                    throw new InvalidDataException("Source light parent graph has malformed XESP data.");
                var flags = fields.Length == 0 ? (byte)0 : fields[0].Data.Span[4];
                if ((flags & ~3) != 0) throw new NotSupportedException("Source light parent graph has unknown XESP flags.");
                var parent = fields.Length == 0 ? null : record.Plugin.AdjustOptionalFormId(
                    BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
                var rootEnabled = (record.Flags & 0x800) == 0;
                chain.Add(new
                {
                    reference = key.ToString(),
                    parent = parent?.ToString(),
                    opposite = (flags & 1) != 0,
                    initiallyDisabled = !rootEnabled,
                    sha256 = Convert.ToHexString(SHA256.HashData(record.ReadData()))
                });
                if (parent is null) return new(key, opposite, false, rootEnabled, chain);
                opposite ^= (flags & 1) != 0;
                key = parent.Value;
            }
            return new(key, opposite, true, true, chain);
        }
        // This oracle reads source bytes independently of the world's enable
        // parser and IsEnabled. Each light is one source root bit XOR parity.
        var domains = references.ToDictionary(reference => reference.FormKey, reference => SourceDomain(reference.FormKey));
        var roots = domains.Values.Select(domain => domain.Root).Where(key => key != enginePlayer)
            .Distinct().OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
        var rootStates = roots.ToDictionary(key => key, key => domains.Values.First(domain => domain.Root == key).InitialRootEnabled);
        bool Expected(FalloutFormKey key) => (domains[key].EnginePlayer || rootStates[domains[key].Root]) != domains[key].Opposite;
        var initial = references.ToDictionary(reference => reference.FormKey, reference => Expected(reference.FormKey));
        if (references.Any(reference => world.IsEnabled(reference.FormKey) != initial[reference.FormKey]))
            throw new InvalidDataException("Initial world light state differs from original source XESP parity.");
        var configuration = RuntimeConfiguration.Load(); var units = configuration.World.GameUnitsToMeters;
        Transform3D Placement(FalloutPlacedReference reference) => new(
            GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
            GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
        var constructions = new Dictionary<FalloutFormKey, int>(); var visibilityChecks = 0; var toggleUpdates = 0;
        var enumerationSetupUpdates = 0; var grayTransitions = 0; var enumeratedStates = 0; var coldRootUpdates = 0;
        // Bound lab work explicitly; larger domains retain exact symbolic
        // factorization rather than asserting that every assignment was run.
        const int maximumEnumeratedRootCount = 12;
        var exhaustive = roots.Length <= maximumEnumeratedRootCount;
        Node3D? root = null;
        try
        {
            (Node3D Root, RuntimeNativeReferencePresentation Projection) Publish(FalloutReferenceWorld authority,
                IReadOnlyDictionary<FalloutFormKey, bool> enabled)
            {
                var nativeRoot = new Node3D { Name = "OwnedLightParentAudit" }; AddChild(nativeRoot);
                Node3D Materialize(FalloutPlacedReference reference)
                {
                    var item = cohort.Single(item => item.Reference.FormKey == reference.FormKey);
                    var light = RuntimeNativePlacedLightBuilder.Build(reference, item.Base, Placement(reference), units,
                        configuration.Renderer.PointLightEnergyScale, configuration.Renderer.MinimumPointLightEnergy,
                        configuration.Renderer.AuthoredPointLightShadows, records,
                        region => skies[reference.Cell].RegionEmittance(region, hour));
                    light.SetMeta("opennv_reference_form_key", reference.FormKey.ToString()); nativeRoot.AddChild(light);
                    constructions[reference.FormKey] = constructions.GetValueOrDefault(reference.FormKey) + 1;
                    return light;
                }
                var projection = new RuntimeNativeReferencePresentation(authority, references, Materialize);
                nativeRoot.AddChild(projection);
                foreach (var reference in references.Where(reference => enabled[reference.FormKey]))
                    projection.Register(reference.FormKey, Materialize(reference));
                return (nativeRoot, projection);
            }
            void Verify(FalloutReferenceWorld authority, RuntimeNativeReferencePresentation projection)
            {
                foreach (var item in cohort)
                {
                    var key = item.Reference.FormKey; var enabled = Expected(key); visibilityChecks++;
                    if (authority.IsEnabled(key) != enabled)
                        throw new InvalidDataException($"World light {key} differs from independent source XESP parity.");
                    if (!projection.Nodes.TryGetValue(key, out var node))
                    {
                        if (enabled) throw new InvalidDataException($"Enabled source light {key} was not materialized.");
                        continue;
                    }
                    if (node is not OmniLight3D light || light.Visible != enabled || light.IsVisibleInTree() != enabled ||
                        light.ProcessMode != (enabled ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled) ||
                        light.GetMeta("opennv_reference_enabled").AsBool() != enabled)
                        throw new InvalidDataException($"Native source light {key} differs from shared enable state.");
                    var source = item.Source;
                    var color = RetailLighting.GodotLightColor(new(source.ShaderColorRgb[0], source.ShaderColorRgb[1], source.ShaderColorRgb[2]));
                    if (light.Transform != Placement(item.Reference) || light.LightColor != color ||
                        light.LightEnergy != MathF.Max(configuration.Renderer.MinimumPointLightEnergy, source.Intensity * configuration.Renderer.PointLightEnergyScale) ||
                        light.OmniRange != source.RadiusGameUnits * units || light.OmniAttenuation != RetailLighting.GodotOmniDecayForRetailRemap ||
                        light.ShadowEnabled != configuration.Renderer.AuthoredPointLightShadows ||
                        light.GetMeta("opennv_ligh_reference").AsString() != key.ToString() ||
                        light.GetMeta("opennv_ligh_radius_game_units").AsSingle() != source.RadiusGameUnits)
                        throw new InvalidDataException($"Native source light {key} lost its placement or source parameters.");
                }
            }
            var publication = Publish(world, initial); root = publication.Root; var projection = publication.Projection;
            Verify(world, projection);
            void QueueRoot(FalloutReferenceWorld authority, RuntimeNativeReferencePresentation native, FalloutFormKey key, bool enabled)
            {
                var before = references.ToDictionary(reference => reference.FormKey, reference => Expected(reference.FormKey));
                var changed = authority.SetEnabled(key, enabled);
                if (rootStates[key] != enabled && !changed) throw new InvalidDataException("An independent source root refused its queued change.");
                if (references.Any(reference => authority.IsEnabled(reference.FormKey) != before[reference.FormKey]))
                    throw new InvalidDataException("The queued parent command bypassed the shared update.");
                native.Advance(0); rootStates[key] = enabled; toggleUpdates++; Verify(authority, native);
            }
            foreach (var key in roots)
                foreach (var enabled in new[] { false, true, false, true })
                    QueueRoot(world, projection, key, enabled);
            if (exhaustive)
            {
                foreach (var key in roots) { QueueRoot(world, projection, key, false); enumerationSetupUpdates++; }
                var states = 1 << roots.Length; var previousGray = 0;
                Verify(world, projection); enumeratedStates = 1;
                for (var ordinal = 1; ordinal < states; ordinal++)
                {
                    var gray = ordinal ^ (ordinal >> 1); var changedBits = (uint)(previousGray ^ gray);
                    if (BitOperations.PopCount(changedBits) != 1) throw new InvalidDataException("Gray transition changed multiple root bits.");
                    var bit = BitOperations.TrailingZeroCount(changedBits);
                    QueueRoot(world, projection, roots[bit], (gray & (1 << bit)) != 0);
                    previousGray = gray; grayTransitions++; enumeratedStates++;
                }
                if (enumeratedStates != states || grayTransitions != states - 1)
                    throw new InvalidDataException("The complete source root domain was not enumerated.");
            }
            if (constructions.Values.Any(count => count != 1)) throw new InvalidDataException("Parent changes rebuilt a source light.");
            var nodes = projection.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
            projection.SetResidency([], _ => throw new InvalidDataException("Warm retirement constructed a source light."), _ => true);
            if (projection.Nodes.Count != 0 || projection.WarmNodeCount != nodes.Count ||
                nodes.Values.Any(node => node.Visible || node.ProcessMode != ProcessModeEnum.Disabled))
                throw new InvalidDataException("Warm nonresident lights remained active or were discarded.");
            projection.SetResidency(references, _ => throw new InvalidDataException("Warm reentry rebuilt a source light."));
            Verify(world, projection);
            if (projection.WarmNodeCount != 0 || nodes.Any(pair => projection.Nodes[pair.Key] != pair.Value))
                throw new InvalidDataException("Warm reentry lost source light instance identity.");
            var settled = references.ToDictionary(reference => reference.FormKey, reference => Expected(reference.FormKey));
            var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(world.Capture());
            root.Free(); root = null;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(snapshotBytes)!);
            foreach (var cell in cells)
                cold.LoadCell(cell with { References = references.Where(reference => reference.Cell == cell.Cell.FormKey).ToArray() });
            if (references.Any(reference => cold.IsEnabled(reference.FormKey) != settled[reference.FormKey]))
                throw new InvalidDataException("Cold light publication changed source-derived enable state.");
            var restored = Publish(cold, settled); root = restored.Root; Verify(cold, restored.Projection);
            foreach (var key in roots)
                foreach (var enabled in new[] { false, true })
                { QueueRoot(cold, restored.Projection, key, enabled); coldRootUpdates++; }
            if (constructions.Values.Any(count => count != 2))
                throw new InvalidDataException("Cold parent changes rebuilt or failed to materialize a source light.");
            var sourceRows = cohort.Select(item => new
            {
                reference = item.Reference.FormKey.ToString(),
                basis = item.Base.FormKey.ToString(),
                cell = item.Reference.Cell.ToString(),
                referenceSha256 = Convert.ToHexString(SHA256.HashData(records.GetEffective(item.Reference.FormKey).ReadData())),
                baseSha256 = Convert.ToHexString(SHA256.HashData(records.GetEffective(item.Base.FormKey).ReadData())),
                parent = item.Reference.EnableParent?.ToString(),
                item.Reference.EnableParentOpposite,
                independentRoot = domains[item.Reference.FormKey].Root.ToString(),
                oppositeParity = domains[item.Reference.FormKey].Opposite,
                engineConstant = domains[item.Reference.FormKey].EnginePlayer,
                sourceChain = domains[item.Reference.FormKey].Chain,
                sourceEnabled = initial[item.Reference.FormKey],
                coldSnapshotEnabled = settled[item.Reference.FormKey],
                coldEnabled = cold.IsEnabled(item.Reference.FormKey),
                item.Source.RadiusGameUnits,
                item.Source.Intensity,
                item.Source.ShaderColorRgb,
                everMaterialized = nodes.ContainsKey(item.Reference.FormKey),
                coldEverMaterialized = restored.Projection.Nodes.ContainsKey(item.Reference.FormKey),
                sourcePlacement = item.Reference.Position,
            }).ToArray();
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                schema = "opennv-native-owned-light-parents/v1",
                runtimeBuild = typeof(FalloutPlacedLight).Module.ModuleVersionId,
                content.SaveCompatibilityId,
                cells = cells.Select(cell => cell.Cell.FormKey.ToString()),
                denominator,
                unparented,
                unbound,
                parentedStaticPoints = cohort.Count,
                initialEnabled = initial.Count(pair => pair.Value),
                initialDisabled = initial.Count(pair => !pair.Value),
                independentRoots = roots.Length,
                toggleUpdates,
                visibilityChecks,
                lateMaterializations = nodes.Keys.Count(key => !initial[key]),
                enableDomain = new
                {
                    stateCount = exhaustive ? (1 << roots.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : $"2^{roots.Length}",
                    rootBits = roots.Select((key, bit) => new { bit, reference = key.ToString() }),
                    eachRootDomain = new[] { false, true },
                    formula = "light = rootBit XOR sourceOppositeParity; engine player root is constant true",
                    exhaustive,
                    enumeratedStates,
                    grayTransitions,
                    enumerationSetupUpdates,
                    maximumEnumeratedRootCount,
                    factorizedRootStatesVerified = roots.Length * 2,
                    originalSourceOracle = true,
                    coldRootUpdates,
                    scope = "Settled source XESP enable assignments only; script/event order, clocks, other gameplay state and final pixels are unverified.",
                },
                repeatedEnable = true,
                warmInstanceReuse = true,
                coldValidation = true,
                sources = sourceRows,
                sourceReadOnly = true,
                gameplay = false,
                finalPixels = "unverified",
                matchedRetailParity = "unverified",
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            GD.Print($"OPENNV_OWNED_LIGHT_PARENT_NATIVE_PASS denominator={denominator} parented={cohort.Count} initialEnabled={initial.Count(pair => pair.Value)} " +
                $"initialDisabled={initial.Count(pair => !pair.Value)} roots={roots.Length} updates={toggleUpdates} late={nodes.Keys.Count(key => !initial[key])} " +
                $"unbound={unbound.Count} states={enumeratedStates} grayTransitions={grayTransitions} exhaustive={exhaustive} " +
                "sourceOracle=true repeated=true warmReuse=true cold=true pixels=unverified");
        }
        finally { root?.Free(); }
    }
}
