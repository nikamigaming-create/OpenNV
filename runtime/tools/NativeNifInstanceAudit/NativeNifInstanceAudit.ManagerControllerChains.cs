using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseManagerControllerChains(string? game = null, string[]? models = null)
    {
        await SettleCollisionLifetimeResources();
        var orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        var bytes = ManagerControllerChainFixture("");
        var sourceHash = SHA256.HashData(bytes);
        ExerciseManagerControllerInstances(FalloutNifFile.Read(bytes), "synthetic", [6, 13, 7]);
        if (!SHA256.HashData(bytes).SequenceEqual(sourceHash)) throw new InvalidDataException("Manager construction changed source bytes.");
        foreach (var mode in new[] { "cycle", "foreign-target", "ambiguous", "unowned", "companion-flags", "priority" })
        {
            var negative = ManagerControllerChainFixture(mode);
            var hash = SHA256.HashData(negative);
            RuntimeNativeNifScene? admitted = null;
            try
            {
                try { admitted = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(negative), .02f); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                {
                    var expected = mode switch
                    {
                        "cycle" => "controller chain repeats block",
                        "foreign-target" => "foreign or unknown target declaration",
                        "ambiguous" => "no unique transform companion",
                        "unowned" => "has no native node channel owner",
                        "companion-flags" => "unsupported target chain",
                        _ => "unsupported sequence chain"
                    };
                    if (!error.Message.Contains(expected, StringComparison.Ordinal))
                        throw new InvalidDataException("Manager chain negative reached an unrelated refusal: " + mode, error);
                }
                if (admitted is not null) throw new InvalidDataException("Manager chain admitted " + mode);
            }
            finally { admitted?.Root.Free(); }
            if (!SHA256.HashData(negative).SequenceEqual(hash)) throw new InvalidDataException("Manager refusal changed source bytes.");
        }
        await SettleCollisionLifetimeResources();
        if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != orphans ||
            Performance.GetMonitor(Performance.Monitor.ObjectResourceCount) != resources)
            throw new InvalidDataException("Manager chain fixture retained native nodes/resources.");
        GD.Print("OPENNV_MANAGER_CONTROLLER_CHAIN_PASS typedInterveningController=true materialAlpha=true cold=true clones=true cycleForeignAmbiguousUnownedRefused=true sourceFlagsAndPriorityPreserved=true pixels=unverified");
        if (game is null) return;
        if (models is not { Length: > 0 }) throw new ArgumentException("Owned manager audit requires model paths.");
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        var content = RuntimeLiveContentSource.Current!;
        foreach (var model in models)
        {
            if (!content.TryRead(model, null, out var owned, out var identity)) throw new FileNotFoundException(model);
            var hash = SHA256.HashData(owned);
            ExerciseManagerControllerInstances(FalloutNifFile.Read(owned), identity, null);
            if (!content.TryRead(model, null, out var after, out var afterIdentity) || identity != afterIdentity ||
                !SHA256.HashData(after).SequenceEqual(hash)) throw new InvalidDataException("Owned manager source changed.");
        }
    }

    private void ExerciseManagerControllerInstances(FalloutNifFile source, string identity, int[]? expectedChain)
    {
        var prototype = new RuntimeNativeNifPrototype(source, .02f);
        Node3D? first = null, second = null;
        try
        {
            first = prototype.Instantiate(); second = prototype.Instantiate();
            first.ProcessMode = second.ProcessMode = ProcessModeEnum.Disabled;
            AddChild(first); AddChild(second);
            var player = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var cold = second.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var original = prototype.Scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            if (expectedChain is not null && !first.GetChild<Node3D>(0).GetMeta("opennv_nif_node_controller_chain")
                .AsInt32Array().SequenceEqual(expectedChain)) throw new InvalidDataException("Manager reordered or omitted a typed source controller.");
            var manager = (FalloutNifControllerManager)source.ReadObject(player.SourceController);
            var samples = 0;
            foreach (var sequenceIndex in manager.Sequences)
            {
                var sequence = source.ReadControllerSequence(sequenceIndex);
                var untouched = JsonSerializer.Serialize(cold.CaptureObjectState());
                var originalClock = JsonSerializer.Serialize(original.CaptureObjectState());
                var untouchedMaterials = ManagerMaterialState(second);
                var originalMaterials = ManagerMaterialState(prototype.Scene.Root);
                player.RequestSourceSequence(sequence.Name, 1);
                foreach (var fraction in new[] { .271, .713 })
                {
                    var time = sequence.StartTime + (sequence.StopTime - sequence.StartTime) * fraction;
                    player.SeekSourceTime(time);
                    samples += RequireManagerMaterialSamples(first, source, sequence, (float)time);
                    if (JsonSerializer.Serialize(cold.CaptureObjectState()) != untouched ||
                        JsonSerializer.Serialize(original.CaptureObjectState()) != originalClock ||
                        ManagerMaterialState(second) != untouchedMaterials || ManagerMaterialState(prototype.Scene.Root) != originalMaterials)
                        throw new InvalidDataException("Manager instances shared a source clock or animated material.");
                }
                var saved = JsonSerializer.Deserialize<FalloutObjectAnimationSnapshot>(JsonSerializer.Serialize(player.CaptureScriptState()))!;
                cold.RestoreScriptState(saved);
                if (JsonSerializer.Serialize(cold.CaptureScriptState()) != JsonSerializer.Serialize(saved))
                    throw new InvalidDataException("Cold manager restore changed its consumed clock/group.");
                var textKeys = player.TextKeyCount; var coldKeys = cold.TextKeyCount;
                player._Process(0); cold._Process(0);
                if (player.TextKeyCount != textKeys || cold.TextKeyCount != coldKeys)
                    throw new InvalidDataException("Cold or zero-time manager update replayed a source text key.");
                var step = (sequence.StopTime - sequence.StartTime) * .05;
                player._Process(step); cold._Process(step);
                if (player.SourceTimeSeconds != cold.SourceTimeSeconds || player.ActiveSourceSequence != cold.ActiveSourceSequence)
                    throw new InvalidDataException("Cold manager suffix lost source timing or identity.");
                samples += RequireManagerMaterialSamples(first, source, sequence, (float)player.SourceTimeSeconds);
                samples += RequireManagerMaterialSamples(second, source, sequence, (float)cold.SourceTimeSeconds);
            }
            if (samples == 0) throw new InvalidDataException("Manager audit exercised no real material channels.");
            var groups = string.Join(',', player.SequenceNames);
            var retainedSiblingClock = JsonSerializer.Serialize(cold.CaptureObjectState());
            var retainedSiblingMaterials = ManagerMaterialState(second);
            first.Free(); first = null;
            if (!GodotObject.IsInstanceValid(cold) || JsonSerializer.Serialize(cold.CaptureObjectState()) != retainedSiblingClock ||
                ManagerMaterialState(second) != retainedSiblingMaterials)
                throw new InvalidDataException("Retiring one manager changed its sibling's clock or material.");
            GD.Print($"OPENNV_MANAGER_CONTROLLER_INSTANCE_PASS source={identity} sha256={source.Sha256} groups={groups} materialSamples={samples} coldClock=true independentInstances=true visibilityAnimation=unverified ordinaryActivation=unverified pixels=unverified");
        }
        finally { first?.Free(); second?.Free(); prototype.Scene.Root.Free(); }
    }

    private static string ManagerMaterialState(Node3D root) => JsonSerializer.Serialize(
        root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(index => mesh.Mesh.SurfaceGetMaterial(index)))
            .OfType<ShaderMaterial>().Where(material => material.ResourceName is NativeNifEffectMaterial.ResourceIdentity or NativeNifLightingMaterial.ResourceIdentity)
            .Select(material => new
            {
                material.ResourceName,
                Color = material.GetShaderParameter(material.ResourceName == NativeNifEffectMaterial.ResourceIdentity ? "source_color_multiplier" : "base_factor").AsVector4().ToString(),
                Offset = material.GetShaderParameter("source_uv_offset").AsVector2().ToString()
            }).ToArray());

    private static int RequireManagerMaterialSamples(Node3D root, FalloutNifFile source, FalloutNifControllerSequence sequence, float time)
    {
        var meshes = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        var count = 0;
        foreach (var link in sequence.ControlledBlocks.Where(value => value.ControllerType is "NiAlphaController" or "NiTextureTransformController"))
        {
            var geometry = source.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
                .Select(block => source.ReadGeometry(block.Index)).Single(value => value.Name == link.NodeName);
            var mesh = meshes.Single(value => value.GetMeta("opennv_nif_geometry_block").AsInt32() == geometry.Block.Index);
            var sampler = new FalloutNifFloatAnimation(source, link.Interpolator);
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var material = mesh.Mesh.SurfaceGetMaterial(surface) as ShaderMaterial ?? throw new InvalidDataException("Manager lost its actual shader.");
                float actual;
                if (link.ControllerType == "NiAlphaController")
                    actual = material.GetShaderParameter(material.ResourceName == NativeNifEffectMaterial.ResourceIdentity
                        ? "source_color_multiplier" : "base_factor").AsVector4().W;
                else
                {
                    var controller = (FalloutNifTextureTransformController)source.ReadObject(link.Controller);
                    var parameter = controller.Operation == 2 ? "source_uv_rotation" : controller.Operation < 2 ? "source_uv_offset" : "source_uv_scale";
                    actual = controller.Operation == 2 ? material.GetShaderParameter(parameter).AsSingle() : controller.Operation is 0 or 3
                        ? material.GetShaderParameter(parameter).AsVector2().X : material.GetShaderParameter(parameter).AsVector2().Y;
                }
                var expected = sampler.Sample(time);
                if (!float.IsFinite(actual) || !float.IsFinite(expected) || Math.Abs(actual - expected) > .000001f)
                    throw new InvalidDataException($"Actual manager material diverged from source {sequence.Name}/{link.ControllerType} at {time:R}.");
                count++;
            }
        }
        return count;
    }

    private static byte[] ManagerControllerChainFixture(string mode)
    {
        var templateBytes = EmptyManagedSequenceFixture();
        var template = FalloutNifFile.Read(templateBytes);
        var blocks = template.Blocks.Select(block => (Type: block.TypeName, Data: templateBytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var names = template.Strings.Concat(new[] { "NiMaterialProperty", "NiAlphaController" }).ToArray();
        void Time(BinaryWriter w, int next, ushort flags, int target, float start = float.MaxValue, float stop = float.MinValue)
        { w.Write(next); w.Write(flags); w.Write(1f); w.Write(0f); w.Write(start); w.Write(stop); w.Write(target); }
        BinaryPrimitives.WriteInt32LittleEndian(blocks[6].Data, 13);
        if (mode == "companion-flags") BinaryPrimitives.WriteUInt16LittleEndian(blocks[7].Data.AsSpan(4), 0x004c);
        var mesh = blocks[1].Data;
        blocks[1] = (blocks[1].Type, mesh[..68].Concat(BitConverter.GetBytes(3)).Concat(mesh[72..80])
            .Concat(BitConverter.GetBytes(14)).Concat(mesh[80..]).ToArray());
        byte[] Sequence(int name, uint cycle, float start, float stop, int keys) => Bytes(w =>
        {
            w.Write(name); w.Write(1); w.Write(0); w.Write(17); w.Write(15); w.Write((byte)(mode == "priority" ? 1 : 0));
            w.Write(0); w.Write(6); w.Write(7); w.Write(-1); w.Write(-1);
            w.Write(1f); w.Write(keys); w.Write(cycle); w.Write(1f); w.Write(start); w.Write(stop);
            w.Write(6); w.Write(0); w.Write((ushort)0);
        });
        blocks[8] = (blocks[8].Type, Sequence(1, 0, 2, 4, 10));
        blocks[9] = (blocks[9].Type, Sequence(2, 2, 10, 11, 11));
        blocks.Add((mode == "unowned" ? "NiFloatExtraDataController" : "NiVisController", Bytes(w =>
        {
            Time(w, mode == "cycle" ? 6 : mode == "ambiguous" ? 19 : 7, 0x006c, mode == "foreign-target" ? 1 : 0, 33.5f, 37.333336f);
            w.Write(-1); if (mode == "unowned") w.Write(0);
        })));
        blocks.Add(("NiMaterialProperty", Bytes(w =>
        { w.Write(0); w.Write(0); w.Write(15); foreach (var value in new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f }) w.Write(value); })));
        blocks.Add(("NiAlphaController", Bytes(w => { Time(w, -1, 0x006c, 14, 2, 11); w.Write(16); })));
        blocks.Add(("NiBlendFloatInterpolator", Bytes(w => { w.Write((byte)1); w.Write((byte)0); w.Write(0f); w.Write(0f); })));
        blocks.Add(("NiFloatInterpolator", Bytes(w => { w.Write(float.MinValue); w.Write(18); })));
        blocks.Add(("NiFloatData", Bytes(w => { w.Write(2); w.Write(1U); w.Write(2f); w.Write(.2f); w.Write(11f); w.Write(.9f); })));
        if (mode == "ambiguous") blocks.Add(("NiMultiTargetTransformController", Bytes(w => { Time(w, 7, 0x006c, 0); w.Write((ushort)0); })));
        return Bytes(w =>
        {
            w.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
            w.Write(blocks.Count); w.Write(template.UserVersion2); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); w.Write((ushort)blocks.Count);
            foreach (var block in blocks) { w.Write(block.Type.Length); w.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Count; index++) w.Write((ushort)index);
            foreach (var block in blocks) w.Write(block.Data.Length);
            w.Write(names.Length); w.Write(names.Max(name => name.Length));
            foreach (var name in names) { w.Write(name.Length); w.Write(Encoding.ASCII.GetBytes(name)); }
            w.Write(0); foreach (var block in blocks) w.Write(block.Data); w.Write(1); w.Write(0);
        });
    }
}
