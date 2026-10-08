using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private static void AddonDependencyDeclarations()
    {
        const string foreign = "ForeignAddon.esm", overrides = "AddonOverride.esp";
        var directory = Path.Combine(Path.GetTempPath(), "opennv-source-addons-" + Guid.NewGuid().ToString("N"));
        var cases = 0;
        try
        {
            foreach (var stream in new[] { 21u, 34u })
                foreach (var mode in new[] { "winner", "selected-layer", "index-moved", "deleted", "missing-index", "duplicate-index",
                    "duplicate-deleted", "disabled-record", "zero-max-index", "value-flags", "config-flags", "missing-model", "unsupported-header",
                    "missing-texture", "unknown-block", "self-cycle", "indirect-cycle", "shared-model", "diamond", "role-upgrade", "incompatible-role",
                    "sound-zero", "sound-self", "sound-missing", "bad-data", "bad-config", "bad-sound", "duplicate-data", "duplicate-sound", "bad-path",
                    "unused-bad-data", "unused-missing-model", "truncated-value", "extra-value" })
                {
                    var game = Path.Combine(directory, stream + "-" + mode, "Game"); var data = Path.Combine(game, "Data");
                    var layer = Path.Combine(directory, stream + "-" + mode, "SelectedLayer");
                    Directory.CreateDirectory(data); Directory.CreateDirectory(layer);
                    var archive = Path.Combine(data, "FalloutNV.bsa"); ModContentContracts.WriteArchive(archive, "unrelated authored input");
                    var ini = Path.Combine(game, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
                    var fixture = AddonPluginFixture(mode);
                    File.WriteAllBytes(Path.Combine(data, Plugin), fixture.Base);
                    File.WriteAllBytes(Path.Combine(data, foreign), fixture.Foreign);
                    File.WriteAllBytes(Path.Combine(data, overrides), fixture.Overrides);
                    uint[] rootIndices = mode == "zero-max-index" ? [0u, uint.MaxValue] :
                        mode is "value-flags" or "config-flags" or "diamond" or "shared-model" ? [17u, 29u] : [17u, 17u];
                    WriteTextureIdentityInput(data, "meshes/addon-root.nif", AddonModelFixture(stream, rootIndices,
                        valueFlags: mode == "value-flags" ? (byte)2 : (byte)0, firstValueMutation: mode));
                    WriteTextureIdentityInput(data, "meshes/losing.nif", AddonModelFixture(stream, [], texture: "textures/losing.dds"));
                    WriteTextureIdentityInput(data, "meshes/foreign.nif", AddonModelFixture(stream, [], texture: "textures/foreign.dds"));
                    WriteTextureIdentityInput(data, "meshes/grandchild.nif", AddonModelFixture(stream, mode == "indirect-cycle" ? [17u] : mode == "diamond" ? [31u] : [], texture: "bare-grandchild.dds"));
                    WriteTextureIdentityInput(data, "meshes/common.nif", AddonModelFixture(stream, [], texture: "textures/common.dds"));
                    uint[] childIndices = mode == "self-cycle" ? [17u] : mode == "diamond" ? [31u] :
                        mode is "shared-model" or "role-upgrade" or "incompatible-role" or "zero-max-index" ? [] : [29u];
                    var winningModel = mode == "role-upgrade" ? NifFixture(texture: true) :
                        AddonModelFixture(stream, childIndices, texture: "Data/textures/winning.dds", unknownBlock: mode == "unknown-block");
                    if (mode == "unsupported-header") winningModel = AddonModelFixture(99, childIndices);
                    if (mode != "missing-model") WriteTextureIdentityInput(data, "meshes/winning.nif", winningModel);
                    if (mode == "selected-layer") WriteTextureIdentityInput(layer, "meshes/winning.nif", AddonModelFixture(stream, [29u], texture: "textures/selected.dds"));
                    foreach (var texture in new[] { "textures/probe.dds", "textures/root.dds", "textures/losing.dds", "textures/foreign.dds",
                        "bare-grandchild.dds", "textures/common.dds", "textures/selected.dds", "textures/Data/textures/winning.dds" })
                        WriteTextureIdentityInput(data, texture, TextureIdentityDds(71));
                    if (mode != "missing-texture") WriteTextureIdentityInput(data, "textures/winning.dds", TextureIdentityDds(19));
                    var before = Directory.EnumerateFiles(Path.GetDirectoryName(game)!, "*", SearchOption.AllDirectories)
                        .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
                    using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                        mode == "selected-layer" ? [layer] : [], [Plugin, foreign, overrides], ini);
                    using var records = FalloutPluginStack.Load(source.PluginSources);
                    var resources = new CellGraphAudit.CellAuditResources(source, .1f, records);
                    if (mode == "role-upgrade")
                        Require(resources.Read("meshes/winning.nif", "lod-model").CollisionShapes == 0,
                            "The independent LOD source role invented placed collision before ADDN admission.");
                    if (mode == "incompatible-role") _ = resources.Read("meshes/winning.nif", "texture");
                    var root = resources.Read("meshes/addon-root.nif", "model");
                    resources.ReadAddonDeclarations();
                    var coverage = JsonSerializer.SerializeToElement(resources.AddonCoverage);
                    var badValue = mode is "truncated-value" or "extra-value";
                    Require(root.DecodedBlocks == root.Blocks - (badValue ? 1 : 0) && root.AddonLinks.Count == (badValue ? 1 : 2) &&
                        root.AddonLinks.Select(use => use.Index).SequenceEqual(badValue ? rootIndices.Skip(1) : rootIndices) &&
                        root.AddonLinks.Select(use => use.Block).SequenceEqual(badValue ? [2] : [1, 2]) && root.DependencyEdges.Contains(new("textures/root.dds", "texture")) &&
                        !resources.Resources.ContainsKey("textures/data/textures/winning.dds"),
                        "Original value blocks/ordinals or an unrelated texture sibling disappeared, or an invented texture prefix was read.");
                    Require(root.AddonLinks.Select((use, ordinal) => use.Translation.SequenceEqual([43f, -17f, 9f]) && use.Scale == 1.75f &&
                        use.Rotation.SequenceEqual([0f, -1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f]) && use.ValueFlags == (mode == "value-flags" && ordinal == 0 ? 2 : 0) &&
                        use.NativeInstance.StartsWith("unverified", StringComparison.Ordinal) && use.ComposedCollision.StartsWith("uninspected", StringComparison.Ordinal)).All(value => value),
                        "Addon declarations lost their original transform/flags or claimed invented native/placed geometry.");
                    var catalogInvalid = mode is "duplicate-index" or "bad-data" or "bad-config" or "bad-sound" or "duplicate-data" or "duplicate-sound" or "bad-path" or "unused-bad-data";
                    Require(resources.AddonCatalogPassed != catalogInvalid && coverage.GetProperty("catalogAttempts").GetInt32() == 1,
                        "The exact complete catalog refusal was bypassed or retried as a partial referenced-index catalog.");
                    if (catalogInvalid)
                        Require(root.AddonLinks.All(use => use.Definition is null && AddonHasLane(use.Failures, "nif-addon-catalog")) &&
                            coverage.GetProperty("catalogFailure").ValueKind == JsonValueKind.Object,
                            "A catalog-wide malformed/duplicate refusal disappeared or chose one apparently valid winner.");
                    else if (mode is "deleted" or "missing-index" or "index-moved")
                    {
                        Require(root.AddonLinks.All(use => use.Definition is null && AddonHasLane(use.Failures, "nif-addon-index")),
                            "Missing/changed/deleted index resurrected a losing or FormID-derived addon.");
                        if (mode == "deleted") Require(records.TryGetWinner(Key(0x610), out var deletion) && deletion.IsDeleted,
                            "Actual source deletion was discarded instead of retained outside the effective catalog.");
                    }
                    else
                    {
                        var definition = root.AddonLinks[0].Definition!;
                        Require(definition.Form == Key(0x610).ToString() && definition.Winner == overrides && definition.ParticleCap ==
                            (mode == "zero-max-index" ? 0 : 617) && definition.Flags == (mode == "config-flags" ? 2 : 1) &&
                            definition.Sha256 == Convert.ToHexString(SHA256.HashData(records.GetEffective(Key(0x610)).ReadData())) &&
                            root.AddonLinks.All(use => use.Definition is not null && use.Model is not null),
                            "The index join used a losing/foreign/low-ID form or omitted repeated typed model bindings.");
                        Require(root.DependencyEdges.Any(edge => edge.Kind == "model") && root.DependencyEdges.All(edge => edge.Kind is "model" or "texture"),
                            "Nested model resources were coerced into the texture-only walker.");
                        if (mode == "zero-max-index") Require(root.AddonLinks[1].Definition is { Index: uint.MaxValue, ParticleCap: ushort.MaxValue },
                            "The original unsigned index/cap endpoints were artificially restricted.");
                        if (mode is "sound-zero" or "sound-self" or "sound-missing")
                        {
                            var expected = mode == "sound-zero" ? null : new FalloutFormKey(mode == "sound-self" ? overrides : foreign,
                                mode == "sound-self" ? 0x912u : 0x913u).ToString();
                            Require(definition.Sound.Form == expected && definition.Sound.Encoded ==
                                (mode == "sound-zero" ? 0u : mode == "sound-self" ? 0x02000912u : 0x00000913u) &&
                                root.AddonLinks.All(use => use.NativeAudio == definition.Sound.AudioOwner),
                                "SNAM lost the declaring master/self/zero identity or fabricated a played sound.");
                        }
                        else Require(definition.Sound.Form == new FalloutFormKey(foreign, 0x910).ToString() && definition.Sound.Encoded == 0x910 &&
                            definition.Sound.Winner == foreign && root.AddonLinks[0].NativeAudio.StartsWith("unbound", StringComparison.Ordinal),
                            "SNAM used the global slot order or a same-low-ID decoy, or claimed bound native audio.");
                        if (mode == "disabled-record") Require((definition.RecordFlags & 0x800) != 0,
                            "The effective source ADDN's disabled flag was silently excluded or stripped.");
                        if (mode == "value-flags") Require(AddonHasLane(root.AddonLinks[0].Failures, "nif-addon-value-flags") &&
                            root.AddonLinks[1].Failures.Count == 0, "Unbound value-node flags were admitted or hid the independent good sibling.");
                        if (mode == "config-flags") Require(AddonHasLane(root.AddonLinks[0].Failures, "nif-addon-configuration-flags") &&
                            root.AddonLinks[1].Failures.Count == 0, "Unbound ADDN configuration flags were admitted or hid the independent good sibling.");
                        if (mode is "self-cycle" or "indirect-cycle")
                            Require(resources.Resources.Values.SelectMany(row => row.AddonLinks).Any(use => AddonHasLane(use.Failures, "nif-addon-cycle")),
                                "The cached model row suppressed an actual active-index dependency cycle.");
                        else Require(!resources.Resources.Values.SelectMany(row => row.AddonLinks).Any(use => AddonHasLane(use.Failures, "nif-addon-cycle")),
                            "Sibling/shared/diamond source reuse was misclassified as a cycle.");
                        if (mode == "role-upgrade") Require(resources.Resources["meshes/winning.nif"] is { Kind: "model", CollisionShapes: 1 } upgraded &&
                            upgraded.InspectionRoles.SetEquals(["lod-model", "model"]), "An early source role hid the later ADDN model collision inspection.");
                        if (mode == "incompatible-role") Require(AddonHasLane(resources.Resources["meshes/winning.nif"].Failures, "resource-role-owner"),
                            "Incompatible same-path texture/model declarations reused a false format pass.");
                        if (mode is "missing-model" or "unsupported-header") Require(resources.Resources["meshes/winning.nif"].Stream is null &&
                            resources.Resources["meshes/winning.nif"].Failures.Count != 0, "Missing/unsupported nested source was invented or substituted.");
                        if (mode == "missing-texture") Require(resources.Resources["textures/winning.dds"].Source is null &&
                            resources.Resources["textures/winning.dds"].Failures.Count != 0, "A missing native namespace was replaced by an authored decoy.");
                        if (mode == "unknown-block") Require(AddonHasLane(resources.Resources["meshes/winning.nif"].Failures, "nif-block") &&
                            resources.Resources.ContainsKey("textures/winning.dds") && resources.Resources.ContainsKey("bare-grandchild.dds"),
                            "An unsupported nested block hid later valid texture/addon siblings.");
                        if (mode == "selected-layer") Require(source.TryRead("meshes/winning.nif", null, out var selected, out var identity) &&
                            root.AddonLinks[0].Model!.Source == identity && root.AddonLinks[0].Model!.Sha256 == Convert.ToHexString(SHA256.HashData(selected)),
                            "The exact selected loose-layer model winner was replaced by base/losing data.");
                        if (mode == "unused-missing-model") Require(resources.AddonModelRoots.Contains("meshes/unused-missing.nif") &&
                            resources.Resources["meshes/unused-missing.nif"].Failures.Count != 0,
                            "An unused effective ADDN model declaration was outside the complete resource denominator.");
                        if (badValue) Require(AddonHasLane(root.Failures, "nif-block") && root.AddonLinks[0].Block == 2 &&
                            root.AddonLinks[0].Definition is not null, "A malformed value payload fabricated an index or hid the later healthy value sibling.");
                    }
                    using (var other = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                        activePlugins: [Plugin, foreign, overrides], archiveIniPath: ini))
                    using (var foreignRecords = FalloutPluginStack.Load(other.PluginSources))
                    {
                        try { _ = new CellGraphAudit.CellAuditResources(source, .1f, foreignRecords); throw new InvalidDataException("Foreign ADDN source was admitted."); }
                        catch (ArgumentException) { }
                    }
                    if (stream == 34 && mode == "unused-missing-model") AddonComponentLedger(source, records, directory);
                    Require(before.All(item => item.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Key)))) &&
                        before.Count == Directory.EnumerateFiles(Path.GetDirectoryName(game)!, "*", SearchOption.AllDirectories).Count(),
                        "The ADDN audit changed an authored input or wrote a transformed resource.");
                    cases++;
                }
            AddonDeepDeclarations(directory);
            Require(cases == 68, "ADDN declarations did not execute the complete authored mode/stream matrix.");
            Console.WriteLine("OPENNV_CELL_ADDON_DEPENDENCIES_PASS cases=68 streams=21,34 indexedWinner=true declaringMaster=true unsignedIndex=true repeatedEdges=true nestedModels=true typedRoles=true cachedCycleRefused=true flagsRefused=true deleted=true duplicateCatalogRefused=true unusedDeclarations=true malformedValueRefused=true deepGraph=true missingOwnerRefused=true foreignOwnerRefused=true sourceBytes=unchanged nativeInstance=unverified composedCollision=uninspected audio=unbound");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static bool AddonHasLane(IEnumerable<object> issues, string lane) => issues.Any(issue =>
        JsonSerializer.SerializeToElement(issue).GetProperty("lane").GetString() == lane);

    private static void AddonComponentLedger(RuntimeLiveContentSource source, FalloutPluginStack records, string directory)
    {
        var configuration = Path.Combine(directory, "addon-component-runtime.json");
        File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
        var options = CellGraphAudit.ParseOptions([Path.GetDirectoryName(source.ContentRoot)!, Path.Combine(directory, "addon-component-output"),
            "--runtime-config", configuration]);
        Directory.CreateDirectory(options.Output);
        Require(!CellGraphAudit.RunComponent(source, records, options, .1f, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(configuration)))),
            "Source declarations unexpectedly certified a native scene.");
        using var component = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.Output, "component.private.json")));
        var graph = component.RootElement.GetProperty("sourceGraph").GetProperty("rows").EnumerateArray()
            .Where(row => row.GetProperty("signature").GetString() == "ADDN").ToArray();
        var resources = component.RootElement.GetProperty("alternatives").GetProperty("resources").EnumerateArray().ToArray();
        Require(graph.Length == 3 && graph.All(row => row.GetProperty("sourceFields").GetArrayLength() >= 4) &&
            resources.Single(row => row.GetProperty("path").GetString() == "meshes/unused-missing.nif").GetProperty("failures").GetArrayLength() != 0 &&
            !component.RootElement.GetProperty("coverage").GetProperty("sourceAccountingPassed").GetBoolean() &&
            !component.RootElement.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean(),
            "The complete graph omitted ungrouped ADDN rows, unused model failures or their false source/native readiness.");
    }

    private static (byte[] Base, byte[] Foreign, byte[] Overrides) AddonPluginFixture(string mode)
    {
        byte[] Header(params string[] masters)
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            return Record("TES4", 0, 0, [Field("HEDR", header), .. masters.SelectMany(master => new[] { Field("MAST", Text(master)), Field("DATA", new byte[8]) })]);
        }
        byte[] Addon(uint form, uint index, string model, ushort cap = 617, ushort flags = 1, uint recordFlags = 0, uint sound = 0x910, bool corrupt = false)
        {
            var config = new byte[4]; BinaryPrimitives.WriteUInt16LittleEndian(config, cap); BinaryPrimitives.WriteUInt16LittleEndian(config.AsSpan(2), flags);
            var fields = new List<byte[]> { Field("DATA", corrupt && mode == "bad-data" ? new byte[3] : BitConverter.GetBytes(index)),
                Field("DNAM", corrupt && mode == "bad-config" ? new byte[5] : config), Field("MODL", Text(corrupt && mode == "bad-path" ? "../unsafe.nif" : model)),
                Field("SNAM", corrupt && mode == "bad-sound" ? new byte[3] : BitConverter.GetBytes(sound)) };
            if (corrupt && mode == "duplicate-data") fields.Add(Field("DATA", BitConverter.GetBytes(index)));
            if (corrupt && mode == "duplicate-sound") fields.Add(Field("SNAM", BitConverter.GetBytes(sound)));
            return Record("ADDN", form, recordFlags, fields.ToArray());
        }
        var basis = Join(Header(), Record("STAT", 0x700, 0, Field("MODL", Text("addon-root.nif"))), Cell(0x800, "AuthoredAddonGraph"),
            Group(0x800, Reference(0x810, 0x700), Reference(0x811, 0x700, flags: 0x800)),
            Addon(0x610, 17, "losing.nif", cap: 3, flags: 0), Record("SOUN", 0x910, 0, Field("EDID", Text("WrongMasterSound"))));
        var foreign = Join(Header(), Addon(0x610, mode is "duplicate-index" or "duplicate-deleted" ? 17u : 77u,
                mode == "unused-missing-model" ? "unused-missing.nif" : "foreign.nif", cap: 2, flags: 0),
            Record("SOUN", 0x910, 0, Field("EDID", Text("ActualForeignSound"))));
        var winnerIndex = mode == "zero-max-index" ? 0u : mode == "index-moved" ? 31u : 17u;
        var secondaryIndex = mode == "zero-max-index" ? uint.MaxValue : 29u;
        var winner = mode == "deleted" ? Record("ADDN", 0x01000610, 0x20) : mode == "missing-index" ? Record("ADDN", 0x01000610, 0x20) :
            Addon(0x01000610, winnerIndex, mode == "self-cycle" ? "addon-root.nif" : "winning.nif", cap: mode == "zero-max-index" ? (ushort)0 : (ushort)617,
                flags: mode == "config-flags" ? (ushort)2 : (ushort)1, recordFlags: mode == "disabled-record" ? 0x800u : 0,
                sound: mode == "sound-zero" ? 0u : mode == "sound-self" ? 0x02000912u : mode == "sound-missing" ? 0x00000913u : 0x00000910u, corrupt: true);
        var secondary = Addon(0x02000611, secondaryIndex, mode == "shared-model" ? "winning.nif" : "grandchild.nif",
            cap: mode == "zero-max-index" ? ushort.MaxValue : (ushort)33, flags: 0, sound: 0);
        var deletedDuplicate = mode == "duplicate-deleted" ? Record("ADDN", 0x00000610, 0x20) : Array.Empty<byte>();
        var common = mode == "diamond" ? Addon(0x02000613, 31, "common.nif", cap: 18, flags: 0, sound: 0) : Array.Empty<byte>();
        var unused = mode == "unused-bad-data" ? Record("ADDN", 0x02000614, 0,
            Field("DATA", new byte[3]), Field("DNAM", new byte[4]), Field("MODL", Text("foreign.nif"))) : Array.Empty<byte>();
        return (basis, foreign, Join(Header("ForeignAddon.esm", Plugin), winner, secondary, common, unused, deletedDuplicate,
            Record("SOUN", 0x02000912, 0, Field("EDID", Text("ActualSelfSound")))));
    }

    private static byte[] AddonModelFixture(uint stream, uint[] indices, byte valueFlags = 0, string texture = "textures/root.dds", bool unknownBlock = false,
        string? firstValueMutation = null)
    {
        byte[] NodePayload(int[] children, uint? value, byte flags = 0)
        {
            return Bytes(writer =>
            {
                writer.Write(0); writer.Write(0u); writer.Write(-1);
                if (stream >= 27) writer.Write(14u); else writer.Write((ushort)14);
                foreach (var point in new[] { 43f, -17f, 9f }) writer.Write(point);
                foreach (var scalar in new[] { 0f, -1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f }) writer.Write(scalar);
                writer.Write(1.75f); writer.Write(0u); writer.Write(-1); writer.Write(children.Length);
                foreach (var child in children) writer.Write(child); writer.Write(0u);
                if (value is { } index) { writer.Write(index); writer.Write(flags); }
            });
        }
        var blocks = new List<(string Type, byte[] Payload)> { ("NiNode", NodePayload(Enumerable.Range(1, indices.Length).ToArray(), null)) };
        blocks.AddRange(indices.Select((index, ordinal) => ("BSValueNode", NodePayload([], index, ordinal == 0 ? valueFlags : (byte)0))));
        if (indices.Length != 0 && firstValueMutation is "truncated-value" or "extra-value")
        {
            var first = blocks[1];
            blocks[1] = (first.Type, firstValueMutation == "truncated-value" ? first.Payload[..^1] : [.. first.Payload, 0]);
        }
        if (unknownBlock) blocks.Add(("AuthoredUnknownAddonDependency", [1, 2, 3]));
        blocks.Add(("BSShaderTextureSet", Bytes(writer => { writer.Write(1); writer.Write(texture.Length); writer.Write(Encoding.ASCII.GetBytes(texture)); })));
        return Bytes(writer =>
        {
            void Sized(string value) { writer.Write(value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion); writer.Write(blocks.Count); writer.Write(stream);
            writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); writer.Write((ushort)blocks.Count);
            foreach (var block in blocks) Sized(block.Type); for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Payload.Length); writer.Write(1u); writer.Write(7u); Sized("fixture"); writer.Write(0u);
            foreach (var block in blocks) writer.Write(block.Payload); writer.Write(1u); writer.Write(0);
        });
    }

    private static void AddonDeepDeclarations(string directory)
    {
        const int depth = 1024;
        var game = Path.Combine(directory, "Deep", "Game"); var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var recordsData = new List<byte[]> { Record("TES4", 0, 0, Field("HEDR", header)) };
        WriteTextureIdentityInput(data, "meshes/deep-root.nif", AddonModelFixture(34, [1000u]));
        WriteTextureIdentityInput(data, "textures/root.dds", DdsFixture());
        for (var index = 0; index < depth; index++)
        {
            var model = "chain/" + index + ".nif";
            recordsData.Add(Record("ADDN", 0x2000u + (uint)index, 0, Field("DATA", BitConverter.GetBytes(1000u + (uint)index)),
                Field("DNAM", new byte[4]), Field("MODL", Text(model))));
            WriteTextureIdentityInput(data, "meshes/" + model, AddonModelFixture(34, index == depth - 1 ? [] : [1001u + (uint)index]));
        }
        File.WriteAllBytes(Path.Combine(data, Plugin), Join(recordsData.ToArray()));
        ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "unrelated authored deep-graph input");
        var ini = Path.Combine(game, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
        var before = Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
        using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame, activePlugins: [Plugin], archiveIniPath: ini);
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var resources = new CellGraphAudit.CellAuditResources(source, .1f, records);
        var root = resources.Read("meshes/deep-root.nif", "model"); resources.ReadAddonDeclarations();
        var coverage = JsonSerializer.SerializeToElement(resources.AddonCoverage);
        Require(resources.AddonCatalogPassed && resources.AddonModelRoots.Count == depth && root.AddonLinks.Single().Definition is { Index: 1000 } &&
            resources.Resources.Count == depth + 2 && resources.Resources.Values.All(row => row.Failures.Count == 0) &&
            coverage.GetProperty("visitedAddonIndices").GetInt32() == depth && coverage.GetProperty("decodedValueDeclarations").GetInt32() == depth,
            "The actual authored deep source chain lost edges/resources, exceeded a fabricated nesting limit, or reported guessed inspection counts.");
        var unbound = new CellGraphAudit.CellAuditResources(source, .1f).Read("meshes/deep-root.nif", "model");
        Require(unbound.AddonLinks.Count == 1 && AddonHasLane(unbound.Failures, "nif-addon-source-owner") && unbound.AddonLinks[0].Definition is null,
            "Missing exact source context silently reopened a different/current stack or admitted a guessed index.");
        Require(before.All(item => item.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Key)))) &&
            before.Count == Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Count(), "Deep source inspection changed an authored input.");
    }
}
