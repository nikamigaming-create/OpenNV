# Source radio broadcast state

SetBroadcastState and GetBroadcastState share a mutable continuous-broadcast
flag on the winning placed radio reference. Its initial value comes from the
TACT Continuous Broadcast flag. Independent placements of one base station
retain independent overrides. Zero selects scripted broadcasting and one
selects continuous broadcasting, as documented by the
[source command](https://geckwiki.com/index.php/SetBroadcastState).

This state does not disable the reference, change reception, tune or retire the
Pip-Boy receiver, or fabricate a radio voice. The radio scheduler and scripted
radio conversations still require their own execution and audio owners.
Reference events, result scripts and fallback quest scripts share the same
state, including explicit targets, calling references and postfix queries.
Invalid states and non-radio targets fail before mutation.

Reference snapshots retain the nullable override. Fresh-world restoration
validates the target against winning radio declarations before publishing any
reference. Campaign schema v35 admits this state; v34 indexed tag slots and
v33 membership continue to load and upgrade when saved. Earlier schemas reject
an injected broadcast override without replacing an existing valid save.

Synthetic contracts cover source defaults, separate placements, receiver/voice/
enable independence, both script owners, calling and explicit references,
postfix queries, cold restoration and invalid atomic restoration. The selected
owned audit executes the original TTW CG04 stage-zero broadcast command from
its original compiled bindings, retains scripted mode cold, and verifies
unchanged quest, reference and base bytes. That isolated audit does not establish
ordinary Vault escape or broadcast timing parity.
