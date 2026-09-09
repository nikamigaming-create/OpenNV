# Native reference event contract

The ordinary native player ray and source primitive contacts feed
`FalloutReferenceScripts`, the same C# owner exercised by the development lab.
Reference locals outlive the Godot nodes. The adapter has no quest/stage table.

## Admitted behavior

- Frame events are evaluated in authored block order. Ordinary activation is
  queued into that frame, alongside contact, load and GameMode events.
- An authored OnActivate block suppresses the default action even when its body
  does nothing. An Activate command bypasses that block. Header arguments on
  OnActivate are ignored; action-reference queries filter inside the body.
- Trigger admission adds one entering reference per frame. When no enter is
  pending, it removes one departing reference. Enter and leave do not share a
  frame. OnTrigger uses admitted contact membership without supplying an action
  reference. Physics encounter order is retained; retail tie ordering is unproven.
- Stage-completion queries read entered-stage state, not just the current maximum
  stage. Effects execute synchronously so subsequent script queries see them.
  Reached unsupported commands preserve the executed prefix and fault the owner.
- XPRM has 32 bytes: three half extents, editor color, an unknown float and a
  shape kind. The native adapter binds boxes and uniform spheres. XTRI is an
  optional uint32 collision layer; the trigger layer is 12. Other shape/filter
  cases fail visibly. Reference scale and transform remain source-owned.

These neutral contracts draw on the published
[activation behavior](https://geckwiki.com/index.php/OnActivate),
[enter behavior](https://geckwiki.com/index.php/OnTriggerEnter),
[leave behavior](https://geckwiki.com/index.php?title=OnTriggerLeave),
[contact block](https://geckwiki.com/index.php/OnTrigger), and
[xEdit FNV format declarations](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.5/Core/wbDefinitionsFNV.pas).
No external implementation or retail script body is incorporated into OpenNV.

## Executable checks and limits

`ReferenceScriptContractProbe` checks synthetic overrides, activation suppression,
effect/query order, simultaneous contacts, filters, independent locals and faults.
An optional owned installation argument additionally checks Doc's trigger/menu
scripts and the Goodsprings cave's cross-reference trigger, including restored
reference locals. Its effect host does not establish visible menus or gameplay.

`res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn` exercises
real Godot overlap/leave/re-entry, source half extents, axis conversion and queued
activation with disposable synthetic state. The existing full gate includes it.

From the repository root, reproduce the selected checks with:

```powershell
dotnet run --project contract-tests/ReferenceScriptContractProbe -c Release -- 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
dotnet build runtime/OpenNV.sln -c Debug --nologo
& 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path runtime res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn
```

The ordinary entry points are connected, but the full ordinary opening has not
been replayed after this change. Actual player furniture, posed actor query
contacts, source conversation choices and non-fading reference enable now have
component checks; see dialogue-and-furniture.md. Complete actor/creature physics,
native fades, deletion, MenuMode event admission and cold interaction/procedure
restoration remain incomplete. No matched retail timing, collision-filter,
presentation or parity claim follows.
