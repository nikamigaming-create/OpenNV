# Source radio broadcast state

Campaign save v48 retains ended scripted conversations separately from station
mode. The original station/reference/base/topic, winning record hashes and
cumulative completed lines validate against native speaker generations. Cold
restoration selects no INFO, plays no audio, calls no result script and consumes
no random draw. A later authored Start uses the same history. An ended native
voice releases only its own radio lease; its station history remains visible.

Save admission checks the actual native continuation fields, including opaque
callbacks, rather than relying only on the active INFO flag. Active radio,
speech and unowned callback suffixes still refuse capture. Synthetic cold and
source-drift checks pass, along with the ordinary FO3 partial manual checkpoint
and fresh-process Continue. Older v47/v46 saves stay readable without inventing
missing ended-radio history. These checks do not establish continuous broadcast,
reception/attenuation or matched retail audio.

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
