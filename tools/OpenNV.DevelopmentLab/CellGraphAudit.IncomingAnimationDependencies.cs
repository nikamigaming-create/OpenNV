using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    internal sealed record IncomingAnimationField(int Ordinal, string Signature, int Bytes, string Sha256);
    internal sealed record IncomingAnimationEvent(string Kind, int MarkerOrdinal, int MarkerBytes, int AnimationFields,
        bool ReaderReturned, bool HasReturnedAnimation, string? ReturnedAnimation);
    internal sealed record IncomingAnimationResponse(int Ordinal, int FieldOrdinal, int Bytes, string Sha256,
        byte? Number, bool ReaderReturned, string? SpeakerAnimation, string? ListenerAnimation);

    internal sealed class IncomingAnimationLink
    {
        public string Field { get; init; } = "";
        public int FieldOrdinal { get; init; }
        public int Offset { get; init; }
        public int Bytes { get; init; }
        public int EntryOrdinal { get; init; }
        public string Role { get; init; } = "";
        public string? Event { get; init; }
        public int? EventMarkerOrdinal { get; init; }
        public int? ResponseOrdinal { get; init; }
        public byte? ResponseNumber { get; init; }
        public uint? Encoded { get; init; }
        public string? Target { get; set; }
        public string? Winner { get; set; }
        public string? Signature { get; set; }
        public bool? Deleted { get; set; }
        public int? TargetCatalogOrdinal { get; set; }
        public string? TargetSha256 { get; set; }
        public List<int> ObjectCatalogOrdinals { get; } = [];
        public bool? ReturnedByReader { get; set; }
        public string Disposition { get; set; } = "raw source occurrence; owner uninspected";
        public List<object> Failures { get; } = [];
    }

    internal sealed class IncomingAnimationDeclaration
    {
        public string Form { get; init; } = "";
        public string Signature { get; init; } = "";
        public string Winner { get; init; } = "";
        public uint RecordFlags { get; init; }
        public bool Deleted { get; init; }
        public string? Sha256 { get; set; }
        public IncomingAnimationField[] Fields { get; set; } = [];
        public List<int> LinkReadSlots { get; } = [];
        public List<int> UnboundAnimationFieldSlots { get; } = [];
        public int[] OtherSemanticFieldSlots => Enumerable.Range(0, Fields.Length).Except(LinkReadSlots).ToArray();
        public int[] ConditionFieldSlots { get; set; } = [];
        public string Disposition { get; set; } = "winning declaration; reader uninspected";
        public string Reader { get; set; } = "";
        public bool ReaderPassed { get; set; }
        public byte? IdleFlags { get; set; }
        public float? IdleTimer { get; set; }
        public int? ReturnedIdleCount { get; set; }
        public List<IncomingAnimationEvent> Events { get; } = [];
        public List<IncomingAnimationResponse> Responses { get; } = [];
        public List<IncomingAnimationLink> Links { get; } = [];
        public List<object> Failures { get; } = [];
        public string Feasibility { get; } = "uninspected; conditions, actor/listener ownership, event scripts/topics, procedure order, timers and saved state were not selected or executed";
        public string Native { get; } = "unverified; an incoming IDLE join does not admit a clip, actor, object attachment, pose, timeline or playback";
    }

    internal sealed class IncomingAnimationInspection
    {
        public string StackId { get; init; } = "";
        public string SaveCompatibilityId { get; init; } = "";
        public List<IncomingAnimationDeclaration> Records { get; } = [];
        public List<object> Failures { get; } = [];
        public bool SourceEnumerationComplete { get; set; }
        public int ExpectedSourceDeclarations { get; set; }
        public int RetainedSourceDeclarations => Records.Count;
        public bool TargetCatalogComplete { get; set; }
        public int ExpectedTargetDeclarations { get; set; }
        public int VerifiedTargetDeclarations { get; set; }
        public int DirectoryQueries { get; } = 0;
        public bool TargetInventoryReused { get; } = true;
        public int FailureEvents => Failures.Count + Records.Sum(row => row.Failures.Count + row.UnboundAnimationFieldSlots.Count +
            row.Links.Sum(link => link.Failures.Count));
        public bool DeclarationsPassed => SourceEnumerationComplete && Records.Count == ExpectedSourceDeclarations && TargetCatalogComplete && Failures.Count == 0 &&
            Records.All(row => row.Failures.Count == 0 && (row.Deleted || row.ReaderPassed) &&
                row.UnboundAnimationFieldSlots.Count == 0 && row.Links.All(link => link.Failures.Count == 0));
        public bool RuntimeReadiness { get; } = false;
        public string Domain { get; } = "All winning/deleted PACK IDLA and lifecycle INAM, IDLM IDLA, and contiguous INFO response SNAM/LNAM occurrences. Every raw occurrence and actual reader return is distinct; the existing all-winning IDLE/ANIO resource inventory is reused.";
        public string TargetCatalog { get; } = "TargetCatalogOrdinal and ObjectCatalogOrdinals refer to actorAnimations.records; no model paths or directory members are recopied or rescanned here";
        public string Feasibility { get; } = "uninspected; a complete finite incoming-link inventory does not establish any condition, event, response, actor, saved-state or plugin-created path feasibility";
    }

    internal sealed class SourceIncomingAnimationDependencies
    {
        private sealed record TargetEntry(int Ordinal, ActorAnimationDeclaration Declaration, bool Valid);
        private readonly RuntimeLiveContentSource _source;
        private readonly FalloutPluginStack _records;
        private readonly ActorAnimationInspection _targets;
        private readonly Dictionary<FalloutFormKey, TargetEntry> _catalog = new(FalloutFormKeyComparer.Instance);
        private IncomingAnimationInspection? _inspection;

        internal SourceIncomingAnimationDependencies(RuntimeLiveContentSource source, FalloutPluginStack records,
            ActorAnimationInspection targets)
        {
            if (!ReferenceEquals(records.OwnedSource, source))
                throw new InvalidDataException("Incoming animation declarations require the exact owned source/stack instance.");
            if (targets.StackId != source.StackId || targets.SaveCompatibilityId != source.SaveCompatibilityId)
                throw new InvalidDataException("Incoming animation declarations require the same immutable target selection.");
            _source = source; _records = records; _targets = targets;
        }

        internal IncomingAnimationInspection Read()
        {
            if (_inspection is not null) return _inspection;
            var result = new IncomingAnimationInspection { StackId = _source.StackId, SaveCompatibilityId = _source.SaveCompatibilityId };
            _inspection = result;
            try
            {
                var sources = new Dictionary<FalloutFormKey, FalloutPluginRecord>(FalloutFormKeyComparer.Instance);
                var targets = new Dictionary<FalloutFormKey, FalloutPluginRecord>(FalloutFormKeyComparer.Instance);
                // One header pass includes winning deletions. No per-cell scan,
                // actor-state choice, IDLE Resolve call or directory query occurs.
                foreach (var declaration in _records.Plugins.SelectMany(plugin => plugin.Plugin.Records))
                {
                    if (declaration.Signature is not ("PACK" or "IDLM" or "INFO" or "IDLE" or "ANIO")) continue;
                    var collection = declaration.Signature is "IDLE" or "ANIO" ? targets : sources;
                    if (!collection.ContainsKey(declaration.FormKey) && _records.TryGetWinner(declaration.FormKey, out var winner))
                        collection.Add(winner.FormKey, winner);
                }
                result.SourceEnumerationComplete = true;
                result.ExpectedSourceDeclarations = sources.Count;
                ValidateTargets(targets, result);
                foreach (var record in sources.Values) ReadRecord(record, result);
            }
            catch (Exception error) { result.Failures.Add(new { lane = "incoming-animation-inventory", error = error.Message }); }
            return result;
        }

        private void ValidateTargets(Dictionary<FalloutFormKey, FalloutPluginRecord> expected, IncomingAnimationInspection result)
        {
            result.ExpectedTargetDeclarations = expected.Count;
            var valid = _targets.Failures.Count == 0;
            if (!valid) result.Failures.Add(new { lane = "incoming-animation-target-catalog", error = "The reused target inventory has a retained enumeration failure." });
            for (var ordinal = 0; ordinal < _targets.Records.Count; ordinal++)
            {
                var row = _targets.Records[ordinal];
                if (row.Signature is not ("IDLE" or "ANIO")) continue;
                try
                {
                    var key = ParseForm(row.Form);
                    if (!expected.TryGetValue(key, out var winner) || row.Form != winner.FormKey.ToString() ||
                        row.Signature != winner.Signature || row.Winner != winner.Plugin.Name || row.RecordFlags != winner.Flags ||
                        row.Deleted != winner.IsDeleted || row.Sha256 != Hash(winner.ReadData()) ||
                        !row.Fields.SequenceEqual(winner.ReadSubrecords().Select((field, index) =>
                            new ActorAnimationField(index, field.Signature, field.Data.Length, Hash(field.Data.Span)))))
                        throw new InvalidDataException("Target catalog provenance differs from its exact source winner, flags, payload or fields.");
                    if (!_catalog.TryAdd(key, new(ordinal, row, true)))
                    {
                        _catalog[key] = _catalog[key] with { Valid = false };
                        throw new InvalidDataException("Target inventory repeats a winning declaration.");
                    }
                    result.VerifiedTargetDeclarations++;
                }
                catch (Exception error)
                {
                    valid = false;
                    result.Failures.Add(new { target = row.Form, lane = "incoming-animation-target-catalog", error = error.Message });
                }
            }
            foreach (var pair in expected)
                if (!_catalog.ContainsKey(pair.Key))
                {
                    valid = false;
                    result.Failures.Add(new { target = pair.Key.ToString(), lane = "incoming-animation-target-catalog", error = "The all-winning target catalog omitted this exact IDLE/ANIO winner or deletion." });
                }
            result.TargetCatalogComplete = valid && _catalog.Count == expected.Count;
        }

        private void ReadRecord(FalloutPluginRecord record, IncomingAnimationInspection result)
        {
            var row = new IncomingAnimationDeclaration { Form = record.FormKey.ToString(), Signature = record.Signature,
                Winner = record.Plugin.Name, RecordFlags = record.Flags, Deleted = record.IsDeleted };
            result.Records.Add(row);
            try
            {
                row.Sha256 = Hash(record.ReadData()); var fields = record.ReadSubrecords().ToArray();
                row.Fields = fields.Select((field, index) => new IncomingAnimationField(index, field.Signature,
                    field.Data.Length, Hash(field.Data.Span))).ToArray();
                row.ConditionFieldSlots = fields.Select((field, index) => (field, index)).Where(pair => pair.field.Signature == "CTDA")
                    .Select(pair => pair.index).ToArray();
                if (record.IsDeleted) { row.Disposition = "winning-deletion; no incoming live owner substituted"; return; }
                row.Disposition = "winning incoming declaration; source reader and exact targets inspected; feasibility independent";
                switch (record.Signature)
                {
                    case "PACK": ReadPackage(record, fields, row); break;
                    case "IDLM": ReadCollection(record, fields, row); break;
                    case "INFO": ReadInfo(record, fields, row); break;
                    default: throw new InvalidDataException("Incoming animation source winner changed signature.");
                }
            }
            catch (Exception error) { row.Failures.Add(new { lane = "incoming-animation-source-reader", error = error.Message }); }
        }

        private void ReadPackage(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, IncomingAnimationDeclaration row)
        {
            row.Reader = "FalloutScriptPackage.Read";
            ReadLists(record, fields, row, "package-idle-list");
            string? current = null; int? marker = null;
            var markers = new List<(string Kind, int Ordinal)>();
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                if (field.Signature is "POBA" or "POEA" or "POCA")
                {
                    current = field.Signature; marker = index; markers.Add((current, index));
                }
                else if (field.Signature == "INAM")
                    AddLink(record, row, field, index, 0, 0, "package-event-animation", true, current, marker, null, null, current is not null);
            }
            FalloutScriptPackage? parsed = null;
            try
            {
                parsed = FalloutScriptPackage.Read(record); row.ReaderPassed = true;
                row.IdleFlags = parsed.IdleFlags; row.IdleTimer = parsed.IdleTimer; row.ReturnedIdleCount = parsed.Idles.Count;
                VerifyLists(row, parsed.Idles);
                foreach (var link in row.Links.Where(link => link.Role == "package-event-animation" && link.Event is not null))
                {
                    link.ReturnedByReader = parsed.Events.TryGetValue(link.Event!, out var idle) && idle?.ToString() == link.Target;
                    if (link.ReturnedByReader != true) throw new InvalidDataException("Raw event animation differs from the actual parsed return.");
                }
            }
            finally
            {
                foreach (var entry in markers)
                {
                    var returned = parsed is not null && parsed.EventPrograms.ContainsKey(entry.Kind);
                    var has = parsed is not null && parsed.Events.ContainsKey(entry.Kind);
                    row.Events.Add(new(entry.Kind, entry.Ordinal, fields[entry.Ordinal].Data.Length,
                        row.Links.Count(link => link.EventMarkerOrdinal == entry.Ordinal), returned, has,
                        has ? parsed!.Events[entry.Kind]?.ToString() : null));
                }
            }
        }

        private void ReadCollection(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, IncomingAnimationDeclaration row)
        {
            row.Reader = "FalloutIdleCollection.Read"; ReadLists(record, fields, row, "marker-idle-list");
            var parsed = FalloutIdleCollection.Read(record); row.ReaderPassed = true;
            row.IdleFlags = parsed.IdleFlags; row.IdleTimer = parsed.IdleTimer; row.ReturnedIdleCount = parsed.Idles.Count;
            VerifyLists(row, parsed.Idles);
        }

        private void ReadLists(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, IncomingAnimationDeclaration row, string role)
        {
            for (var ordinal = 0; ordinal < fields.Length; ordinal++)
            {
                var field = fields[ordinal]; if (field.Signature != "IDLA") continue;
                if (field.Data.Length % 4 != 0)
                {
                    row.UnboundAnimationFieldSlots.Add(ordinal);
                    row.Failures.Add(new { field = ordinal, lane = "incoming-animation-list-extent", error = "The raw IDLA has trailing bytes outside an adjusted FormID." });
                }
                if (!row.LinkReadSlots.Contains(ordinal)) row.LinkReadSlots.Add(ordinal);
                for (var entry = 0; entry < field.Data.Length / 4; entry++)
                    AddLink(record, row, field, ordinal, entry * 4, entry, role, false, null, null, null, null, true);
            }
        }

        private static void VerifyLists(IncomingAnimationDeclaration row, IReadOnlyList<FalloutFormKey> parsed)
        {
            foreach (var link in row.Links.Where(link => link.Role is "package-idle-list" or "marker-idle-list"))
            {
                link.ReturnedByReader = link.EntryOrdinal < parsed.Count && parsed[link.EntryOrdinal].ToString() == link.Target;
                if (link.ReturnedByReader != true) throw new InvalidDataException("Raw idle list differs from the actual parsed return.");
            }
        }

        private void ReadInfo(FalloutPluginRecord record, FalloutPluginSubrecord[] fields, IncomingAnimationDeclaration row)
        {
            row.Reader = "FalloutDialogueTopic.Decode";
            var responses = new List<(int Field, int Ordinal)>(); int? response = null; byte? number = null;
            for (var ordinal = 0; ordinal < fields.Length; ordinal++)
            {
                var field = fields[ordinal];
                if (field.Signature == "TRDT")
                {
                    response = responses.Count; number = field.Data.Length >= 13 ? field.Data.Span[12] : null;
                    responses.Add((ordinal, response.Value)); continue;
                }
                if (field.Signature is not ("NAM1" or "NAM2" or "NAM3" or "SNAM" or "LNAM"))
                { response = null; number = null; continue; }
                if (field.Signature is "SNAM" or "LNAM")
                    AddLink(record, row, field, ordinal, 0, 0, field.Signature == "SNAM" ? "response-speaker-animation" : "response-listener-animation",
                        false, null, null, response, number, response is not null);
            }
            FalloutDialogueInfo? parsed = null;
            try
            {
                parsed = FalloutDialogueTopic.Decode(record); row.ReaderPassed = true;
                foreach (var link in row.Links.Where(link => link.ResponseOrdinal is not null))
                {
                    var original = parsed.Responses[link.ResponseOrdinal!.Value];
                    var last = row.Links.Last(candidate => candidate.ResponseOrdinal == link.ResponseOrdinal && candidate.Role == link.Role);
                    link.ReturnedByReader = ReferenceEquals(link, last);
                    var returned = link.Field == "SNAM" ? original.SpeakerAnimation : original.ListenerAnimation;
                    if (ReferenceEquals(link, last) && returned?.ToString() != link.Target)
                        throw new InvalidDataException("Raw response animation differs from the actual parsed last mapping.");
                }
            }
            finally
            {
                foreach (var entry in responses)
                {
                    var original = parsed is not null && entry.Ordinal < parsed.Responses.Count ? parsed.Responses[entry.Ordinal] : null;
                    var bytes = fields[entry.Field].Data;
                    row.Responses.Add(new(entry.Ordinal, entry.Field, bytes.Length, Hash(bytes.Span), bytes.Length >= 13 ? bytes.Span[12] : null,
                        original is not null, original?.SpeakerAnimation?.ToString(), original?.ListenerAnimation?.ToString()));
                }
            }
        }

        private void AddLink(FalloutPluginRecord record, IncomingAnimationDeclaration row, FalloutPluginSubrecord field,
            int ordinal, int offset, int entry, string role, bool optional, string? kind, int? marker, int? response, byte? number, bool owned)
        {
            var bytes = field.Signature == "IDLA" ? 4 : field.Data.Length;
            var encoded = bytes == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[offset..]) : (uint?)null;
            var link = new IncomingAnimationLink { Field = field.Signature, FieldOrdinal = ordinal, Offset = offset, Bytes = bytes,
                EntryOrdinal = entry, Role = role, Event = kind, EventMarkerOrdinal = marker, ResponseOrdinal = response,
                ResponseNumber = number, Encoded = encoded };
            row.Links.Add(link);
            if (!owned)
            {
                row.UnboundAnimationFieldSlots.Add(ordinal); link.ReturnedByReader = false;
                link.Disposition = "unconsumed by actual reader context; no target or optional-null behavior inferred"; return;
            }
            try
            {
                if (encoded is not { } raw) throw new InvalidDataException("Incoming animation FormID has no exact four-byte source extent.");
                var target = optional ? record.Plugin.AdjustOptionalFormId(raw) : record.Plugin.AdjustFormId(raw);
                if (!row.LinkReadSlots.Contains(ordinal)) row.LinkReadSlots.Add(ordinal);
                if (target is null) { link.Disposition = "encoded-absent optional event animation"; return; }
                link.Target = target.Value.ToString(); JoinTarget(target.Value, link);
            }
            catch (Exception error) { link.Failures.Add(new { lane = "incoming-animation-target", error = error.Message }); }
        }

        private void JoinTarget(FalloutFormKey target, IncomingAnimationLink link)
        {
            _records.TryGetWinner(target, out var winner);
            link.Winner = winner?.Plugin.Name; link.Signature = winner?.Signature; link.Deleted = winner?.IsDeleted;
            link.Disposition = winner is null ? "missing-winning-target" : winner.IsDeleted ? "winning-deletion" :
                winner.Signature != "IDLE" ? "wrong-winning-signature" : "exact-master-scoped-winning-IDLE";
            if (winner is null || winner.IsDeleted || winner.Signature != "IDLE")
                throw new InvalidDataException("Incoming animation has " + link.Disposition + ": " + target);
            if (!_catalog.TryGetValue(target, out var retained) || !retained.Valid)
                throw new InvalidDataException("Incoming animation has no verified exact target catalog declaration: " + target);
            link.TargetCatalogOrdinal = retained.Ordinal; link.TargetSha256 = retained.Declaration.Sha256;
            if (retained.Declaration.Failures.Count != 0)
                throw new InvalidDataException("Incoming animation retains the existing target declaration refusal: " + target);
            foreach (var attachment in retained.Declaration.Objects)
            {
                var form = ParseForm(attachment.Form);
                if (!_catalog.TryGetValue(form, out var item) || !item.Valid || item.Declaration.Signature != "ANIO" ||
                    item.Declaration.Deleted || item.Declaration.Failures.Count != 0)
                    throw new InvalidDataException("Incoming animation retains an unverified exact ANIO object declaration: " + form);
                link.ObjectCatalogOrdinals.Add(item.Ordinal);
            }
        }
    }
}
