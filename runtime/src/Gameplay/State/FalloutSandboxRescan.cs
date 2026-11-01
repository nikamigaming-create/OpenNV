using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// The process timer is independent of game-hour/time-scale arithmetic.
internal sealed class FalloutSandboxProcessClock(Func<uint>? ticks = null)
{
    private readonly Func<uint> _ticks = ticks ?? (() => unchecked((uint)Environment.TickCount));
    private uint? _previous;
    private bool _paused;
    internal uint Now { get; private set; }
    internal void Observe(bool paused)
    {
        var current = _ticks();
        if (_previous is { } previous && !_paused && !paused) Now = unchecked(Now + current - previous);
        _previous = current; _paused = paused;
    }
}

internal sealed record FalloutSandboxRescanSnapshot(FalloutFormKey Package, string PackageSha256,
    uint RemainingMilliseconds, FalloutSandboxCandidateSource[] Candidates)
{
    internal void Validate(FalloutPluginStack? records = null)
    {
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 ||
            PackageSha256 is not { Length: 64 } || !PackageSha256.All(Uri.IsHexDigit) || Candidates is null)
            throw new InvalidDataException("Sandbox rescan has invalid source identity.");
        foreach (var candidate in Candidates)
        {
            if (candidate is null || !Enum.IsDefined(candidate.Activity) || candidate.Inventory &&
                (candidate.Activity != FalloutSandboxActivity.Eat || candidate.Reference is not null) ||
                candidate.Activity is not (FalloutSandboxActivity.Wander or FalloutSandboxActivity.Eat) && candidate.Reference is null)
                throw new InvalidDataException("Sandbox rescan has an invalid retained candidate.");
            foreach (var key in new[] { candidate.Reference, candidate.Base }.OfType<FalloutFormKey>())
                if (string.IsNullOrWhiteSpace(key.OwnerPlugin) || key.ObjectId == 0)
                    throw new InvalidDataException("Sandbox rescan has an invalid candidate identity.");
            if (records is not null && candidate.Reference is { } reference)
            {
                var record = records.GetEffective(reference);
                if (record.Signature is not ("REFR" or "ACHR" or "ACRE") ||
                    candidate.Base != FalloutDialogueTopic.RequiredForm(record, "NAME"))
                    throw new InvalidDataException("Sandbox cached candidate differs from its source reference.");
            }
        }
        if (records is null) return;
        var source = records.GetEffective(Package);
        if (FalloutPackageData.Read(source).Procedure != 12 ||
            !source.ReadDataHash().Equals(PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sandbox rescan differs from its winning package source.");
    }
    internal FalloutSandboxRescanSnapshot Copy() => this with { Candidates = Candidates.ToArray() };
}

internal sealed class FalloutSandboxRescanOwner
{
    private readonly FalloutSandboxProcessClock _clock;
    private readonly FalloutFormKey _package;
    private readonly string _hash;
    private uint _deadline;
    private IReadOnlyList<FalloutSandboxCandidateSource> _candidates;
    internal FalloutSandboxRescanOwner(FalloutSandboxRescanSnapshot saved, FalloutSandboxProcessClock clock)
    {
        saved.Validate(); _clock = clock; _package = saved.Package; _hash = saved.PackageSha256;
        _deadline = unchecked(clock.Now + saved.RemainingMilliseconds); _candidates = Array.AsReadOnly(saved.Candidates.ToArray());
    }
    internal bool Matches(FalloutPluginRecord package) => package.FormKey == _package &&
        package.ReadDataHash().Equals(_hash, StringComparison.OrdinalIgnoreCase);
    internal bool Due => _clock.Now >= _deadline;
    internal IReadOnlyList<FalloutSandboxCandidateSource> Candidates => _candidates;
    internal object StableState => new { _package, _hash, _deadline, candidates = _candidates };
    internal void Refresh(IReadOnlyList<FalloutSandboxCandidateSource> candidates, uint minimumSeconds,
        uint maximumSeconds, Func<uint> random)
    {
        var minimum = unchecked(minimumSeconds * 1000);
        var maximum = unchecked(maximumSeconds * 1000);
        var copy = candidates.ToArray();
        new FalloutSandboxRescanSnapshot(_package, _hash, 0, copy).Validate();
        var span = unchecked(maximum - minimum);
        var delay = unchecked(minimum + (span == 0 ? 0 : random() % span));
        _candidates = Array.AsReadOnly(copy); _deadline = unchecked(_clock.Now + delay);
    }
    internal FalloutSandboxRescanSnapshot Capture() => new(_package, _hash,
        _clock.Now < _deadline ? _deadline - _clock.Now : 0, _candidates.ToArray());
}
