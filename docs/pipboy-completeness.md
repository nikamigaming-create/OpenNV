# Pip-Boy completeness

The user's failed playtest makes device lockout, inaccessible inventory rows,
map controls, fast travel and actual radio playback the current repair scope.
Flat and OpenXR use the same gameplay transactions and persistence. Every row
requires ordinary input and saved-state checks; construction alone is not a pass.

The [official FNV manual](https://cdn.akamai.steamstatic.com/steam/apps/22380/manuals/fnv_gfw_manual-10-steam.pdf)
and winning owned menu/record declarations define the baseline. VR adaptation
adds explicit focus, ray selection, neutral rearming and readable controls.

| Area | Required behavior | Current evidence / owner |
| --- | --- | --- |
| Device | Open, close, reopen, page selection, light, enlargement, tracking loss, input release | Action errors previously disabled all wrist input; recovery repair under verification. |
| Navigation | Reach every row and detail; scroll line/page; keyboard, mouse and controller access | Wrist wheel route was absent and authored scrollbars hidden; repairs under verification. |
| Status | HP/AP/XP, radiation, limb condition, targeted treatment, active effects, hardcore needs | Existing source projections and treatment owners require complete interaction review. |
| Character | SPECIAL, skills, perks, reputation/general statistics and descriptions | Existing pages are partial; every displayed value needs its shared state owner. |
| Inventory | Weapons, apparel, aid, misc, ammunition; count, value, weight, condition and equipped state | Existing equip/use transactions; long-list verification pending. |
| Item actions | Equip/unequip, use, quantity-aware drop, repair, mods, hotkey assignment | Drop/repair/mod/hotkey gameplay and save owners are incomplete; removal alone is not drop. |
| Local map | Resident geometry, player/door/quest markers, pan, zoom, clipping | Drag/wheel exist; wrist wheel route absent; extra viewport cost needs measurement. |
| World map | Source map/markers, pan, zoom, selection, custom marker, quest routing | Selection previously reset view; retained view and explicit zoom under verification. |
| Fast travel | Select known destination, readable refusal/confirmation, actual relocation, followers, time, save/cold continuation | Shared coordinator already owns relocation/time/followers; fresh ordinary UI exercise required. |
| Quests | Active/completed objectives, selection and map linkage | Existing shared quest state; end-to-end routing review required. |
| Notes | Text/image/audio/voice, scrolling, playback and stopping/reopening | Existing pages/playback adapters; long text and lifecycle checks required. |
| Radio | Discovery, range, tuning/off, source broadcast chains, synchronized physical receivers, save/cold continuation | Finite source dialogue/PCM continuation exists. Continuous scheduler and receiver binding remain missing. |
| Recovery | Failed action stays visible without trapping input or corrupting another page | Native owned audit passes failed action/page recovery and fresh pointer capture; physical headset acceptance remains separate. |
| Performance | Page/open/scroll latency, asynchronous media preparation, bounded publication, no inactive duplicate rendering | Menu declarations/named tiles reused; map markers prepare on stack worker. Selected General Stats refresh changed from 1,937 ms to 50 ms; no headset-budget acceptance. |

Phone-shaped objects follow their winning ACTI/TACT scripts, dialogue and sound
records. A model name does not establish telephone or radio behavior. No invented
calls, shuffled audio-file playlist or removal of source results is acceptable.

Acceptance includes both pointer modes, more entries than fit on one page,
zoom/pan before and after selection, interrupted input, close/reopen, active
radio save/load, genuine travel and frame-time measurement. Physical headset
acceptance and retail pixel parity remain separate evidence lanes.

The current native owned audit passes long inventory line scrolling, source
scrollbar clicks, explicit local/world-map zoom, world-map view retention, page
and action failure recovery, and pointer cancellation. The complete runtime gate
passes. These checks use disposable owners restored from an unchanged checkpoint;
they do not establish radio playback, full travel acceptance or physical XR use.
