using System.Collections.ObjectModel;

namespace OpenNV.Runtime.Content;

// Reception, discovery, notifications and tuning are deliberately absent.
internal sealed record FalloutRadioStationIndex(
    IReadOnlyDictionary<FalloutFormKey, FalloutRadioStation> Stations,
    IReadOnlyDictionary<FalloutFormKey, string> Errors) : IFalloutSourceIndex
{
    internal static FalloutRadioStationIndex Read(FalloutPluginStack records, CancellationToken cancellation)
    {
        var bases = new HashSet<FalloutFormKey>();
        foreach (var source in records.EffectiveRecords("TACT"))
        {
            cancellation.ThrowIfCancellationRequested();
            if ((source.Flags & 0x20000) != 0) bases.Add(source.FormKey);
        }
        var stations = new Dictionary<FalloutFormKey, FalloutRadioStation>();
        var errors = new Dictionary<FalloutFormKey, string>();
        if (bases.Count != 0)
            foreach (var reference in records.EffectiveRecords("REFR"))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!bases.Contains(FalloutDialogueTopic.RequiredForm(reference, "NAME"))) continue;
                try { stations.Add(reference.FormKey, FalloutRadioStation.Read(records, reference)); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
                { errors.Add(reference.FormKey, error.Message); }
            }
        cancellation.ThrowIfCancellationRequested();
        return new(new ReadOnlyDictionary<FalloutFormKey, FalloutRadioStation>(stations),
            new ReadOnlyDictionary<FalloutFormKey, string>(errors));
    }
}
