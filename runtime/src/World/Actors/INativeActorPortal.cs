using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

// The source door result commits a prepared native approach. A receipt never
// authorizes another activation or substitutes for walking to the door.
internal interface INativeActorPortal
{
    CharacterBody3D PortalBody { get; }
    RuntimeNativeActorCombat PortalCombat { get; }
    bool PortalTransferred { get; }
    bool WantsPortal(FalloutFormKey door);
    void CommitPortal(FalloutFormKey door);
    void FinishPortalAttempt(FalloutFormKey door, string? error);
    void RetirePortalBindings();
}
