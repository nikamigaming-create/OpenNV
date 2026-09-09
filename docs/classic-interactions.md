# Classic source interactions

The shared native player now owns door state for Fallout 1 and Fallout 2.
Clicking a visible door walks to an adjacent source hex, executes its admitted
use procedure and applies the default action only when the script permits it.
Locked doors and control-operated doors retain those restrictions. Closing
checks the source doorway footprint against the player and blocking objects.

Door animation uses the owned FRM frame count, action frame, offsets and frame
rate. Passage changes update the player's actual navigation graph. Original
sprites and source-derived extruded panels consume the same frame. All frames
use the closed frame's ground plane. These panels still need visual refinement
and finished 3D counterparts; they are not cinematic-quality acceptance.

Native INT module startup and door map-entry procedures execute through the
existing C# interpreter. MAP script IDs join their actual object IDs and
scripts.lst entries. All doors are registered before entry dispatch. Script
locals, program variables, global/map variables, exported values, door locks
and animation phase persist in v4 saves. Campaign-header globals feed this
owner before door initialization. Reached unsupported effects reject the event
without publishing a partial result.

Ordinary parameterized helper declarations no longer prevent the entire INT
file from loading. Their calls still require argument execution support.
Deterministic events can run without a random contract; reaching RANDOM in
that mode fails explicitly. FO1 is never given the FO2 random policy.

Source `message_str` requests resolve the one-based script-list entry to its
owned `text/english/dialog/*.msg` file on demand. Literal INT display strings
also work. Message identities use stable private C# handles, so a saved program
variable still resolves after a cold load. Missing entries fail explicitly.
Startup and entry messages publish together only after the whole event succeeds.
Player SPECIAL/gender queries use the live accepted character's stats.

Shift-clicking a door examines it through `description_p_proc` and the original
PRO description when the script permits the default. Ordinary door use sends
its source display text to the same HUD. Its six-line original panel keeps
wrapped history; the mouse wheel over that panel scrolls earlier lines. The
Vault 13 door's original terminal instruction and FO2 temple door descriptions
execute in owned fixtures. Trapped-door skill/stat checks still reject their
procedures without changing source state or publishing partial messages.
Floating text, dialogue, voice and retail message timing remain separate owners.

The owned loading check covered 72 FO1 maps / 392 door placements and 155 FO2
maps / 1,171 door placements, with no map-load failures from this owner. FO2
has two empty elevations without doors. Four FO1 and 195 FO2 door initializers
still reach unbound behavior. These are loading results, not campaign or script
completion. Source fixtures exercise open/close, passage changes, mid-animation
and terminal cold restore, and changed-source rejection in both games.

## Archive correction and save recovery

The DAT1 decoder now resets its LZSS dictionary for every compressed block and
reads stored-block sizes from the low fifteen bits. The previous decoder could
return the expected byte count while corrupting scripts and other long members,
and rejected some stored speech blocks. A synthetic mixed-block fixture checks
dictionary reset, overlapping matches and an intervening stored block. All
20,456 compressed members of the two owned FO1 archives decode successfully.
The original 960-entry script list now joins vault doors to their proper scripts.

FO1 v1-v3 exploration saves can recover automatically only when their old MAP
hash exactly matches the former decoder applied to the same owned files.
Every affected carried item's original record must remain identical. Unknown
hashes or changed item records are rejected. No original MAP or retail file is
rewritten, and obsolete decoded bytes are used solely for recovery validation.
The first save after recovery preserves the original save beside it with a
`.before-dat1-fix-<hash>` suffix. Max's existing save retained tile 17489,
43 HP, 34 completed steps, its equipped knife and all 24 ammunition rounds.

## Remaining owners

General map/object lifecycle ordering, repeated entry and timed events, door
audio, skill checks, lockpicking, traps, key use and floating messages remain
required. The Vault 13 control interaction is still unbound. Script failures
must remain visible and must never grant passage. Stairs/ladders/elevators,
combat, dialogue, quests, the world map and complete campaign progression remain
unfinished. Full art and gameplay parity are unaccepted.

Focused commands (owned roots stay private):

```powershell
dotnet run --project contract-tests/FalloutDat1RuntimeProbe -- --owned-root '<Fallout installation>'
dotnet run --project contract-tests/ClassicMapInitializationProbe
dotnet tools/OpenNV.DevelopmentLab/bin/Debug/net8.0/OpenNV.DevelopmentLab.dll classic-interactions '<installation>' fallout-1
dotnet tools/OpenNV.DevelopmentLab/bin/Debug/net8.0/OpenNV.DevelopmentLab.dll classic-interactions '<installation>' fallout-2
# Add --all for loading coverage; it does not certify script execution.
```
