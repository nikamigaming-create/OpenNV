using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutImageSpaceState
{
    private readonly Dictionary<Guid, (FalloutSkyTransferState Sky, Guid Instance, int Ordinal)> _sourceSkyInstances = [];
    internal void RegisterSourceSkyInstance(FalloutSkyTransferState sky, Guid instance, int ordinal)
    {
        if (instance == Guid.Empty || ordinal is < 0 or > 3 || _sourceSkyInstances.ContainsKey(instance) ||
            _sourceSkyInstances.Values.Any(row => row.Sky.Identity == sky.Identity && row.Ordinal == ordinal))
            throw new InvalidDataException("Source image manager repeated or aliased a Sky instance/registration ordinal.");
        _sourceSkyInstances.Add(instance, (sky, instance, ordinal));
    }
    internal void RetireSourceSkyInstance(FalloutSkyTransferState sky, Guid instance)
    {
        if (!_sourceSkyInstances.TryGetValue(instance, out var registered)) return;
        if (!ReferenceEquals(registered.Sky, sky)) throw new InvalidOperationException("Another Sky owns the image manager registration.");
        _sourceSkyInstances.Remove(instance);
    }
    private IReadOnlyList<FalloutSourceSkyImageContribution> ReadSourceSkyContributions()
    {
        var result = new List<FalloutSourceSkyImageContribution>();
        foreach (var owner in _sourceSkyInstances.Values.Select(row => row.Sky).Distinct())
        {
            var contributions = owner.ImageContributions();
            foreach (var row in contributions)
            {
                if (!_sourceSkyInstances.TryGetValue(row.Instance.Identity, out var registered) ||
                    !ReferenceEquals(registered.Sky, owner) || registered.Ordinal != row.Instance.ManagerOrdinal)
                    throw new InvalidDataException("Sky field/image manager publication no longer names the same actual instance.");
                result.Add(row);
            }
        }
        return result;
    }
}
