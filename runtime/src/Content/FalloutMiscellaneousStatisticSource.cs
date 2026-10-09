using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMiscellaneousStatisticRow(ushort Index, string ScriptName, string SettingName,
    string Text, int InitialValue, FalloutFormKey? SettingOwner = null, string? SettingOwnerSha256 = null,
    string? SettingWinner = null);

// The catalogue and its setting association are read from the selected original.
// Engine hashes admit neutral consumers, never a game/display-name branch.
internal sealed record FalloutMiscellaneousStatisticSource(string EngineSha256, string RuntimeSha256,
    string ContractSha256, uint StatsMenuId, uint? ChallengeEvent, ushort? SleepStartIndex,
    IReadOnlyList<FalloutMiscellaneousStatisticRow> Rows)
{
    private const string Contract = "misc-statistics/v1;source-indexed-catalogue-and-generated-string-setting;" +
        "constructor-signed-int32-zero;get-signed-int32-to-double;delta-zero-before-index;unchecked-int32-add;" +
        "optional-source-challenge-event-after-value;actual-stats-menu-probe-after-event;" +
        "refresh-existing-menu-only;attempted-prefix-no-retry-or-cold-replay";
    internal string Identity => Hash(JsonSerializer.Serialize(this));

    internal static FalloutMiscellaneousStatisticSource Read(FalloutPluginStack records,
        FalloutAdvancementRuntimeReceipt runtime)
    {
        runtime.Validate();
        var source = records.OwnedSource ?? throw new NotSupportedException("Statistics have no selected owned installation.");
        var catalogue = FalloutExecutableStringTable.ReadMiscellaneousStatistics(source.FalloutExecutablePath,
            runtime.EngineSha256);
        var rows = catalogue.Names.Select((name, index) => new FalloutMiscellaneousStatisticRow(
            checked((ushort)index), name, SettingName(catalogue.SettingFormat, index), name, 0)).ToArray();
        var names = rows.Select(row => row.SettingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, (string Text, FalloutFormKey Owner, string Sha256, string Winner)>(StringComparer.OrdinalIgnoreCase);
        var groups = records.Plugins.SelectMany(plugin => plugin.Plugin.Records).Where(record => record.Signature == "GMST")
            .GroupBy(record => record.FormKey, FalloutFormKeyComparer.Instance);
        foreach (var group in groups)
        {
            if (!records.TryGetWinner(group.Key, out var winner)) throw new InvalidDataException("Statistic GMST lost its winning owner.");
            if (winner.IsDeleted)
            {
                // A deleted winner may omit EDID. Its old registered identity
                // still cannot become an executable-default success.
                if (group.Any(record => record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                    names.Contains(FalloutDialogueTopic.Text(field.Data.Span)))))
                    throw new NotSupportedException("A statistic string setting has a deleted winning owner.");
                continue;
            }
            var fields = winner.ReadSubrecords().ToArray();
            var ids = fields.Where(field => field.Signature == "EDID").ToArray();
            if (ids.Length != 1) continue;
            var name = FalloutDialogueTopic.Text(ids[0].Data.Span);
            if (!names.Contains(name)) continue;
            var data = fields.Where(field => field.Signature == "DATA").ToArray();
            if (data.Length != 1 || !overrides.TryAdd(name, (FalloutDialogueTopic.Text(data[0].Data.Span), winner.FormKey,
                Convert.ToHexString(SHA256.HashData(winner.ReadData())).ToLowerInvariant(), winner.Plugin.Name)))
                throw new InvalidDataException("Statistic string setting has ambiguous winning data/identity.");
        }
        rows = rows.Select(row => overrides.TryGetValue(row.SettingName, out var setting) ? row with
        { Text = setting.Text, SettingOwner = setting.Owner, SettingOwnerSha256 = setting.Sha256, SettingWinner = setting.Winner } : row).ToArray();
        var declaration = Consumers(runtime.EngineSha256);
        var result = new FalloutMiscellaneousStatisticSource(runtime.EngineSha256, runtime.SourceSha256, Hash(Contract),
            catalogue.StatsMenuId, declaration.ChallengeEvent, declaration.SleepStartIndex, Array.AsReadOnly(rows));
        result.Validate(); return result;
    }

    internal static (uint? ChallengeEvent, ushort? SleepStartIndex) Consumers(string engineSha256) => engineSha256 switch
    {
        "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => (11, 23),
        "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => (null, null),
        _ => throw new NotSupportedException("Selected executable has no reviewed miscellaneous-statistic consumer contract."),
    };
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(EngineSha256) || !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) ||
            ContractSha256 != Hash(Contract) || StatsMenuId == 0 || Rows is null || Rows.Count is < 1 or > ushort.MaxValue ||
            (ChallengeEvent, SleepStartIndex) != Consumers(EngineSha256))
            throw new InvalidDataException("Statistic source declaration is incomplete or drifted.");
        var scriptNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index] ?? throw new InvalidDataException("Statistic source row is missing.");
            if (row.Index != index || string.IsNullOrWhiteSpace(row.ScriptName) || row.ScriptName.Contains('\0') ||
                string.IsNullOrWhiteSpace(row.SettingName) || row.SettingName[0] != 's' || row.Text is null ||
                row.InitialValue != 0 || !scriptNames.Add(row.ScriptName) || !settingNames.Add(row.SettingName) ||
                row.SettingOwner is null && (row.SettingOwnerSha256 is not null || row.SettingWinner is not null) || row.SettingOwner is { } setting &&
                (setting.ObjectId == 0 || string.IsNullOrWhiteSpace(setting.OwnerPlugin) || !FalloutAdvancementRuntimeReceipt.Digest(row.SettingOwnerSha256) ||
                    string.IsNullOrWhiteSpace(row.SettingWinner)))
                throw new InvalidDataException("Statistic source catalogue/order/constructor is invalid.");
        }
        if (SleepStartIndex is { } sleep && sleep >= Rows.Count)
            throw new InvalidDataException("The source sleep-start statistic is outside its actual catalogue.");
    }
    internal void RequireCurrent(FalloutMiscellaneousStatisticSource current)
    {
        Validate(); current.Validate();
        if (Identity != current.Identity) throw new InvalidDataException("Saved statistics differ from current selected source/catalogue/settings.");
    }
    internal ushort Index(string name)
    {
        for (var index = 0; index < Rows.Count; index++)
            if (StringComparer.OrdinalIgnoreCase.Equals(Rows[index].ScriptName, name)) return checked((ushort)index);
        throw new NotSupportedException("The statistic argument has no original source catalogue name.");
    }
    internal static string SettingName(string format, int index)
    {
        if (index < 0) throw new InvalidDataException("Statistic formatter index is negative.");
        var match = System.Text.RegularExpressions.Regex.Match(format, @"^(s[A-Za-z][A-Za-z0-9_]*)%(?:0([1-9][0-9]?))?d([A-Za-z0-9_]*)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success) throw new NotSupportedException("Statistic setting-name formatter has an unowned conversion.");
        var width = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var number = index.ToString(width == 0 ? "D" : "D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return match.Groups[1].Value + number + match.Groups[3].Value;
    }
    internal static string CurrentContractSha256 => Hash(Contract);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record FalloutMiscellaneousStatisticCatalogue(IReadOnlyList<string> Names, string SettingFormat, uint StatsMenuId);
