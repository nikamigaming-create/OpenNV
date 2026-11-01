# Placed object animation startup

Placed world models defer managed animation startup until their text-key and
sound handlers are bound. The same selector applies across record classes:
exactly one case-sensitive `Idle` or `SpecialIdle` looping group starts by
default. An arbitrary sole loop is not a default request. Lowercase `idle`
remains registered without starting. If both canonical names exist, their
priority is explicitly unbound; an explicit source request can still select a
group. Nonlooping canonical default startup also remains unqualified.

This replaces the previous STAT-wide suppression. Bounded retail observations
showed advancing canonical default groups for the reported flag and windmill
families, outdoor signs and a lamp, while an earlier lowercase chandelier group
remained inactive. Representative MSTT defaults and ACTI default/script groups
were checked separately. These observations do not establish every model or
animation phase.

Explicit source playback and script-selected saved continuations take precedence
over defaults. Nested ADDN/effect models that already have their own playback
owner retain it. Valid default snapshots preserve elapsed time and consumed start
keys. Older implicit snapshots outside the admitted default are corrected with a
diagnostic rather than becoming new playback authority. Detailed object state
reports selected sequence, source time, Playing/processing and startup ownership;
a non-null selected sequence alone is not evidence of motion.

Direct active controllers keep their independent clock and channel owners. In
the observed flag, the managed loop animates nine bone transforms; separate
APP_TIME morph controllers legitimately remain clamped at their authored end.
Starting the missing bone loop does not restart or loop the morph weights.
Windmill rotation likewise comes from authored transform channels rather than
an invented weather-driven curve. Native initial phase, culling/update timing,
final deformation and matched pixels require independent validation.

The existing managed-sequence registration audit covers canonical case,
advancing channels, saved phase, consumed start events, explicit-request
precedence, ambiguous default refusal and the independent clamped morph clock.
