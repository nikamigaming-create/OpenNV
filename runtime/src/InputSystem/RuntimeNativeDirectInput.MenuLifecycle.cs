using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.InputSystem;

internal sealed partial class RuntimeNativeDirectInput
{
    internal void BindSourceMenuMouseFlush()
    {
        var owner = PlayerManager();
        owner.BindSourceMenuMouseBufferFlush(invocation =>
        {
            invocation.Require(FalloutStandaloneMenuFieldStep.QuietReturn);
            if (!ReferenceEquals(owner, PlayerManager()))
                throw new InvalidOperationException("Menu mouse flush lost the actual current native input owner.");
            if (_device is { } device) return device.FlushSourceMenuMouse(invocation);
            if (_headless is null || owner.Provider != FalloutInputProvider.HeadlessCommands)
                throw new InvalidOperationException("Native source input omitted its genuine device or explicit command provider.");
            return new(invocation.Identity, invocation.Process, invocation.Object, owner.Factory,
                owner.Source, null, false, 20, null, null, null, null, true);
        });
    }
}
