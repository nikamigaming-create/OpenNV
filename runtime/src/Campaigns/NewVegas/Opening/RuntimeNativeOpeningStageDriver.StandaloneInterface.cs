using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private RuntimeNativeStandaloneInterface? _standaloneNativeInterface;
    internal void PublishStandaloneInterface(NativeOwnedGameplayHud actualHud, Func<IEnumerable<uint>?> actualNativeMenus)
    {
        var world = _scripts.References ?? throw new InvalidOperationException("Interface publication has no actual campaign world.");
        if (!world.HasStandaloneInterface) return;
        if (_standaloneNativeInterface is not null) throw new InvalidOperationException("Interface native manager already published.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Native interface has no selected source.");
        _standaloneNativeInterface = RuntimeNativeStandaloneInterface.Attach(this, world, PipBoy, actualHud, source,
            actualNativeMenus, RetainDriverFailure);
        (_conversation ?? throw new InvalidOperationException("Interface has no actual dialog factory owner.")).BindStandaloneInterface(_standaloneNativeInterface);
        var console = _sourceRestConsole ?? throw new InvalidOperationException("Interface has no selected existing-console query owner.");
        BindSourceRestConsole(() => world.StandaloneInterface.ReadConsoleActivity(console.Source));
    }
    private void RetireStandaloneNativeInterface()
    {
        var native = _standaloneNativeInterface;
        if (native is null) return;
        native.Retire();
        if (GodotObject.IsInstanceValid(native)) native.Free();
        _standaloneNativeInterface = null;
    }
}
