using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CellGraphAudit
{
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static object DoorAccess(FalloutReferenceWorld world, FalloutFormKey reference)
    {
        int? locked = null, level = null; string? lockedError = null, levelError = null;
        try { locked = world.GetLocked(reference); } catch (Exception error) { lockedError = error.Message; }
        try { level = world.GetLockLevel(reference); } catch (Exception error) { levelError = error.Message; }
        return new { reference = reference.ToString(), locked, level, lockedError, levelError, physicalActivationUnverified = true };
    }

    private static object EnableDomain(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey subject)
    {
        var nodes = new List<object>(); var visited = new HashSet<FalloutFormKey>();
        var current = subject; var parity = false;
        while (records.RuntimeFormId(current) != 0x14)
        {
            if (!visited.Add(current)) throw new InvalidDataException("Source enable-parent cycle: " + current);
            var record = records.GetEffective(current); var state = world.Get(current); var parent = state.EnableParent;
            nodes.Add(new { reference = current.ToString(), winner = record.Plugin.Name, sha256 = Hash(record.ReadData()),
                parent = parent?.Reference.ToString(), opposite = parent?.Opposite, popIn = parent?.PopIn,
                state.Enabled, state.Deleted, state.Taken, outputIfAbsentAtThisNode = parity });
            if (parent is null)
            {
                var original = state.Enabled;
                bool whenDisabled, whenEnabled;
                try
                {
                    state.Enabled = false; whenDisabled = world.IsEnabled(subject);
                    state.Enabled = true; whenEnabled = world.IsEnabled(subject);
                }
                finally { state.Enabled = original; }
                return new { nodes, root = current.ToString(), rootIsEnginePlayer = false, inverted = parity,
                    domain = new[] { new { rootEnabled = false, subjectEnabled = whenDisabled }, new { rootEnabled = true, subjectEnabled = whenEnabled } },
                    validation = "Two independent root enable alternatives evaluated by the actual owner in a disposable reference world, then restored." };
            }
            parity ^= parent.Opposite; current = parent.Reference;
        }
        return new { nodes, root = current.ToString(), rootIsEnginePlayer = true, inverted = parity,
            constantEnabled = !parity, validation = "Engine player source enable parent is a constant; no independent toggle." };
    }

    private static object PackageAlternatives(FalloutPluginStack records, FalloutFormKey actor)
    {
        var owners = new Dictionary<FalloutFormKey, FalloutPluginRecord>(); var templateNodes = new List<object>();
        var active = new HashSet<FalloutFormKey>(); var completed = new HashSet<FalloutFormKey>();
        var failures = new List<object>();
        void Walk(FalloutFormKey key)
        {
            if (!active.Add(key)) throw new InvalidDataException("Package template source graph has a cycle: " + key);
            try
            {
                if (completed.Contains(key)) return;
                var record = records.GetEffective(key); var fields = record.ReadSubrecords().ToArray();
                if (record.Signature is "NPC_" or "CREA")
                {
                    var acbs = fields.Where(field => field.Signature == "ACBS").ToArray();
                    if (acbs.Length != 1 || acbs[0].Data.Length != 24) throw new InvalidDataException("Package template ACBS extent is invalid: " + key);
                    var flags = BinaryPrimitives.ReadUInt16LittleEndian(acbs[0].Data.Span[22..]);
                    if ((flags & 32) == 0)
                    {
                        owners.TryAdd(key, record); templateNodes.Add(new { actor = key.ToString(), flags, packageOwner = true });
                    }
                    else
                    {
                        var links = fields.Where(field => field.Signature == "TPLT").ToArray();
                        if (links.Length != 1 || links[0].Data.Length != 4) throw new InvalidDataException("Package template TPLT extent is invalid: " + key);
                        var target = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span));
                        templateNodes.Add(new { actor = key.ToString(), flags, template = target.ToString() }); Walk(target);
                    }
                }
                else if (record.Signature is "LVLN" or "LVLC")
                {
                    var entries = fields.Where(field => field.Signature == "LVLO").Select(field =>
                    {
                        if (field.Data.Length != 12) throw new InvalidDataException("Package leveled template entry extent is invalid: " + key);
                        return new { level = BinaryPrimitives.ReadUInt16LittleEndian(field.Data.Span),
                            actor = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[4..])),
                            count = BinaryPrimitives.ReadUInt16LittleEndian(field.Data.Span[8..]) };
                    }).ToArray();
                    var chance = fields.Single(field => field.Signature == "LVLD").Data;
                    var flags = fields.Single(field => field.Signature == "LVLF").Data;
                    if (chance.Length != 1 || flags.Length != 1) throw new InvalidDataException("Package leveled template chance/flags extent is invalid: " + key);
                    templateNodes.Add(new { list = key.ToString(), chanceNone = chance.Span[0], flags = flags.Span[0],
                        entries = entries.Select(entry => new { entry.level, actor = entry.actor.ToString(), entry.count }),
                        chanceGlobalExtents = fields.Where(field => field.Signature == "LVLG").Select(field => field.Data.Length),
                        extraDataExtents = fields.Where(field => field.Signature == "COED").Select(field => field.Data.Length) });
                    foreach (var entry in entries) Walk(entry.actor);
                }
                else throw new InvalidDataException("Package template link is not actor/list: " + key + " " + record.Signature);
                completed.Add(key);
            }
            finally { active.Remove(key); }
        }
        try { Walk(actor); } catch (Exception error) { failures.Add(new { lane = "package-template-alternatives", error = error.Message }); }
        var lists = new List<object>();
        foreach (var owner in owners.Values)
        {
            var alternatives = new List<object>(); var priority = 0;
            foreach (var link in owner.ReadSubrecords().Where(field => field.Signature == "PKID"))
            {
                var slot = priority++; FalloutFormKey? key = null; FalloutPluginRecord? record = null;
                object? parsed = null; object? schedule = null; FalloutPackageData? packageData = null;
                var conditions = new List<object>(); var errors = new List<object>();
                try
                {
                    if (link.Data.Length != 4) throw new InvalidDataException("PKID extent is invalid.");
                    key = owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(link.Data.Span)); record = records.GetEffective(key.Value);
                    if (record.Signature != "PACK") throw new InvalidDataException("PKID does not bind PACK.");
                    try
                    {
                        packageData = FalloutPackageData.Read(record);
                        var package = FalloutScriptPackage.Read(record);
                        parsed = new { package.EditorId, package.Procedure, package.Flags, package.LocationType,
                            locationReference = package.LocationReference?.ToString(), package.LocationRadius, package.IdleFlags, package.IdleTimer,
                            idles = package.Idles.Select(idle => idle.ToString()),
                            events = package.EventPrograms.Select(pair => new { kind = pair.Key, topic = pair.Value.Topic?.ToString(),
                                sourceStatements = FalloutDialogueTopic.CodeLines(pair.Value.Source).Count(),
                                sourceSha256 = Hash(System.Text.Encoding.UTF8.GetBytes(pair.Value.Source)),
                                compiledBytes = pair.Value.Fields.Where(field => field.Signature == "SCDA").Sum(field => field.Data.Length) }) };
                    }
                    catch (Exception error) { errors.Add(new { lane = "package-reader", error = error.Message }); }
                    try { schedule = FalloutPackageSchedule.Read(record); }
                    catch (Exception error) { errors.Add(new { lane = "package-schedule", error = error.Message }); }
                    foreach (var field in record.ReadSubrecords().Where(field => field.Signature == "CTDA"))
                    {
                        try
                        {
                            var condition = FalloutCondition.Read(record, field.Data.Span);
                            conditions.Add(new { condition.Function, condition.Flags, condition.RunOn, condition.Argument1, condition.Argument2, condition.Reference,
                                comparison = (condition.Flags & 4) == 0 ? (float?)condition.Comparison : null,
                                comparisonGlobal = (condition.Flags & 4) == 0 ? null : record.Plugin.AdjustFormId(BitConverter.SingleToUInt32Bits(condition.Comparison)).ToString(),
                                authoritativeEvaluation = "not-executed; function ownership joins the whole quest/script audit" });
                        }
                        catch (Exception error) { errors.Add(new { lane = "package-condition", error = error.Message }); }
                    }
                }
                catch (Exception error) { errors.Add(new { lane = "package-link", error = error.Message }); }
                alternatives.Add(new { priority = slot, package = key?.ToString(), winner = record?.Plugin.Name,
                    sha256 = record is null ? null : Hash(record.ReadData()), sourceProcedure = packageData?.Procedure,
                    packageData, packageDataExtent = packageData?.SourceExtent,
                    parsed, schedule, conditions, errors, nativeProcedureExecution = "unverified unless joined to the selected native snapshot" });
            }
            lists.Add(new { owner = owner.FormKey.ToString(), sha256 = Hash(owner.ReadData()), authoredPriorityAlternatives = alternatives });
        }
        return new { templateNodes, packageOwnerAlternatives = lists, failures,
            domain = "Finite authored identity graph, retaining every leveled actor outcome and priority candidate. Arbitrary clock/global/numeric script valuations are not enumerated." };
    }

}
