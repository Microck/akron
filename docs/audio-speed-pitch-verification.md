# Audio speed and pitch verification

## Contract

Playback speed and pitch are independent quantities. At speed `S`, the event's
original playback pitch is multiplied by `S`; pitch-only compensation contributes
`P / S`. Preserve chooses `P = 1`; Follow Speed (or disabled Pitch Shift) chooses
`P = S`; Independent chooses the pitch multiplier. Original event pitch is retained
for restoration rather than assumed to be 1.

The controls span 0.1–4, so compensation spans 0.025–40. Each FMOD pitch-shift stage
is restricted to 0.5–2; at most six stages are needed for one event. The implementation
owns at most three current event handles. Failed cleanup retains ownership and
prevents unbounded replacement allocation.

## Native test prerequisites

Use an owned Celeste/Everest profile and the actual game window. Keep the original
Everest native-library symlinks when extracting or copying a Linux installation:
`libfmod.so -> libfmod.so.10` and `libfmodstudio.so -> libfmodstudio.so.10` in the
runtime used here. Inspect the installed version before applying these names.
Do not replace differing binary versions with an alias.

A restored archive in this run had identical bytes in separate regular files.
Studio's versioned dependency and .NET's unversioned P/Invoke loaded independent
FMOD Core instances. Studio events remained valid, while Core methods returned
`ERR_INVALID_HANDLE`; isolated recording files were empty. Restoring the installer
aliases and relaunching fixed Core queries and actual PCM capture. This was a test
runtime defect, not evidence of valid audio QA before the repair.

For a desktop without an audio device, the bundled FMOD SDL output can use
`SDL_AUDIODRIVER=dummy`, set before process launch. It runs the real mixer and
callbacks while discarding the final speaker output. Validate growing, nonzero PCM,
not merely a successful game launch or an `AudioInitialized` flag.

Use `akron_qa_audio_state death` to read native output status, channel count, DSP
clock, owned event/DSP counts, event timeline, paused state and raw pitch. Raw pitch
measures playback speed and does **not** prove the compensated audible pitch.

## Initial native results (superseded candidate)

The candidate uses the actual Linux Celeste/Everest session, Prologue music and the
recorder's real FMOD full-mix output. No synthetic tone or audio-instance injection
is used. Automatic replay capture starts at level entry; AAC output is fully decoded
to 48 kHz stereo PCM for independent timing and spectral comparison. Ambience remains
in the mix, so measurements target identifiable musical material.

- QA245: speed 0.5, Preserve. Two owned events / two DSPs, no steady-state error,
  both events unpaused, raw speed pitch 0.5. Valid 32.4-second capture and clean exit.
- QA246: both controls off. Zero owned events / zero DSPs, raw pitch 1, valid neutral
  capture and clean exit.
- Fifteen matched attacks fit Preserve time = 2.0015 × neutral time + offset, with
  45 ms RMS residual at 50 ms analysis resolution. Absolute-frequency similarity
  is strongest at unchanged pitch (0.778 versus octave-down 0.235 / octave-up 0.194).
  Matching tones include approximately 330.32/330.69, 496.22/495.48 and 659.18/659.18 Hz.
- QA247: speed 0.5, Independent pitch 1.5. Two events / four DSPs, no error, events
  unpaused. Measured timing ratio 2.000; strongest spectral match is +7 semitones
  (0.725 versus unchanged 0.246). Corresponding tones approximately 495.85/743.41/
  988.40 Hz versus neutral 330.69/495.48/659.18 Hz support the requested 1.5× pitch.
- QA248: speed 0.5, Follow Speed. Two events / zero DSPs, no error, unpaused.
  The trimmed 60-second capture aligns at 2.000× time and −12 semitones (similarity
  0.835 versus unchanged 0.229); tones near 165/248 Hz corroborate the octave drop.
- The moderate compared samples contain no clipped PCM samples. Later analysis
  found a small positive DC offset in Preserve and Independent, so these results
  establish pitch/timing but do not establish final waveform-quality acceptance. This does not certify
  perceptual transparency or physical-speaker playback.

## Extreme-range failure and repair under verification

QA249 (speed 0.1, pitch 4) produced severe positive DC and saturation. In decoded
PCM the channel means were +0.723/+0.704 full scale and 5.36% of samples saturated.
QA250 (speed 4, pitch 0.1) also had positive DC (+0.357/+0.401) and 0.188% saturation.
Both had apparently healthy native handles. Neither is accepted audio output.

FMOD documents a pitch-shifter DC-offset defect and recommends a low-frequency
high-pass after the shifter:
https://qa.fmod.com/t/extreme-dc-offset-in-profiler-waveform-also-volume-issue/21020

The revised candidate uses a 4096-sample pitch analysis window and one final 10 Hz
high-pass after the complete cascade. A final filter avoids transposing the cutoff
of intermediate filters into the audible band. FFTSIZE is a **float** parameter in
this FMOD version; the integer setter returned ERR_UNSUPPORTED in pilot QA251.
The candidate owns at most six pitch stages plus one filter per event (21 DSPs for
three events). No gain reduction or post-filtering of captured audio is used to
hide the failure. Fresh native capture, moderate-policy rechecks and restoration
are required before merge; QA252 is the current upward-extreme attempt.

## Regression discovered live

The installed FMOD wrapper's `HandleBase.GetHashCode()` narrows the native pointer
with `IntPtr.ToInt32()`. A `HashSet<DSP>` therefore threw on 64-bit addresses during
initial attachment and cleanup. Attachment tracking now uses full-width `IntPtr`
keys; a native-wrapper regression covers duplicate wrappers of a high address.

Ordinary tests exhaust all 1,600 UI multiplier pairs and check factor bounds,
neutral policies and invalid settings. Native-reference Release/Debug each pass
2,866 tests with zero skips at the current code revision.
