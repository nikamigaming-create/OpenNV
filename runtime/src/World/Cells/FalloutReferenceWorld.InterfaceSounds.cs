using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutIndexedInterfaceSounds? _campaignIndexedInterfaceSounds;
    internal FalloutIndexedInterfaceSounds CampaignIndexedInterfaceSounds
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _campaignIndexedInterfaceSounds ?? throw new NotSupportedException("Campaign has no actual indexed interface sound authority.");
        }
    }
    private void ConfigureCampaignIndexedInterfaceSounds(FalloutAdvancementRuntimeSource source,
        FalloutIndexedInterfaceSoundSnapshot? restore)
    {
        if (_campaignIndexedInterfaceSounds is not null) throw new InvalidOperationException("Campaign indexed sound authority cannot be replaced.");
        _campaignIndexedInterfaceSounds = new(new FalloutIndexedInterfaceSoundSource(records, source), restore);
        if (Challenges.Source is not null) Challenges.BindInterfaceSounds(_campaignIndexedInterfaceSounds);
    }
    private void RetireCampaignIndexedInterfaceSounds() => _campaignIndexedInterfaceSounds?.Retire();
}
