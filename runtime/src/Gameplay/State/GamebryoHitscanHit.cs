using Godot;

namespace OpenNV.Runtime.Gameplay.State;

internal readonly record struct GamebryoHitscanHit(
    string WeaponFormId,
    int? WeaponAnimationType,
    Node Collider);
