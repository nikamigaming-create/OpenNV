using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public partial class NativeFallout3ActorLedgerAudit : Node
{
    public override void _Ready()
    {
        var exitCode = 1;
        try
        {
            var arguments = ParseArguments(OS.GetCmdlineUserArgs());
            var manifestPath = Path.GetFullPath(arguments["source-stack"]);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(manifestBytes);
            var manifest = document.RootElement;
            var dataRoot = Path.GetFullPath(manifest.GetProperty("roots")[0]
                .GetProperty("root").GetString()!);
            RuntimeOwnedContentSource.Configure(
                dataRoot,
                manifestPath,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                manifest.GetProperty("stackId").GetString());
            var source = RuntimeOwnedContentSource.Current!;
            if (source.Game != RuntimeOwnedContentSource.Fallout3Game)
                throw new InvalidDataException(
                    "The actor ledger audit requires standalone Fallout 3 and rejects TTW/New Vegas stacks.");

            using var stack = FalloutPluginStack.Load(source.PluginSources);
            var ledger = FalloutActorCreatureLedgerBuilder.Build(stack, source);
            var blockerSummary = ledger.Rows
                .Where(row => row.Blocker is not null)
                .GroupBy(row => row.Blocker!, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}:{group.Count()}")
                .ToArray();
            GD.Print(
                $"OPENNV_NATIVE_FO3_ACTOR_LEDGER game={ledger.Game} " +
                $"effectiveCells={ledger.EffectiveCells} cellsWithActors={ledger.CellsWithActors} " +
                $"references={ledger.TotalReferences} achr={ledger.HumanoidReferences} " +
                $"acre={ledger.CreatureReferences} uniqueNpcBases={ledger.UniqueHumanoidBases} " +
                $"uniqueCreatureBases={ledger.UniqueCreatureBases} disabled={ledger.InitiallyDisabledReferences} " +
                $"modelResolved={ledger.ModelResolvedReferences} modelMissing={ledger.ModelMissingReferences} " +
                $"modelAbsent={ledger.ModelAbsentReferences} templateLinkedBases={ledger.TemplateLinkedBases} " +
                $"blocked={ledger.BlockedReferences} " +
                $"blockers={(blockerSummary.Length == 0 ? "none" : string.Join(',', blockerSummary))} " +
                "source=standalone-owned-fallout3 stack=distinct-from-ttw cache=none writes=zero " +
                "rendered=false video=false");

            if (ledger.TotalReferences == 0 || ledger.EffectiveCells == 0)
                throw new InvalidDataException("Fallout 3 actor ledger is unexpectedly empty.");
            if (ledger.BlockedReferences != 0)
                throw new InvalidDataException(
                    $"Fallout 3 actor ledger has {ledger.BlockedReferences} fail-closed placements.");
            exitCode = 0;
        }
        catch (Exception error)
        {
            GD.PrintErr(
                $"OPENNV_NATIVE_FO3_ACTOR_LEDGER_ERROR {error.GetType().Name}: {error.Message} " +
                $"inner={error.InnerException?.Message ?? "none"}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
    }

    private static Dictionary<string, string> ParseArguments(IReadOnlyList<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; ++index)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
                throw new ArgumentException("Audit arguments must be --name value pairs.");
            result.Add(args[index][2..], args[++index]);
        }
        if (!result.ContainsKey("source-stack"))
            throw new ArgumentException("Native Fallout 3 actor ledger audit requires --source-stack.");
        return result;
    }
}
