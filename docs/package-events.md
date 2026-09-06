# Package event ownership

PACK event sections declare behavior; loading a package does not execute its
end or change section. POBA belongs to starting a new package, POEA to the
procedure reaching its done state, and POCA to replacing the current package.
Reevaluating the same active package does not create another begin event.
Repeated observations of arrival do not execute its end event again.

The reader retains each section's script and compiled references separately.
At dispatch, the result script precedes the event's other effects. Empty,
comment-only and bound Look/StopLook programs are admitted; compiled extent mismatches, compiled code
without an available source program, nonempty unimplemented scripts and event
topics remain explicit failures. A failed event cannot be retried implicitly
or reported as completed. No actor or location identity selects these rules.

The NPC travel owner emits completion after its authored locomotion reaches
the NAVM destination. Package changes from occupied furniture wait for the
existing source exit animation. Event idles use the shared owned IDLE/KF
player; change idles still require deferred replacement ownership. Initial
furniture placement retains its existing limitation: subsequent approaches
to furniture do not yet own a full navigation and entry sequence.

Synthetic coverage exercises same-package reevaluation, single completion,
ordered replacement, delayed admission of an unreached script, reached
failure and suppression of retry after failure. Selected owned data exercises
the opening actor's nine package declarations and their event scopes. These
are component contracts, not matched timing or visual acceptance.

Private read-only observation of the native Look and StopLook handlers finds
separate target-setting and target-clearing operations, with a distinct
optional whole-body branch. The selected target-slot lifetime and physical head
owner now execute these commands in their compiled scope. Ordinary room-81
completes the former Look failure, subsequent speech/StopLook and couch travel.
See head-tracking.md. Automatic/eye targeting, complete reference/process state,
native event timing, event topics, deferred change idles and save restoration
remain open. No native code or addresses are repository inputs.
