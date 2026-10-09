using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

// Implemented by the real actor body, never an actor proxy or metadata node.
internal interface IRuntimeNativeFollower
{
    CharacterBody3D FollowerBody { get; }
    RuntimeNativeActorCombat FollowerCombat { get; }
    FalloutFormKey FollowerReference { get; }
    bool FollowingPlayer { get; }
}
