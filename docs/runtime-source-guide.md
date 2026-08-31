# Reading the OpenNV runtime

This directory is the executable half of OpenNV. It turns verified descriptions
of user-owned content into one authoritative game world, then presents that world
through desktop or OpenXR adapters. The directory tree is the table of contents:
namespaces match folders, and the root contains only application composition.

## Start here

Read a normal startup in this order:

1. `RuntimeConfiguration.cs` loads and validates versioned runtime policy.
2. `RuntimeLaunchRequest.cs` reduces command-line options to one content source.
3. `RuntimeLaunchValidator.cs` rejects incompatible proof and product switches.
4. `RuntimeCoordinator.cs` composes the request; `RuntimeCoordinator.Launch.cs` dispatches it.
5. `Content/` verifies and loads generated, asset-free descriptions.
6. `World/` creates authoritative actors, cells, interactions, portals, and streaming state.
7. `Gameplay/` changes that state according to game rules.
8. `Presentation/` renders and controls the same state in flat-screen and OpenXR modes.
9. `Diagnostics/` observes the result without becoming part of gameplay authority.

Campaign-specific vertical slices live under `Campaigns/`. They may compose the
shared layers, but they do not bypass content verification or create a second
gameplay state for VR.

## Chapters

- `Campaigns/` — bounded Fallout 1, Fallout 2, Fallout 3, New Vegas, and TTW flows.
- `Compatibility/` — narrow adapters for external proof and jam contracts.
- `Content/` — hash verification and loading of prepared, distributable descriptions.
- `Diagnostics/` — acceptance drivers, captures, and performance observation.
- `Formats/` — small implementation-neutral Gamebryo value contracts.
- `Gameplay/` — rules, inventory, interaction, and save/session state.
- `InputSystem/` — desktop action mapping and input telemetry.
- `Presentation/` — actors, character creation, rendering, UI, and OpenXR adapters.
- `Properties/` — assembly metadata only; it contains no runtime behavior.
- `SceneGraph/` — shared bounded algorithms over Godot scene trees.
- `World/` — cells, actors, interactions, portals, and active-set streaming.

## Dependency direction

The usual flow is:

```text
Runtime composition
    -> Campaigns / Diagnostics / Presentation
        -> Gameplay / World
            -> Content / Formats / SceneGraph
```

Dependencies can skip downward when the smaller contract is sufficient. Lower
layers must not depend on a proof driver or on a campaign coordinator. Content
loaders never decide gameplay, presentation never owns save truth, and diagnostics
never repairs a failed baseline.

Explicit `using` directives make every cross-domain dependency visible at the top
of a file. There is deliberately no project-wide import of OpenNV runtime domains.
This keeps a move or boundary violation discoverable by the compiler.

## Following common stories

For an owned asset, begin with the preparation scripts outside this directory,
then follow `Content/LegalAssetPreparer.cs` into a verified loader and finally the
corresponding `World/` or `Presentation/` consumer. Retail files remain read-only;
only hashes, implementation-neutral contracts, and disposable local caches cross
that boundary.

For player input, begin in `InputSystem/DesktopInputMap.cs`, continue through the
active presentation adapter, and end at authoritative `Gameplay/` or `World/`
state. OpenXR and desktop controls meet at that state rather than implementing
parallel games.

For persistence, begin in `Gameplay/State/GameplaySession.cs`. A campaign may add
a focused serializable contract, but presentation nodes and diagnostics are never
save owners.

## Design rules

- Prefer a concrete value or collaborator with one current responsibility.
- Introduce an interface only when two real implementations need substitution.
- Keep authoritative state in one place; derive views and reports from it.
- Name proof-only code as proof or acceptance code and keep it in `Diagnostics/`.
- Share algorithms such as scene traversal instead of copying local recursion.
- Reject invalid input before allocating a world or mutating a save.
- Use bounded passes and indexed lookups on hot paths; document unavoidable scans.
- Do not add managers, placeholders, or extension points for hypothetical features.

“Identity verified,” “state matched,” and “rendering matched” are separate claims.
The runtime and its documentation must preserve that distinction.
