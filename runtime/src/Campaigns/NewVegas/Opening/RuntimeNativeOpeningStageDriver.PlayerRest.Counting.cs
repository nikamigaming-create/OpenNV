using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private void BeginCurrentSourceRestCounting(FalloutRestRequest request)
    {
        if (_playerRestRetired || !IsInsideTree() || _playerRestEntry is null)
            throw new NotSupportedException("Original rest counting controls have no actual current menu publication.");
        _playerRestEntry.BeginSourceCounting(request);
    }
}
