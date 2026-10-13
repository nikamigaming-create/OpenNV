using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSourceHudMenuBinding(Guid Menu, Guid Process, long Constructed,
    FalloutUiComponentBinding Root);

internal sealed partial class FalloutStandaloneInterfaceState
{
    private readonly List<FalloutSourceHudMenuBinding> _hudMenus = [];
    private readonly Dictionary<Guid, (FalloutUiComponentStore Ui, FalloutUiComponentBinding Root)> _hudRoots = [];

    internal void PublishHudTransitionMenu(Guid menu, FalloutUiComponentStore ui, FalloutUiComponentBinding root)
    {
        RequireCurrent(native: false); ArgumentNullException.ThrowIfNull(ui); ArgumentNullException.ThrowIfNull(root);
        if (menu == Guid.Empty || _hudRoots.ContainsKey(menu) || root.Path != FalloutPlayerControlHudSource.Menu ||
            _hudMenus.Any(item => item.Menu == menu))
            throw new InvalidOperationException("Source HUD transition menu omitted or replayed its actual XML factory.");
        ui.RequireComponent(root);
        _hudRoots.Add(menu, (ui, root)); _hudMenus.Add(new(menu, _process, Next(), root));
    }

    internal void ReplaceHudMenuTransition(Guid menu, uint duration)
    {
        RequireCurrent(native: false); RequireHudTransitionMenu(menu);
        // This is the same Interface queue that other source menus use. The
        // source scans once, removes the first matching menu and appends.
        for (var node = _menuTransitions.First; node is not null; node = node.Next)
            if (node.Value.Menu == menu) { _menuTransitions.Remove(node); Next(); break; }
        var requested = Next();
        _menuTransitions.AddLast(new FalloutStandaloneMenuTransition(Guid.NewGuid(), menu, requested, 0,
            FalloutStandaloneMenuTransitions.Duration(duration), Next()));
    }

    internal IReadOnlyList<FalloutPlayerControlHudTransition> AdvanceHudMenuTransitions(FalloutPlayerControlHudFrame frame)
    {
        RequireCurrent(native: false); ArgumentNullException.ThrowIfNull(frame);
        if (frame.Clock.Source != Source.Main.EngineSha256 || frame.Clock.Process != _process || frame.Clock.Epoch < 1 ||
            frame.Clock.Sequence < 0 || (frame.ControlMode == 4) != (frame.DivisorBits is not null))
            throw new InvalidDataException("Interface transition advance borrowed another source process clock.");
        // Preserve source order with one pass. Every actual queued menu must
        // have its current source tree; an old cold/native identity cannot
        // make an otherwise unowned queue element return.
        for (var node = _menuTransitions.First; node is not null;)
        {
            var next = node.Next; var current = node.Value; RequireHudTransitionMenu(current.Menu);
            var value = FalloutStandaloneMenuTransitions.Advance(current.ElapsedBits, current.DurationBits,
                frame.Clock.ScaledSecondsBits, frame.ControlMode, frame.DivisorBits);
            var changed = Next();
            node.Value = current with { ElapsedBits = value.Elapsed, ProgressBits = value.Progress, Changed = changed };
            if (value.Remove) _menuTransitions.Remove(node);
            node = next;
        }
        return CaptureHudMenuTransitions();
    }

    internal IReadOnlyList<FalloutPlayerControlHudTransition> CaptureHudMenuTransitions()
    {
        RequireOwnerThread();
        return _menuTransitions.Select(item => new FalloutPlayerControlHudTransition(item.Identity, item.Menu,
            item.ElapsedBits, item.DurationBits, item.ProgressBits, false)).ToArray();
    }

    private void RequireHudTransitionMenu(Guid menu)
    {
        if (!_hudRoots.TryGetValue(menu, out var tree))
            throw new NotSupportedException("source-Interface-queued-menu-current-XML-factory-unbound");
        tree.Ui.RequireComponent(tree.Root);
    }

    private static void ValidateHudTransitions(FalloutStandaloneInterfaceSnapshot saved)
    {
        if (saved.HudMenus is not { } bindings)
            throw new InvalidDataException("Interface omitted its source HUD menu-factory history.");
        var menus = new Dictionary<Guid, FalloutSourceHudMenuBinding>(); long constructed = 0;
        foreach (var binding in bindings)
        {
            if (binding.Menu == Guid.Empty || !menus.TryAdd(binding.Menu, binding) || binding.Process == Guid.Empty ||
                binding.Constructed <= constructed || binding.Constructed > saved.Sequence || binding.Root is null ||
                binding.Root.Factory == Guid.Empty || binding.Root.Epoch < 0 || binding.Root.Path != FalloutPlayerControlHudSource.Menu)
                throw new InvalidDataException("Interface HUD transition registration lost its actual source XML lifetime.");
            constructed = binding.Constructed;
        }
        var identities = new HashSet<Guid>(); long requested = 0;
        foreach (var transition in saved.Menus.Transitions)
        {
            if (transition.Identity == Guid.Empty || !identities.Add(transition.Identity) ||
                !menus.TryGetValue(transition.Menu, out var menu) || transition.Requested <= requested ||
                transition.Requested <= menu.Constructed || transition.Changed <= transition.Requested || transition.Changed > saved.Sequence ||
                transition.DurationBits != FalloutStandaloneMenuTransitions.Duration(transition.DurationBits) ||
                transition.ProgressBits == 0x3f800000)
                throw new InvalidDataException("Interface queue lost source allocation, order, duration or current membership.");
            requested = transition.Requested;
        }
    }
}
