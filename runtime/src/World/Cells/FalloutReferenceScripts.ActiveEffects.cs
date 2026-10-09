using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    internal FalloutCompiledActiveEffectReceipt ExecuteActiveEffect(FalloutCompiledActiveEffectInvocation effect) =>
        ExecuteCompiledActiveEffect(effect);
}
