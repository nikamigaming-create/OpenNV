using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutOwnedActorResource(
    string Role,
    string LogicalPath,
    string Source,
    string Sha256,
    int Bytes);

internal sealed record FalloutHumanoidAppearance(
    FalloutFormKey Reference,
    FalloutFormKey Base,
    FalloutFormKey TraitsSource,
    FalloutFormKey ModelSource,
    IReadOnlyDictionary<string, FalloutFormKey> CategorySources,
    bool Female,
    FalloutFormKey Race,
    FalloutFormKey Hair,
    FalloutFormKey Eyes,
    IReadOnlyList<FalloutFormKey> HeadParts,
    IReadOnlyList<FalloutOwnedActorResource> Resources,
    int FaceGenCoordinateBytes,
    IReadOnlyList<string> VisualBlockers);

internal static class FalloutHumanoidAppearanceResolver
{
    private const string FalloutNewVegasMasterName = "FalloutNV" + ".esm";
    private const string GoodspringsLocationName = "Good" + "springs";
    internal static readonly FalloutFormKey GoodspringsSunnyReference =
        new(FalloutNewVegasMasterName, 0x104e85);

    private const uint FemaleActorFlag = 0x0000_0001;
    private const ushort TraitsTemplateFlag = 0x0001;
    private const ushort ModelTemplateFlag = 0x0040;
    private const int ActorConfigurationBytes = 24;
    private const int FaceSymmetricGeometryBytes = 50 * sizeof(float);
    private const int FaceAsymmetricGeometryBytes = 30 * sizeof(float);
    private const int FaceSymmetricTextureBytes = 50 * sizeof(float);

