// Gameplay state consumes shared runtime composition types, while campaign,
// presentation, and world adapters consume the same authoritative session.
// Keep that compile-time join here so this ownership move changes no call sites.
global using OpenNV.Runtime;
global using OpenNV.Runtime.Gameplay.State;
