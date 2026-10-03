# Source radio refresh

The shared C# radio owner reads winning TACT/REFR station identities and XRDO
broadcast declarations. Global ForceRadioStationUpdate and FRSU refresh that
owner from the actual player placement and applied reference enable state.
They do not advance quests or replace the source command with a no-op.

## Source contract

TACT's station, non-Pipboy and continuous-broadcast flags retain independent
meaning. XRDO contains radius, range type, static percentage and an optional
master-adjusted position reference. The admitted owned extent is16 bytes.
Unknown layouts, invalid ranges and missing anchors remain visible errors.
See the [REFR format](https://tes5edit.github.io/fopdoc/FalloutNV/Records/REFR.html)
and [authored radio setup](https://geckwiki.com/index.php/Radio_Stations).

Reception distinguishes Everywhere, current physical CELL, linked interiors,
worldspace with linked interiors and same-space radius. Interior links use the
winning XTEL graph; exterior current-CELL checks use source coordinates rather
than a persistent reference's parent CELL. Radius anchors are independent of
the station's own placement. Cross-portal radius distance remains unbound and
withholds that reception with an explicit per-transmitter error.

Enqueued Enable does not immediately change reception. The shared world applies
it, then ordinary refresh exposes the transmitter. No station-specific runtime
path is used. The initial refresh seeds known signals without discovery notices;
later discoveries publish once through the shared HUD and sound owners. Private
owned observation supplies an implementation-neutral initialization contract;
no executable addresses or decoded instructions are repository inputs.

Discovery text resolves the winning sRadioStationDiscovered setting. The owned
executable's declaration supplies the icon, sound EDID and duration; there are no
fitted values or substituted media. The original Pip-Boy XML radio list consumes
the same available station identities in flat and OpenXR. Tuning still fails
visibly because conversation/static playback has no owner yet.

## Persistence and checks

Campaign schema30 stores source-bound discovery history and queued HUD notices.
Schema29 and the previously admitted older schemas remain readable; older schemas
reject new radio state. Cold validation checks station ownership before live
mutation. Availability is rebuilt from the winning graph, restored applied world
state and player placement; a load never replays a discovery sound or notice.

Synthetic contracts cover winning overrides and master-relative identities,
range separation, position anchors, queued/applied enable, non-Pipboy exclusion,
initial silence, once-only discovery, cold refresh, invalid restoration and owned
HUD declarations. A selected installed TTW audit dispatches the reached CG02
ForceRadioStationUpdate result through the shared script adapter, reads55
transmitters covering all five range types, and checks the authored Vault101
enable/discovery sequence and cold state. Seven cross-portal radius queries remain
visible in that isolated fixture. The fixture does not execute the other stage12
commands or establish ordinary campaign progress.

## Remaining owners

Radio conversations, continuous/static audio, physical ACTI listeners, tuning,
cross-portal radius distances, exact update cadence, waveform UI and matched retail
timing/pixels remain unaccepted. Station availability and discovery are component
support, not complete radio, mod or campaign parity.
