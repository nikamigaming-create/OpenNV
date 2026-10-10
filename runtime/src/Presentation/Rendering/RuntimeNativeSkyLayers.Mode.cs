using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeSkyLayers
{
    private sealed partial class SourceCloudResetChild : IFalloutSkyCloudModeRelease
    {
        public FalloutSkyChildSnapshot ReleaseForMode(FalloutSkyModeCall call)
        {
            Require();
            if (call.Sky != Sky || call.Process == Guid.Empty || call.SourceContract != FalloutSkyModeSource.Read(sky.Source).Contract ||
                call.EnteredChild != FalloutSkyModeStep.CloudRelease || call.Returned)
                throw new InvalidDataException("Cloud resource release has no actual entered original mode child.");
            ReleaseNativeSamplers();
            var fields = Capture();
            if (fields.CloudSlots.Any(slot => slot.Texture is not null || slot.PropertyTexture is not null || slot.PrimaryTexture is not null || slot.BlendBits != 0))
                throw new InvalidOperationException("Cloud mode release retained a live sampler or source blend store.");
            return fields;
        }
    }
}
