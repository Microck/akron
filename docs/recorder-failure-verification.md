# Recorder failure verification

Fixes #200. Manual recordings are published only after a natural FFmpeg exit 0
and a nonempty output. Timeout, unknown exit status, capture/pipe failures and
missing/empty output retain the previous successful clip and report failure.
Encoder diagnostics belong to the process and are bounded; warning-only stderr
does not invalidate successful media. Pipe cleanup cannot skip termination.
Failed encodes skip remux, audio mux and sidecar publication; returned audio
tracks are cleaned, including tracks with no samples. Partial video is retained
for potential recovery, without labeling it successful. Optional postprocessing
failures retain the validated original video.

## Native baseline

QA138 used CPU/x264, 640x360 at 60 FPS, default output, MKV, audio/replay/remux Off.
The valid control produced a 2,800,771-byte video, 15.150 seconds and 909 frames,
with full decode exit 0. Selecting the built-in WebM container without changing
x264 produced two 262-byte invalid files, both with erroneous sidecars. The
second attempt visibly reported Recording saved and replaced the previous good
clip in the Start Recording tooltip. No fake encoder or injected state was used.

## Candidate status

Native Release and Debug each pass all 2,490 tests. The final Release package is
`b7f339f0f43d4352b0cb6d8554aa9539666dbd80e5741b424b0e2c3998770b51`.

QA139 used the same initial profile and native GUI. A good MKV preceded two
x264/WebM rejections. The failure toast was readable, the previous good clip stayed
in the Start tooltip, and the invalid 262-byte files received no sidecars. A fresh
MKV afterward produced 6,169,916 bytes, 33.417 seconds and 2,005 frames with full
decode exit 0. Repeated Stop after failure did not republish the rejected file.

With Full mix and auto-remux enabled, WebM was rejected again without a sidecar.
However, Full mix reported ERR_INVALID_HANDLE before audio capture could start.
This does NOT establish native audio-track cleanup or audio mux acceptance. A
subsequent MKV still recorded and remuxed successfully to a 6,729,222-byte MP4,
36.199 seconds and 2,172 frames, full decode exit 0. The retained MKV also decoded.
The remux check is a success-path control; WebM does not enter the MKV remux branch
even without the new failure gate. Audio and remux were restored Off afterward.

Screenshots, logs, profiles and media remain private. Unit tests exercise process
completion and bounded diagnostics; source-order assertions are not native audio
lifecycle proof.

## Limits

Nonempty output plus exit 0 is the encoder completion contract, not a full decode
in the game loop. Offline full decode remains part of QA. No frame-perfect timing
claim. Replay encoder finalization, start-time path exceptions, optional mux
end-to-end timeout behavior and sidecar container metadata are not claimed fixed.

## Filename-collision review correction

A failed recording could overwrite a previous successful clip when a custom
template reused its path. Manual, remux and audio-derived destinations now reserve
a fresh file with CreateNew, adding a numeric suffix when media or its sidecar
already exists. FFmpeg overwrites only that attempt's empty reservation. Existing
media and sidecars remain untouched. Failed attempts can retain empty or partial
files for recovery; they are not published as the last successful clip. The clip
browser may still list retained media files, which is not changed here.

Revised native Release and Debug each pass 2,492 tests. Revised Release SHA-256:
`031619cb611cddb9a59bbad45d5faf9dcf87a9dc6c6dc882dbdc4d23cd125324`.
Final-package collision and audio-extension native acceptance is pending. QA139
above remains evidence for the earlier package, not this revision.
