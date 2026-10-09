using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private static void ActorAnimationDependencies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-source-actor-animations-" + Guid.NewGuid().ToString("N"));
        var cases = 0;
        try
        {
            foreach (var stream in new[] { 21u, 34u })
                foreach (var mode in new[] { "implicit", "selected-layer", "deleted-npc", "deleted-idle", "deleted-anio", "foreign-master",
                    "missing-idle", "wrong-idle", "duplicate-anio-data", "bad-anio-data", "bad-idle-model", "bad-npc-model",
                    "missing-npc-model", "inherited-model", "leveled-model", "disabled-records", "kffz-empty-middle",
                    "kffz-wrong-extension", "kf-reader-failure", "kf-variant-bad-group", "shared-directory", "absent-anio-data",
                    "absent-idle-model", "zero-anio-data", "non-kf-file" })
                {
                    var caseRoot = Path.Combine(directory, stream + "-" + mode);
                    var game = Path.Combine(caseRoot, "Game"); var data = Path.Combine(game, "Data");
                    var layer = Path.Combine(caseRoot, "SelectedLayer"); Directory.CreateDirectory(data); Directory.CreateDirectory(layer);
                    var plugins = ActorAnimationPlugins(mode);
                    File.WriteAllBytes(Path.Combine(data, Plugin), plugins.Base);
                    File.WriteAllBytes(Path.Combine(data, "ForeignAnimations.esm"), plugins.Foreign);
                    File.WriteAllBytes(Path.Combine(data, "AnimationOverride.esp"), plugins.Override);
                    ActorAnimationArchive(Path.Combine(data, "FalloutNV.bsa"), "meshes/creatures/derived/mtidle.kf", ActorAnimationKf(stream, ["MTIdle"]));
                    var ini = Path.Combine(game, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
                    foreach (var path in new[] { "meshes/box.nif", "meshes/characters/shared/skeleton.nif", "meshes/characters/inherited/skeleton.nif",
                        "meshes/creatures/derived/skeleton.nif", "meshes/characters/_1stperson/skeleton.nif", "meshes/effects/idle-attachment.nif",
                        "meshes/effects/unattached.nif", "meshes/effects/foreign-attachment.nif" })
                        ActorAnimationInput(data, path, NifFixture());
                    foreach (var path in new[] { "meshes/characters/shared/locomotion/male/mtforward.kf", "meshes/characters/shared/locomotion/female/mtforward.kf",
                        "meshes/characters/shared/locomotion/mtidle.kf", "meshes/characters/shared/idle-winner.kf", "meshes/characters/shared/idle-losing.kf",
                        "meshes/characters/foreign/foreign-idle.kf", "meshes/characters/inherited/mtidle.kf", "meshes/characters/_1stperson/mtidle.kf",
                        "meshes/creatures/derived/explicit.kf", "meshes/creatures/derived/later.kf" })
                        ActorAnimationInput(data, path, ActorAnimationKf(stream, [Path.GetFileNameWithoutExtension(path)]));
                    ActorAnimationInput(data, "meshes/characters/shared/h2hattackleft_a.kf", ActorAnimationKf(stream,
                        mode == "kf-variant-bad-group" ? ["Unrelated_a"] : ["AttackLeft_a"]));
                    ActorAnimationInput(data, "meshes/characters/shared/h2hattackleft_b.kf", ActorAnimationKf(stream, ["AttackLeft_b"]));
                    if (mode == "non-kf-file") ActorAnimationInput(data, "meshes/characters/shared/idle-file.nif", ActorAnimationKf(stream, ["actual-file-sequence"]));
                    // A layout failure must remain a resource failure while the
                    // next source sibling still reaches the actual KF reader.
                    if (mode == "kf-reader-failure") ActorAnimationInput(data, "meshes/characters/shared/broken.kf", ActorAnimationKf(99, ["broken"]));
                    if (mode == "selected-layer") ActorAnimationInput(layer, "meshes/creatures/derived/mtidle.kf", ActorAnimationKf(stream, ["MTIdle"], stop: 3.5f));
                    var before = Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories)
                        .Concat(Directory.EnumerateFiles(layer, "*", SearchOption.AllDirectories))
                        .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
                    using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                        mode == "selected-layer" ? [layer] : [], [Plugin, "ForeignAnimations.esm", "AnimationOverride.esp"], ini);
                    using var records = FalloutPluginStack.Load(source.PluginSources);
                    var owner = new CellGraphAudit.SourceActorAnimationDependencies(source, records);
                    var inspection = owner.Read(); Require(ReferenceEquals(inspection, owner.Read()), "Repeated actor source inspection did not retain its exact result/refusal.");
                    var rows = inspection.Records.ToDictionary(row => row.Form, StringComparer.OrdinalIgnoreCase);
                    var npc = rows[Key(0x300).ToString()]; var creature = rows[Key(0x400).ToString()];
                    var idle = rows[Key(0x500).ToString()]; var attachment = rows[Key(0x600).ToString()];
                    Require(inspection.StackId == source.StackId && inspection.SaveCompatibilityId == source.SaveCompatibilityId && !inspection.RuntimeReadiness &&
                        inspection.Directories.All(row => row.GroupEligibility.StartsWith("uninspected", StringComparison.Ordinal)) &&
                        inspection.Records.All(row => row.NativeEligibility.StartsWith("unverified", StringComparison.Ordinal)),
                        "File/declaration coverage falsely admitted an action, actor, native controller or another selection.");
                    Require(inspection.Records.All(row => row.Sha256 is { Length: 64 } && row.Fields.Select(field => field.Ordinal).SequenceEqual(Enumerable.Range(0, row.Fields.Length))) &&
                        inspection.Records.Where(row => row.Deleted).All(row => row.DependencyEdges.Count == 0 && row.Objects.Count == 0 && row.RecordLinks.Count == 0),
                        "Original winner/field ordinals were lost or a winning deletion was substituted by a dependency.");
                    foreach (var row in inspection.Records)
                    {
                        var colon = row.Form.LastIndexOf(':');
                        var form = new FalloutFormKey(row.Form[..colon], uint.Parse(row.Form[(colon + 1)..], System.Globalization.NumberStyles.HexNumber));
                        Require(records.TryGetWinner(form, out var winner) && winner.Plugin.Name == row.Winner &&
                            winner.Flags == row.RecordFlags && Convert.ToHexString(SHA256.HashData(winner.ReadData())).Equals(row.Sha256, StringComparison.OrdinalIgnoreCase) &&
                            winner.ReadSubrecords().Select((field, index) => new CellGraphAudit.ActorAnimationField(index, field.Signature, field.Data.Length,
                                Convert.ToHexString(SHA256.HashData(field.Data.Span)))).SequenceEqual(row.Fields),
                            "Actor animation provenance differs from the independent winning record, raw field extent or hash.");
                    }
                    Require(inspection.Records.Single(row => row.Form == Key(0x302).ToString()).Deleted &&
                        inspection.Records.Single(row => row.Form == Key(0x502).ToString()).Deleted &&
                        inspection.Records.Single(row => row.Form == Key(0x602).ToString()).Deleted,
                        "Unused, deleted actor/idle/attachment winners disappeared from the denominator.");
                    var animation = new CellGraphAudit.ResourceDependency("meshes/creatures/derived/mtidle.kf", "animation");
                    Require(inspection.DependencyEdges.Contains(animation) && creature.ListEntries.All(entry => entry.LogicalPath != animation.Path) &&
                        inspection.DependencyEdges.Contains(new("meshes/characters/_1stperson/mtidle.kf", "animation")),
                        "Actual creature implicit idle or the separate first-person source directory remained absent from the typed union.");
                    if (mode is not ("kffz-empty-middle" or "kffz-wrong-extension"))
                    {
                        var creatureAppearance = FalloutCreatureAppearanceResolver.Resolve(records, Key(0x400));
                        Require(creatureAppearance.AnimationFiles.Select(path => path.Replace('\\', '/').ToLowerInvariant())
                            .SequenceEqual(creature.ListEntries.Select(entry => entry.LogicalPath)), "Independent complete CREA reader disagrees with KFFZ dependency parsing.");
                        Require(FalloutCreatureAppearanceResolver.SelectIdle(creatureAppearance, inspection.DependencyEdges.Where(edge => edge.Kind == "animation").Select(edge => edge.Path)) == animation.Path,
                            "The authored implicit idle is not actually selectable by the current creature source owner.");
                    }
                    else
                    {
                        var refused = false;
                        try { _ = FalloutCreatureAppearanceResolver.Resolve(records, Key(0x400)); }
                        catch (InvalidDataException) { refused = true; }
                        Require(refused, "The independent complete CREA source reader admitted the invalid list that this source inventory refused.");
                    }
                    var healthyRoot = inspection.Directories.Single(row => row.Path == "meshes/creatures/derived");
                    Require(healthyRoot.Files.Contains(animation.Path) && healthyRoot.Files.Contains("meshes/creatures/derived/later.kf") &&
                        inspection.QueryReuse is { ResourceDirectoryQueries: var queries, ResourceDirectoryEntries: var entries } && queries == entries && entries == inspection.Directories.Count,
                        "Distinct immutable directory results/refusals were rescanned, capped or stripped of relative subdirectories.");
                    var expectedRecordFailure = mode is "deleted-idle" or "missing-idle" or "wrong-idle" or "duplicate-anio-data" or "bad-anio-data" or "bad-idle-model" or "bad-npc-model" or
                        "missing-npc-model" or "leveled-model" or "kffz-empty-middle" or "kffz-wrong-extension" or "zero-anio-data";
                    Require(inspection.DeclarationsPassed != expectedRecordFailure, "Actor declaration admission ignored a source refusal or invented one for a valid source alternative.");
                    if (mode == "shared-directory")
                    {
                        var directoryRow = inspection.Directories.Single(row => row.Path == "meshes/characters/shared");
                        Require(directoryRow.DeclarationOwners.Count == 102 && inspection.Directories.Count(row => row.Path == directoryRow.Path) == 1 &&
                            inspection.DependencyEdges.Distinct().Count() == inspection.DependencyEdges.Count,
                            "Many actual source actors copied or erased their shared directory/resource denominator.");
                    }
                    if (mode == "inherited-model") Require(npc.ResolvedModelOwner == Key(0x308).ToString() && npc.Model is null &&
                        rows[Key(0x308).ToString()].Directory == "meshes/characters/inherited" && npc.RecordLinks.Single().Target == Key(0x308).ToString(),
                        "The actual model template owner was replaced by caller model state or an invented selected arm.");
                    if (mode == "leveled-model") Require(npc.ResolvedModelOwner is null && npc.ModelChoice.Contains("refused", StringComparison.Ordinal) &&
                        rows[Key(0x308).ToString()].Directory == "meshes/characters/inherited", "A leveled model requirement lost its finite actor arm or silently selected a state.");
                    if (mode == "disabled-records") Require(npc.RecordFlags == 0x800 && creature.RecordFlags == 0x800 && npc.Directory is not null &&
                        creature.Directory is not null && attachment.DependencyEdges.Count == 1, "An inactive source alternative was dropped or its record flag asserted as native support.");
                    if (mode is "kffz-empty-middle" or "kffz-wrong-extension") Require(creature.ListEntries.Count == 3 &&
                        creature.ListEntries[1].Error is not null && creature.ListEntries[2].LogicalPath == "meshes/creatures/derived/later.kf",
                        "An invalid list entry silently passed, disappeared or suppressed an independent later source dependency.");
                    if (mode == "foreign-master")
                    {
                        var foreignIdle = rows[new FalloutFormKey("ForeignAnimations.esm", 0x500).ToString()];
                        Require(attachment.RecordLinks.Single().Target == foreignIdle.Form && idle.Objects.All(item => item.Form != attachment.Form) &&
                            foreignIdle.Objects.Any(item => item.Form == attachment.Form), "A matching low24 ID replaced the exact declaring plugin's master-scoped idle identity.");
                    }
                    if (mode == "deleted-idle") Require(idle.Deleted && attachment.RecordLinks.Single().Deleted == true && attachment.Failures.Count != 0,
                        "Deleted target IDLE was resurrected or the attachment's failure was hidden.");
                    if (mode == "deleted-anio") Require(attachment.Deleted && idle.Objects.All(item => item.Form != attachment.Form),
                        "Deleted ANIO still entered actual source attachments.");
                    if (mode == "absent-anio-data") Require(attachment.RecordLinks.Count == 0 && idle.Objects.All(item => item.Form != attachment.Form) &&
                        attachment.DependencyEdges.Count == 1, "Source DATA absence was guessed as a join or hid its independent declared model.");
                    if (mode == "absent-idle-model") Require(idle.Model is null && idle.DependencyEdges.Count == 0 && idle.Disposition.StartsWith("encoded-absent", StringComparison.Ordinal),
                        "An absent IDLE model became an invented resource.");
                    if (mode == "non-kf-file") Require(idle.DependencyEdges.Contains(new("meshes/characters/shared/idle-file.nif", "animation")) &&
                        idle.Objects.Any(item => item.Form == attachment.Form), "A real declared IDLE animation file was hidden solely because its source suffix is not KF.");
                    var grouping = rows[Key(0x501).ToString()];
                    Require(grouping.Model == "meshes/characters/shared/idlegroup" && grouping.DependencyEdges.Count == 0 && grouping.Objects.Count == 0,
                        "A non-KF source grouping path was misread as a NIF, animation file or selected action.");
                    var resourceOwner = new CellGraphAudit.CellAuditResources(source, .1f, records);
                    var resourceRows = inspection.DependencyEdges.Select(edge => resourceOwner.Read(edge.Path, edge.Kind)).ToArray();
                    var resourceFailure = mode == "kf-reader-failure";
                    Require(resourceRows.Any(row => row.Failures.Count != 0) == resourceFailure &&
                        resourceRows.Single(row => row.Path == "meshes/creatures/derived/later.kf").DecodedBlocks == 1 &&
                        resourceRows.Where(row => row.Path.EndsWith(".kf", StringComparison.Ordinal)).All(row => row.InspectionRoles.Contains("animation")),
                        "Typed KF role, independent sibling decode or exact layout failure disappeared at the actual resource owner.");
                    var actualIdle = resourceRows.Single(row => row.Path == animation.Path);
                    Require(actualIdle.Sha256 is { Length: 64 } && actualIdle.Source is not null &&
                        (mode == "selected-layer" ? actualIdle.Source.StartsWith(layer, StringComparison.OrdinalIgnoreCase) : actualIdle.Source.Contains("FalloutNV.bsa::", StringComparison.OrdinalIgnoreCase)),
                        "Winning loose/BSA/selected-layer identity was replaced by an unrelated filename or source stack.");
                    if (mode is "implicit" or "kf-variant-bad-group")
                    {
                        IReadOnlyList<string>? variants = null; Exception? variantFailure = null;
                        try { variants = source.ActorAnimations.Variants("meshes/characters/shared", "h2hattackleft"); }
                        catch (InvalidDataException error) { variantFailure = error; }
                        Require(mode == "kf-variant-bad-group" ? variantFailure is not null && variants is null : variantFailure is null && variants is not null &&
                            variants.SequenceEqual(new[] { "meshes/characters/shared/h2hattackleft_a.kf", "meshes/characters/shared/h2hattackleft_b.kf" }),
                            "The actual variant owner lost source suffixes or accepted incompatible exported groups.");
                        if (mode == "implicit")
                        {
                            try { _ = source.ActorAnimations.Find("meshes/characters/shared", "h2hattackleft"); throw new Exception("Ambiguous actual source group was arbitrarily selected."); }
                            catch (NotSupportedException) { }
                        }
                    }
                    // An unbound/foreign stack must never acquire the exact
                    // content selection by reopening an ambient Current source.
                    using (var foreign = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                        mode == "selected-layer" ? [layer] : [], [Plugin, "ForeignAnimations.esm", "AnimationOverride.esp"], ini))
                    using (var foreignRecords = FalloutPluginStack.Load(foreign.PluginSources))
                    {
                        try { _ = new CellGraphAudit.SourceActorAnimationDependencies(source, foreignRecords); throw new Exception("Foreign actor-animation stack was accepted."); }
                        catch (InvalidDataException) { }
                    }
                    if (stream == 34 && mode == "kf-reader-failure") ActorAnimationComponent(source, records, directory);
                    Require(before.All(item => item.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Key)))) &&
                        before.Count == Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Count() + Directory.EnumerateFiles(layer, "*", SearchOption.AllDirectories).Count(),
                        "Actor source dependency inspection changed an authored input.");
                    cases++;
                }
            Console.WriteLine($"OPENNV_CELL_ACTOR_ANIMATION_DEPENDENCIES_PASS cases={cases} streams=21,34 source=read-only runtime=unverified group-eligibility=uninspected dynamic-paths=unknown");
        }
        finally
        {
            var absolute = Path.GetFullPath(directory); var temporary = Path.GetFullPath(Path.GetTempPath());
            if (!absolute.StartsWith(temporary.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Authored animation fixture cleanup left its own temporary root.");
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
        }
    }

    private static (byte[] Base, byte[] Foreign, byte[] Override) ActorAnimationPlugins(string mode)
    {
        byte[] Header(params string[] masters)
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            return Record("TES4", 0, 0, [Field("HEDR", header), .. masters.SelectMany(master => new[] { Field("MAST", Text(master)), Field("DATA", new byte[8]) })]);
        }
        byte[] Actor(string signature, uint form, string? model, ushort templateMask = 0, uint? template = null, uint flags = 0, byte[]? animations = null)
        {
            var acbs = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(22), templateMask);
            var fields = new List<byte[]> { Field("ACBS", acbs) };
            if (model is not null) fields.Add(Field("MODL", Text(model)));
            if (template is { } target) fields.Add(Field("TPLT", BitConverter.GetBytes(target)));
            if (signature == "CREA") { fields.Add(Field("BNAM", BitConverter.GetBytes(1f))); fields.Add(Field("KFFZ", animations ?? Text("explicit.kf"))); }
            return Record(signature, form, flags, fields.ToArray());
        }
        byte[] Idle(uint form, string? path, uint flags = 0) => Record("IDLE", form, flags,
            path is null ? [Field("DATA", new byte[6]), Field("ANAM", new byte[8])] :
            [Field("MODL", Text(path)), Field("DATA", new byte[6]), Field("ANAM", new byte[8])]);
        byte[] Object(uint form, byte[]? identity, string model, uint flags = 0)
        {
            var fields = new List<byte[]> { Field("MODL", Text(model)) };
            if (identity is not null) fields.Add(Field("DATA", identity));
            if (mode == "duplicate-anio-data" && form == 0x01000600) fields.Add(Field("DATA", BitConverter.GetBytes(0x01000500u)));
            return Record("ANIO", form, flags, fields.ToArray());
        }
        var animations = mode == "kffz-empty-middle" ? Join(Text("explicit.kf"), [0], Text("later.kf")) :
            mode == "kffz-wrong-extension" ? Join(Text("explicit.kf"), Text("bad.nif"), Text("later.kf")) : Text("explicit.kf");
        var npcModel = mode == "bad-npc-model" ? "../skeleton.nif" : mode is "missing-npc-model" or "inherited-model" or "leveled-model" ? null : "characters/shared/skeleton.nif";
        var disabled = mode == "disabled-records" ? 0x800u : 0;
        var masterRows = new List<byte[]>
        {
            Header(), Record("STAT", 0x700, 0, Field("MODL", Text("box.nif"))), Cell(0x800, "AnimationHub"), Group(0x800, Reference(0x810, 0x700)),
            Actor("NPC_", 0x300, npcModel, mode is "inherited-model" or "leveled-model" ? (ushort)64 : (ushort)0,
                mode == "inherited-model" ? 0x308u : mode == "leveled-model" ? 0x309u : null, disabled),
            Actor("NPC_", 0x301, "characters/shared/skeleton.nif"), Actor("NPC_", 0x308, "characters/inherited/skeleton.nif"),
            Actor("CREA", 0x400, "creatures/derived/skeleton.nif", flags: disabled, animations: animations),
            Idle(0x500, "characters/shared/idle-losing.kf"), Idle(0x501, "characters/shared/IdleGroup"),
            Object(0x600, BitConverter.GetBytes(0x500u), "effects/idle-attachment.nif"),
            Object(0x601, null, "effects/unattached.nif"), Actor("NPC_", 0x302, "not-read.nif", flags: 0x20),
            Idle(0x502, "not-read.kf", 0x20), Object(0x602, BitConverter.GetBytes(0x500u), "not-read.nif", 0x20)
        };
        if (mode == "shared-directory")
            for (var index = 0; index < 100; index++) masterRows.Add(Actor("NPC_", 0x1000u + (uint)index, "characters/shared/skeleton.nif"));
        if (mode == "leveled-model")
        {
            var first = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(first.AsSpan(4), 0x301);
            var second = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(second.AsSpan(4), 0x308);
            BinaryPrimitives.WriteUInt16LittleEndian(first.AsSpan(8), 1); BinaryPrimitives.WriteUInt16LittleEndian(second.AsSpan(8), 1);
            masterRows.Add(Record("LVLN", 0x309, 0, Field("LVLD", [0]), Field("LVLF", [1]), Field("LVLO", first), Field("LVLO", second)));
        }
        var foreign = Join(Header(), Idle(0x500, "characters/foreign/foreign-idle.kf"),
            Object(0x600, BitConverter.GetBytes(0x500u), "effects/foreign-attachment.nif"));
        var idleWinner = mode == "deleted-idle" ? Record("IDLE", 0x01000500, 0x20) :
            Idle(0x01000500, mode == "absent-idle-model" ? null : mode == "bad-idle-model" ? "../escape.kf" : mode == "non-kf-file" ? "characters/shared/idle-file.nif" : "characters/shared/idle-winner.kf");
        var identity = mode == "bad-anio-data" ? new byte[3] : mode == "absent-anio-data" ? null : BitConverter.GetBytes(
            mode == "foreign-master" ? 0x00000500u : mode == "missing-idle" ? 0x010005ffu : mode == "wrong-idle" ? 0x01000301u : mode == "zero-anio-data" ? 0u : 0x01000500u);
        var anioWinner = mode == "deleted-anio" ? Record("ANIO", 0x01000600, 0x20) : Object(0x01000600, identity, "effects/idle-attachment.nif", disabled);
        var overrideRows = new List<byte[]> { Header("ForeignAnimations.esm", Plugin), idleWinner, anioWinner };
        if (mode == "deleted-npc") overrideRows.Add(Record("NPC_", 0x01000300, 0x20));
        return (Join(masterRows.ToArray()), foreign, Join(overrideRows.ToArray()));
    }

    private static byte[] ActorAnimationKf(uint stream, string[] groups, float stop = 1)
    {
        var blocks = groups.Select((_, index) => Bytes(writer =>
        {
            writer.Write(index); writer.Write(0u); writer.Write(0u); writer.Write(1f); writer.Write(-1);
            writer.Write(0u); writer.Write(1f); writer.Write(0f); writer.Write(stop); writer.Write(-1); writer.Write(index);
            if (stream >= 24 && stream <= 28) writer.Write(-1); else if (stream > 28) writer.Write((ushort)0);
        })).ToArray();
        return Bytes(writer =>
        {
            void Sized(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion); writer.Write(blocks.Length);
            writer.Write(stream); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); writer.Write((ushort)1); Sized("NiControllerSequence");
            foreach (var block in blocks) writer.Write((ushort)0); foreach (var block in blocks) writer.Write(block.Length);
            writer.Write((uint)groups.Length); writer.Write((uint)groups.Max(Encoding.UTF8.GetByteCount)); foreach (var group in groups) Sized(group);
            writer.Write(0u); foreach (var block in blocks) writer.Write(block);
            writer.Write((uint)blocks.Length); for (var index = 0; index < blocks.Length; index++) writer.Write(index);
        });
    }

    private static void ActorAnimationInput(string root, string logical, byte[] bytes)
    {
        var path = Path.Combine(root, logical.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }

    private static void ActorAnimationArchive(string path, string logical, byte[] payload)
    {
        var split = logical.LastIndexOf('/'); var folder = Encoding.ASCII.GetBytes(logical[..split].Replace('/', '\\') + '\0');
        var name = Encoding.ASCII.GetBytes(logical[(split + 1)..] + '\0');
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x00415342u); writer.Write(104u); writer.Write(36u); writer.Write(3u);
        writer.Write(1u); writer.Write(1u); writer.Write((uint)folder.Length); writer.Write((uint)name.Length); writer.Write(16u);
        writer.Write(0ul); writer.Write(1u); writer.Write((uint)(52 + name.Length)); writer.Write((byte)folder.Length); writer.Write(folder);
        writer.Write(0ul); writer.Write((uint)payload.Length); writer.Write((uint)(52 + 1 + folder.Length + 16 + name.Length)); writer.Write(name); writer.Write(payload);
    }

    private static void ActorAnimationComponent(RuntimeLiveContentSource source, FalloutPluginStack records, string directory)
    {
        var configuration = Path.Combine(directory, "actor-animation-component-runtime.json");
        File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
        var options = CellGraphAudit.ParseOptions([Path.GetDirectoryName(source.ContentRoot)!, Path.Combine(directory, "actor-animation-component-output"), "--runtime-config", configuration]);
        Directory.CreateDirectory(options.Output);
        Require(!CellGraphAudit.RunComponent(source, records, options, .1f, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(configuration)))),
            "Animation source union unexpectedly admitted native scene readiness.");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.Output, "component.private.json")));
        var report = document.RootElement; var alternatives = report.GetProperty("alternatives");
        var resources = alternatives.GetProperty("resources").EnumerateArray().ToArray();
        Require(resources.Any(row => row.GetProperty("path").GetString() == "meshes/characters/shared/broken.kf") &&
            resources.Any(row => row.GetProperty("path").GetString() == "meshes/creatures/derived/mtidle.kf"),
            "The actual complete-source resource ledger omitted an implicit KF layout refusal or an existing creature archive idle not declared in KFFZ.");
        var actorAnimations = alternatives.GetProperty("actorAnimations");
        Require(actorAnimations.GetProperty("records").EnumerateArray().Any(row => row.GetProperty("form").GetString() == Key(0x300).ToString()) &&
            resources.Single(row => row.GetProperty("path").GetString() == "meshes/characters/shared/broken.kf").GetProperty("failures").GetArrayLength() != 0 &&
            resources.Single(row => row.GetProperty("path").GetString() == "meshes/creatures/derived/mtidle.kf").GetProperty("source").GetString()!.Contains("FalloutNV.bsa::", StringComparison.OrdinalIgnoreCase) &&
            !report.GetProperty("coverage").GetProperty("sourceAccountingPassed").GetBoolean() && !report.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean(),
            "Unplaced/inactive actor declaration, implicit KF archive identity or its actual decoder failure remained outside the existing complete-source gate.");
    }
}
