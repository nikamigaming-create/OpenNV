using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandaloneMenuStackResult(IReadOnlyList<uint> Slots, uint Mode,
    int Result, byte? ImageMenuByte, bool Removed);

// Fixed source storage makes these operations bounded. In particular, an
// ordinary failed removal leaves every slot intact; it does not become a pop.
internal static class FalloutStandaloneMenuStack
{
    internal static FalloutStandaloneMenuStackResult Push(IReadOnlyList<uint> source, uint mode, uint code)
    {
        var slots = Copy(source);
        if (code == FalloutStandaloneMenuLifecycleSource.MenuMode)
        {
            var previous = slots[0]; slots[0] = code; var index = 1;
            for (; index < 9; ++index)
            {
                var value = slots[index]; slots[index] = previous; previous = value;
                if (value == 0) break;
            }
            if (index == 1) mode = FalloutStandaloneMenuLifecycleSource.MenuMode;
            return new(slots, mode, index, index == 1 ? (byte)1 : null, false);
        }
        var at = Array.FindIndex(slots, value => value == 0);
        if (at < 0) return new(slots, mode, -1, null, false);
        slots[at] = code;
        if (at == 0) mode = FalloutStandaloneMenuLifecycleSource.MenuMode;
        var image = at == 0 && code != FalloutStandaloneMenuLifecycleSource.HudMenu ||
            at == 1 && slots[0] == FalloutStandaloneMenuLifecycleSource.HudMenu;
        return new(slots, mode, at, image ? (byte)1 : null, false);
    }

    internal static FalloutStandaloneMenuStackResult Remove(IReadOnlyList<uint> source, uint mode, uint code,
        bool force, uint? actualAdjacentWord = null)
    {
        var slots = Copy(source);
        if (slots[0] == 0) return new(slots, mode, -1, null, false);
        if (!force)
        {
            var found = false; var index = 0;
            for (; index < slots.Length; ++index)
            {
                if (found && slots[index] > FalloutStandaloneMenuLifecycleSource.HudMenu) break;
                if (slots[index] == code) found = true;
            }
            if (!found) return new(slots, mode, -1, null, false);
            if (index < slots.Length && code != FalloutStandaloneMenuLifecycleSource.MenuMode)
                return new(slots, mode, -2, null, false);
        }
        var before = (uint[])slots.Clone(); var written = 0; var read = 0;
        for (; written < slots.Length; ++written)
        {
            if (Read(read) == code) ++read;
            var value = Read(read);
            slots[written] = value;
            if (value == 0) break;
            ++read;
        }
        if (written != 0)
        {
            var onlyHud = written == 1 && slots[0] == FalloutStandaloneMenuLifecycleSource.HudMenu;
            var top = slots[Math.Min(written, slots.Length) - 1];
            return new(slots, mode, unchecked((int)top), onlyHud ? (byte)1 : null, true);
        }
        mode = FalloutStandaloneMenuLifecycleSource.ReturningMode;
        return new(slots, mode, 0, code == FalloutStandaloneMenuLifecycleSource.HudMenu ? (byte)0 : (byte)1, true);

        uint Read(int index) => index < before.Length ? before[index] :
            index == before.Length && actualAdjacentWord is { } word ? word :
            throw new NotSupportedException("source-menu-full-stack-adjacent-current-word-unowned");
    }

    internal static uint Top(IReadOnlyList<uint> source)
    {
        Require(source);
        var count = 0; while (count < source.Count && source[count] != 0) ++count;
        return count == 0 ? 0 : source[count - 1];
    }
    private static uint[] Copy(IReadOnlyList<uint> source) { Require(source); return source.ToArray(); }
    private static void Require(IReadOnlyList<uint> source)
    {
        if (source is not { Count: FalloutStandaloneMenuLifecycleSource.StackExtent })
            throw new InvalidDataException("Menu stack changed its genuine ten-word source extent.");
    }
}
