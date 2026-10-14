using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

internal sealed partial class RuntimeNativeDirectInput
{
    private WindowsOriginalInputPlatform? _sourcePlatform;
    internal void BindSourceQueries(FalloutOriginalInputSource source, Action<FalloutInputInvocation> requireInvocation,
        FalloutOriginalInputHookBinding hooks)
    {
        if (_retired || !IsInsideTree() || GetParent() is null || _sourcePlatform is not null)
            throw new InvalidOperationException("Original input publication requires its attached, fresh product device owner.");
        var platform = new WindowsOriginalInputPlatform();
        // Retain a failed constructor's real platform until owner retirement.
        _sourcePlatform = platform;
        _device?.BindSourceOwner(State);
        State.BindSourceControlQueries(source, platform, requireInvocation, hooks);
    }
}
