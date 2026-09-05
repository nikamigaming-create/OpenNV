# Quest script timing contract

This is a partial, implementation-neutral contract from private Win32 observe
snapshots and owned-file declaration analysis. It does not certify startup
event order or prescribe captured values as runtime state.

## Verified observations

- The selected native lists contain 640 QUST and 3,707 SCPT objects. Every
  identity and position agrees with reversing first source registration order
  across the active plugins. Winning overrides retain the first registration
  position; numeric FormID order does not describe these lists.
- Quest-script initialization uses the engine's configured quest script delay.
  Its phase is distributed with eight successively halved fractions of that
  delay and a shared initialization counter. A zero low counter byte selects
  the full delay. The native initializer performs Float32 stores at each step.
  Embedded declarations participate before the standalone script-list pass.
  The selected winning graph contains five empty terminal-entry scripts and
  two empty quest-stage scripts with the quest-type header byte. Their private
  initial countdowns occupy the first seven slots; the quest-stage scripts
  then clear their type byte. All 489 standalone quest-script declarations
  consume subsequent slots, whether active, attached or supported by the VM.
  The resulting counter and all 252 retained standalone initial countdowns
  match the selected native observation, including exact Float32 bits.
- An authored quest delay of zero does not request every-frame execution in
  the observed native path. The recurring scheduler uses the configured
  default unless the associated authored delay is positive.
- The quest DATA delay applies independently of the quest flags. The shorter
  two-byte DATA layout retains a zero delay. Native positive delay selection
  happens at the script's associated quest; repeated quest bindings publish
  the last association in source registration order. Runtime delay changes
  can differ from the initial DATA value and need their own mutable owner.
- Recurrence adds the selected delay to the remaining countdown, retaining
  overshoot, instead of replacing it with a fresh duration. Elapsed script
  time is a separate owner and resets after a scheduled invocation.
- Countdown and elapsed time accrue a frame delta only when the countdown
  was positive on entry. An already-due script does not consume that delta
  again. Each update invokes a due script at most once; it does not loop to
  catch up after a long frame. Countdown and elapsed stores are Float32.
- Private snapshots taken during the same visible modal show changing native
  script countdowns and elapsed times. Freezing the entire script scheduler
  while MessageMenu is present does not reproduce this behavior. GameMode
  block admission and menu-time scheduling must remain separate concerns.
- A fresh launch also advances these clocks behind the main menu, before
  New Game. Script initialization can precede the final script-list linking
  pass; an initialized form is skipped by that pass. The embedded declarations
  above account for the earlier quest-type initialization slots in this graph.

## Implemented recurrence and remaining gaps

FalloutQuestScriptClock now reads the configured delay through the installation
settings owner, chooses a positive authored override, preserves overshoot and
Float32 stores, and separates elapsed time from countdown. Synthetic checks
cover long frames, modal recurrence and identical clock bits after cold JSON
restoration. The selected owned-script audit verifies source intervals and
unchanged inventory/message state after restoration. Missing legacy clocks
are rejected before a campaign is restored or a valid save is replaced.

FalloutQuestScriptInitialization now derives the complete standalone quest
clock phases from winning embedded headers, first source registration and all
standalone declarations. It does not store a fitted initial counter or pack
order. Synthetic fixtures cover overrides, inactive and unattached scripts,
embedded declarations, shared definitions and cold consistency. The selected
owned audit matches the retained native countdown bits described above.

The Godot adapter retains the clocks from the main-menu session through New
Game and advances recurrence during modal presentation while excluding
GameMode effects. Continue replaces the menu session with saved clocks.
Source MenuMode blocks, exact block admission, mutable processing delays,
dynamic quest start/stop, full engine-event identity and aligned running-clock
timing remain open. No startup event-order or scene parity claim follows from
the initial-countdown and recurrence evidence. Do not repair visible pack
order with named IDs or a sorted list of the observed messages.

The default ShowMessage button is an independent declaration: when no authored
button is enabled, the command selects its owned literal. It does not select
the separate sOk setting. The runtime now follows this declaration, with
synthetic malformed/ambiguous declaration checks and an explicitly selected
owned executable audit. Native conditional-button evaluation remains unbound.