    internal static FalloutHumanoidAppearance ResolveGoodspringsSunny(
        FalloutPluginStack stack,
        RuntimeLiveContentSource source)
    {
        if (source.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
            throw new InvalidDataException(
                $"{GoodspringsLocationName} actor appearance requires a New Vegas or TTW stack.");
        if (!stack.TryGetEffective(GoodspringsSunnyReference, out var reference) ||
            reference.Signature != "ACHR")
            throw new InvalidDataException(
                $"The effective {GoodspringsLocationName} Sunny ACHR is absent.");
        var baseKey = RequiredForm(reference, "NAME");
        var actor = RequireRecord(stack, baseKey, "NPC_");
        var categories = new Dictionary<string, FalloutFormKey>(StringComparer.Ordinal)
        {
            ["traits"] = CategorySource(stack, actor, TraitsTemplateFlag, "traits"),
            ["model"] = CategorySource(stack, actor, ModelTemplateFlag, "model"),
        };
        var traits = RequireRecord(stack, categories["traits"], "NPC_");
        var model = RequireRecord(stack, categories["model"], "NPC_");
        var traitsRows = Rows(traits);
        var modelRows = Rows(model);
        var actorConfiguration = Single(traitsRows, "ACBS", required: true);
        if (actorConfiguration.Length != ActorConfigurationBytes)
            throw new InvalidDataException("Sunny traits-source ACBS layout is unsupported.");
        var female = (BinaryPrimitives.ReadUInt32LittleEndian(actorConfiguration.Span) & FemaleActorFlag) != 0;
        var raceKey = RequiredForm(traits, "RNAM");
        var hairKey = RequiredForm(traits, "HNAM");
        var eyesKey = RequiredForm(traits, "ENAM");
        var headParts = FormRows(traits, traitsRows, "PNAM");
        var race = RequireRecord(stack, raceKey, "RACE");
        var hair = RequireAppearancePart(stack, hairKey, "HAIR");
        var eyes = RequireAppearancePart(stack, eyesKey, "EYES");
        var parts = headParts.Select(key => RequireAppearancePart(stack, key, "HDPT")).ToArray();

        var resources = new List<FalloutOwnedActorResource>();
        AddResource(source, resources, "skeleton", ModelPath(model, modelRows, "MODL"));
        AddRaceResources(source, resources, race, female);
        AddPartResources(source, resources, hair, "hair");
        AddPartResources(source, resources, eyes, "eyes");
        for (var index = 0; index < parts.Length; ++index)
            AddPartResources(source, resources, parts[index], $"head-part-{index}");
        if (resources.Select(row => row.LogicalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            resources.Count)
            throw new InvalidDataException("Sunny appearance graph contains duplicate resource roles.");

        var faceBytes = FaceFieldBytes(traitsRows, "FGGS", FaceSymmetricGeometryBytes) +
            FaceFieldBytes(traitsRows, "FGGA", FaceAsymmetricGeometryBytes) +
            FaceFieldBytes(traitsRows, "FGTS", FaceSymmetricTextureBytes);
        var blockers = new List<string> { "multi-bone-skin-assembly-unsupported" };
        if (faceBytes > 0)
            blockers.Add("facegen-coordinate-application-unsupported");
        var owner = baseKey.OwnerPlugin;
        var objectId = baseKey.ObjectId;
        AddOptionalResource(source, resources, "facegen-geometry",
            $"meshes\\characters\\facegendata\\facegeom\\{owner}\\{objectId:x8}.nif");
        AddOptionalResource(source, resources, "facegen-texture",
            $"textures\\characters\\facemods\\{owner}\\{objectId:x8}_0.dds");

        return new FalloutHumanoidAppearance(
            reference.FormKey,
            baseKey,
            categories["traits"],
            categories["model"],
            categories,
            female,
            raceKey,
            hairKey,
            eyesKey,
            headParts,
            resources,
            faceBytes,
            blockers);
    }

    internal static Node3D BuildSourceIdentityNode(FalloutHumanoidAppearance appearance)
    {
        if (appearance.VisualBlockers.Count == 0)
            throw new InvalidDataException("Humanoid source-identity transport requires an explicit visual boundary.");
        var root = new Node3D { Name = $"NativeActor_{appearance.Reference}" };
        root.SetMeta("source_reference", appearance.Reference.ToString());
        root.SetMeta("source_base", appearance.Base.ToString());
        root.SetMeta("traits_source", appearance.TraitsSource.ToString());
        root.SetMeta("model_source", appearance.ModelSource.ToString());
        root.SetMeta("female", appearance.Female);
        root.SetMeta("race", appearance.Race.ToString());
        root.SetMeta("resource_count", appearance.Resources.Count);
        root.SetMeta("facegen_coordinate_bytes", appearance.FaceGenCoordinateBytes);
        root.SetMeta("visual_build", "fail-closed");
        root.SetMeta("visual_blockers", string.Join(',', appearance.VisualBlockers));
        root.SetMeta("generated_asset_inputs", 0);
        root.SetMeta("content_writes", 0);
        foreach (var resource in appearance.Resources)
        {
            var child = new Node3D { Name = $"Source_{resource.Role}" };
            child.SetMeta("logical_path", resource.LogicalPath);
            child.SetMeta("source", resource.Source);
            child.SetMeta("sha256", resource.Sha256);
            child.SetMeta("bytes", resource.Bytes);
            root.AddChild(child);
        }
        return root;
    }

    private static FalloutFormKey CategorySource(
        FalloutPluginStack stack,
        FalloutPluginRecord start,
        ushort categoryFlag,
        string category)
    {
        var current = start;
        var seen = new HashSet<FalloutFormKey>();
        while (true)
        {
            if (!seen.Add(current.FormKey))
                throw new InvalidDataException($"NPC_ template cycle while resolving {category}.");
            var rows = Rows(current);
            var templateFlags = OptionalTemplateFlags(current, rows);
            var templateRows = rows.Where(row => row.Signature == "TPLT").ToArray();
            if ((templateFlags & categoryFlag) == 0 || templateRows.Length == 0)
                return current.FormKey;
            if (templateRows.Length != 1 || templateRows[0].Data.Length != sizeof(uint))
                throw new InvalidDataException($"NPC_ {category} TPLT layout is unsupported.");
            var target = current.Plugin.AdjustFormId(
                BinaryPrimitives.ReadUInt32LittleEndian(templateRows[0].Data.Span));
            if (!stack.TryGetEffective(target, out current))
                throw new InvalidDataException($"NPC_ {category} template is not effective: {target}.");
            if (current.Signature == "LVLN")
                throw new NotSupportedException(
                    $"NPC_ {category} requires dynamic LVLN selection: {target}.");
            if (current.Signature != "NPC_")
                throw new InvalidDataException(
                    $"NPC_ {category} template has unsupported type {current.Signature}.");
        }
    }

    private static ushort OptionalTemplateFlags(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> rows)
    {
        var matches = rows.Where(row => row.Signature == "EAMT").ToArray();
        if (matches.Length == 0) return 0;
        if (matches.Length != 1 || matches[0].Data.Length != sizeof(ushort))
            throw new InvalidDataException($"{record.FormKey} EAMT layout is unsupported.");
        return BinaryPrimitives.ReadUInt16LittleEndian(matches[0].Data.Span);
    }

    private static void AddRaceResources(
        RuntimeLiveContentSource source,
        List<FalloutOwnedActorResource> resources,
        FalloutPluginRecord race,
        bool female)
    {
        var group = string.Empty;
        var sex = string.Empty;
        uint? index = null;
        foreach (var row in race.ReadSubrecords())
        {
            if (row.Signature == "NAM0") { group = "head"; sex = "male"; index = null; continue; }
            if (row.Signature == "NAM1") { group = "body"; sex = "male"; index = null; continue; }
            if (row.Signature == "MNAM") { sex = "male"; index = null; continue; }
            if (row.Signature == "FNAM") { sex = "female"; index = null; continue; }
            if (row.Signature == "INDX")
            {
                if (row.Data.Length != sizeof(uint))
                    throw new InvalidDataException($"RACE {race.FormKey} INDX layout is unsupported.");
                index = BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span);
                continue;
            }
            if (index is null || sex != (female ? "female" : "male") || group is not ("head" or "body"))
                continue;
            if (row.Signature == "MODL")
                AddResource(source, resources, $"race-{group}-model-{index}", ResourcePath(row.Data.Span, "meshes"));
            else if (row.Signature == "ICON")
                AddResource(source, resources, $"race-{group}-texture-{index}", ResourcePath(row.Data.Span, "textures"));
        }
        if (!resources.Any(row => row.Role.StartsWith("race-head-model-", StringComparison.Ordinal)) ||
            !resources.Any(row => row.Role.StartsWith("race-body-model-", StringComparison.Ordinal)))
            throw new InvalidDataException($"RACE {race.FormKey} has no complete sex-specific head/body model identity.");
    }

    private static FalloutPluginRecord RequireAppearancePart(
        FalloutPluginStack stack,
        FalloutFormKey key,
        string expected) => RequireRecord(stack, key, expected);

    private static void AddPartResources(
        RuntimeLiveContentSource source,
        List<FalloutOwnedActorResource> resources,
        FalloutPluginRecord part,
        string role)
    {
        var rows = Rows(part);
        var model = rows.Where(row => row.Signature == "MODL").ToArray();
        var texture = rows.Where(row => row.Signature == "ICON").ToArray();
        if (model.Length > 1 || texture.Length > 1)
            throw new InvalidDataException($"Appearance part {part.FormKey} repeats a resource identity.");
        if (model.Length == 1)
            AddResource(source, resources, $"{role}-model", ResourcePath(model[0].Data.Span, "meshes"));
        if (texture.Length == 1)
            AddResource(source, resources, $"{role}-texture", ResourcePath(texture[0].Data.Span, "textures"));
    }

    private static string ModelPath(
        FalloutPluginRecord record,
        IReadOnlyList<FalloutPluginSubrecord> rows,
        string signature)
    {
        var data = Single(rows, signature, required: true);
        return ResourcePath(data.Span, "meshes");
    }

    private static string ResourcePath(ReadOnlySpan<byte> data, string prefix)
    {
        var terminator = data.IndexOf((byte)0);
        var payload = terminator >= 0 ? data[..terminator] : data;
        if (payload.Length == 0 || terminator >= 0 && data[(terminator + 1)..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Actor resource path is empty or has trailing source bytes.");
        var path = Encoding.UTF8.GetString(payload).Replace('/', '\\').TrimStart('\\');
        if (!path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
            path = $"{prefix}\\{path}";
        return FalloutBsaArchive.CanonicalPath(path);
    }

    private static void AddResource(
        RuntimeLiveContentSource source,
        List<FalloutOwnedActorResource> resources,
        string role,
        string logicalPath)
    {
        if (!source.TryRead(logicalPath, null, out var data, out var sourceIdentity))
            throw new FileNotFoundException($"Actor {role} resource is missing: {logicalPath}");
        resources.Add(new FalloutOwnedActorResource(
            role,
            logicalPath,
            sourceIdentity,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            data.Length));
    }

    private static void AddOptionalResource(
        RuntimeLiveContentSource source,
        List<FalloutOwnedActorResource> resources,
        string role,
        string logicalPath)
    {
        if (!source.TryRead(logicalPath, null, out var data, out var sourceIdentity)) return;
        resources.Add(new FalloutOwnedActorResource(
            role,
            FalloutBsaArchive.CanonicalPath(logicalPath),
            sourceIdentity,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            data.Length));
    }

    private static int FaceFieldBytes(
        IReadOnlyList<FalloutPluginSubrecord> rows,
        string signature,
        int expectedBytes)
    {
        var matches = rows.Where(row => row.Signature == signature).ToArray();
        if (matches.Length == 0) return 0;
        if (matches.Length != 1 || matches[0].Data.Length != expectedBytes)
            throw new NotSupportedException($"NPC_ {signature} FaceGen layout is unsupported.");
        for (var offset = 0; offset < expectedBytes; offset += sizeof(float))
            if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(matches[0].Data.Span[offset..])))
                throw new InvalidDataException($"NPC_ {signature} contains a non-finite coordinate.");
        return expectedBytes;
    }

    private static IReadOnlyList<FalloutFormKey> FormRows(
        FalloutPluginRecord owner,
        IReadOnlyList<FalloutPluginSubrecord> rows,
        string signature) => rows.Where(row => row.Signature == signature).Select(row =>
    {
        if (row.Data.Length != sizeof(uint))
            throw new InvalidDataException($"{owner.FormKey} {signature} layout is unsupported.");
        return owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span));
    }).ToArray();

