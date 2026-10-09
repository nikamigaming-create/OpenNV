using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutQueuedReferences
{
    private FalloutQueuedTaskPriorities _taskPriorities = null!;
    internal FalloutQueuedTaskPriorities TaskPriorities => _taskPriorities;
    private void ConstructTaskPriorities(FalloutQueuedReferencesSnapshot? restore)
    {
        var declaration = FalloutMainFrameDeclaration.ForExecutable(_source.ExecutableSha256);
        _taskPriorities = new(declaration, _stack, restore?.TaskPriorities);
    }
    private static void ValidateTaskPriorityJoin(FalloutQueuedReferencesSnapshot saved)
    {
        if (saved.TaskPriorities is null) throw new InvalidDataException("Current queued loader omitted its genuine task priority owner.");
        FalloutQueuedTaskPriorities.Validate(saved.TaskPriorities);
        var executable = FalloutMainFrameDeclaration.Executables.FirstOrDefault(image =>
            FalloutActorProcessQueueDeclaration.ForExecutable(image).Contract == saved.Contract) ??
            throw new InvalidDataException("Queued source continuation has no selected original declaration.");
        if (saved.TaskPriorities.Contract != FalloutMainFrameDeclaration.ForExecutable(executable).Contract ||
            saved.TaskPriorities.Stack != saved.Stack || !saved.TaskPriorities.Tasks.Select(value => value.Identity).ToHashSet()
                .SetEquals(saved.Objects.Select(value => value.Identity)))
            throw new InvalidDataException("Source queued map/caller/priority object identities disagree.");
        foreach (var item in saved.Objects)
        {
            var task = saved.TaskPriorities.Tasks.Single(value => value.Identity == item.Identity);
            if (!task.Retired || unchecked((byte)(task.Key >> 16)) != item.Priority)
                throw new InvalidDataException("Cold queued reference priority lost its real last declared key/provider retirement.");
        }
    }
}
