using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private sealed class StandaloneIdleModelOwner(RuntimeNativePlayer player, FalloutReferenceWorld world) : IFalloutPlayerIdleModelOwner
    {
        private readonly int _thread = System.Environment.CurrentManagedThreadId;
        public Guid Process => world.CampaignStandalonePlayerScene.Process;
        public string Stack => world.CampaignStandalonePlayerScene.Stack;
        public string Source => world.CampaignStandalonePlayerScene.Source.Identity;
        public void RequireCurrent()
        {
            var body = player._firstPerson;
            if (_thread != System.Environment.CurrentManagedThreadId || !GodotObject.IsInstanceValid(player) ||
                !player.IsInsideTree() || player.IsQueuedForDeletion() || !ReferenceEquals(player._standaloneSceneWorld, world) ||
                body is null || !GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || body.IsQueuedForDeletion() ||
                player._standaloneFirstPersonOwner is not { Released: null } first ||
                first.Publication.Factory != body.SourceSceneFactory || first.Publication.NativeRoot != body.GetInstanceId() ||
                first.Publication.Process != Process || first.Publication.Stack != Stack || first.Publication.Source != Source)
                throw new NotSupportedException("Player idle model lost its current native field, source factory or calling thread.");
        }
        public FalloutIdleAnimationContext ReadFirstPersonAnimation(FalloutSourceAnimationContextState context)
        { RequireCurrent(); return player._firstPerson!.ReadIdleAnimationContext(context); }
        public uint? ReadBoundRadius(FalloutSourceSceneNode model)
        {
            RequireCurrent(); var body = player._firstPerson!;
            if (model.Process != Process || model.Factory != body.SourceSceneFactory || model.NativeRoot != body.GetInstanceId())
                throw new InvalidOperationException("Player bound cannot borrow another selected model or historical native root.");
            return body.ReadSourcePoseBoundRadius();
        }
    }
}
