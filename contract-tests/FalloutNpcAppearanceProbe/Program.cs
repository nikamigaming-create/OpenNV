using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.FaceGen;

Synthetic.Run();
FaceGenSynthetic.Run();

if (args is ["--face-controls", var controlRoot, var controlActor])
{
    RuntimeLiveContentSource.Configure(controlRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var actor = FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(Convert.ToUInt32(controlActor, 16)));
    if (!content.TryRead("facegen/si.ctl", null, out var bytes, out _)) throw new FileNotFoundException("facegen/si.ctl");
    var controls = FalloutCtlFile.Read(bytes);
    foreach (var (group, npc, race) in new[] { (0, actor.FaceGen.SymmetricGeometry, actor.RaceFaceGen.SymmetricGeometry),
        (2, actor.FaceGen.SymmetricTexture, actor.RaceFaceGen.SymmetricTexture) })
    {
        var coordinates = FalloutFaceGenCoefficients.AddSourceGeometry(npc, race, controls.BasisCounts[group]);
        foreach (var (control, index) in controls.Controls[group].Select((control, index) => (control, index)))
        {
            var dot = coordinates.Zip(control.Axis).Sum(pair => (double)pair.First * pair.Second);
            var norm = control.Axis.Sum(value => (double)value * value);
            Console.WriteLine($"CTL_PROJECTION group={group} index={index} label={control.Label} dot={dot:R} normalized={dot / norm:R}");
        }
    }
    return;
}

if (args is ["--actor", var actorRoot, var actorHex])
{
    RuntimeLiveContentSource.Configure(actorRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var appearance = FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(Convert.ToUInt32(actorHex, 16)));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { appearance.Npc, appearance.Race,
        appearance.Hair, appearance.Eyes, hclr = Convert.ToHexString(appearance.HairColorBytes),
        faceHashes = new[] { appearance.FaceGen.SymmetricGeometry, appearance.FaceGen.AsymmetricGeometry,
            appearance.FaceGen.SymmetricTexture }.Select(bytes => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))).ToArray(),
        appearance.Models, appearance.EquippedArmor }));
    return;
}

