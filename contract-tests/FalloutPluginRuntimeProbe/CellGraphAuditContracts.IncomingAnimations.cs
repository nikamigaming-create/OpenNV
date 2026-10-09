using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void IncomingAnimationDependencies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-source-incoming-animations-" + Guid.NewGuid().ToString("N"));
        var cases = 0;
        try
        {
            foreach (var mode in new[] { "valid", "uint-count", "zero-event", "zero-list", "zero-response", "repeated-response",
                "superseded-missing", "empty-pack", "empty-idlm", "present-empty-idlm", "empty-info", "out-of-event",
                "bad-marker", "duplicate-event", "duplicate-inam", "bad-count", "wrong-count", "bad-idla-extent", "duplicate-idla",
                "bad-flags", "bad-timer", "bad-idlm-count", "bad-idlm-timer", "bad-response-extent", "orphan-response",
                "noncontiguous-response", "bad-response-animation", "duplicate-response-number", "missing-target", "deleted-target",
                "wrong-target", "deleted-sources", "disabled-sources", "foreign-master", "bad-target-model", "catalog-missing",
                "catalog-duplicate", "catalog-payload-drift", "catalog-field-drift", "catalog-flag-drift", "catalog-foreign-selection",
                "foreign-source-instance", "many-consumers", "uninspected-feasibility", "absent-event-animation", "multiple-responses",
                "response16", "response20" })
            {
                var caseRoot = Path.Combine(directory, mode); var game = Path.Combine(caseRoot, "Game"); var data = Path.Combine(game, "Data");
                Directory.CreateDirectory(data); var plugins = IncomingAnimationPlugins(mode);
                File.WriteAllBytes(Path.Combine(data, Plugin), plugins.Base);
                File.WriteAllBytes(Path.Combine(data, "IncomingForeign.esm"), plugins.Foreign);
                File.WriteAllBytes(Path.Combine(data, "IncomingOverride.esp"), plugins.Override);
                ActorAnimationArchive(Path.Combine(data, "FalloutNV.bsa"), "meshes/objects/winner.nif", NifFixture());
                var ini = Path.Combine(game, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
                foreach (var path in new[] { "meshes/box.nif", "meshes/characters/_1stperson/skeleton.nif", "meshes/objects/base.nif",
                    "meshes/objects/winner.nif", "meshes/objects/foreign.nif" }) IncomingAnimationInput(data, path, NifFixture());
                foreach (var path in new[] { "meshes/idles/losing.kf", "meshes/idles/winner.kf", "meshes/idles/healthy.kf", "meshes/idles/foreign.kf" })
                    IncomingAnimationInput(data, path, ActorAnimationKf(34, [Path.GetFileNameWithoutExtension(path)]));
                var before = Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).ToDictionary(path => path,
                    path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);
                using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame, [],
                    [Plugin, "IncomingForeign.esm", "IncomingOverride.esp"], ini);
                using var records = FalloutPluginStack.Load(source.PluginSources);
                var targetOwner = new CellGraphAudit.SourceActorAnimationDependencies(source, records); var targets = targetOwner.Read();
                var queries = targets.QueryReuse; var idleQueries = targets.IdleSourceQueries;
                if (mode == "catalog-missing") targets.Records.RemoveAll(row => row.Form == Key(0x500).ToString());
                if (mode == "catalog-duplicate") targets.Records.Add(targets.Records.Single(row => row.Form == Key(0x500).ToString()));
                if (mode == "catalog-payload-drift") targets.Records.Single(row => row.Form == Key(0x500).ToString()).Sha256 = new string('0', 64);
                if (mode == "catalog-field-drift")
                {
                    var target = targets.Records.Single(row => row.Form == Key(0x500).ToString());
                    target.Fields[0] = target.Fields[0] with { Bytes = target.Fields[0].Bytes + 1 };
                }
                if (mode == "catalog-flag-drift")
                {
                    var ordinal = targets.Records.FindIndex(row => row.Form == Key(0x500).ToString()); var original = targets.Records[ordinal];
                    targets.Records[ordinal] = new CellGraphAudit.ActorAnimationDeclaration { Form = original.Form, Signature = original.Signature,
                        Winner = original.Winner, RecordFlags = original.RecordFlags ^ 0x800, Deleted = original.Deleted,
                        Sha256 = original.Sha256, Fields = original.Fields };
                }
                if (mode == "catalog-foreign-selection")
                {
                    var foreign = new CellGraphAudit.ActorAnimationInspection { StackId = new string('0', 64), SaveCompatibilityId = targets.SaveCompatibilityId };
                    foreign.Records.AddRange(targets.Records); var rejected = false;
                    try { _ = new CellGraphAudit.SourceIncomingAnimationDependencies(source, records, foreign); }
                    catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "A foreign target selection lent declarations to an incoming source graph.");
                }
                if (mode == "foreign-source-instance")
                {
                    using var another = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame, [],
                        [Plugin, "IncomingForeign.esm", "IncomingOverride.esp"], ini); var rejected = false;
                    Require(another.StackId == source.StackId && another.SaveCompatibilityId == source.SaveCompatibilityId,
                        "The negative did not construct a content-identical independent source instance.");
                    try { _ = new CellGraphAudit.SourceIncomingAnimationDependencies(another, records, targets); }
                    catch (InvalidDataException) { rejected = true; }
                    Require(rejected, "An incoming owner accepted records owned by another exact source instance.");
                }
                var owner = new CellGraphAudit.SourceIncomingAnimationDependencies(source, records, targets); var inspection = owner.Read();
                Require(ReferenceEquals(inspection, owner.Read()) && ReferenceEquals(targets, targetOwner.Read()) &&
                    ReferenceEquals(queries, targets.QueryReuse) && targets.IdleSourceQueries == idleQueries && inspection.DirectoryQueries == 0,
                    "Incoming joins rebuilt the target inventory, repeated IDLE attachment joins or scanned animation directories.");
                Require(inspection.StackId == source.StackId && inspection.SaveCompatibilityId == source.SaveCompatibilityId &&
                    inspection.SourceEnumerationComplete && !inspection.RuntimeReadiness &&
                    inspection.Records.All(row => row.Feasibility.StartsWith("uninspected", StringComparison.Ordinal) && row.Native.StartsWith("unverified", StringComparison.Ordinal)),
                    "Incoming transport inventory admitted another selection, event/condition feasibility or native playback.");
                var rows = inspection.Records.ToDictionary(row => row.Form, StringComparer.OrdinalIgnoreCase);
                foreach (var row in inspection.Records)
                {
                    if (!records.TryGetWinner(IncomingAnimationForm(row.Form), out var winner))
                        throw new InvalidDataException("The source metadata has no actual winning fixture record.");
                    Require(row.Winner == winner.Plugin.Name && row.Signature == winner.Signature && row.RecordFlags == winner.Flags && row.Deleted == winner.IsDeleted &&
                        row.Sha256 == Convert.ToHexString(SHA256.HashData(winner.ReadData())) && row.Fields.SequenceEqual(winner.ReadSubrecords().Select((field, ordinal) =>
                            new CellGraphAudit.IncomingAnimationField(ordinal, field.Signature, field.Data.Length, Convert.ToHexString(SHA256.HashData(field.Data.Span))))),
                        "Incoming source winner, master owner, field ordinal, raw extent or payload hash was lost.");
                    if (row.Deleted) Require(row.Links.Count == 0 && !row.ReaderPassed && row.Disposition.StartsWith("winning-deletion", StringComparison.Ordinal),
                        "A winning incoming deletion was activated or replaced by the losing record's links.");
                    else
                    {
                        var parsed = false;
                        try
                        {
                            switch (winner.Signature)
                            {
                                case "PACK": _ = FalloutScriptPackage.Read(winner); break;
                                case "IDLM": _ = FalloutIdleCollection.Read(winner); break;
                                case "INFO": _ = FalloutDialogueTopic.Decode(winner); break;
                                default: throw new InvalidDataException("Unexpected authored signature.");
                            }
                            parsed = true;
                        }
                        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { }
                        Require(row.ReaderPassed == parsed, "Incoming metadata differs from the independent actual full runtime reader's admission.");
                    }
                }
                var primary = rows[Key(0x700).ToString()]; var marker = rows[Key(0x710).ToString()]; var info = rows[Key(0x720).ToString()];
                var sourceFault = mode is "bad-marker" or "duplicate-event" or "duplicate-inam" or "bad-count" or "wrong-count" or "bad-idla-extent" or "duplicate-idla" or
                    "bad-flags" or "bad-timer" or "bad-idlm-count" or "bad-idlm-timer" or "bad-response-extent" or "orphan-response" or
                    "noncontiguous-response" or "bad-response-animation" or "duplicate-response-number";
                Require(new[] { primary, marker, info }.Any(row => !row.Deleted && !row.ReaderPassed) == sourceFault,
                    "The authored runtime-reader refusal was omitted or invented for a valid source layout.");
                var catalogFault = mode is "catalog-missing" or "catalog-duplicate" or "catalog-payload-drift" or "catalog-field-drift" or "catalog-flag-drift";
                var targetFault = mode is "zero-list" or "zero-response" or "superseded-missing" or "missing-target" or "deleted-target" or "wrong-target" or "bad-target-model";
                Require(inspection.TargetCatalogComplete != catalogFault && inspection.DeclarationsPassed == !(sourceFault || catalogFault || targetFault || mode == "out-of-event"),
                    "A source/callee/target refusal silently passed or an actual valid transport was refused.");
                foreach (var id in new[] { 0x704u, 0x714u, 0x724u })
                {
                    var sibling = rows[Key(id).ToString()];
                    Require(sibling.ReaderPassed && sibling.Failures.Count == 0 && sibling.Links.All(link => link.Failures.Count == 0 && link.Target == Key(0x501).ToString()),
                        "An unrelated healthy incoming record was suppressed or substituted after a failed sibling.");
                }
                if (mode == "valid")
                {
                    Require(primary.ReturnedIdleCount == 2 && primary.Links.Count(link => link.Field == "IDLA") == 2 &&
                        marker.ReturnedIdleCount == 2 && info.Responses.Count == 1 && primary.ConditionFieldSlots.Length == 1 && info.ConditionFieldSlots.Length == 1 &&
                        primary.Links.Where(link => link.Target == Key(0x500).ToString()).All(link => link.ObjectCatalogOrdinals.Count == 1 &&
                            targets.Records[link.ObjectCatalogOrdinals.Single()].Form == Key(0x600).ToString()) &&
                        primary.Links.All(link => link.ReturnedByReader == true),
                        "List duplicates, raw conditions, actual lifecycle returns or exact winning ANIO closure were dropped.");
                }
                if (mode is "repeated-response" or "superseded-missing")
                {
                    var speaker = info.Links.Where(link => link.Field == "SNAM").ToArray();
                    Require(speaker.Length == 2 && speaker[0].ReturnedByReader == false && speaker[1].ReturnedByReader == true &&
                        info.Responses.Single().SpeakerAnimation == Key(0x500).ToString() && speaker[0].Target != speaker[1].Target,
                        "Earlier raw INFO animation was lost, declared a duplicate error, or mistaken for the actual last returned mapping.");
                    if (mode == "superseded-missing") Require(speaker[0].Failures.Count != 0 && speaker[1].Failures.Count == 0,
                        "A replaced but missing authored animation link disappeared from the finite declaration denominator.");
                }
                if (mode == "zero-event") Require(primary.Links.Single(link => link.Field == "INAM").Target is null &&
                    primary.Links.Single(link => link.Field == "INAM").Failures.Count == 0 && primary.Events.Single().HasReturnedAnimation &&
                    primary.Events.Single().ReturnedAnimation is null && primary.Links.Single(link => link.Field == "INAM").ReturnedByReader == true,
                    "Optional event zero was confused with an absent declaration or required master-scoped object zero.");
                if (mode == "absent-event-animation") Require(primary.Events.Single().ReaderReturned &&
                    !primary.Events.Single().HasReturnedAnimation && primary.Events.Single().AnimationFields == 0 &&
                    primary.Links.All(link => link.Field != "INAM"), "An absent event animation was replaced by an optional-null INAM declaration.");
                if (mode is "response16" or "response20") Require(info.Responses.Single().Bytes == (mode == "response16" ? 16 : 20),
                    "An actual supported response extent was rewritten as the longer layout.");
                if (mode == "multiple-responses") Require(info.Responses.Count == 2 && info.Responses.Select(row => row.Number).SequenceEqual(new byte?[] { 7, 8 }) &&
                    info.Links.Where(link => link.ResponseOrdinal == 1).All(link => link.Target == Key(0x501).ToString() && link.ReturnedByReader == true),
                    "A second response borrowed its preceding response's animation or number.");
                if (mode is "empty-pack" or "empty-idlm" or "present-empty-idlm") Require((mode == "empty-pack" ? primary : marker).ReturnedIdleCount == 0,
                    "An authored empty idle list acquired a fallback animation.");
                if (mode == "empty-info") Require(info.Responses.Count == 0 && info.Links.Count == 0 && info.ReaderPassed,
                    "An actual optional empty INFO response collection acquired a synthetic response.");
                if (mode == "uninspected-feasibility")
                {
                    var refused = false; var pack = FalloutScriptPackage.Read(records.GetEffective(Key(0x700)));
                    try { pack.EventPrograms["POBA"].ValidateScript(); }
                    catch (InvalidDataException) { refused = true; }
                    Require(refused && primary.ReaderPassed && primary.Fields.Where(field => field.Signature is "SCTX" or "TNAM")
                        .All(field => primary.OtherSemanticFieldSlots.Contains(field.Ordinal)) && inspection.Feasibility.StartsWith("uninspected", StringComparison.Ordinal),
                        "Animation transport substituted event/script feasibility or claimed its separately refused execution owner.");
                }
                if (mode is "zero-list" or "zero-response")
                {
                    var adjustedZero = records.GetEffective(Key(mode == "zero-list" ? 0x700u : 0x720u)).Plugin.AdjustFormId(0);
                    Require(inspection.Records.SelectMany(row => row.Links).Any(link => link.Encoded == 0 &&
                        link.Target == adjustedZero.ToString() && link.Failures.Count != 0),
                        "Required zero animation identity was converted into optional null or the caller/global namespace.");
                }
                if (mode == "out-of-event") Require(primary.ReaderPassed && primary.UnboundAnimationFieldSlots.Count == 1 &&
                    primary.Links.Single(link => link.Event is null && link.Field == "INAM").Target is null &&
                    primary.Links.Single(link => link.Event is null && link.Field == "INAM").ReturnedByReader == false,
                    "An INAM outside every package event borrowed the next marker's lifecycle owner.");
                if (mode == "foreign-master")
                {
                    var foreign = new FalloutFormKey("IncomingForeign.esm", 0x500);
                    Require(primary.Winner == "IncomingOverride.esp" && primary.Links.Single(link => link.Field == "IDLA").Target == foreign.ToString() &&
                        primary.Links.Single(link => link.Field == "INAM").Target == Key(0x501).ToString() &&
                        info.Links.Single(link => link.Field == "SNAM").Target == foreign.ToString() &&
                        targets.Records[primary.Links.Single(link => link.Field == "IDLA").ObjectCatalogOrdinals.Single()].Form ==
                            new FalloutFormKey("IncomingForeign.esm", 0x600).ToString(),
                        "Incoming master reordering used global load index, low object ID or the caller's target/ANIO declaration.");
                }
                if (mode == "deleted-sources") Require(new[] { primary, marker, info }.All(row => row.Deleted && row.Links.Count == 0) &&
                    inspection.Records.Count(row => row.Deleted) == 6, "Winning deleted incoming sources were omitted from complete metadata.");
                if (mode == "disabled-sources") Require(new[] { primary, marker, info }.All(row => row.RecordFlags == 0x800 && row.Links.Count != 0 && row.ReaderPassed),
                    "Inactive source links were omitted or record flags claimed native support.");
                if (mode == "many-consumers") Require(inspection.Records.Count(row => row.Signature == "PACK") == 103 &&
                    inspection.Records.Where(row => row.Form.StartsWith(Plugin + ":001", StringComparison.Ordinal)).All(row => row.Links.Count == 1),
                    "Many actual incoming source records were capped, merged or substituted by one target directory.");
                if (mode == "missing-target") IncomingAnimationComponent(source, records, caseRoot);
                Require(before.Count == Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Count() && before.All(pair =>
                    File.Exists(pair.Key) && pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)))),
                    "Incoming audit transformed or mutated its original authored input graph.");
                cases++;
            }
            Console.WriteLine($"OPENNV_CELL_INCOMING_ANIMATION_DEPENDENCIES_PASS cases={cases} rawAndReturned=separate exactMasterWinner=true deletedAndDisabled=retained targetInventory=reused directoriesRead=0 native=unverified");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static FalloutFormKey IncomingAnimationForm(string value)
    {
        var colon = value.LastIndexOf(':'); return new(value[..colon], uint.Parse(value[(colon + 1)..], System.Globalization.NumberStyles.HexNumber));
    }

    private static (byte[] Base, byte[] Foreign, byte[] Override) IncomingAnimationPlugins(string mode)
    {
        byte[] Header(params string[] masters)
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            return Record("TES4", 0, 0, new[] { Field("HEDR", header) }.Concat(masters.SelectMany(master =>
                new[] { Field("MAST", Text(master)), Field("DATA", new byte[8]) })).ToArray());
        }
        byte[] Pack(uint form, uint target, bool primary, uint flags = 0)
        {
            var pkdt = new byte[8]; pkdt[4] = 6;
            var list = primary && mode == "valid" ? Join(BitConverter.GetBytes(target), BitConverter.GetBytes(target)) : BitConverter.GetBytes(target);
            var count = primary && mode == "valid" ? 2 : 1;
            if (primary && mode == "empty-pack") { count = 0; list = []; }
            if (primary && mode == "zero-list") list = BitConverter.GetBytes(0u);
            if (primary && mode == "wrong-count") count = 2;
            if (primary && mode == "bad-idla-extent") list = Join(list, [9]);
            var fields = new List<byte[]> { Field("EDID", Text("IncomingPackage" + form)), Field("PKDT", pkdt),
                Field("IDLF", [primary && mode == "bad-flags" ? (byte)2 : (byte)1]),
                Field("IDLC", primary && mode == "bad-count" ? [1, 0] : mode == "uint-count" ? BitConverter.GetBytes(count) : [(byte)count]),
                Field("IDLT", BitConverter.GetBytes(primary && mode == "bad-timer" ? float.NaN : 2.5f)), Field("IDLA", list), Field("CTDA", new byte[28]) };
            if (primary && mode == "duplicate-idla") fields.Add(Field("IDLA", list));
            if (primary && mode == "out-of-event") fields.Add(Field("INAM", BitConverter.GetBytes(target)));
            fields.Add(Field("POBA", primary && mode == "bad-marker" ? [1] : []));
            if (!(primary && mode == "absent-event-animation"))
                fields.Add(Field("INAM", BitConverter.GetBytes(primary && mode == "zero-event" ? 0u : primary && mode == "foreign-master" ? 0x01000501u : target)));
            if (primary && mode == "duplicate-inam") fields.Add(Field("INAM", BitConverter.GetBytes(target)));
            if (primary && mode == "duplicate-event") fields.Add(Field("POBA", []));
            if (primary && mode == "uninspected-feasibility")
            { fields.Add(Field("SCTX", Text("UnsupportedEventCommand"))); fields.Add(Field("TNAM", BitConverter.GetBytes(0x01000501u))); }
            return Record("PACK", form, flags, fields.ToArray());
        }
        byte[] Marker(uint form, uint target, bool primary, uint flags = 0)
        {
            var count = primary && mode == "valid" ? (byte)2 : (byte)1;
            if (primary && mode is "empty-idlm" or "present-empty-idlm") count = 0;
            if (primary && mode == "bad-idlm-count") count = 2;
            var list = primary && mode == "valid" ? Join(BitConverter.GetBytes(target), BitConverter.GetBytes(target)) : BitConverter.GetBytes(target);
            if (count == 0) list = [];
            var fields = new List<byte[]> { Field("IDLF", [5]), Field("IDLC", [count]),
                Field("IDLT", BitConverter.GetBytes(primary && mode == "bad-idlm-timer" ? -1f : .75f)) };
            if (!(primary && mode == "empty-idlm")) fields.Add(Field("IDLA", list));
            return Record("IDLM", form, flags, fields.ToArray());
        }
        byte[] Info(uint form, uint target, bool primary, uint flags = 0)
        {
            var response = new byte[primary && mode == "bad-response-extent" ? 17 : primary && mode == "response16" ? 16 : primary && mode == "response20" ? 20 : 24]; response[12] = 7;
            var fields = new List<byte[]> { Field("DATA", [0, 0, 0]), Field("QSTI", BitConverter.GetBytes(0x01000900u)), Field("CTDA", new byte[28]) };
            if (primary && mode == "empty-info") return Record("INFO", form, flags, fields.ToArray());
            if (primary && mode == "orphan-response") fields.Add(Field("SNAM", BitConverter.GetBytes(target)));
            fields.Add(Field("TRDT", response)); fields.Add(Field("NAM1", Text("Authored incoming response.")));
            if (primary && mode == "noncontiguous-response") fields.Add(Field("EDID", Text("breaks response group")));
            if (primary && mode is "repeated-response" or "superseded-missing")
                fields.Add(Field("SNAM", BitConverter.GetBytes(mode == "superseded-missing" ? 0x01000599u : 0x01000501u)));
            fields.Add(Field("SNAM", primary && mode == "bad-response-animation" ? new byte[3] : BitConverter.GetBytes(primary && mode == "zero-response" ? 0u : target)));
            fields.Add(Field("LNAM", BitConverter.GetBytes(primary && mode == "foreign-master" ? 0x01000501u : target)));
            if (primary && mode == "duplicate-response-number") { fields.Add(Field("TRDT", response)); fields.Add(Field("NAM1", Text("Duplicate number."))); }
            if (primary && mode == "multiple-responses")
            {
                var second = new byte[16]; second[12] = 8; fields.Add(Field("TRDT", second)); fields.Add(Field("NAM1", Text("Second response.")));
                fields.Add(Field("SNAM", BitConverter.GetBytes(0x01000501u))); fields.Add(Field("LNAM", BitConverter.GetBytes(0x01000501u)));
            }
            return Record("INFO", form, flags, fields.ToArray());
        }
        byte[] Idle(uint form, string model) => Record("IDLE", form, 0, Field("MODL", Text(model)));
        byte[] Object(uint form, uint target, string model) => Record("ANIO", form, 0, Field("DATA", BitConverter.GetBytes(target)), Field("MODL", Text(model)));
        var baseline = Join(Header(), Idle(0x500, "idles/losing.kf"), Idle(0x501, "idles/healthy.kf"), Object(0x600, 0x500, "objects/base.nif"),
            Record("QUST", 0x900, 0, Field("EDID", Text("IncomingQuest"))), Record("STAT", 0x701, 0, Field("MODL", Text("box.nif"))),
            Cell(0x800, "IncomingHub"), Group(0x800, Reference(0x810, 0x701)),
            Record("PACK", 0x700, 0), Record("IDLM", 0x710, 0), Record("INFO", 0x720, 0),
            Record("PACK", 0x705, 0x20), Record("IDLM", 0x715, 0x20), Record("INFO", 0x725, 0x20));
        var foreign = Join(Header(), Idle(0x500, "idles/foreign.kf"), Object(0x600, 0x500, "objects/foreign.nif"));
        var target = mode == "missing-target" ? 0x01000599u : mode == "wrong-target" ? 0x01000900u : mode == "foreign-master" ? 0x00000500u : 0x01000500u;
        var flags = mode == "disabled-sources" ? 0x800u : 0;
        var overrides = new List<byte[]> { Header("IncomingForeign.esm", Plugin),
            mode == "deleted-target" ? Record("IDLE", 0x01000500, 0x20) : Idle(0x01000500, mode == "bad-target-model" ? "../invalid.kf" : "idles/winner.kf"),
            Object(0x01000600, 0x01000500, "objects/winner.nif"),
            mode == "deleted-sources" ? Record("PACK", 0x01000700, 0x20) : Pack(0x01000700, target, true, flags),
            mode == "deleted-sources" ? Record("IDLM", 0x01000710, 0x20) : Marker(0x01000710, target, true, flags),
            mode == "deleted-sources" ? Record("INFO", 0x01000720, 0x20) : Info(0x01000720, target, true, flags),
            Pack(0x01000704, 0x01000501, false), Marker(0x01000714, 0x01000501, false), Info(0x01000724, 0x01000501, false) };
        if (mode == "many-consumers")
            for (var index = 0; index < 100; index++) overrides.Add(Record("PACK", 0x01001000u + (uint)index, 0,
                Field("EDID", Text("UnusedIncoming" + index)), Field("PKDT", new byte[8]), Field("IDLC", [1]), Field("IDLA", BitConverter.GetBytes(0x01000501u))));
        return (baseline, foreign, Join(overrides.ToArray()));
    }

    private static void IncomingAnimationInput(string root, string logical, byte[] bytes)
    {
        var path = Path.Combine(root, logical.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }

    private static void IncomingAnimationComponent(RuntimeLiveContentSource source, FalloutPluginStack records, string directory)
    {
        var configuration = Path.Combine(directory, "incoming-animation-component-runtime.json");
        File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
        var options = CellGraphAudit.ParseOptions([Path.GetDirectoryName(source.ContentRoot)!, Path.Combine(directory, "incoming-animation-component-output"), "--runtime-config", configuration]);
        Directory.CreateDirectory(options.Output);
        Require(!CellGraphAudit.RunComponent(source, records, options, .1f, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(configuration)))),
            "Incoming animation fixture unexpectedly admitted native scene readiness.");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.Output, "component.private.json")));
        var report = document.RootElement;
        // Regression-first assertion uses the existing gate, before requiring
        // the new report field. An unplaced dangling incoming declaration must
        // actually change source accounting, not merely add plausible metadata.
        Require(!report.GetProperty("coverage").GetProperty("sourceAccountingPassed").GetBoolean(),
            "Existing complete-source gate silently passed an unplaced PACK/IDLM/INFO incoming link whose winning IDLE is absent.");
        var incoming = report.GetProperty("alternatives").GetProperty("incomingAnimations");
        Require(!incoming.GetProperty("declarationsPassed").GetBoolean() && incoming.GetProperty("directoryQueries").GetInt32() == 0 &&
            incoming.GetProperty("records").EnumerateArray().Where(row => row.GetProperty("form").GetString() == Key(0x700).ToString())
                .SelectMany(row => row.GetProperty("links").EnumerateArray()).Any(link => link.GetProperty("failures").GetArrayLength() != 0) &&
            !report.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean(),
            "The new incoming ledger omitted the exact missing target or misreported source feasibility/native readiness.");
    }
}
