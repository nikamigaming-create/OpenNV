using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicMapIntProgram(
    int ScriptsListIndex,
    string Program,
    string LogicalPath,
    string Sha256,
    int ProcedureCount,
    int RandomSiteCount);

internal sealed record ClassicMapIntScriptSlot(
    int Order,
    int Type,
    int Extent,
    int Slot,
    string Sid,
    int ScriptIndex,
    ClassicMapIntProgram Program);

internal sealed record ClassicMapIntRandomSite(
    string Owner,
    string? Sid,
    string Program,
    string Procedure,
    int Offset,
    string OperandKind,
    int? Minimum,
    int? Maximum,
    string ExpressionStatus,
    string? Unsupported,
    ClassicIntExpression? MinimumExpression,
    ClassicIntExpression? MaximumExpression)
{
    internal string SourceIdentity => $"{Owner}:{Sid ?? "map-header"}:" +
        $"{Program}:{Procedure}:0x{Offset:x}";
}

internal sealed record ClassicMapIntInitialization(
    ClassicMapIntProgram? HeaderProgram,
    IReadOnlyList<ClassicMapIntScriptSlot> ScriptSlots,
    IReadOnlyList<ClassicMapIntRandomSite> RandomSites,
    bool EngineInterleavingTransported);

internal static class ClassicMapIntInitializationOwner
{
    private const string Schema = "opennv-classic-map-int-initialization/v1";
    private const string InventorySchema = "opennv-classic-int-initialization-inventory/v3";
    private const string RandomOpcode = "80b4";
    private const int Sha256HexCharacterCount = 64;

    internal static ClassicMapIntInitialization Parse(
        JsonElement source,
        ClassicMapInitialization map)
    {
        if (RequiredString(source, "schema") != Schema ||
            source.GetProperty("engineInterleavingTransported").GetBoolean())
            throw new InvalidOperationException(
                "Classic MAP INT initialization boundary drifted.");
        var header = source.GetProperty("mapHeader");
        var storedHeaderIndex = header.GetProperty("storedScriptIndex").GetInt32();
        var headerProgramElement = header.GetProperty("program");
        var headerProgram = headerProgramElement.ValueKind == JsonValueKind.Null
            ? null
            : ParseProgram(headerProgramElement);
        if (headerProgram is null && (storedHeaderIndex != 0 ||
                RequiredString(header, "indexSemantics") !=
                    "MAP-header-zero-means-no-program") ||
            headerProgram is not null && (storedHeaderIndex <= 0 ||
                RequiredString(header, "indexSemantics") !=
                    "MAP-header-one-based-to-scripts-list"))
            throw new InvalidOperationException(
                "Classic MAP header INT index semantics drifted.");

        var sourceSlots = source.GetProperty("liveScriptSlots").EnumerateArray().ToArray();
        if (sourceSlots.Length != map.ScriptSlots.Count)
            throw new InvalidOperationException(
                "Classic MAP INT live script slot count drifted.");
        var slots = new List<ClassicMapIntScriptSlot>();
        for (var index = 0; index < sourceSlots.Length; index++)
        {
            var row = sourceSlots[index];
            var mapRow = map.ScriptSlots[index];
            var order = row.GetProperty("order").GetInt32();
            var type = row.GetProperty("type").GetInt32();
            var extent = row.GetProperty("extent").GetInt32();
            var slot = row.GetProperty("slot").GetInt32();
            var sid = RequiredString(row, "sid");
            var scriptIndex = row.GetProperty("scriptIndex").GetInt32();
            if (order != index || type != mapRow.Type || extent != mapRow.Extent ||
                slot != mapRow.Slot || sid != mapRow.Sid || scriptIndex < 0)
                throw new InvalidOperationException(
                    $"Classic MAP INT source-order join drifted at slot {index}.");
            var program = ParseProgram(row.GetProperty("program"));
            if (program.ScriptsListIndex != scriptIndex)
                throw new InvalidOperationException(
                    $"Classic MAP INT scripts.lst join drifted at slot {index}.");
            slots.Add(new ClassicMapIntScriptSlot(
                order, type, extent, slot, sid, scriptIndex, program));
        }

        var randomSites = source.GetProperty("randomSites").EnumerateArray()
            .Select(row =>
            {
                var operandKind = RequiredString(row, "operandKind");
                var minimumElement = row.GetProperty("minimum");
                var maximumElement = row.GetProperty("maximum");
                int? minimum = minimumElement.ValueKind == JsonValueKind.Null
                    ? null
                    : minimumElement.GetInt32();
                int? maximum = maximumElement.ValueKind == JsonValueKind.Null
                    ? null
                    : maximumElement.GetInt32();
                if (operandKind == "literal-inclusive-range" &&
                    (minimum is null || maximum is null || minimum > maximum) ||
                    operandKind == "source-stack-expression" &&
                    (minimum is not null || maximum is not null) ||
                    operandKind is not (
                        "literal-inclusive-range" or "source-stack-expression"))
                    throw new InvalidOperationException(
                        "Classic MAP INT RANDOM operand contract drifted.");
                var expressionStatus = RequiredString(row, "expressionStatus");
                var unsupportedElement = row.GetProperty("unsupported");
                var unsupported = unsupportedElement.ValueKind == JsonValueKind.Null
                    ? null
                    : RequiredString(row, "unsupported");
                var minimumExpression = OptionalExpression(
                    row.GetProperty("minimumExpression"));
                var maximumExpression = OptionalExpression(
                    row.GetProperty("maximumExpression"));
                if (expressionStatus == "executable" &&
                    (unsupported is not null || minimumExpression is null ||
                        maximumExpression is null) ||
                    expressionStatus == "unsupported" &&
                    (unsupported is null || minimumExpression is not null &&
                        maximumExpression is not null) ||
                    expressionStatus is not ("executable" or "unsupported"))
                    throw new InvalidOperationException(
                        "Classic MAP INT expression status drifted.");
                var owner = RequiredString(row, "owner");
                var sid = row.GetProperty("sid").ValueKind == JsonValueKind.Null
                    ? null
                    : RequiredString(row, "sid");
                if (owner == "map-header" && sid is not null ||
                    owner == "live-map-script-slot" &&
                    (sid is null || !slots.Any(slot => slot.Sid == sid)))
                    throw new InvalidOperationException(
                        "Classic MAP INT RANDOM owner join drifted.");
                return new ClassicMapIntRandomSite(
                    owner,
                    sid,
                    RequiredString(row, "program"),
                    RequiredString(row, "procedure"),
                    row.GetProperty("offset").GetInt32(),
                    operandKind,
                    minimum,
                    maximum,
                    expressionStatus,
                    unsupported,
                    minimumExpression,
                    maximumExpression);
            })
            .ToArray();
        var expectedRandomSites = (headerProgram?.RandomSiteCount ?? 0) +
            slots.Sum(row => row.Program.RandomSiteCount);
        if (randomSites.Length != expectedRandomSites || randomSites.Any(row =>
            row.Owner == "map-header" && row.Program != headerProgram?.Program ||
            row.Owner == "live-map-script-slot" && !slots.Any(slot =>
                slot.Sid == row.Sid && slot.Program.Program == row.Program)))
            throw new InvalidOperationException(
                "Classic MAP INT RANDOM source-instance coverage drifted.");
        return new ClassicMapIntInitialization(
            headerProgram, slots, randomSites, false);
    }

