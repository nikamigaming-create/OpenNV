using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private RuntimeNativeMainInterface? _sourceMainNativeInterface;
    internal void PublishSourceMainInterface(NativeOwnedGameplayHud actualHud, Func<IEnumerable<uint>?> actualNativeMenus)
    {
        var world = _scripts.References ?? throw new InvalidOperationException("Main interface publication has no actual campaign world.");
        if (!world.HasSourceMainInterface) return;
        if (_sourceMainNativeInterface is not null) throw new InvalidOperationException("Source Main native manager already published.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Main interface has no selected resource lifetime.");
        _sourceMainNativeInterface = RuntimeNativeMainInterface.Attach(this, world, PipBoy, actualHud, source,
            actualNativeMenus, RetainDriverFailure);
        (_conversation ?? throw new InvalidOperationException("Main interface has no actual dialog factory owner."))
            .BindSourceMainInterface(_sourceMainNativeInterface);
    }
    private void RetireSourceMainNativeInterface()
    {
        var native = _sourceMainNativeInterface;
        if (native is null) return;
        native.Retire();
        if (GodotObject.IsInstanceValid(native)) native.Free();
        _sourceMainNativeInterface = null;
    }
}
