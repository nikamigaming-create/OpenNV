using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutLevelUpMenuSession
{
    internal bool HasExperienceFrameSource(FalloutAdvancementFrameDeclaration frame) =>
        _source.Runtime.EngineSha256 == frame.ExecutableSha256;
}
