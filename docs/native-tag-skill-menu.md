# Owned tag skill menu

`RuntimeNativeTagSkillEntry` presents the winning
`menus/chargen/char_gen_menu.xml` through `NativeOwnedTagSkillMenu` and the
shared owned tile, bitmap font and texture-atlas renderer. It replaces the
generic Godot column previously used by `SetTagSkills`. Flat and XR retain the
same tag selection and acceptance owner; physical XR presentation is unverified.

The expanded XML supplies the list template, selection markers, highlight,
description panel, icon, count panel, Reset and Done artwork, and PC shortcuts.
Winning AVIF records supply identity, display name, description and optional
icon. No retail UI art, text or transformed resources are repository inputs.

The skill-page declarations observed in the owned executable establish these
implementation-neutral contracts:

- The heading formats the selected and required counts through `sSkillsTitle`.
  The counter formats remaining selections through `sSkillsCount`, appends the
  executable's plural literal for non-singular counts, and hides at zero.
- Selecting a skill toggles a draft marker. The draft cannot exceed the
  command's required count. Done accepts only a complete draft; Reset clears
  the draft without changing accepted tags. Acceptance preserves AVIF identity.
- The native row obtains a permanent integer player value first, removes an
  existing tag's float bonus from `fAVDTagSkillBonus`, then applies the draft
  bonus. The formatter sums the row values, converts to an integer, and clamps
  to 0–100. Fractional synthetic bonuses distinguish this order from converting
  only after draft arithmetic. The menu refreshes changed values
  while open. The live player owner currently supplies its current value;
  the native permanent actor-value distinction remains unbound.
- The executable assigns the list's Y placement after selecting its row
  template. An address-free declaration reader obtains that float and the
  plural suffix from the selected owned file, rejecting missing or ambiguous
  associations. Blank row heights use bitmap text height plus authored vertical
  spacing. Blank list height uses first-row height times the authored visible
  item count. Source-authored list heights remain expressions.

Transparent input targets follow rendered tile bounds. Pointer clicks,
keyboard row navigation and source-declared action shortcuts update the same
draft. Hidden scrollbar targets do not evaluate their inactive layout
expressions. Menu errors disable input and reach the opening driver's
`ExecutionError`. Error telemetry remains readable without repeating the
failing player-value query.

The wrapper pauses gameplay while accepting menu input, remembers the prior
pause and mouse mode, and restores them once on accepted closure, cancellation
or tree exit. The opening driver acquires player modal input separately and
restores its prior value through the wrapper's release event. Failure leaves
the menu blocked and visible to telemetry until its owner closes it. The
opening driver releases the wrapper before freeing it.

## Verification

`GamebryoUiTileContractProbe` includes synthetic tests for source declaration
changes and rejection, winning AVIF overrides, identity validation, draft
isolation, limits, Reset, live values and setting changes, integer conversion,
clamps and source count formatting. Synthetic plugins are removed in `finally`.

`NativeTagSkillMenuAudit` is an isolated owned-data fixture. It resolves the
selected mod stack, renders through a native Godot renderer, sends ordinary
viewport mouse and keyboard events, checks actual pixel changes and matched
focus Reset pixels, observes a paused gameplay clock, exercises acceptance,
prior pause, cancellation and visible failure. An isolated inherited driver
suppresses campaign startup, binds owned menu/player data, and executes the
production sync, acceptance and retirement paths for acceptance, cancellation,
failure cancellation and tree exit. Each path restores both prior-modal cases,
mouse mode and pause once while preserving the source control mask. It does
not exercise campaign initialization or progression. The audit verifies the owned XML is
unchanged. Recording is off. Its one private PNG is a temporary visual
diagnostic; the caller inspects it and deletes it in a cleanup path. Failed runs
delete it themselves. The fixture does not represent campaign progression.

Exact retail layout, sort ties, focus/click sounds, scrollbar dragging, timing,
permanent actor values, physical XR input and matched retail pixels are
unverified. This work does not certify GOAT, quest progression or mod support.

## Opening HUD boundary

The toddler's winning source stages re-enable movement and rollover text while
leaving other controls disabled. The current HUD follows movement for HP/AP
and reticle visibility, and movement plus rollover for target text. The
original HUD XML does not provide a toddler exclusion. The examined normal HUD
consumer includes the HP/AP flags and a separate visibility override; no
winning opening script establishing a toddler override was found. Disabling
HUD from fighting, Pip-Boy or character-generation state would be an inferred
presentation rule and is not implemented here. The reported opening HUD
discrepancy remains open for a matched native-state investigation.
