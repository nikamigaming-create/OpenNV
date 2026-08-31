using System.Security.Cryptography;
using System.Text.Json;


using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout1;

/// <summary>One explicit source-script use contract for the VAULT13 MAP flare stack.</summary>
internal sealed record Fo1DestinationFlareUseContract(
    string Path, string Sha256, int HostSerial, string Symbol, string Pid,
    string PrototypeSha256, string ScriptSha256, ClassicScriptProgram Program)
{
    private const string Schema = "opennv-fo1-destination-flare-use/v1";

    internal static Fo1DestinationFlareUseContract Load(
        string path, Fo1DestinationInventoryInteractionContract interaction)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(path);
        var bytes = File.ReadAllBytes(resolved);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (Required(root, "schema") != Schema ||
            Required(root, "status") != "compiled-owned-scripted-flare-use")
            throw new InvalidOperationException("Unexpected Fallout destination flare use descriptor.");
        var source = root.GetProperty("interaction");
        if (Required(source, "path") != interaction.Path || Required(source, "sha256") != interaction.Sha256)
            throw new InvalidOperationException("Fallout flare use descriptor interaction join drifted.");
        var item = root.GetProperty("item");
        var hostSerial = item.GetProperty("hostSerial").GetInt32();
        var symbol = Required(item, "symbol");
        var pid = Required(item, "pid");
        var prototypeSha256 = Required(item, "prototypeSha256");
        var owned = interaction.Host.Items.SingleOrDefault(row =>
            row.Symbol == symbol && row.Pid == pid && row.PrototypeSha256 == prototypeSha256);
        if (hostSerial != interaction.Host.Serial || !owned.IsValid ||
            Required(item.GetProperty("profile"), "subtypeName") != "weapon")
            throw new InvalidOperationException("Fallout flare use descriptor item is not the admitted MAP stack.");
        var semantics = root.GetProperty("semantics");
        if (Required(semantics, "action") != "use_proc" ||
            Required(semantics, "result") != "lit-state" ||
            !semantics.GetProperty("storesGameTime").GetBoolean() ||
            Required(semantics, "expiry") != "unimplemented-fail-closed")
            throw new InvalidOperationException("Fallout flare use descriptor semantics are not bounded.");
        var scriptSha256 = Required(root.GetProperty("script"), "sha256");
        if (!Hash(scriptSha256))
            throw new InvalidOperationException("Fallout flare use descriptor script hash is invalid.");
        var program = ClassicScriptProgram.Parse(root.GetProperty("effectProgram"));
        return new Fo1DestinationFlareUseContract(
            resolved, sha256, hostSerial, symbol, pid, prototypeSha256, scriptSha256,
            program);
    }

    internal object Report() => new
    {
        schema = Schema,
        path = Path,
        sha256 = Sha256,
        HostSerial,
        Symbol,
        Pid,
        PrototypeSha256,
        ScriptSha256,
        semantics = new { action = "use_proc", result = "lit-state", expiry = "unimplemented-fail-closed" },
    };

    private static string Required(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException($"Fallout flare use descriptor is missing {name}.");

    private static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
