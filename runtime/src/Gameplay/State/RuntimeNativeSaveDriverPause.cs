using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Freeze the actual gameplay producer without overriding its descendant audio
// nodes. A finite native voice's explicit Always mode remains its drain owner.
internal sealed class RuntimeNativeSaveDriverPause : IDisposable
{
    private readonly Node _driver;
    private readonly ulong _identity;
    private readonly Node.ProcessModeEnum _mode;
    private bool _disposed;

    internal RuntimeNativeSaveDriverPause(Node driver)
    {
        if (!GodotObject.IsInstanceValid(driver) || !driver.IsInsideTree() || driver.IsQueuedForDeletion())
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("The gameplay source driver retired before save preparation.");
        _driver = driver; _identity = driver.GetInstanceId(); _mode = driver.ProcessMode;
        driver.ProcessMode = Node.ProcessModeEnum.Disabled;
    }

    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!GodotObject.IsInstanceValid(_driver) || _driver.GetInstanceId() != _identity ||
            !_driver.IsInsideTree() || _driver.IsQueuedForDeletion() || _driver.ProcessMode != Node.ProcessModeEnum.Disabled)
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("The saved gameplay driver generation or pause lease changed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_driver) && _driver.GetInstanceId() == _identity) _driver.ProcessMode = _mode;
    }
}
