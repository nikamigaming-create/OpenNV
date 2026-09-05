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
  The exact counter ownership and number/order of initialization calls are
  still unresolved; iterating only supported runtime scripts cannot supply it.
- An authored quest delay of zero does not request every-frame execution in
  the observed native path. The recurring scheduler uses the configured
  default unless the associated authored delay is positive.
- Recurrence adds the selected delay to the remaining countdown, retaining
  overshoot, instead of replacing it with a fresh duration. Elapsed script
  time is a separate owner and resets after a scheduled invocation.
- Private snapshots taken during the same visible modal show changing native
  script countdowns and elapsed times. Freezing the entire script scheduler
  while MessageMenu is present does not reproduce this behavior. GameMode
  block admission and menu-time scheduling must remain separate concerns.

## Current implementation gaps

FalloutQuestScripts enumerates numeric QUST IDs, starts supported scripts with
zero remaining time, accepts authored zero as every-frame execution, resets
the countdown on recurrence, and is skipped by the Godot adapter during a
modal. Source script initialization, engine configuration, block admission,
Float32 cadence, shared-script ownership and cold restoration must be bound
together. Do not repair visible pack order with named IDs or a sorted list of
the four observed messages.

The default ShowMessage button is an independent declaration: when no authored
button is enabled, the command selects its owned literal. It does not select
the separate sOk setting. The runtime now follows this declaration, with
synthetic malformed/ambiguous declaration checks and an explicitly selected
owned executable audit. Native conditional-button evaluation remains unbound.
