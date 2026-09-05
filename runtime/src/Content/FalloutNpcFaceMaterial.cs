using System.Buffers.Binary;
using OpenNV.Runtime.Formats.FaceGen;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutFaceGenTextureInput(string SourceName, string? LogicalPath,
    int Width, int Height, byte[] Rgba8);

internal sealed record FalloutNpcFaceGenSettings(bool FaceGenTexturing, bool LoadHeadEgtFiles,
    string SourcePath);

internal sealed record FalloutNpcPartTexturePaths(string BaseTexturePath, string NormalTexturePath,
    string? ScatteringTexturePath);

internal sealed record FalloutNpcFaceMaterialInputs(string BaseTexturePath, string NormalTexturePath,
    string? ScatteringTexturePath, FalloutFaceGenTextureInput BaseMod,
    FalloutFaceGenTextureInput DetailMod, FalloutNpcFaceGenSettings SourceSettings,
    IReadOnlyList<string> Blockers)
{
    internal bool CanRender => Blockers.Count == 0;
}

/// <summary>Owned NPC face texture bindings and the native engine's named texture defaults.</summary>
internal static class FalloutNpcFaceMaterial
{
    internal static FalloutNpcFaceMaterialInputs Resolve(RuntimeLiveContentSource source,
        FalloutNpcAppearance appearance, FalloutNpcAppearancePart part, string baseTexturePath,
        string normalTexturePath, string? scatteringTexturePath, FalloutPluginStack? stack = null)
    {
        var settings = ReadSettings(source);
        using var temporaryStack = stack is null ? FalloutPluginStack.Load(source.PluginSources) : null;
        return Resolve(stack ?? temporaryStack!, appearance, part, baseTexturePath, normalTexturePath,
            scatteringTexturePath, settings, path => source.TryRead(path, null, out var data, out _) ? data : null);
    }

    internal static FalloutNpcFaceMaterialInputs Resolve(FalloutPluginStack stack,
        FalloutNpcAppearance appearance, FalloutNpcAppearancePart part, string baseTexturePath,
        string normalTexturePath, string? scatteringTexturePath, FalloutNpcFaceGenSettings settings,
        Func<string, byte[]?> readResource)
    {
        var blockers = new List<string>();
        var baseMod = DefaultBaseMod();
        var detailMod = DefaultDetailMod();
        if (!settings.FaceGenTexturing) blockers.Add("disabled-facegen-texturing-shader-policy-required");
        if (!appearance.RuntimeFace && appearance.Npc.ObjectId == 7 &&
            appearance.Npc.OwnerPlugin.Equals("FalloutNV.esm", StringComparison.OrdinalIgnoreCase))
            blockers.Add("player-facegen-texture-generation-required");

        var texturePaths = ResolvePartTexturePaths(part, baseTexturePath, normalTexturePath, scatteringTexturePath);
        baseTexturePath = texturePaths.BaseTexturePath;
        normalTexturePath = texturePaths.NormalTexturePath;
        scatteringTexturePath = texturePaths.ScatteringTexturePath;

        var body = part.Role is "body" or "hand-left" or "hand-right" or "armor" or "armor-addon";
        if (part.Role != "head" && !body)
            blockers.Add($"facegen-texture-part-policy-required:{part.Role}");
        else if (appearance.RuntimeFace || settings.LoadHeadEgtFiles || body)
        {
            var path = body ? appearance.RaceParts.SingleOrDefault(candidate => candidate.Role == "body-texture")?.ModelPath :
                Path.ChangeExtension(part.ModelPath, ".egt");
            if (path is null) throw new InvalidDataException("Dynamic FaceGen part has no source statistical color model.");
            var bytes = readResource(path);
            if (bytes is null) blockers.Add($"facegen-statistical-color-resource-absent:{path}");
            else
            {
                var egt = FalloutEgtFile.Read(bytes);
                var weights = FalloutFaceGenCoefficients.AddSourceGeometry(
                    appearance.FaceGen.SymmetricTexture, appearance.RaceFaceGen.SymmetricTexture, egt.SymmetricModes.Count);
                var delta = egt.EvaluateDelta(weights, []);
                baseMod = new(path, null, egt.Width, egt.Height, FalloutFaceGenTexture.EncodeBaseMod(delta));
            }
        }
        else
        {
            var model = stack.GetEffective(appearance.ModelOwner);
            var acbs = model.ReadSubrecords().Single(row => row.Signature == "ACBS").Data;
            if (acbs.Length != 24) throw new InvalidDataException("NPC FaceGen material requires complete ACBS data.");
            var master = LastMasterContaining(stack, appearance.ModelOwner);
            if (master is null) blockers.Add("facegen-source-master-owner-required");
            else
            {
                var path = PreprocessedBaseModPath(appearance.ModelOwner, master,
                    (BinaryPrimitives.ReadUInt32LittleEndian(acbs.Span) & 0x00400000) != 0,
                    appearance.Female, appearance.Race);
                var bytes = readResource(path);
                if (bytes is null) blockers.Add($"facegen-base-mod-resource-absent:{path}");
                else baseMod = OwnedDds(path, bytes);
            }
        }
        RequireResource(baseTexturePath, readResource, blockers);
        RequireResource(normalTexturePath, readResource, blockers);
        if (scatteringTexturePath is { } scattering) RequireResource(scattering, readResource, blockers);
        return new FalloutNpcFaceMaterialInputs(baseTexturePath, normalTexturePath, scatteringTexturePath,
            baseMod, detailMod, settings, blockers);
    }

