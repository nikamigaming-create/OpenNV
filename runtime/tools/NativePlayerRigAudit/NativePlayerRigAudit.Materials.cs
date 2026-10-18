using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;

public partial class NativePlayerRigAudit
{
    private async Task AuditMaterials(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Expected owned Data, native save, weapon editor ID and fresh private material report.");
        if (File.Exists(args[3])) throw new IOException("Use a fresh material report path.");
        File.WriteAllText(args[3], "{\"schema\":\"opennv-player-material-audit/1\",\"status\":\"running\"}");
        RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(args[1]))!;
        var player = records.RuntimeFormKey(7);
        var appearance = FalloutNpcAppearanceResolver.Resolve(records, player,
            equippedArmor: saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey).Where(key => records.GetEffective(key).Signature == "ARMO").ToArray(),
            appearanceState: FalloutNativeCharacterCreation.ActorState(records, player, saved.Character));
        var weapon = args[2] == "equipped" ? saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey)
            .Single(key => records.GetEffective(key).Signature == "WEAP") : records.EffectiveRecords("WEAP").Single(record => !record.IsDeleted && record.ReadSubrecords()
            .Any(field => field.Signature == "EDID" && Encoding.UTF8.GetString(field.Data.Span).TrimEnd('\0')
                .Equals(args[2], StringComparison.OrdinalIgnoreCase))).FormKey;
        var reads = new Dictionary<string, (int Count, int Bytes)>(StringComparer.Ordinal);
        var loads = new List<object>();
        var uploads = new List<object>();
        content.ResourceReadObserver = (logical, identity, bytes) =>
        {
            if (!logical.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return;
            var prior = reads.GetValueOrDefault(identity);
            reads[identity] = (prior.Count + 1, bytes.Length);
        };
        NativeDdsTexture.LoadObserver = (source, normal, bytes, partial, milliseconds) =>
            loads.Add(new { source, normal, bytes, partial, milliseconds });
        NativeDdsTexture.UploadObserver = (format, width, height, prepare, upload) =>
            uploads.Add(new { format = format.ToString(), width, height, prepare, upload });
        RuntimeNativePlayerActor? first = null, body = null;
        try
        {
            var started = Stopwatch.GetTimestamp();
            first = new(records, content, appearance, weapon, true, .0142875f, Colors.White);
            body = new(records, content, appearance, weapon, false, .0142875f, Colors.White);
            var construction = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (first.Error is not null || body.Error is not null) throw new InvalidDataException(first.Error ?? body.Error);
            content.ResourceReadObserver = null; NativeDdsTexture.LoadObserver = null; NativeDdsTexture.UploadObserver = null;
            AddChild(first); AddChild(body);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var bindings = new List<object>();
            Observe(first, true); Observe(body, false);
            if (bindings.Count == 0 || loads.Count == 0 || uploads.Count == 0) throw new InvalidDataException("Material audit omitted production surfaces or texture observations.");
            var encoded = JsonSerializer.Serialize(bindings);
            var bindingHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded)));
            var liveCache = NativeDdsTexture.CacheState(content);
            content.Dispose();
            var retiredCache = JsonSerializer.SerializeToElement(NativeDdsTexture.CacheState(content));
            if (!retiredCache.GetProperty("retired").GetBoolean() || retiredCache.GetProperty("entries").GetInt32() != 0 ||
                retiredCache.GetProperty("bytes").GetInt64() != 0) throw new InvalidDataException("Owned image cache did not retire with its source.");
            bindings.Clear(); Observe(first, true); Observe(body, false);
            var retiredBindingHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(bindings))));
            if (retiredBindingHash != bindingHash) throw new InvalidDataException("Source retirement changed a retained material binding.");
            try { NativeDdsTexture.LoadCached(content, [], "retired-source"); throw new InvalidOperationException("Retired source accepted an image."); }
            catch (ObjectDisposedException) { }
            var report = new
            {
                schema = "opennv-player-material-audit/1",
                status = "complete",
                weapon = weapon.ToString(),
                constructionMilliseconds = construction,
                sourceReads = reads.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new { source = pair.Key, count = pair.Value.Count, bytes = pair.Value.Bytes }),
                loads,
                uploads,
                cache = liveCache,
                retiredCache,
                bindings,
                bindingSha256 = bindingHash,
                retiredBindingSha256 = retiredBindingHash,
                recording = false,
                renderedPixelParity = "unverified",
                physicalXrAccepted = false
            };
            File.WriteAllText(args[3], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"OPENNV_PLAYER_MATERIAL_AUDIT_PASS surfaces={bindings.Count} loads={loads.Count} uploads={uploads.Count} recording=false parity=unverified");

            void Observe(RuntimeNativePlayerActor actor, bool firstPerson)
            {
                foreach (var (part, index) in actor.Actor.Parts.Select((part, index) => (part, index)))
                    foreach (var mesh in part.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
                        for (var surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); surface++)
                        {
                            var material = mesh.GetActiveMaterial(surface) ?? throw new InvalidDataException("A presented surface has no material.");
                            if (material is not ShaderMaterial shader) throw new NotSupportedException($"Material signature owner is absent: {material.GetClass()}.");
                            var parameters = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                            foreach (var uniform in shader.Shader.GetShaderUniformList())
                            {
                                var name = uniform.AsGodotDictionary()["name"].AsString();
                                var value = shader.GetShaderParameter(name);
                                if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Texture2D texture)
                                {
                                    using var image = texture.GetImage();
                                    if (image is null || image.IsEmpty()) throw new InvalidDataException("A bound texture has no image signature.");
                                    parameters[name] = new
                                    {
                                        source = texture.GetMeta("opennv_source_texture", "").AsString(),
                                        width = image.GetWidth(),
                                        height = image.GetHeight(),
                                        mips = image.GetMipmapCount(),
                                        format = image.GetFormat().ToString(),
                                        bytesSha256 = Convert.ToHexString(SHA256.HashData(image.GetData()))
                                    };
                                }
                                else if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is not null)
                                    throw new NotSupportedException($"Material uniform object signature is absent: {name}.");
                                else parameters[name] = new { type = value.VariantType.ToString(), value = value.ToString() };
                            }
                            bindings.Add(new
                            {
                                firstPerson,
                                part = index,
                                model = part.Root.GetMeta("opennv_source_model", "").AsString(),
                                geometry = mesh.GetMeta("opennv_nif_geometry_block", -1).AsInt32(),
                                surface,
                                shaderSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shader.Shader.Code))),
                                parameters
                            });
                        }
            }
        }
        finally
        {
            content.ResourceReadObserver = null; NativeDdsTexture.LoadObserver = null; NativeDdsTexture.UploadObserver = null;
            first?.Free(); body?.Free();
        }
    }
}