    private static ClassicMapIntProgram ParseProgram(JsonElement source)
    {
        var inventory = source.GetProperty("inventory");
        if (RequiredString(inventory, "schema") != InventorySchema ||
            RequiredString(inventory, "randomOpcode") != RandomOpcode)
            throw new InvalidOperationException(
                "Classic MAP INT procedure inventory drifted.");
        var procedures = inventory.GetProperty("procedures").EnumerateArray().ToArray();
        if (procedures.Length == 0 || procedures.Any(row =>
            string.IsNullOrWhiteSpace(row.GetProperty("name").GetString()) ||
            row.GetProperty("bodyOffset").GetInt32() >=
                row.GetProperty("bodyEndOffset").GetInt32()))
            throw new InvalidOperationException(
                "Classic MAP INT procedure inventory is invalid.");
        return new ClassicMapIntProgram(
            source.GetProperty("scriptsListIndex").GetInt32(),
            RequiredString(source, "program"),
            RequiredString(source, "logicalPath"),
            RequiredHash(source, "sha256"),
            procedures.Length,
            inventory.GetProperty("randomSites").GetArrayLength());
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Classic MAP INT string is empty: {property}.");
        return value;
    }

    private static ClassicIntExpression? OptionalExpression(JsonElement source)
    {
        if (source.ValueKind == JsonValueKind.Null)
            return null;
        var kind = RequiredString(source, "kind");
        var offset = source.GetProperty("offset").GetInt32();
        var valueElement = source.GetProperty("value");
        int? value = valueElement.ValueKind == JsonValueKind.Null
            ? null
            : valueElement.GetInt32();
        var arguments = source.GetProperty("arguments").EnumerateArray()
            .Select(row => OptionalExpression(row) ?? throw new InvalidOperationException(
                "Classic INT expression argument is null."))
            .ToArray();
        if (kind == "literal" && (value is null || arguments.Length != 0) ||
            kind != "literal" && value is not null)
            throw new InvalidOperationException(
                "Classic INT expression value contract drifted.");
        return new ClassicIntExpression(kind, offset, value, arguments);
    }

    private static string RequiredHash(JsonElement source, string property)
    {
        var value = RequiredString(source, property).ToLowerInvariant();
        if (value.Length != Sha256HexCharacterCount ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Classic MAP INT hash is invalid: {property}.");
        return value;
    }
}
