# Independent image-space lifetime

The shared C# IMAD state has one native gameplay-clock adapter per session,
outside the quest/character-creation driver and individual CELL presentations.
Interior/exterior presenters compose that same state; they do not advance or
restart its effects when their nodes are replaced.

The adapter advances only on unpaused gameplay frames and precedes presentation
publication. Source durations, animation keys, static-effect persistence and
explicit removal remain unchanged. A failed appearance, package, save or source
invocation remains visible but cannot freeze an unrelated finite image effect.
Clock failures are independently logged and exposed in live state.

The reported original exit modifier, `DEMOEyeAdjustISFX`, is an animated ten-second
source effect. The last genuine exterior observation retained it at zero elapsed
time while the character driver was stopped on an authored model-less HDPT
selection. Its clock now expires independently at the original threshold.
Model-less head-part declarations retain their selection and extra-part graph
without inventing drawable geometry or passing a null mesh to the native builder.

Synthetic current/pause/fault/static checks and the unchanged owned exit modifier
pass in native Godot. These tests prove lifetime ownership, not the final exterior
pixels, all lighting/HDR behavior or matched retail adaptation. Ordinary exterior
replay now additionally reaches Escape stage 150 in exterior CELL 0010c1 at
115 HP through actual owned input. Original modifier 0213fe advances from
0.566 to 9.624 seconds and then disappears at its authored ten-second boundary.
The inspected final native view retains visible terrain, weapon, HUD and night
sky without the reported persistent whiteout. Temporary PNG/pixel/metadata
captures are deleted after inspection and recording stays off.
Complete exterior saving, all lighting/HDR behavior and matched retail final
pixels remain separate requirements; the reached exterior still has explicit
source/resource divergences.

```powershell
& $Godot --headless --path .\runtime 'res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn' -- --image-space-clock
& $Godot --headless --path .\runtime 'res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn' -- --owned-image-space-clock $OwnedFNV 'ttw' $OwnedTTW 'FalloutNV.esm:0213fe' @OwnedDependencies
dotnet run --project .\contract-tests\FalloutPluginRuntimeProbe --configuration Release -- --audit-model-less-headpart ttw $OwnedTTW $OwnedFNV FalloutNV.esm 0bcbff 000007 @OwnedDependencies
```
