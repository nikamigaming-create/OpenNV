using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPipBoyRadioSnapshot(FalloutFormKey? LastStation = null, string? SourceSha256 = null)
{
    internal void Validate()
    {
        if (LastStation is null ? SourceSha256 is not null : SourceSha256 is null ||
            SourceSha256.Length != 64 || !SourceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Saved Pip-Boy station identity is invalid.");
    }
}

// The player receiver owns its playback lease independently of world radios,
// dialogue and ambient SOUN voices. A missing broadcast scheduler cannot tune.
internal sealed class FalloutPipBoyRadio(FalloutPluginStack records)
{
    private Func<FalloutRadioStation, IDisposable>? _prepare;
    private IDisposable? _playback;
    internal FalloutFormKey? LastStation { get; private set; }
    internal FalloutFormKey? CurrentStation => _playback is null ? null : LastStation;
    internal long Revision { get; private set; }
    internal long OffRequests { get; private set; }
    internal object State => new
    {
        currentStation = CurrentStation?.ToString(),
        lastStation = LastStation?.ToString(),
        enabled = _playback is not null,
        bound = _prepare is not null,
        Revision,
        OffRequests,
        continuation = "off-state-and-last-station;active-broadcast-timeline-unbound"
    };

    internal IDisposable Bind(Func<FalloutRadioStation, IDisposable> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        if (_prepare is not null) throw new InvalidOperationException("Pip-Boy radio already has a playback owner.");
        _prepare = prepare;
        return new Scope(() => { Off(); _prepare = null; });
    }

    internal void Select(FalloutRadioStation station)
    {
        if (FalloutRadioStation.Read(records, records.GetEffective(station.Reference)) != station)
            throw new InvalidDataException("Receiver station differs from its winning source.");
        if (!station.PipBoy) throw new InvalidDataException("Pip-Boy receiver requires a source Pip-Boy station.");
        if (CurrentStation == station.Reference) return;
        var next = (_prepare ?? throw new NotSupportedException("Pip-Boy tuning requires its station broadcast scheduler."))(station) ??
            throw new InvalidDataException("Pip-Boy playback owner prepared no lease.");
        try { _playback?.Dispose(); }
        catch { next.Dispose(); throw; }
        _playback = next; LastStation = station.Reference; ++Revision;
    }

    internal void Off()
    {
        _playback?.Dispose();
        _playback = null;
        ++OffRequests; ++Revision;
    }

    private string SourceHash(FalloutFormKey reference)
    {
        var record = records.GetEffective(reference);
        var station = FalloutRadioStation.Read(records, record);
        if (!station.PipBoy) throw new InvalidDataException("Saved receiver station has no Pip-Boy source flag.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(record.ReadData()); hash.AppendData(records.GetEffective(station.Base).ReadData());
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal FalloutPipBoyRadioSnapshot Capture()
    {
        if (_playback is not null) throw new NotSupportedException("Saving an active Pip-Boy station requires its broadcast timeline.");
        return new(LastStation, LastStation is { } station ? SourceHash(station) : null);
    }

    internal void Restore(FalloutPipBoyRadioSnapshot? snapshot)
    {
        if (Revision != 0 || _playback is not null || LastStation is not null)
            throw new InvalidOperationException("Pip-Boy radio restoration requires a fresh owner.");
        if (snapshot is null) return;
        snapshot.Validate();
        if (snapshot.LastStation is { } station && SourceHash(station) != snapshot.SourceSha256)
            throw new InvalidDataException("Saved receiver station differs from its source records.");
        LastStation = snapshot.LastStation;
    }

    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
    }
}
