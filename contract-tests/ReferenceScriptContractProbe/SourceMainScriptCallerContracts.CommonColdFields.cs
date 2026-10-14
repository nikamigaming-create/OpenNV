using System.Text.Json;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RequireColdCommonFields(FalloutProcessCommonSnapshot before, FalloutProcessCommonSnapshot after)
    {
        var normalized = new List<FalloutProcessCommonEntry>();
        Require(after.Current.Count == before.Current.Count && before.CapturedProcess != after.CapturedProcess,
            "Cold Common lost source entries or reused its prior process.");
        foreach (var current in after.Current)
        {
            var old = before.Current.Single(entry => entry.Source.Reference == current.Source.Reference);
            if (current.Packages is not { } packages || old.Packages is not { } prior)
            { normalized.Add(current); continue; }
            var live = current.Phase is not (FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired);
            normalized.Add(current with { Packages = packages with
            {
                Current = Normalize(packages.Current, prior.Current, live),
                Primary = packages.Primary is { } primary && prior.Primary is { } oldPrimary ? Normalize(primary, oldPrimary, live) : packages.Primary
            } });
        }
        Require(JsonSerializer.Serialize(normalized) == JsonSerializer.Serialize(before.Current),
            "Cold Common changed retained source fields/constructors/move/writer histories or replayed field stores.");
        FalloutProcessPackageFields Normalize(FalloutProcessPackageFields current, FalloutProcessPackageFields old, bool live)
        {
            if (!live) return current;
            Require(current.Publication.Process == after.CapturedProcess && current.Publication.Factory != old.Publication.Factory &&
                current.Publication.PreviousProcess == old.Publication.Process && current.Publication.PreviousFactory == old.Publication.Factory &&
                current.Publications is { } publications && old.Publications is { } prior && publications.Count == prior.Count + 1 &&
                publications[^1] == current.Publication && publications.Take(prior.Count).SequenceEqual(prior),
                "Cold package field factory lost its fresh actual process/publication handoff.");
            return current with { Publication = old.Publication, Publications = old.Publications };
        }
    }
}
