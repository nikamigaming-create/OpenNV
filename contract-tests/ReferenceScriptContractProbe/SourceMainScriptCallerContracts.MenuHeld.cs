using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunHeldPlayerMenuContracts()
    {
        using var fields = new PackageFieldFixture();
        using var fixture = new StandaloneFixture(authoredStack: fields.Stack);
        var main = fixture.Host.Source;
        var source = FalloutStandaloneMenuLifecycleSource.Reviewed(FalloutStandaloneInterfaceSource.Read(main));
        void Actual(FalloutMainPlayerCellInvocation call)
        {
            call.Require(FalloutMainPlayerCellStep.HeldChild);
            if (!ReferenceEquals(call.Owner, fixture.Owner) || call.Main.Process != fixture.Owner.ReadMainNativeDeliveryBindings().Process)
                throw new InvalidOperationException("Held authored child changed its actual Main owner.");
        }
        var firstReads = 0;
        var owner = new FalloutSourceHeldPlayerMenu(source, Actual, fields.ReadCommon, call =>
        {
            ++firstReads; return fields.Fields.ReadHeldPlayerMenuFirstContext(call, Actual);
        });
        fixture.BindHeldMenu(call => owner.Execute(call));
        using (fixture.Bind()) fixture.Owner.ExecuteMainScriptCaller(10, .02f).GetAwaiter().GetResult();
        Require(owner.LastCall is { Returned: true, Common.AnimationContext: null, First.Context: null } && firstReads == 1 &&
            fixture.Owner.CaptureMainPlayerCell().LastCall is { Disposition: FalloutMainPlayerCellDisposition.HeldReturned },
            "Cached Menu held child skipped its actual pointer reads or entered unrelated scene/group/timer effects.");
        Console.WriteLine("OPENNV_SOURCE_HELD_PLAYER_MENU_PASS actualMainChild=true sharedCommonAndFirst=true nullReturn=true nativeGameplay=UNEXECUTED");
    }
}
