using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Component histories are validated by their owners. This boundary joins each
// retained child to the same Interface call and the actual containing owners.
internal static class FalloutStandaloneMenuSaveContract
{
    internal static void Validate(FalloutStandaloneInterfaceSnapshot saved,
        FalloutSourceMenuImageFields? images, IReadOnlyList<FalloutSourceThreadKindScope>? scopes,
        FalloutStandaloneInputManagerQuery? manager = null)
    {
        FalloutStandaloneInterfaceState.Validate(saved);
        var fields = saved.MenuFields!;
        var constructors = new Dictionary<Guid, Guid> { [saved.CapturedProcess] = saved.Constructor };
        var intervals = new Dictionary<Guid, (long After, long? Before)>();
        FalloutStandaloneInterfaceColdHandoff? previous = null;
        foreach (var cold in fields.ColdConstructors)
        {
            if (previous is not null && (cold.PreviousProcess != previous.CurrentProcess || cold.PreviousConstructor != previous.CurrentConstructor))
                throw new InvalidDataException("Menu cold constructors do not form the actual connected process history.");
            RequireConstructor(cold.PreviousProcess, cold.PreviousConstructor);
            RequireConstructor(cold.CurrentProcess, cold.CurrentConstructor);
            if (!intervals.TryAdd(cold.PreviousProcess, (previous?.Changed ?? 0, cold.Changed)))
                throw new InvalidDataException("Menu cold history reused an earlier process lifetime.");
            previous = cold;
        }
        if (previous is not null && (previous.CurrentProcess != saved.CapturedProcess || previous.CurrentConstructor != saved.Constructor ||
            previous != saved.ColdHandoff))
            throw new InvalidDataException("Menu cold history omitted its actual current Interface constructor.");
        if (previous is null && saved.ColdHandoff is not null)
            throw new InvalidDataException("Menu cold history omitted its actual process handoff.");
        if (!intervals.TryAdd(saved.CapturedProcess, (previous?.Changed ?? 0, null)))
            throw new InvalidDataException("Menu cold history restored an old current process.");
        FalloutPlayerThreadContextState.ValidateKindScopes(scopes, FalloutPlayerThreadContextSource.Reviewed(saved.Source.Main));
        var allocations = (scopes ?? []).ToDictionary(row => row.Invocation);
        var inputs = fields.Inputs.ToDictionary(row => row.Process, row => row.State);
        var closes = fields.Inputs.SelectMany(row => (row.State.MenuCloses ?? [])
            .Select(close => (row.Process, Close: close))).ToDictionary(row => (row.Process, row.Close.Invocation), row => row.Close);
        var menuCloses = new HashSet<(Guid Process, Guid Invocation)>();
        var flushes = fields.Inputs.SelectMany(row => (row.State.MenuMouseFlushes ?? [])
            .Select(flush => (row.Process, Flush: flush))).ToDictionary(row => (row.Process, row.Flush.Invocation), row => row.Flush);
        var menuFlushes = new HashSet<(Guid Process, Guid Invocation)>();
        if (inputs.Keys.Any(process => !constructors.ContainsKey(process)) || manager is not null &&
            (!inputs.TryGetValue(manager.Process, out var actualManager) || actualManager.Factory != manager.Factory || actualManager.Stack != manager.Stack))
            throw new InvalidDataException("Menu input history differs from its actual Scene manager/Interface lifetime.");
        var menuAllocations = new HashSet<Guid>();
        var children = new Dictionary<(Guid Invocation, long Changed), FalloutSourceMenuImageWrite>();
        foreach (var write in fields.Writes)
        {
            if (!constructors.TryGetValue(write.Process, out var owner) || !intervals.TryGetValue(write.Process, out var interval) ||
                write.Entered <= interval.After || interval.Before is { } before && write.Changed >= before)
                throw new InvalidDataException("Menu call has no actual retained Interface constructor.");
            if (write.Allocation is { } allocation && (allocation.Process != write.Process || allocation.OwnerFactory != owner ||
                !allocations.TryGetValue(write.Invocation, out var actual) || actual != allocation))
                throw new InvalidDataException("Menu allocation differs from its actual Interface and Scene thread child.");
            if (write.Allocation is not null) menuAllocations.Add(write.Invocation);
            if (write.InputClose is { } close && (!inputs.TryGetValue(write.Process, out var input) ||
                close.InputFactory != input.Factory || close.Source != input.Source || close.Stack != input.Stack ||
                !closes.TryGetValue((write.Process, write.Invocation), out var actualClose) || !FalloutOriginalInputValidation.SameMenuClose(close, actualClose)))
                throw new InvalidDataException("Menu close differs from its actual retained input manager/source lifetime.");
            if (write.InputClose is not null) menuCloses.Add((write.Process, write.Invocation));
            if (write.MouseFlush is { } flush && (!inputs.TryGetValue(write.Process, out var mouse) ||
                flush.InputFactory != mouse.Factory || flush.Stack != mouse.Stack ||
                !flushes.TryGetValue((write.Process, write.Invocation), out var actualFlush) || actualFlush != flush))
                throw new InvalidDataException("Menu mouse-buffer child differs from its actual retained input manager write.");
            if (write.MouseFlush is not null) menuFlushes.Add((write.Process, write.Invocation));
            foreach (var image in write.ImageWrites ?? [])
                if (image.Interface != owner || !children.TryAdd((image.Invocation, image.Changed), image))
                    throw new InvalidDataException("Menu image child changed its actual Interface constructor or containing call.");
        }
        var menuFactories = constructors.Values.ToHashSet();
        if (closes.Keys.Any(call => !menuCloses.Contains(call)))
            throw new InvalidDataException("Input retained a menu-close child without its actual Interface call.");
        if (flushes.Keys.Any(call => !menuFlushes.Contains(call)))
            throw new InvalidDataException("Input retained a mouse-buffer child without its actual Interface call.");
        if (allocations.Values.Any(row => menuFactories.Contains(row.OwnerFactory) && !menuAllocations.Contains(row.Invocation)))
            throw new InvalidDataException("Scene retained a menu allocation without its actual Interface call.");
        if (images is null)
        {
            if (children.Count != 0) throw new InvalidDataException("Menu calls omitted their actual image field owner.");
            return;
        }
        FalloutImageSpaceState.ValidateSourceMenuStackEffect(images);
        if (images.Source.Interface != saved.Source || images.Process != saved.CapturedProcess || images.Writes.Count != children.Count)
            throw new InvalidDataException("Menu image history differs from its actual Interface source/process and call count.");
        foreach (var image in images.Writes)
            if (!children.TryGetValue((image.Invocation, image.Changed), out var actual) || actual != image)
                throw new InvalidDataException("Menu image child differs from its actual image factory, field or scalar write.");

        void RequireConstructor(Guid process, Guid constructor)
        {
            if (constructors.TryGetValue(process, out var actual) && actual != constructor)
                throw new InvalidDataException("Menu cold history assigned two Interface constructors to one process.");
            constructors[process] = constructor;
        }
    }
}
