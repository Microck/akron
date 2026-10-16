# Recorder launch specifications (#198)

## Fresh native baseline

QA152 used native Celeste/Everest UI and real encoder output with x264/MKV,320×180,30fps, audio tracks Off, replay buffer0/auto-startOff, and remuxOff. The unchanged control has coherent geometry (910frames,30.333s). A second recording began identically, then only Resolution X was edited to640 while Stop Recording remained active. Its320×180/30fps header remains valid and full decode returns0, but later frames have scanline tearing, tiled/reordered room geometry and misplaced terrain/sign features (3175frames,105.833s). A fresh third recording at640×180 is coherent at its expected stretched aspect (1359frames,45.300s).

The mutation's first observed encoded corruption is frame1522 (~50.733s), following coherent1520/1521. Native text entry passes through intermediate digits; that encoded transition is not claimed as the exact GUI/engine frame for final640. Persistent later corruption, valid controls and the original screenshots establish the failure independently of decode success. All three files decode0. Width320 was restored before native save/exit.

## Correction

Manual and replay encoders now retain separate immutable launch specifications and separate cadence objects. Frame writers use each sink's own width/height while the current viewport remains the dynamic source. A new sink starts its deadline at the current shared clock offset without resetting another sink's cadence. Each synchronous write completes before the shared conversion buffer is rebuilt. Replay conversion/write/pruning remains inside the isolated replay failure boundary.

Manual and replay counters and warning state are separate. Replay-only drop feedback is retained; public counters select manual while recording and replay while only buffering. Settings edits can restart replay with a new spec under existing replay behavior, but cannot silently change a running manual encoder's byte format or cadence.

## Verification status

Corrected-package native acceptance is pending. Required checks include the width-mutation control/recovery, FPS mutation, and manual/replay coexistence at differing launch specifications. No native acceptance is inferred from automated scheduling/source tests or successful FFmpeg decode alone.

## Review correction: retain diagnostics after replay stops

The first published candidate fixed geometry but selected old manual counters after a replay-only stop. QA153 genuinely reproduced this through the native Everest console: prior manual3382frames/3drops; running replay2106frames/1drop; stop-replay incorrectly returned3382/3. The revised diagnostic selection retains the most recently stopped sink while idle, prioritizes active manual recording, and otherwise selects active replay. Start/stop bookkeeping changes only for a real process, so no-op stops do not erase the last result. Eight selection cases and source lifecycle guards are covered. Revised native validation is pending.
