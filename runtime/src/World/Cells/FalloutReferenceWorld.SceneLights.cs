using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly FalloutSceneLightCollection _sceneLights = new();
    internal object SourceSceneLightState => _sceneLights.State;
    internal IDisposable AttachScenePointLight(FalloutSourcePointLight light)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var instance = Get(light.Reference);
        if (records.GetEffective(instance.Base).Signature != "LIGH")
            throw new InvalidDataException("Scene light membership requires its real current LIGH reference.");
        try
        {
            return _sceneLights.Attach(light, () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this); return Placement(light.Reference);
            }, () => IsEnabled(light.Reference));
        }
        catch (Exception error) { _sceneLights.ObserveAttachmentFailure(error); throw; }
    }
}