if (args is ["--settings", var settingsRoot, var prefix])
{
    RuntimeLiveContentSource.Configure(settingsRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    foreach (var record in records.EffectiveRecords("GMST"))
    {
        var rows = record.ReadSubrecords();
        var id = Encoding.UTF8.GetString(rows.Single(row => row.Signature == "EDID").Data.Span).TrimEnd('\0');
        if (!id.Contains(prefix, StringComparison.OrdinalIgnoreCase)) continue;
        var data = rows.Single(row => row.Signature == "DATA").Data.Span;
        var value = id[0] switch { 'f' => BinaryPrimitives.ReadSingleLittleEndian(data).ToString("R"),
            'i' => BinaryPrimitives.ReadInt32LittleEndian(data).ToString(),
            _ => Encoding.UTF8.GetString(data).TrimEnd('\0') };
        Console.WriteLine($"{id}={value}");
    }
    return;
}

if (args is ["--texture-contract", var textureRoot, var npcEditorId, var outputDirectory])
{
    var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    if (!Path.IsPathFullyQualified(outputDirectory) || Path.GetFullPath(outputDirectory).StartsWith(repository, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Owned texture evidence must be outside the repository.");
    RuntimeLiveContentSource.Configure(textureRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var npc = records.EffectiveRecords("NPC_").Single(record => record.ReadSubrecords()
        .Any(field => field.Signature == "EDID" && Encoding.UTF8.GetString(field.Data.Span).TrimEnd('\0') == npcEditorId));
    var appearance = FalloutNpcAppearanceResolver.Resolve(records, npc.FormKey);
    var head = appearance.Models.Single(part => part.Role == "head");
    var path = Path.ChangeExtension(head.ModelPath!, ".egt");
    if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
    var egt = FalloutEgtFile.Read(bytes);
    var inputs = FalloutNpcFaceMaterial.Resolve(content, appearance, head, head.TexturePath!, "unused", null, records);
    Directory.CreateDirectory(outputDirectory);
    if (inputs.BaseMod.LogicalPath is { } baseMod && content.TryRead(baseMod, null, out var dds, out _))
        File.WriteAllBytes(Path.Combine(outputDirectory, "preprocessed.dds"), dds);
    foreach (var (label, coefficients) in new[] {
        ("npc", Floats(appearance.FaceGen.SymmetricTexture)),
        ("race", Floats(appearance.RaceFaceGen.SymmetricTexture)),
        ("combined", FalloutFaceGenCoefficients.AddSourceGeometry(appearance.FaceGen.SymmetricTexture, appearance.RaceFaceGen.SymmetricTexture, egt.SymmetricModes.Count)) })
    {
        var delta = egt.EvaluateDelta(coefficients, []);
        using var writer = new BinaryWriter(File.Create(Path.Combine(outputDirectory, label + ".float32")));
        foreach (var value in delta.Rgb) writer.Write(value);
        Console.WriteLine($"EGT_CANDIDATE source={path} input={label} width={egt.Width} height={egt.Height} minimum={delta.Rgb.Min()} maximum={delta.Rgb.Max()}");
    }
    return;
}

if (args is [var mode, var root, var cellHex, ..] && mode is "--inspect" or "--resolve" or "--facegen")
{
    if (mode != "--inspect") RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var liveSource = mode != "--inspect" ? RuntimeLiveContentSource.Current : null;
    using var stack = liveSource is null ? FalloutPluginStack.Load(root, args.Length > 3 ? args[3..] : ["FalloutNV.esm"]) : FalloutPluginStack.Load(liveSource.PluginSources);
    var cell = new FalloutFormKey("FalloutNV.esm", Convert.ToUInt32(cellHex, 16));
    var keys = new Queue<FalloutFormKey>();
    foreach (var reference in stack.EffectiveCellChildren(cell, new HashSet<string> { "ACHR" }))
    {
        if ((reference.Flags & 0x800) != 0) continue;
        var baseKey = reference.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(reference.ReadSubrecords().Single(row => row.Signature == "NAME").Data.Span));
        if (mode != "--inspect")
        {
            var appearance = FalloutNpcAppearanceResolver.Resolve(stack, baseKey, reference.FormKey);
            Console.WriteLine($"OPENNV_NPC_APPEARANCE npc={appearance.Npc} reference={appearance.Reference} race={appearance.Race} female={appearance.Female} skeleton={appearance.SkeletonPath} canConstruct={appearance.CanConstruct} blockers={string.Join(';', appearance.Blockers)}");
            foreach (var part in appearance.Models)
                Console.WriteLine($"  role={part.Role} source={part.Source} slots=0x{part.BipedSlots:x8} model={part.ModelPath} texture={part.TexturePath}");
            foreach (var part in appearance.Models.Where(part => part.Role is "hair" or "head-addon"))
            {
                var rgb = FalloutNpcAppearanceHairColor.Resolve(stack, appearance, part);
                Console.WriteLine($"OPENNV_OWNED_HAIR_COLOUR source={part.Source} role={part.Role} rgb={rgb} hclr={Convert.ToHexString(appearance.HairColorBytes)} shaderFlagSelectsApplication=true lightingParity=unverified");
            }
            Console.WriteLine($"  npcFaceGenBytes={appearance.FaceGen.SymmetricGeometry.Length}/{appearance.FaceGen.AsymmetricGeometry.Length}/{appearance.FaceGen.SymmetricTexture.Length} raceParts={appearance.RaceParts.Count} inventory={appearance.Inventory.Count} equippedArmor={string.Join(',', appearance.EquippedArmor)}");
            var paths = new[] { appearance.SkeletonPath }.Concat(appearance.Models.Concat(appearance.RaceParts)
                .SelectMany(part => new[] { part.ModelPath, part.TexturePath }.Concat(part.AlternateTextures.SelectMany(texture => texture.Textures.Values))))
                .Where(path => path is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var path in paths)
                if (!liveSource!.TryRead(path, null, out var bytes, out _) || bytes.Length == 0)
                    throw new FileNotFoundException("Owned NPC appearance resource is absent or empty: " + path);
            Console.WriteLine($"OPENNV_OWNED_NPC_RESOURCES_OK plugins={stack.Plugins.Count} resources={paths.Length} inMemory=true renderParity=unmeasured");
            if (mode == "--facegen")
            {
                var head = appearance.Models.Single(part => part.Role == "head");
                var material = FalloutNpcFaceMaterial.Resolve(liveSource!, appearance, head,
                    head.TexturePath!, "unused-source-normal", null, stack);
                if (!material.CanRender) throw new InvalidDataException(string.Join(';', material.Blockers));
                var morphSources = appearance.Models.Select(part => FalloutNpcAppearanceMorph.Resolve(liveSource!, part,
                    FalloutNpcAppearanceHairShape.Select(appearance, part)))
                    .Where(morph => morph is not null).ToArray();
                Console.WriteLine($"OPENNV_OWNED_FACEGEN_INPUTS_OK base={material.BaseTexturePath} normal={material.NormalTexturePath} scattering={material.ScatteringTexturePath} baseMod={material.BaseMod.LogicalPath} baseModDimensions={material.BaseMod.Width}x{material.BaseMod.Height} detailMod={material.DetailMod.SourceName} detailDimensions={material.DetailMod.Width}x{material.DetailMod.Height} detailRgba={Convert.ToHexString(material.DetailMod.Rgba8.AsSpan(0, 4))} morphCompanions={morphSources.Length} pixelParity=unmeasured");
                foreach (var path in appearance.Models.Select(part => part.ModelPath).Where(path => path is not null).Cast<string>())
                {
                    var egmPath = Path.ChangeExtension(path, ".egm");
                    if (liveSource!.TryRead(egmPath, null, out var egmBytes, out _))
                    {
                        var egm = FalloutEgmFile.Read(egmBytes);
                        var delta = egm.EvaluateDeltas(Floats(appearance.FaceGen.SymmetricGeometry), Floats(appearance.FaceGen.AsymmetricGeometry));
                        Console.WriteLine($"EGM {egmPath} vertices={egm.VertexCount} symmetric={egm.SymmetricModes.Count} asymmetric={egm.AsymmetricModes.Count} basis={egm.BasisVersion} deltaMaximum={delta.Max(value => value.Length())} input=npc-only-test-vector composition=unresolved");
                    }
                    var egtPath = Path.ChangeExtension(path, ".egt");
                    if (liveSource!.TryRead(egtPath, null, out var egtBytes, out _))
                    {
                        var egt = FalloutEgtFile.Read(egtBytes);
                        var delta = egt.EvaluateDelta(Floats(appearance.FaceGen.SymmetricTexture), []);
                        Console.WriteLine($"EGT {egtPath} width={egt.Width} height={egt.Height} symmetric={egt.SymmetricModes.Count} asymmetric={egt.AsymmetricModes.Count} basis={egt.BasisVersion} deltaMinimum={delta.Rgb.Min()} deltaMaximum={delta.Rgb.Max()} scale0={egt.SymmetricModes[0].Scale} input=npc-only-test-vector composition=unresolved");
                    }
                }
                foreach (var path in appearance.RaceParts.Select(part => part.ModelPath).Where(path => path?.EndsWith(".egt", StringComparison.OrdinalIgnoreCase) == true).Cast<string>())
                {
                    if (!liveSource!.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
                    var egt = FalloutEgtFile.Read(bytes);
                    var delta = egt.EvaluateDelta(Floats(appearance.FaceGen.SymmetricTexture), []);
                    var uniform = Enumerable.Range(0, egt.Width * egt.Height).All(index => delta.Rgb.AsSpan(index * 3, 3).SequenceEqual(delta.Rgb.AsSpan(0, 3)));
                    var modeUniform = egt.SymmetricModes.All(mode => Enumerable.Range(0, 3).All(channel => mode.PlanarSignedRgb.Span.Slice(channel * egt.Width * egt.Height, egt.Width * egt.Height).IndexOfAnyExcept(mode.PlanarSignedRgb.Span[channel * egt.Width * egt.Height]) < 0));
                    Console.WriteLine($"EGT {path} width={egt.Width} height={egt.Height} symmetric={egt.SymmetricModes.Count} asymmetric={egt.AsymmetricModes.Count} basis={egt.BasisVersion} scale0={egt.SymmetricModes[0].Scale} allModePlanesUniform={modeUniform} evaluatedPixelsUniform={uniform} firstDeltaRgb={string.Join(',', delta.Rgb.Take(3))} input=npc-only-test-vector composition=unresolved");
                }
            }
        }
        else { Dump(reference); keys.Enqueue(baseKey); }
    }
    var seen = new HashSet<FalloutFormKey>();
    while (keys.TryDequeue(out var key))
    {
        if (!seen.Add(key)) continue;
        var record = stack.GetEffective(key);
        Dump(record);
        foreach (var field in record.ReadSubrecords())
        {
            if ((record.Signature == "NPC_" && field.Signature is "RNAM" or "HNAM" or "ENAM" or "TPLT" or "PNAM" or "CNTO") ||
                (record.Signature == "HDPT" && field.Signature == "HNAM") ||
                (record.Signature is "ARMO" or "ARMA" && field.Signature == "BIPL") ||
                (record.Signature == "FLST" && field.Signature == "LNAM"))
            {
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                if (raw != 0) keys.Enqueue(record.Plugin.AdjustFormId(raw));
            }
        }
    }
}

static float[] Floats(byte[] bytes) => Enumerable.Range(0, bytes.Length / 4)
    .Select(index => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * 4))).ToArray();

static void Dump(FalloutPluginRecord record)
{
    Console.WriteLine($"{record.Signature} {record.FormKey} winning={record.Plugin.Name}");
    foreach (var field in record.ReadSubrecords())
    {
        var text = field.Signature is "EDID" or "MODL" or "MOD2" or "MOD3" or "MOD4" or "ICON" or "ICO2" ?
            Encoding.UTF8.GetString(field.Data.Span).TrimEnd('\0') : Convert.ToHexString(field.Data.Span);
        Console.WriteLine($"  {field.Signature} ({field.Data.Length}) {text}");
    }
}
