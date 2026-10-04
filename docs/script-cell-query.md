# Source cell queries

GetInCell reads the actual subject's current cell and compares its winning EDID
with the winning interior CELL argument's EDID prefix. The argument is a typed,
master-adjusted Form. The player uses the current campaign cell; placed
references use shared placement, including moved and unloaded state. Cold saves
already retain those placements. No selected quest or location supplies the
result.

The [GECK contract](https://geckwiki.com/index.php/GetInCell) permits an interior
argument to match related named exterior cells. Exterior arguments return false;
an unnamed exterior has no matching prefix. Missing player location, wrong record
types and absent required interior EDIDs remain visible failures. The shared and
fallback quest interpreters, reference/results executor, dialogue conditions and
recipe conditions use the same C# prefix owner. Typed postfix calls remain lazy
and this read-only query cannot replay a latched script's consumed prefix.

Synthetic full-reader checks cover implicit and grouped receivers, typed cell
arguments, winning overrides, case-insensitive prefixes, distinct player/NPC
cells, changes between ticks, moved/unloaded/cold references, named exterior and
wrong-type/missing-owner negatives. Both quest execution paths are exercised.

The selected owned audit runs the unchanged Escape GameMode program in an
isolated fixture. Its original two CELL operands select their original stage
requests independently for the first Vault cell, the requested second cell and
a cell outside the Vault prefix. The timer/radio prefix executes once and source
bytes remain unchanged. These isolated stage requests do not establish campaign
progress, retail timing or parity. The genuine post-exam save is the starting
point for subsequent ordinary Escape input.
