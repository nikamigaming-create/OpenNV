using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private static void NifControllerLinks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-controller-links-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game"); var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(Path.Combine(data, "meshes"));
        try
        {
            File.WriteAllBytes(Path.Combine(data, Plugin), PluginFixture());
            ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "unrelated authored member");
            var archiveIni = Path.Combine(game, "Archives.ini");
            File.WriteAllText(archiveIni, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            string[] names = ["valid", "valid-compact", "transform-data-wrong", "float-data-wrong", "bool-data-wrong", "point-data-wrong",
                "null-keyed", "null-text-keys", "text-keys-wrong", "manager-sequence-wrong", "manager-palette-wrong",
                "manager-next-wrong", "active-spline-both-wrong", "inactive-spline-untyped", "required-spline-null",
                "out-of-range", "invalid-negative", "unsupported-data-target", "malformed-data-target", "spline-handle-out-of-range", "float-tbc-owner-refusal"];
            var authored = names.ToDictionary(name => name, ControllerLinkNif);
            foreach (var (name, bytes) in authored) File.WriteAllBytes(Path.Combine(data, "meshes", name + ".kf"), bytes);
            var before = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
            using (var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame, [], [Plugin], archiveIni))
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            {
                source.ArchiveWarmup.GetAwaiter().GetResult();
                Require(ReferenceEquals(records.OwnedSource, source), "Controller link fixture uses unrelated source records.");
                var resources = new CellGraphAudit.CellAuditResources(source, .1f, records);

                // Regression before any new metadata-owner call: the original
                // complete reader decodes NiNode, but the real pure transform
                // owner refuses that actual Data link in the selected file.
                var wrongNif = FalloutNifFile.Read(authored["transform-data-wrong"]);
                var independentlyDecoded = wrongNif.Blocks.Select(block => wrongNif.ReadObject(block.Index)).ToArray();
                Require(wrongNif.ReadObject(0) is FalloutNifNode &&
                    ((FalloutNifTransformInterpolator)wrongNif.ReadObject(3)).Data == 0 &&
                    independentlyDecoded.Length == 21,
                    "The negative did not reach a completely decoded in-range wrong-type target.");
                var transformError = ControllerInputError(wrongNif, 3);
                Require(transformError.Length != 0, "Actual transform sampler accepted the independent wrong target.");
                var wrong = resources.Read("meshes/transform-data-wrong.kf", "animation");
                Require(wrong.Failures.Any(value => ControllerFailure(value, "nif-controller-input-owner", 3, transformError)),
                    "A completely decoded transform Data link to NiNode has no actual input-owner refusal in the resource audit.");

                var validNif = FalloutNifFile.Read(authored["valid"]);
                var valid = resources.Read("meshes/valid.kf", "animation");
                Require(valid.Blocks == 21 && valid.DecodedBlocks == 21 && valid.Failures.Count == 0,
                    "Complete authored controller sequences, palettes, keyed/spline/path inputs did not decode independently.");
                RequireControllerDeclarations(valid, validNif);
                var receipts = ControllerReceipts(valid);
                Require(receipts.Select(receipt => receipt.Block).SequenceEqual([3, 5, 7, 9, 11, 18, 19, 20]) &&
                    receipts.All(receipt => receipt.Disposition == "pure-input-constructor-admitted" && receipt.Error is null),
                    "The audit did not invoke every actual transform/float/bool/point/spline/path input constructor.");
                var channels = valid.DependencyDeclarations.OfType<CellGraphAudit.ControllerChannelDeclaration>().ToArray();
                Require(channels.Length == 2 && channels[0].Ordinal == 0 && channels[1].ControllerType == "AuthoredUnsupportedController" &&
                    channels[1].NodeName == "Literal/Target With Spaces" && channels[1].Variable1 == "textures/declaration.dds" &&
                    channels.All(channel => channel.TargetBinding.StartsWith("uninspected", StringComparison.Ordinal) &&
                        channel.ExternalResource == "none inferred from names" && channel.Timeline == "uninspected" && channel.NativeAdmission == "unverified") &&
                    valid.Dependencies.Count == 0 && valid.DependencyEdges.Count == 0,
                    "Named targets became guessed files or unsupported channels acquired native/clock bindings.");
                Require(ControllerLinks(valid).Single(link => link.Block == 1 && link.Field == "Manager").TypedDisposition == "uninspected" &&
                    ControllerLinks(valid).Where(link => link.Field is "Time.Target" or "ExtraTargets" or "Objects.Object")
                        .All(link => link.TypedDisposition == "uninspected") &&
                    ControllerLinks(valid).Single(link => link.Block == 16 && link.Field == "Objects.Object").DeclaredName == "Authored target" &&
                    ControllerLinks(valid).All(link => link.Field != "AnimationNotes"),
                    "Decoded controller targets borrowed a guessed universal class or instance ownership rule.");
                var declarationCount = valid.DependencyDeclarations.Count;
                Require(ReferenceEquals(resources.Read("MESHES\\VALID.KF", "animation"), valid) &&
                    valid.DependencyDeclarations.Count == declarationCount,
                    "Case lookup duplicated controller declarations or pure constructor receipts.");
                var compact = resources.Read("meshes/valid-compact.kf", "animation");
                RequireControllerDeclarations(compact, FalloutNifFile.Read(authored["valid-compact"]));
                Require(compact.Failures.Count == 0 && compact.DecodedBlocks == 21 &&
                    ControllerReceipts(compact).All(receipt => receipt.Error is null) &&
                    ControllerReceipts(compact).Where(receipt => receipt.Block is 11 or 18 or 19)
                        .All(receipt => receipt.Type.StartsWith("NiBSplineComp", StringComparison.Ordinal)),
                    "Original compact spline layouts or their exact typed input constructors were artificially excluded.");

                foreach (var (name, block) in new[] { ("transform-data-wrong", 3), ("float-data-wrong", 5),
                    ("bool-data-wrong", 7), ("point-data-wrong", 9) })
                {
                    var nif = FalloutNifFile.Read(authored[name]); var error = ControllerInputError(nif, block);
                    var row = resources.Read("meshes/" + name + ".kf", "animation");
                    RequireControllerDeclarations(row, nif);
                    var link = ControllerLinks(row).Single(value => value.Block == block && value.Field == "Data");
                    Require(error.Length != 0 && row.DecodedBlocks == 21 && link.TargetBlock == 0 && link.TargetType == "NiNode" &&
                        link.DecodeDisposition == "target-block-decoded" && link.TypedDisposition == "consumer-link-refused" && link.Error == error &&
                        row.Failures.Any(value => ControllerFailure(value, "nif-controller-link", block, error)) &&
                        row.Failures.Any(value => ControllerFailure(value, "nif-controller-input-owner", block, error)) &&
                        ControllerReceipts(row).Single(value => value.Block == block).Error == error &&
                        ControllerReceipts(row).Where(value => value.Block != block).All(value => value.Error is null),
                        "A typed input refusal was changed, lost or used to conceal later valid siblings.");
                }

                var nullKeyed = resources.Read("meshes/null-keyed.kf", "animation");
                RequireControllerDeclarations(nullKeyed, FalloutNifFile.Read(authored["null-keyed"]));
                var absentInputs = ControllerLinks(nullKeyed).Where(link => link.Field == "Data" && new[] { 3, 5, 7, 9 }.Contains(link.Block)).ToArray();
                Require(nullKeyed.Failures.Count == 0 && ControllerReceipts(nullKeyed).All(receipt => receipt.Error is null) &&
                    absentInputs.Length == 4 && absentInputs.All(link => link.TargetBlock == -1 && link.TargetType is null && link.TargetOffset is null && link.TargetBytes is null &&
                            link.DecodeDisposition == "encoded-null" && link.TypedDisposition == "encoded-absent; no target substituted"),
                    "Authored constant/keyless inputs acquired a fabricated data target or a new format restriction.");
                var nullText = resources.Read("meshes/null-text-keys.kf", "animation");
                Require(nullText.Failures.Count == 0 && ControllerLinks(nullText).Single(link => link.Block == 1 && link.Field == "TextKeys")
                    .TypedDisposition == "encoded-absent; no target substituted",
                    "The KF consumer's nullable text-key declaration became an invented managed-event requirement.");

                foreach (var (name, block, field, ordinal) in new[]
                {
                    ("text-keys-wrong", 1, "TextKeys", (int?)null),
                    ("manager-sequence-wrong", 14, "Sequences", (int?)0),
                    ("manager-palette-wrong", 14, "ObjectPalette", (int?)null),
                    ("manager-next-wrong", 14, "Time.NextController", (int?)null),
                })
                {
                    var nif = FalloutNifFile.Read(authored[name]); var row = resources.Read("meshes/" + name + ".kf", "animation");
                    RequireControllerDeclarations(row, nif);
                    var link = ControllerLinks(row).Single(value => value.Block == block && value.Field == field && value.Ordinal == ordinal);
                    Require(link.TargetBlock == 0 && link.TargetType == "NiNode" && link.DecodeDisposition == "target-block-decoded" &&
                        link.TypedDisposition == "consumer-link-refused" && !string.IsNullOrEmpty(link.Error) &&
                        row.Failures.Any(value => ControllerFailure(value, "nif-controller-link", block, link.Error!)) &&
                        ControllerLinks(row).Single(value => value.Block == 17 && value.Field == "TextKeys").Error is null &&
                        ControllerReceipts(row).All(value => value.Error is null),
                        "A decoded sequence/manager target refusal lost provenance or concealed an independent valid sequence.");
                    if (name == "manager-sequence-wrong")
                    {
                        string? original = null;
                        try { _ = nif.ReadControllerSequence(0); } catch (Exception error) { original = error.Message; }
                        Require(link.Error == original && ControllerLinks(row).Single(value => value.Block == 14 && value.Field == "Sequences" &&
                            value.Ordinal == 1).TypedDisposition.StartsWith("typed-consumer-target-admitted", StringComparison.Ordinal),
                            "Manager sequence slots no longer use the actual typed reader independently.");
                    }
                }

                var both = resources.Read("meshes/active-spline-both-wrong.kf", "animation");
                var bothLinks = ControllerLinks(both).Where(link => link.Block == 11 && link.Field is "Data" or "BasisData").ToArray();
                Require(bothLinks.Select(link => link.Field).SequenceEqual(["Data", "BasisData"]) &&
                    bothLinks.All(link => link.TargetBlock == 0 && link.TypedDisposition == "consumer-link-refused" && !string.IsNullOrEmpty(link.Error)) &&
                    ControllerReceipts(both).Single(receipt => receipt.Block == 11).Error ==
                        ControllerInputError(FalloutNifFile.Read(authored["active-spline-both-wrong"]), 11),
                    "An early spline-data failure concealed the separate basis declaration.");
                var inactive = resources.Read("meshes/inactive-spline-untyped.kf", "animation");
                var ignoredLinks = ControllerLinks(inactive).Where(link => link.Block == 11 && link.Field is "Data" or "BasisData").ToArray();
                Require(inactive.Failures.Count == 0 && ControllerReceipts(inactive).All(receipt => receipt.Error is null) &&
                    ignoredLinks.Select(link => link.Field).SequenceEqual(["Data", "BasisData"]) &&
                    ignoredLinks.All(link => link.TargetBlock == 0 && link.TargetType == "NiNode" && link.DecodeDisposition == "target-block-decoded" &&
                            link.TypedDisposition == "uninspected" && link.TypedOwner!.Contains("no active source handles", StringComparison.Ordinal)),
                    "Ignored inactive spline references acquired a guessed typed requirement or a native success claim.");
                var missingSpline = resources.Read("meshes/required-spline-null.kf", "animation");
                var missingError = ControllerInputError(FalloutNifFile.Read(authored["required-spline-null"]), 11);
                Require(missingError.Length != 0 && ControllerLinks(missingSpline).Single(link => link.Block == 11 && link.Field == "Data")
                    .Error == missingError && ControllerReceipts(missingSpline).Single(receipt => receipt.Block == 11).Error == missingError,
                    "A required active spline input substituted a default for the actual null-reader refusal.");
                foreach (var (name, block) in new[] { ("spline-handle-out-of-range", 11), ("float-tbc-owner-refusal", 5) })
                {
                    var nif = FalloutNifFile.Read(authored[name]); var error = ControllerInputError(nif, block);
                    var row = resources.Read("meshes/" + name + ".kf", "animation");
                    RequireControllerDeclarations(row, nif);
                    Require(error.Length != 0 && row.DecodedBlocks == 21 &&
                        ControllerLinks(row).Where(link => link.Block == block).All(link => link.Error is null) &&
                        ControllerReceipts(row).Single(receipt => receipt.Block == block).Error == error &&
                        row.Failures.Any(value => ControllerFailure(value, "nif-controller-input-owner", block, error)) &&
                        ControllerReceipts(row).Where(receipt => receipt.Block is 7 or 9 or 18 or 19).All(receipt => receipt.Error is null),
                        "A typed target class alone certified an invalid handle extent or unsupported actual interpolation owner.");
                }

                foreach (var name in new[] { "out-of-range", "invalid-negative" })
                {
                    var nif = FalloutNifFile.Read(authored[name]); string? original = null;
                    try { _ = nif.ReadObject(3); } catch (Exception error) { original = error.Message; }
                    var row = resources.Read("meshes/" + name + ".kf", "animation");
                    Require(original is not null && row.DecodedBlocks == 20 &&
                        row.Failures.Any(value => ControllerFailure(value, "nif-block", 3, original!)) &&
                        ControllerLinks(row).All(link => link.Block != 3) && ControllerReceipts(row).All(receipt => receipt.Block != 3) &&
                        ControllerReceipts(row).Select(receipt => receipt.Block).SequenceEqual([5, 7, 9, 11, 18, 19, 20]),
                        "An undecoded controller acquired fabricated typed links or lost later complete inputs.");
                }
                foreach (var name in new[] { "unsupported-data-target", "malformed-data-target" })
                {
                    var nif = FalloutNifFile.Read(authored[name]); var error = ControllerInputError(nif, 3);
                    var row = resources.Read("meshes/" + name + ".kf", "animation");
                    Require(error.Length != 0 && row.DecodedBlocks == 20 &&
                        row.Failures.Any(value => ControllerFailure(value, "nif-block", 4, error)) &&
                        ControllerLinks(row).Single(link => link.Block == 3 && link.Field == "Data").Error == error &&
                        ControllerReceipts(row).Single(receipt => receipt.Block == 3).Error == error &&
                        ControllerReceipts(row).Where(receipt => receipt.Block != 3).All(receipt => receipt.Error is null),
                        "Original unsupported/malformed target extents were accepted or their valid siblings were omitted.");
                }
            }
            Require(before.All(pair => File.Exists(pair.Key) &&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value),
                "Controller declaration inspection changed authored original files.");
            Console.WriteLine("OPENNV_CELL_NIF_CONTROLLER_LINK_PASS fullReader=true actualPureOwners=true independentSiblings=true native=false timeline=false");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static CellGraphAudit.ControllerLinkDeclaration[] ControllerLinks(CellGraphAudit.ResourceRow row) =>
        row.DependencyDeclarations.OfType<CellGraphAudit.ControllerLinkDeclaration>().ToArray();
    private static CellGraphAudit.ControllerInputReceipt[] ControllerReceipts(CellGraphAudit.ResourceRow row) =>
        row.DependencyDeclarations.OfType<CellGraphAudit.ControllerInputReceipt>().ToArray();

    private static void RequireControllerDeclarations(CellGraphAudit.ResourceRow row, FalloutNifFile nif)
    {
        var links = ControllerLinks(row);
        Require(links.Length == 30 && links.Select(link => (link.Block, link.Field, link.Ordinal)).Distinct().Count() == 30,
            "The complete authored source reference denominator was omitted, duplicated or padded with nonencoded defaults.");
        foreach (var link in links)
        {
            var block = nif.Blocks[link.Block];
            Require(link.SourceSha256 == nif.Sha256 && link.Type == block.TypeName && link.Offset == block.Offset && link.Bytes == block.Size &&
                link.Activation == "uninspected" && link.Interval == "uninspected" && link.Timeline == "uninspected" && link.NativeAdmission == "unverified",
                "A link lost original block extents, exact NIF identity or the unknown activation/native boundaries.");
            if (link.TargetBlock == -1)
                Require(link.TargetType is null && link.TargetOffset is null && link.TargetBytes is null,
                    "An encoded absent controller link acquired an invented target.");
            else
            {
                var target = nif.Blocks[link.TargetBlock];
                Require(link.TargetType == target.TypeName && link.TargetOffset == target.Offset && link.TargetBytes == target.Size,
                    "A controller link lost its exact referenced block extent or type.");
            }
        }
        foreach (var receipt in ControllerReceipts(row))
        {
            var block = nif.Blocks[receipt.Block];
            Require(receipt.SourceSha256 == nif.Sha256 && receipt.Type == block.TypeName && receipt.Offset == block.Offset && receipt.Bytes == block.Size &&
                receipt.Sampling == "not performed" && receipt.Activation == "uninspected" && receipt.Interval == "uninspected" &&
                receipt.Timeline == "uninspected" && receipt.NativeAdmission == "unverified",
                "Pure constructor admission became a sampled interval or native animation claim.");
        }
    }

    private static bool ControllerFailure(object failure, string lane, int block, string error)
    {
        var value = JsonSerializer.SerializeToElement(failure);
        return value.TryGetProperty("lane", out var laneValue) && laneValue.GetString() == lane &&
            value.TryGetProperty("block", out var blockValue) && blockValue.GetInt32() == block &&
            value.TryGetProperty("error", out var errorValue) && errorValue.GetString() == error;
    }

    private static string ControllerInputError(FalloutNifFile source, int block)
    {
        try
        {
            switch (block)
            {
                case 3 or 11 or 20: _ = new FalloutNifAnimationSampler(source, block); break;
                case 5 or 19: _ = new FalloutNifFloatAnimation(source, block); break;
                case 7: _ = new FalloutNifBoolAnimation(source, block); break;
                case 9 or 18: _ = new FalloutNifPoint3Animation(source, block); break;
                default: throw new ArgumentOutOfRangeException(nameof(block));
            }
        }
        catch (Exception error) { return error.Message; }
        return "";
    }

    private static byte[] ControllerLinkNif(string kind)
    {
        int Data(int block, int normal) => kind == "null-keyed" ||
            kind == "transform-data-wrong" && block == 3 || kind == "float-data-wrong" && block == 5 ||
            kind == "bool-data-wrong" && block == 7 || kind == "point-data-wrong" && block == 9
                ? kind == "null-keyed" ? -1 : 0 : block == 3 && kind == "out-of-range" ? 21 :
                    block == 3 && kind == "invalid-negative" ? -2 : normal;
        string[] strings = ["Authored sequence", "Authored keys", "start", "end", "Authored target", "", "NiTransformController",
            "Authored independent sequence", "Literal/Target With Spaces", "AuthoredUnsupportedController", "textures/declaration.dds"];
        byte[] Sequence(bool first) => Bytes(writer =>
        {
            writer.Write(first ? 0 : 7); writer.Write(1U); writer.Write(0U);
            writer.Write(3); writer.Write(15); writer.Write((byte)0);
            writer.Write(first ? 4 : 8); writer.Write(5); writer.Write(first ? 6 : 9); writer.Write(first ? 5 : 10); writer.Write(5);
            writer.Write(1f); writer.Write(first && kind == "text-keys-wrong" ? 0 : first && kind == "null-text-keys" ? -1 : 2);
            writer.Write(0U); writer.Write(1f); writer.Write(0f); writer.Write(1f); writer.Write(14); writer.Write(4); writer.Write((ushort)0);
        });
        void Vector(BinaryWriter writer, float x, float y, float z) { writer.Write(x); writer.Write(y); writer.Write(z); }
        void Clock(BinaryWriter writer, int next)
        {
            writer.Write(next); writer.Write((ushort)0x40); writer.Write(1f); writer.Write(0f);
            writer.Write(float.MaxValue); writer.Write(float.MinValue); writer.Write(0);
        }
        var inactive = kind == "inactive-spline-untyped";
        var compact = kind == "valid-compact";
        var blocks = new (string Type, byte[] Payload)[]
        {
            ("NiNode", Node([0, 0, 0], 1, -1, [])),
            ("NiControllerSequence", Sequence(first: true)),
            ("NiTextKeyExtraData", Bytes(writer => { writer.Write(1); writer.Write(2U); writer.Write(0f); writer.Write(2); writer.Write(1f); writer.Write(3); })),
            ("NiTransformInterpolator", Bytes(writer =>
            {
                Vector(writer, 1, 2, 3); writer.Write(1f); writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(Data(3, 4));
            })),
            (kind == "unsupported-data-target" ? "AuthoredUnknownControllerData" : "NiTransformData", Bytes(writer =>
            {
                if (kind == "unsupported-data-target") { writer.Write(new byte[] { 13, 17, 23 }); return; }
                if (kind == "malformed-data-target") { writer.Write(1U); return; }
                writer.Write(0U); writer.Write(0U); writer.Write(0U);
            })),
            ("NiFloatInterpolator", Bytes(writer => { writer.Write(4.25f); writer.Write(Data(5, 6)); })),
            ("NiFloatData", Bytes(writer =>
            {
                writer.Write(2U); writer.Write(kind == "float-tbc-owner-refusal" ? 3U : 1U);
                writer.Write(0f); writer.Write(0f); if (kind == "float-tbc-owner-refusal") Vector(writer, 0, 0, 0);
                writer.Write(1f); writer.Write(1f); if (kind == "float-tbc-owner-refusal") Vector(writer, 0, 0, 0);
            })),
            ("NiBoolInterpolator", Bytes(writer => { writer.Write((byte)1); writer.Write(Data(7, 8)); })),
            ("NiBoolData", Bytes(writer => { writer.Write(2U); writer.Write(1U); writer.Write(0f); writer.Write((byte)0); writer.Write(1f); writer.Write((byte)1); })),
            ("NiPoint3Interpolator", Bytes(writer => { Vector(writer, 1, 2, 3); writer.Write(Data(9, 10)); })),
            ("NiPosData", Bytes(writer => { writer.Write(2U); writer.Write(1U); writer.Write(0f); Vector(writer, 0, 0, 0); writer.Write(1f); Vector(writer, 3, 2, 1); })),
            (compact ? "NiBSplineCompTransformInterpolator" : "NiBSplineTransformInterpolator", Bytes(writer =>
            {
                writer.Write(0f); writer.Write(1f);
                writer.Write(kind == "active-spline-both-wrong" || inactive ? 0 : kind == "required-spline-null" ? -1 : 12);
                writer.Write(kind == "active-spline-both-wrong" || inactive ? 0 : 13);
                Vector(writer, 1, 2, 3); writer.Write(1f); writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
                writer.Write(inactive ? (uint)ushort.MaxValue : kind == "spline-handle-out-of-range" ? 1U : 0U);
                writer.Write((uint)ushort.MaxValue); writer.Write((uint)ushort.MaxValue);
                if (compact) for (var component = 0; component < 3; component++) { writer.Write(0f); writer.Write(1f); }
            })),
            ("NiBSplineData", Bytes(writer =>
            {
                writer.Write(compact ? 0U : 12U);
                if (!compact) foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 2, 0, 0, 3, 0, 0 }) writer.Write(value);
                writer.Write(compact ? 12U : 0U); if (compact) for (short value = 0; value < 12; value++) writer.Write(value);
            })),
            ("NiBSplineBasisData", Bytes(writer => writer.Write(4U))),
            ("NiControllerManager", Bytes(writer =>
            {
                Clock(writer, kind == "manager-next-wrong" ? 0 : 15); writer.Write(false); writer.Write(2U);
                writer.Write(kind == "manager-sequence-wrong" ? 0 : 1); writer.Write(17); writer.Write(kind == "manager-palette-wrong" ? 0 : 16);
            })),
            ("NiMultiTargetTransformController", Bytes(writer => { Clock(writer, -1); writer.Write((ushort)1); writer.Write(0); })),
            ("NiDefaultAVObjectPalette", Bytes(writer =>
            {
                writer.Write(0U); writer.Write(1U); var name = Encoding.UTF8.GetBytes("Authored target"); writer.Write(name.Length); writer.Write(name); writer.Write(0);
            })),
            ("NiControllerSequence", Sequence(first: false)),
            (compact ? "NiBSplineCompPoint3Interpolator" : "NiBSplinePoint3Interpolator", Bytes(writer =>
            {
                writer.Write(0f); writer.Write(1f); writer.Write(12); writer.Write(13); Vector(writer, 1, 2, 3); writer.Write(0U);
                if (compact) { writer.Write(0f); writer.Write(1f); }
            })),
            (compact ? "NiBSplineCompFloatInterpolator" : "NiBSplineFloatInterpolator", Bytes(writer =>
            {
                writer.Write(0f); writer.Write(1f); writer.Write(12); writer.Write(13); writer.Write(4.25f); writer.Write(0U);
                if (compact) { writer.Write(0f); writer.Write(1f); }
            })),
            ("NiPathInterpolator", Bytes(writer => { writer.Write((ushort)2); writer.Write(1); writer.Write(0f); writer.Write(0f); writer.Write((short)0); writer.Write(10); writer.Write(6); })),
        };
        return Bytes(writer =>
        {
            void Sized(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Length); foreach (var block in blocks) Sized(block.Type);
            for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Payload.Length);
            writer.Write(strings.Length); writer.Write((uint)strings.Max(value => Encoding.UTF8.GetByteCount(value)));
            foreach (var value in strings) Sized(value);
            writer.Write(0U); foreach (var block in blocks) writer.Write(block.Payload);
            writer.Write(2U); writer.Write(1); writer.Write(17);
        });
    }
}