    internal static FalloutFaceGenTextureInput DefaultBaseMod() =>
        Uniform("DefaultBaseModFaceGenTexture", 128, 128, 128, 128);

    internal static FalloutFaceGenTextureInput DefaultDetailMod() =>
        Uniform("DefaultDetailModFaceGenTexture", 62, 65, 62, 64);

    internal static FalloutNpcPartTexturePaths ResolvePartTexturePaths(FalloutNpcAppearancePart part,
        string baseTexturePath, string normalTexturePath, string? scatteringTexturePath)
    {
        if (part.TexturePath is not { } replacement)
            return new(baseTexturePath, normalTexturePath, scatteringTexturePath);
        baseTexturePath = replacement;
        // RACE facial textures replace their normal companions. EYES and HAIR supply
        // a diffuse override only, retaining the source NIF's normal/highlight maps.
        if (part.Role is "head" or "ears" or "mouth" or "teeth-lower" or "teeth-upper" or "tongue")
            normalTexturePath = Companion(replacement, "_n");
        if (part.Role == "head") scatteringTexturePath = Companion(replacement, "_sk");
        return new(baseTexturePath, normalTexturePath, scatteringTexturePath);
    }

    internal static string PreprocessedBaseModPath(FalloutFormKey npc, string masterName,
        bool allRaces, bool female, FalloutFormKey race)
    {
        if (string.IsNullOrWhiteSpace(masterName) || masterName.Contains('/') || masterName.Contains('\\') ||
            masterName is "." or "..") throw new InvalidDataException("Invalid FaceGen source plugin name.");
        // FNV's master-file resolver folds Update-prefixed masters into its base-game folder.
        var folder = masterName.StartsWith("update", StringComparison.OrdinalIgnoreCase) ? "FalloutNV.esm" : masterName;
        var stem = allRaces ? $"{(female ? 'F' : 'M')}{race.ObjectId:X8}_{npc.ObjectId:X8}" : $"{npc.ObjectId:X8}";
        return $"textures/characters/facemods/{folder}/{stem}_0.dds";
    }

    internal static FalloutNpcFaceGenSettings ParseSettings(IEnumerable<string> lines, string sourcePath)
    {
        var enabled = true;
        var headEgt = false;
        var section = string.Empty;
        foreach (var raw in lines)
        {
            var line = raw.Split(';', '#')[0].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            if (!section.Equals("General", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            if (!key.Equals("bFaceGenTexturing", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("bLoadFaceGenHeadEGTFiles", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(separator + 1)..].Trim() switch
            {
                "0" => false,
                "1" => true,
                _ => throw new InvalidDataException($"Invalid native FaceGen boolean {key} in {sourcePath}."),
            };
            if (key.Equals("bFaceGenTexturing", StringComparison.OrdinalIgnoreCase)) enabled = value;
            else headEgt = value;
        }
        return new FalloutNpcFaceGenSettings(enabled, headEgt, sourcePath);
    }

    private static FalloutNpcFaceGenSettings ReadSettings(RuntimeLiveContentSource source)
    {
        var profile = source.Game == RuntimeLiveContentSource.Fallout3Game ? "Fallout3" : "FalloutNV";
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var configured = Path.Combine(documents, "My Games", profile, "Fallout.ini");
        var defaults = Path.Combine(Directory.GetParent(source.ContentRoot)?.FullName ?? source.ContentRoot, "Fallout_default.ini");
        var selected = File.Exists(configured) ? configured : defaults;
        return ParseSettings(File.Exists(selected) ? File.ReadLines(selected) : [], selected);
    }

    private static string? LastMasterContaining(FalloutPluginStack stack, FalloutFormKey key)
    {
        string? result = null;
        foreach (var context in stack.Plugins)
        {
            var plugin = context.Plugin;
            var header = plugin.Records.FirstOrDefault(row => row.Signature == "TES4");
            if (header is null || (header.Flags & 1) == 0) continue;
            if (plugin.Records.Any(row => row.Signature == "NPC_" &&
                row.FormKey.ObjectId == key.ObjectId &&
                row.FormKey.OwnerPlugin.Equals(key.OwnerPlugin, StringComparison.OrdinalIgnoreCase))) result = plugin.Name;
        }
        return result;
    }

    private static FalloutFaceGenTextureInput OwnedDds(string path, byte[] bytes)
    {
        if (bytes.Length < 128 || !bytes.AsSpan(0, 4).SequenceEqual("DDS "u8))
            throw new InvalidDataException($"FaceGen texture is not a complete DDS: {path}");
        var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16));
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            throw new InvalidDataException($"FaceGen DDS dimensions are invalid: {path}");
        return new FalloutFaceGenTextureInput(path, path, (int)width, (int)height, []);
    }

    private static FalloutFaceGenTextureInput Uniform(string name, byte red, byte green, byte blue, byte alpha)
    {
        var pixels = new byte[32 * 32 * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = red; pixels[offset + 1] = green;
            pixels[offset + 2] = blue; pixels[offset + 3] = alpha;
        }
        return new FalloutFaceGenTextureInput(name, null, 32, 32, pixels);
    }

    private static string Companion(string path, string suffix) =>
        path[..^Path.GetExtension(path).Length] + suffix + ".dds";

    private static void RequireResource(string path, Func<string, byte[]?> read, List<string> blockers)
    {
        if (read(path) is not { Length: > 0 }) blockers.Add($"facegen-material-resource-absent:{path}");
    }
}
