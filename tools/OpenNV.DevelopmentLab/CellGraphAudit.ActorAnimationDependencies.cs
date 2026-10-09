using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    internal sealed record ActorAnimationField(int Ordinal, string Signature, int Bytes, string Sha256);
    internal sealed record ActorAnimationRecordLink(string Field, int Ordinal, uint Encoded, string Target,
        string? Winner, string? Signature, bool? Deleted, string Disposition);
    internal sealed record ActorAnimationObjectLink(string Form, string Model, string DeclarationOwner);
    internal sealed record ActorAnimationListEntry(int FieldOrdinal, int EntryOrdinal, string DeclaredPath,
        string? LogicalPath, string Disposition, string? Error);
    internal sealed record ActorAnimationReadRange(int FieldOrdinal, int Offset, int Bytes, string Meaning);

    internal sealed class ActorAnimationDeclaration
    {
        public string Form { get; init; } = "";
        public string Signature { get; init; } = "";
        public string Winner { get; init; } = "";
        public uint RecordFlags { get; init; }
        public bool Deleted { get; init; }
        public string? Sha256 { get; set; }
        public ActorAnimationField[] Fields { get; set; } = [];
        public List<int> InspectedFieldOrdinals { get; } = [];
        public List<ActorAnimationReadRange> ReadRanges { get; } = [];
        public int[] UninspectedFieldOrdinals => Enumerable.Range(0, Fields.Length).Except(InspectedFieldOrdinals).ToArray();
        public string Disposition { get; set; } = "source-declaration; uninspected";
        public string? Model { get; set; }
        public string? DeclaredModel { get; set; }
        public string? Directory { get; set; }
        public string? ResolvedModelOwner { get; set; }
        public string ModelChoice { get; set; } = "uninspected; no saved or random template choice supplied";
        public List<ResourceDependency> DependencyEdges { get; } = [];
        public List<ActorAnimationRecordLink> RecordLinks { get; } = [];
        public List<ActorAnimationObjectLink> Objects { get; } = [];
        public List<ActorAnimationListEntry> ListEntries { get; } = [];
        public List<object> Failures { get; } = [];
        public string NativeEligibility { get; } = "unverified; source files and declarations do not select an action or construct an actor";
    }

    internal sealed class ActorAnimationDirectory
    {
        public string Path { get; init; } = "";
        public List<string> DeclarationOwners { get; } = [];
        public string[] Files { get; set; } = [];
        public int WinningMembers { get; set; }
        public List<object> Failures { get; } = [];
        public string GroupEligibility { get; } = "uninspected; recursive file inventory is not a requested ActorAnimations group";
        public string NativeControllerBinding { get; } = "unverified; no skeleton, sequence, pose or playback was admitted";
    }

    internal sealed class ActorAnimationInspection
    {
        public string StackId { get; init; } = "";
        public string SaveCompatibilityId { get; init; } = "";
        public List<ActorAnimationDeclaration> Records { get; } = [];
        public List<ActorAnimationDirectory> Directories { get; } = [];
        public List<ResourceDependency> DependencyEdges { get; } = [];
        public List<object> Failures { get; } = [];
        public AlternativeQueryReuse? QueryReuse { get; set; }
        public int IdleSourceQueries { get; set; }
        public bool DeclarationsPassed => Failures.Count == 0 && Records.All(row => row.Failures.Count == 0) &&
            Directories.All(row => row.Failures.Count == 0);
        public bool RuntimeReadiness { get; } = false;
        public string Domain { get; } = "All winning/deleted NPC_, CREA, IDLE and ANIO declarations; all actual winning KF members under declared actor model directories and the existing first-person policy. Losing resources, group/action eligibility and runtime state are independent lanes.";
        public string DynamicPaths { get; } = "unknown; arbitrary script/plugin/state-created paths, templates and references are not enumerated by this finite source inventory";
        public string Native { get; } = "unverified; controller joins, skeleton targets, interpolation, events, blends, root motion and pixels are not established by a resource union";
    }

    // One exact immutable selection. Directories and dependency paths are shared,
    // while each source record retains its own identity, fields and join refusal.
    // This does not retain decoded NIFs or duplicate a KF list for every actor.
    internal sealed class SourceActorAnimationDependencies
    {
        private readonly RuntimeLiveContentSource _source;
        private readonly FalloutPluginStack _records;
        private readonly AlternativeSourceQueries _queries;
        private readonly Dictionary<string, ActorAnimationDirectory> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ResourceDependency> _dependencies = [];
        private ActorAnimationInspection? _inspection;

        internal SourceActorAnimationDependencies(RuntimeLiveContentSource source, FalloutPluginStack records)
        {
            if (!ReferenceEquals(records.OwnedSource, source))
                throw new InvalidDataException("Actor animation declarations require the exact owned source/stack instance.");
            _source = source; _records = records; _queries = new(source, records);
        }

        internal ActorAnimationInspection Read()
        {
            if (_inspection is not null) return _inspection;
            var result = new ActorAnimationInspection { StackId = _source.StackId, SaveCompatibilityId = _source.SaveCompatibilityId };
            _inspection = result;
            try { ReadDeclarations(result); }
            catch (Exception error) { result.Failures.Add(new { lane = "actor-animation-inventory", error = error.Message }); }
            result.Directories.AddRange(_directories.Values.OrderBy(row => row.Path, StringComparer.OrdinalIgnoreCase));
            result.DependencyEdges.AddRange(_dependencies.OrderBy(edge => edge.Path, StringComparer.OrdinalIgnoreCase).ThenBy(edge => edge.Kind, StringComparer.Ordinal));
            result.QueryReuse = _queries.Reuse;
            return result;
        }

        private void ReadDeclarations(ActorAnimationInspection result)
        {
            var winners = new Dictionary<FalloutFormKey, FalloutPluginRecord>(FalloutFormKeyComparer.Instance);
            // EffectiveRecords excludes winning deletions. This is one header
            // pass for this component, never a per-cell or per-actor source scan.
            foreach (var source in _records.Plugins.SelectMany(plugin => plugin.Plugin.Records))
                if (source.Signature is "NPC_" or "CREA" or "IDLE" or "ANIO" && !winners.ContainsKey(source.FormKey) &&
                    _records.TryGetWinner(source.FormKey, out var winner)) winners.Add(winner.FormKey, winner);
            foreach (var record in winners.Values)
            {
                var row = new ActorAnimationDeclaration { Form = record.FormKey.ToString(), Signature = record.Signature,
                    Winner = record.Plugin.Name, RecordFlags = record.Flags, Deleted = record.IsDeleted };
                result.Records.Add(row);
                try
                {
                    row.Sha256 = Hash(record.ReadData()); var fields = record.ReadSubrecords().ToArray();
                    row.Fields = fields.Select((field, ordinal) => new ActorAnimationField(ordinal, field.Signature, field.Data.Length, Hash(field.Data.Span))).ToArray();
                    if (record.IsDeleted) { row.Disposition = "winning-deletion; no dependency or live owner substituted"; continue; }
                    row.Disposition = "winning-source-declaration; activation and conditions uninspected";
                    switch (record.Signature)
                    {
                        case "NPC_": case "CREA": ReadActor(record, fields, row); break;
                        case "IDLE": ReadIdle(record, fields, row, result); break;
                        case "ANIO": ReadAnimationObject(record, fields, row); break;
                    }
                }
                catch (Exception error) { row.Failures.Add(new { lane = "actor-animation-record", error = error.Message }); }
            }
            // This directory is the existing player first-person owner policy,
            // not a fabricated placed actor or a guessed record FormID.
            Directory("meshes/characters/_1stperson", "RuntimeNativePlayerActor first-person directory policy");
            AddDependency("meshes/characters/_1stperson/skeleton.nif", "model", null);
        }

        private void ReadActor(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, ActorAnimationDeclaration row)
        {
            // Every authored own MODL is retained even when the current template
            // group inherits elsewhere. The finite template arms are not a roll.
            try
            {
                var modelFields = fields.Where(field => field.Signature == "MODL").ToArray();
                if (modelFields.Length != 0)
                {
                    row.Model = record.Signature == "CREA" ? CreatureModel(record, fields) :
                        FalloutNpcAppearanceResolver.PathField(record, "MODL", "meshes", true, fields);
                    var declared = modelFields.Single().Data.Span;
                    row.DeclaredModel = record.Signature == "CREA" ? Encoding.Latin1.GetString(declared[..^1]) : new UTF8Encoding(false, true).GetString(declared[..^1]);
                    Mark(row, fields, "MODL");
                    if (row.Model is { } model)
                    {
                        AddDependency(model, "model", row);
                        row.Directory = Canonical(model[..model.LastIndexOf('/')]);
                        Directory(row.Directory, row.Form + "/MODL");
                    }
                }
            }
            catch (Exception error) { row.Failures.Add(new { lane = "actor-animation-model-declaration", error = error.Message }); }
            if (fields.Any(field => field.Signature == "TPLT"))
            {
                try { RecordLink(record, fields, "TPLT", row, [record.Signature, record.Signature == "NPC_" ? "LVLN" : "LVLC"]); Mark(row, fields, "TPLT"); }
                catch (Exception error) { row.Failures.Add(new { lane = "actor-animation-template-link", error = error.Message }); }
            }
            try
            {
                var owner = FalloutActorTemplateOwner.Resolve(_records, record, 64);
                row.ResolvedModelOwner = owner.FormKey.ToString(); row.ModelChoice = "existing group-64 deterministic owner resolved; state alternatives unverified";
                var acbs = Array.FindIndex(fields, field => field.Signature == "ACBS");
                row.ReadRanges.Add(new(acbs, 22, 2, "actual group-64 template mask; other ACBS semantics uninspected"));
                if (owner.FormKey == record.FormKey && row.Model is null)
                    row.Failures.Add(new { lane = "actor-animation-model-declaration", error = "The existing model owner requires its absent own MODL." });
            }
            catch (Exception error)
            {
                row.ModelChoice = "existing group-64 owner refused; no source or saved alternative substituted";
                row.Failures.Add(new { lane = "actor-animation-template-owner", error = error.Message });
            }
            if (record.Signature != "CREA") return;
            try
            {
                var lists = fields.Where(field => field.Signature == "KFFZ").ToArray();
                if (lists.Length == 0) return;
                if (lists.Length != 1 || lists[0].Data.IsEmpty || lists[0].Data.Span[^1] != 0)
                    throw new InvalidDataException("CREA KFFZ must be one terminated source list.");
                var skeleton = CreatureModel(record, fields); var directory = skeleton[..skeleton.LastIndexOf('/')];
                var text = Encoding.Latin1.GetString(lists[0].Data.Span).TrimEnd('\0');
                var ordinal = Array.FindIndex(fields, field => field.Signature == "KFFZ");
                var paths = text.Length == 0 ? Array.Empty<string>() : text.Split('\0');
                for (var index = 0; index < paths.Length; index++)
                {
                    string? path = null;
                    try
                    {
                        path = CreaturePath(paths[index], directory, ".kf"); AddDependency(path, "animation", row);
                        row.ListEntries.Add(new(ordinal, index, paths[index], path, "actual CREA list/path source rules; native activation unverified", null));
                    }
                    catch (Exception error)
                    {
                        row.ListEntries.Add(new(ordinal, index, paths[index], path, "source-path-refused; later siblings retained", error.Message));
                        row.Failures.Add(new { lane = "actor-animation-kffz-entry", field = ordinal, entry = index, declaredPath = paths[index], error = error.Message });
                    }
                }
                Mark(row, fields, "KFFZ");
            }
            catch (Exception error) { row.Failures.Add(new { lane = "actor-animation-kffz", error = error.Message }); }
        }

        private void ReadIdle(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, ActorAnimationDeclaration row,
            ActorAnimationInspection result)
        {
            if (!fields.Any(field => field.Signature == "MODL"))
            {
                row.Disposition = "encoded-absent MODL; no file, group or selected idle substituted"; return;
            }
            // Resolve uses the actual IDLE -> ANIO.DATA master-scoped join. Its
            // path is then independently admitted by the shared resource owner.
            // A failed join must not hide the original IDLE MODL dependency.
            try
            {
                row.DeclaredModel = FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "MODL").Data.Span);
                var path = IdleModel(record, fields); row.Model = path; Mark(row, fields, "MODL");
                if (!path.EndsWith(".kf", StringComparison.OrdinalIgnoreCase) && !_source.TryResolve(path, null, out _))
                {
                    _ = FalloutFurnitureIdleTree.Read(record);
                    row.Directory = path;
                    row.Disposition = "non-KF IDLE branch layout read; no winning file; branch eligibility/activation uninspected";
                    return;
                }
                AddDependency(path, "animation", row);
            }
            catch (Exception error) { row.Failures.Add(new { lane = "idle-animation-model", error = error.Message }); }
            try
            {
                result.IdleSourceQueries++;
                var source = FalloutActorIdleSource.Resolve(_records, record);
                if (row.Model is not null && Canonical(source.AnimationPath) != row.Model)
                    throw new InvalidDataException("IDLE dependency differs from actual idle-source normalization.");
                foreach (var attachment in source.Objects)
                {
                    var path = Canonical(attachment.ModelPath); ValidatePath(path);
                    AddDependency(path, "model", row);
                    row.Objects.Add(new(attachment.Form.ToString(), path, "actual FalloutActorIdleSource.Resolve master-scoped ANIO.DATA join"));
                }
            }
            catch (Exception error) { row.Failures.Add(new { lane = "idle-animation-source-join", error = error.Message }); }
        }

        private void ReadAnimationObject(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, ActorAnimationDeclaration row)
        {
            var data = fields.Where(field => field.Signature == "DATA").ToArray();
            if (data.Length == 0) row.Disposition = "ANIO has no DATA identity; unrelated to actual IDLE attachment join";
            else
            {
                try { RecordLink(record, fields, "DATA", row, ["IDLE"]); Mark(row, fields, "DATA"); }
                catch (Exception error) { row.Failures.Add(new { lane = "anio-idle-identity", error = error.Message }); }
            }
            if (!fields.Any(field => field.Signature == "MODL"))
            {
                row.Disposition += "; encoded-absent MODL"; return;
            }
            try
            {
                row.DeclaredModel = FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "MODL").Data.Span);
                row.Model = IdleModel(record, fields); Mark(row, fields, "MODL");
                AddDependency(row.Model, "model", row);
            }
            catch (Exception error) { row.Failures.Add(new { lane = "anio-model-declaration", error = error.Message }); }
        }

        private void RecordLink(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, string field,
            ActorAnimationDeclaration row, string[] signatures)
        {
            var ordinal = Array.FindIndex(fields, item => item.Signature == field);
            var target = FalloutDialogueTopic.RequiredForm(record, field);
            var encoded = BinaryPrimitives.ReadUInt32LittleEndian(fields[ordinal].Data.Span);
            _records.TryGetWinner(target, out var winner);
            var disposition = winner is null ? "missing-winning-target" : winner.IsDeleted ? "winning-deletion" :
                signatures.Contains(winner.Signature, StringComparer.Ordinal) ? "exact-master-scoped-winning-target" : "wrong-winning-signature";
            row.RecordLinks.Add(new(field, ordinal, encoded, target.ToString(), winner?.Plugin.Name,
                winner?.Signature, winner?.IsDeleted, disposition));
            if (winner is null || winner.IsDeleted || !signatures.Contains(winner.Signature, StringComparer.Ordinal))
                throw new InvalidDataException("Animation declaration has " + disposition + ": " + target);
        }

        private void Directory(string directory, string owner)
        {
            directory = Canonical(directory); ValidatePath(directory);
            if (_directories.TryGetValue(directory, out var retained))
            {
                if (!retained.DeclarationOwners.Contains(owner, StringComparer.Ordinal)) retained.DeclarationOwners.Add(owner); return;
            }
            var row = new ActorAnimationDirectory { Path = directory }; row.DeclarationOwners.Add(owner); _directories.Add(directory, row);
            try
            {
                var members = _queries.ResourcePathsUnder(directory); row.WinningMembers = members.Count;
                row.Files = members.Select(Canonical).Where(path => path.EndsWith(".kf", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var path in row.Files)
                {
                    ValidatePath(path);
                    if (!path.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Actor directory catalog returned a resource outside its exact root.");
                    // Do not synthesize a group request from filenames, node
                    // targets or an exported sequence. Eligibility is separate.
                    AddDependency(path, "animation", null);
                }
            }
            catch (Exception error) { row.Failures.Add(new { lane = "actor-animation-directory", error = error.Message }); }
        }

        private void AddDependency(string path, string kind, ActorAnimationDeclaration? row)
        {
            path = Canonical(path); ValidatePath(path); var edge = new ResourceDependency(path, kind);
            _dependencies.Add(edge);
            if (row is not null && !row.DependencyEdges.Contains(edge)) row.DependencyEdges.Add(edge);
        }

        private static string IdleModel(FalloutPluginRecord record, FalloutPluginSubrecord[] fields)
        {
            // The actual idle source owner uses CP1252 text and only a meshes
            // prefix. Do not normalize away a path component it would retain.
            var path = FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
            ValidatePath(path);
            path = path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path : "meshes/" + path;
            path = Canonical(path); ValidatePath(path); return path;
        }

        private static string CreatureModel(FalloutPluginRecord record, FalloutPluginSubrecord[] fields)
        {
            var bytes = fields.Single(field => field.Signature == "MODL").Data.Span;
            if (bytes.Length < 2 || bytes[^1] != 0 || bytes[..^1].Contains((byte)0))
                throw new InvalidDataException("CREA MODL is not one terminated source string: " + record.FormKey);
            return CreaturePath(Encoding.Latin1.GetString(bytes[..^1]), "meshes", ".nif");
        }

        private static string CreaturePath(string path, string directory, string extension)
        {
            path = path.Replace('\\', '/'); ValidatePath(path);
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CREA animation/model declaration has the wrong source extension.");
            return Canonical(path.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ? path : directory + "/" + path);
        }

        private static void ValidatePath(string path)
        {
            if (path.Length == 0 || path.StartsWith('/') || path.Contains(':') ||
                path.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Animation resource leaves the exact owned logical namespace.");
        }

        private static void Mark(ActorAnimationDeclaration row, FalloutPluginSubrecord[] fields, string signature)
        {
            for (var ordinal = 0; ordinal < fields.Length; ordinal++)
                if (fields[ordinal].Signature == signature && !row.InspectedFieldOrdinals.Contains(ordinal)) row.InspectedFieldOrdinals.Add(ordinal);
        }
    }
}
