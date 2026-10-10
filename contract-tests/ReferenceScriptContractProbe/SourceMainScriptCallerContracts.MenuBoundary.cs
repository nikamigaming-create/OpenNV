using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunMainMenuBoundaryContracts()
    {
        var active = new byte[84];
        Require(FalloutMainInterfaceSource.ActiveMenuCount == 84 &&
            FalloutMainInterfaceSource.FirstActiveMenu == 1001 && FalloutMainInterfaceSource.LastActiveMenu == 1084,
            "Independent menu-query source bounds changed.");
        for (uint code = 1001; code <= 1084; code++)
        {
            active[code - 1001] = 1;
            Require(FalloutMainInterfaceSource.ReadActiveMenu(active, code), "Source-valid active menu was omitted.");
            active[code - 1001] = 0;
            Require(!FalloutMainInterfaceSource.ReadActiveMenu(active, code), "Source-zero menu was fabricated as active.");
        }
        Require(!FalloutMainInterfaceSource.ReadActiveMenu(active, 1000) &&
            !FalloutMainInterfaceSource.ReadActiveMenu(active, 1085) &&
            !FalloutMainInterfaceSource.ReadActiveMenu(active, uint.MaxValue), "Original outside-range zero return was replaced.");
        Reject(() => FalloutMainInterfaceSource.ReadActiveMenu(new byte[60], 1061));
        Reject(() => FalloutMainInterfaceSource.ReadActiveMenu(new byte[85], 1001));
        var source = FalloutMainInterfaceSource.Read(FalloutMainScriptCallerSource.Read(new(Engine, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(Engine))));
        var warm = new FalloutMainInterfaceState(source, "authored-menu-query", Guid.NewGuid());
        Publish(warm, 1);
        Require(warm.Read(FalloutStandaloneInterfaceQuery.ForeignMenu) == 0,
            "Known constructor menu state failed its independent shared query.");
        var saved = warm.Capture();
        Require(saved.ActiveMenus.Count == 84 && saved.ActiveMenus.All(value => value == 0),
            "Warm capture omitted original global active-menu bytes.");
        FalloutMainInterfaceState.RequirePlayable(saved);
        Reject(() => FalloutMainInterfaceState.Validate(saved with { ActiveMenus = new byte[60] }));
        var cold = new FalloutMainInterfaceState(source, saved.Stack, Guid.NewGuid(), saved);
        Publish(cold, 2);
        Require(cold.Read(FalloutStandaloneInterfaceQuery.ForeignMenu) == 0 && cold.Capture().ActiveMenus.Count == 84,
            "Cold menu query shrank its source table or replayed a menu writer.");
        warm.Retire(); cold.Retire();
        Console.WriteLine("OPENNV_MAIN_MENU_BOUNDARY_PASS originalInterval=true full84=true selector1061=true " +
            "outsideRangeZero=true truncatedTableRefused=true warmCold=true nativeGameplay=UNEXECUTED");

        static void Publish(FalloutMainInterfaceState state, ulong native)
        {
            state.PublishOwnManager(new(Guid.NewGuid(), state.Process, state.Source.Identity, "authored-menu-query",
                FalloutStandaloneInterfaceObjectRole.PipBoyManager, native, "menus/main/hud_main_menu.xml", new('a', 64),
                "authored-native-manager/" + native), () => true);
        }
    }
}
