# Replay window duration (#203)

## Native baseline
QA160's ordinary in-game Save Replay action with Buffer5 produced 320 frames at30fps:10.666 seconds, despite a sidecar requested interval of exactly5.000 seconds. Full decode passed. This native observation supplements the original synthetic-segment report; neither baseline independently proves A/V alignment.

## Correction
Replay capture now owns a monotonic, anchored CFR frame grid. Missed samples hold the previous replay-owned image rather than compressing elapsed time or moving future imagery backward. Catch-up has explicit frame and byte budgets; excessive stalls fail instead of silently shortening history. Manual capture cadence and frame buffers remain independent.

FFmpeg writes segment timing CSV. Export selects finalized media by that timeline, rounds requested endpoints upward to a half-open frame grid (less than one frame per boundary), and re-encodes exactly the chosen frame count. Fully available five seconds at30fps is150 frames. Missing early/late history is clamped and reported as partial; missing interior media is rejected. Sidecars retain both actual frame-grid bounds/duration and original requested bounds.

One background worker owns a detached replay generation and all currently due requests. It finalizes once, shares PCM across that batch, and publishes completion on the main thread. A later request crossing an automatic restart may have only partial available history; it is not silently labeled a complete original window. User Save while export is busy leaves capture untouched and reports that the export is running.

Replay audio slicing uses true sample intersections, streaming silence for unavailable head/tail. The replay mux is capped to video duration rather than letting a short track shrink the video. This fixes interval arithmetic; FMOD sample-origin precision still needs native A/V measurement. Encoder/filter settings do not reapply capture colorspace filters during export.

## Verification status
Pure tests cover gap holding, absolute-slot jitter, exact stop boundaries, rejected geometry/clock/gap errors, five-second and in-segment planning, missing history and PCM padding. Native corrected duration/content, concurrent manual continuity, failure/recovery and audio checks are pending. Automated/source checks do not substitute for live acceptance.