    private static FalloutFormKey RequiredForm(FalloutPluginRecord record, string signature)
    {
        var data = Single(Rows(record), signature, required: true);
        if (data.Length != sizeof(uint))
            throw new InvalidDataException($"{record.FormKey} {signature} layout is unsupported.");
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        if (raw == 0)
            throw new InvalidDataException($"{record.FormKey} {signature} is null.");
        return record.Plugin.AdjustFormId(raw);
    }

    private static ReadOnlyMemory<byte> Single(
        IReadOnlyList<FalloutPluginSubrecord> rows,
        string signature,
        bool required)
    {
        var matches = rows.Where(row => row.Signature == signature).ToArray();
        if (matches.Length == 0 && !required) return ReadOnlyMemory<byte>.Empty;
        if (matches.Length != 1)
            throw new InvalidDataException($"Actor record requires one {signature} subrecord.");
        return matches[0].Data;
    }

    private static FalloutPluginRecord RequireRecord(
        FalloutPluginStack stack,
        FalloutFormKey key,
        string signature)
    {
        if (!stack.TryGetEffective(key, out var record) || record.Signature != signature)
            throw new InvalidDataException($"Effective {signature} record is absent: {key}.");
        return record;
    }

    private static IReadOnlyList<FalloutPluginSubrecord> Rows(FalloutPluginRecord record) =>
        record.ReadSubrecords().ToArray();
}
