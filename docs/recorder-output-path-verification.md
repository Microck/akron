# Recorder output-path verification

Fixes #239. Output folders are resolved to absolute paths in the game process
before constructing encoder arguments and selecting FFmpeg's working directory.
Relative custom folders keep their original current-working-directory basis;
default output remains under Everest.PathGame/Saves/AkronRecordings. Absolute
custom folders remain absolute. The common resolver also feeds replay/completion
paths, although those flows are not part of this native acceptance.

## Native baseline

QA135 used CPU/libx264, 640x360, 60 FPS, MKV, with audio, replay, and auto-remux Off.
A fresh relative folder produced only a 185-byte .akrclip sidecar and no MKV. The
running UI nevertheless showed Recording saved. Changing only the folder to Use
default produced a 3,370,127-byte H.264 MKV, 18.217 seconds and 1,093 frames, with
full decode exit 0. The two durations are not a performance comparison: the
relative attempt stopped immediately after failure.

The false success/sidecar publication is already tracked separately as #200;
this path-resolution fix does not claim to fix general encoder-failure reporting.

## Automated coverage

Both native Release and Debug test runs pass all 2,476 tests. New tests cover
relative paths, spaces, dot segments, absolute paths, blank/default configuration,
a game directory different from process CWD, and consistent resolution of the
output argument from the encoder's working directory.

Release package SHA-256:
`aa8dcb310bdd8d99e7b9d07d97698b2fdfaea829ced09cef3e67fe007068793b`.

## Scope

Media existence, full decode, and representative video frames are required for
native acceptance; a toast, filename, or sidecar alone is not success evidence.
Dropped-frame warnings are retained; no frame-perfect capture, audio, replay,
remux, completion-video or all-platform gameplay acceptance is claimed. Raw
recordings, screenshots, profiles and audit receipts remain private.

## Candidate native results

QA136 used the same initial isolated profile. A fresh relative folder containing
a space produced a real 4,483,040-byte H.264 MKV in the requested folder: 24.350
seconds, 1,461 frames, full decode exit 0. No repeated nested folder was required.
Switching to Use default produced a 4,510,435-byte MKV: 24.567 seconds, 1,474 frames,
full decode exit 0. Both were started through the native recorder UI and stopped
with its native F5 binding. Folder selection and auto-remux were changed through
the running GUI; no runtime state injection was used. Default output and remux Off
were retained at cleanup.
