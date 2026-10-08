using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private static void NifTextureSourceIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-texture-identity-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game");
        var data = Path.Combine(game, "Data");
        var patch = Path.Combine(directory, "SelectedLayer");
        Directory.CreateDirectory(Path.Combine(data, "meshes")); Directory.CreateDirectory(patch);
        try
        {
            File.WriteAllBytes(Path.Combine(data, Plugin), PluginFixture());
            var archiveIni = Path.Combine(game, "Archives.ini");
            File.WriteAllText(archiveIni, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            var positives = new (string Raw, string Expected)[]
            {
                ("textures/probe.dds", "textures/probe.dds"),
                ("textures\\PROBE.DDS", "textures/probe.dds"),
                ("Data/textures/probe.dds", "textures/probe.dds"),
                ("DATA\\TEXTURES\\PROBE.DDS", "textures/probe.dds"),
                ("probe.dds", "probe.dds"),
                ("Data\\probe.dds", "probe.dds"),
                ("texturesSomething/probe.dds", "texturessomething/probe.dds"),
                ("Data\\texturesSomething\\probe.dds", "texturessomething/probe.dds"),
                ("Data/textures/archive-only.dds", "textures/archive-only.dds"),
                ("TEXTURES\\ARCHIVE-ONLY.DDS", "textures/archive-only.dds"),
            };
            var missing = new (string Raw, string Expected)[]
            {
                ("missing-root.dds", "missing-root.dds"),
                ("Data/textures/missing.dds", "textures/missing.dds"),
                ("archive-only.dds", "archive-only.dds"),
            };
            var invalid = new[] { "../probe.dds", "Data/../probe.dds", "/textures/probe.dds", "D:/outside.dds", "Data//textures/probe.dds", " " };
            foreach (var root in new[] { data, patch })
            {
                var selected = root == patch;
                byte tag = selected ? (byte)90 : (byte)10;
                foreach (var path in new[] { "textures/probe.dds", "probe.dds", "texturessomething/probe.dds" })
                    WriteTextureIdentityInput(root, path, TextureIdentityDds(tag++));
                // These distinct, valid DDS decoys make an invented prefix a
                // wrong-source pass, rather than merely an absent-file error.
                foreach (var path in new[] { "textures/Data/textures/probe.dds", "textures/Data/probe.dds",
                    "textures/Data/texturesSomething/probe.dds", "textures/Data/textures/archive-only.dds",
                    "textures/missing-root.dds", "textures/Data/textures/missing.dds" })
                    WriteTextureIdentityInput(root, path, TextureIdentityDds(tag++));
                WriteTextureIdentityArchive(Path.Combine(root, "FalloutNV.bsa"), TextureIdentityDds(tag));
            }
            var positiveModel = "meshes/texture-identities.nif";
            WriteTextureIdentityInput(data, positiveModel, TextureIdentityNif(positives.Select(test => test.Raw).Append("").ToArray()));
            var malformedModel = "meshes/malformed-texture-siblings.nif";
            WriteTextureIdentityInput(data, malformedModel, TextureIdentityNif([.. invalid, "Data/textures/probe.dds"]));
            WriteTextureIdentityInput(data, "meshes/unknown-texture-block.nif", TextureIdentityNif(["Data/textures/probe.dds"], unknownBlock: true));
            for (var index = 0; index < missing.Length; index++)
                WriteTextureIdentityInput(data, $"meshes/missing-texture-{index}.nif", TextureIdentityNif([missing[index].Raw]));
            foreach (var type in new[] { "BSShaderNoLightingProperty", "NiSourceTexture", "SkyShaderProperty", "TileShaderProperty" })
                WriteTextureIdentityInput(data, "meshes/" + type + ".nif", TextureIdentityNif(["Data/textures/probe.dds"], type));
            var before = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
            foreach (var selectedLayer in new[] { false, true })
            {
                using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    selectedLayer ? [patch] : [], [Plugin], archiveIni);
                using var records = FalloutPluginStack.Load(source.PluginSources);
                source.ArchiveWarmup.GetAwaiter().GetResult();
                Require(ReferenceEquals(records.OwnedSource, source), "Texture identity used an unrelated source owner.");
                var resources = new CellGraphAudit.CellAuditResources(source, .1f);
                var row = resources.Read(positiveModel, "animation");
                var model = resources.ReadModel(positiveModel.ToUpperInvariant().Replace('/', '\\'));
                Require(ReferenceEquals(row, model.Report) && row.Kind == "model" && row.InspectionRoles.SetEquals(["animation", "model"]) &&
                    row.Failures.Count == 0 && row.DecodedBlocks == row.Blocks && row.Blocks == 2 && row.Roots == 1,
                    "Complete NIF decode or a later resource role changed the original texture namespace binding.");
                Require(row.Dependencies.Order(StringComparer.Ordinal).SequenceEqual(positives.Select(test => test.Expected)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)), "NIF aliases lost an independent expected resource key; actual=" + string.Join(',', row.Dependencies));
                var declared = row.DependencyDeclarations.Select(value => JsonSerializer.SerializeToElement(value)).ToArray();
                Require(declared.Length == positives.Length + 1 && declared[^1].GetProperty("declaredPath").GetString() == "" &&
                    declared[^1].GetProperty("logicalPath").ValueKind == JsonValueKind.Null,
                    "Raw alias/encoded-empty declarations were collapsed or assigned invented resources.");
                for (var index = 0; index < positives.Length; index++)
                {
                    var test = positives[index];
                    Require(declared[index].GetProperty("declaredPath").GetString() == test.Raw &&
                        declared[index].GetProperty("logicalPath").GetString() == test.Expected,
                        "A raw NIF path or its consumer-derived key disappeared.");
                    var key = FalloutNifSurfaceInputs.TexturePath(test.Raw);
                    Require(key.Replace('\\', '/') == test.Expected,
                        "The shared native consumer key differs from the independent expected namespace: " + test.Raw);
                    Require(source.TryRead(key, null, out var bytes, out var identity),
                        "The shared native consumer key did not bind the expected authored resource: " + test.Raw);
                    var report = resources.Resources[test.Expected];
                    var expectedRoot = selectedLayer ? patch : data;
                    var expectedIdentity = test.Expected == "textures/archive-only.dds"
                        ? Path.Combine(expectedRoot, "FalloutNV.bsa") + "::textures\\archive-only.dds"
                        : Path.Combine(expectedRoot, test.Expected.Replace('/', Path.DirectorySeparatorChar));
                    Require(report.Path == test.Expected && report.Source == identity && report.Source == expectedIdentity &&
                        report.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)) && report.Failures.Count == 0,
                        "The audit used an invented prefix, losing actual loose/BSA winner identity or decoded bytes: " + test.Raw);
                }
                Require(resources.Resources.Count == 5 &&
                    !resources.Resources.ContainsKey("textures/data/textures/probe.dds") &&
                    !resources.Resources.ContainsKey("textures/data/probe.dds") &&
                    JsonSerializer.SerializeToElement(row.Native).GetProperty("admission").GetString() == "unverified",
                    "Decoys or alias duplicates entered the winning resource ledger, or path equality became native readiness.");
                var malformed = resources.Read(malformedModel, "model");
                var malformedDeclarations = malformed.DependencyDeclarations.Select(value => JsonSerializer.SerializeToElement(value)).ToArray();
                Require(malformed.Failures.Count == invalid.Length && malformedDeclarations.Length == invalid.Length + 1 &&
                    malformed.Dependencies.SequenceEqual(["textures/probe.dds"]) &&
                    malformedDeclarations.Take(invalid.Length).Select(value => value.GetProperty("declaredPath").GetString()).SequenceEqual(invalid) &&
                    malformedDeclarations.Take(invalid.Length).All(value => value.GetProperty("logicalPath").ValueKind == JsonValueKind.Null) &&
                    malformedDeclarations[^1].GetProperty("logicalPath").GetString() == "textures/probe.dds",
                    "Malformed normalization hid raw declarations or prevented an unrelated valid sibling read.");
                foreach (var path in invalid)
                {
                    try { _ = FalloutNifSurfaceInputs.TexturePath(path); }
                    catch (InvalidDataException) { continue; }
                    throw new InvalidDataException("The authored invalid namespace was accepted by the native consumer helper: " + path);
                }
                var unknown = resources.Read("meshes/unknown-texture-block.nif", "model");
                Require(unknown.Blocks == 3 && unknown.DecodedBlocks == 2 && unknown.Failures.Count == 1 &&
                    JsonSerializer.SerializeToElement(unknown.Failures.Single()).GetProperty("lane").GetString() == "nif-block" &&
                    unknown.Dependencies.SequenceEqual(["textures/probe.dds"]),
                    "An unsupported NIF block disappeared or hid a later valid texture declaration.");
                for (var index = 0; index < missing.Length; index++)
                {
                    var test = missing[index];
                    Require(!source.TryRead(FalloutNifSurfaceInputs.TexturePath(test.Raw), null, out _, out _),
                        "Missing native namespace unexpectedly resolved an authored decoy.");
                    var missingRow = resources.Read($"meshes/missing-texture-{index}.nif", "model");
                    var refused = resources.Resources[test.Expected];
                    Require(missingRow.Dependencies.SequenceEqual([test.Expected]) && refused.Source is null && refused.Sha256 is null && refused.Failures.Count != 0,
                        "A missing native namespace was replaced by a valid prefixed loose/BSA resource: " + test.Raw);
                }
                foreach (var type in new[] { "BSShaderNoLightingProperty", "NiSourceTexture" })
                {
                    var supportedNamespace = resources.Read("meshes/" + type + ".nif", "model");
                    Require(supportedNamespace.Failures.Count == 0 && supportedNamespace.DecodedBlocks == supportedNamespace.Blocks &&
                        supportedNamespace.Dependencies.SequenceEqual(["textures/probe.dds"]),
                        "A shared legacy/no-lighting namespace did not retain its declared path and actual resource key.");
                }
                foreach (var type in new[] { "SkyShaderProperty", "TileShaderProperty" })
                {
                    var separateOwner = resources.Read("meshes/" + type + ".nif", "model");
                    var declaration = JsonSerializer.SerializeToElement(separateOwner.DependencyDeclarations.Single());
                    Require(separateOwner.DecodedBlocks == separateOwner.Blocks && separateOwner.Dependencies.Count == 0 &&
                        separateOwner.Failures.Count == 1 && declaration.GetProperty("declaredPath").GetString() == "Data/textures/probe.dds" &&
                        declaration.GetProperty("logicalPath").ValueKind == JsonValueKind.Null,
                        "A decoded sky/tile declaration was hidden or assigned a guessed consumer policy.");
                }
            }
            Require(before.All(item => item.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Key)))) &&
                before.Count == Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count(),
                "The texture identity audit changed any authored source file or created a replacement asset.");
            Console.WriteLine("OPENNV_CELL_NIF_TEXTURE_IDENTITY_PASS originalDataPrefix=true bareNamespace=true texturesSomething=true aliases=true actualLooseBsaWinner=true selectedLayer=true rawDeclarations=true malformedSiblingsRetained=true missingNamespaceRefused=true skyTileConsumerUninspected=true sourceBytes=unchanged nativeDrawAdmission=unverified");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] TextureIdentityDds(byte tag)
    {
        var bytes = DdsFixture(); bytes[128] = tag; return bytes;
    }

    private static void WriteTextureIdentityInput(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }

    private static byte[] TextureIdentityNif(string[] textures, string type = "BSShaderTextureSet", bool unknownBlock = false)
    {
        var strings = type == "NiSourceTexture" ? new[] { "fixture", textures.Single() } : new[] { "fixture" };
        var blocks = new[]
        {
            (Type: "NiNode", Payload: Node([0, 0, 0], 1, -1, [])),
            (Type: type, Payload: Bytes(writer =>
            {
                void Sized(string value) { var encoded = Encoding.UTF8.GetBytes(value); writer.Write(encoded.Length); writer.Write(encoded); }
                if (type == "BSShaderTextureSet")
                { writer.Write(textures.Length); foreach (var path in textures) Sized(path); return; }
                writer.Write(0); writer.Write(0u); writer.Write(-1); // ObjectNET.
                if (type == "NiSourceTexture")
                {
                    writer.Write((byte)1); writer.Write(1); writer.Write(-1);
                    writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write((byte)1); writer.Write(true); writer.Write(false); return;
                }
                writer.Write((ushort)0); writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0f); writer.Write(0u); Sized(textures.Single());
                if (type == "BSShaderNoLightingProperty") { writer.Write(1f); writer.Write(0f); writer.Write(1f); writer.Write(0f); }
                if (type == "SkyShaderProperty") writer.Write(5u);
            }))
        }.ToList();
        if (unknownBlock) blocks.Insert(1, ("AuthoredUnknownTextureOwner", new byte[] { 1, 2, 3 }));
        return Bytes(writer =>
        {
            void Sized(string value) { var encoded = Encoding.UTF8.GetBytes(value); writer.Write(encoded.Length); writer.Write(encoded); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Count); writer.Write(34u); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Count); foreach (var block in blocks) Sized(block.Type);
            for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Payload.Length);
            writer.Write((uint)strings.Length); writer.Write((uint)strings.Max(value => Encoding.UTF8.GetByteCount(value)));
            foreach (var value in strings) Sized(value); writer.Write(0u);
            foreach (var block in blocks) writer.Write(block.Payload);
            writer.Write(1u); writer.Write(0);
        });
    }

    private static void WriteTextureIdentityArchive(string path, byte[] payload)
    {
        var folder = Encoding.ASCII.GetBytes("textures\0"); var name = Encoding.ASCII.GetBytes("archive-only.dds\0");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x00415342u); writer.Write(104u); writer.Write(36u); writer.Write(3u);
        writer.Write(1u); writer.Write(1u); writer.Write((uint)folder.Length); writer.Write((uint)name.Length); writer.Write(2u);
        writer.Write(0ul); writer.Write(1u); writer.Write((uint)(52 + name.Length));
        writer.Write((byte)folder.Length); writer.Write(folder);
        writer.Write(0ul); writer.Write((uint)payload.Length); writer.Write((uint)(52 + 1 + folder.Length + 16 + name.Length));
        writer.Write(name); writer.Write(payload);
    }
}
