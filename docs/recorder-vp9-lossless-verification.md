# VP9 Lossless rate control

The explicit **Rate ctrl: Lossless** choice uses libvpx-vp9's `-lossless 1`. Other codecs retain their existing arguments. The decision follows the immutable captured codec in manual capture, replay segments and replay export, even when export settings default to another codec.

This is codec-level losslessness in the encoder's pixel format. RGB-to-YUV conversion/chroma subsampling may still change framebuffer pixels. The separate Quality preset does not select the Rate ctrl mode. Selecting Lossless for export cannot restore information already lost in an earlier lossy replay buffer; start a fresh buffer with Lossless selected.

## Native acceptance
1. Select VP9, MKV, 320x180/30fps, Rate ctrl Lossless and Quality Balanced. Keep audio/remux/replay off for the manual case.
2. Start recording, confirm the recorder warning, close the overlay and move/jump normally. Stop with the configured native key.
3. Decode the actual output and inspect VP9 bitstream headers:
   `ffmpeg -hide_banner -loglevel info -i clip.mkv -map 0:v:0 -c:v copy -bsf:v vp9_superframe_split,trace_headers -an -f null -`
4. Check every coded frame has base_q_idx=0 and all three delta quantizers0 (an uncoded delta is0); inspect segmentation quantizer changes as applicable. Show-existing frames reuse an already coded frame.
5. Start a fresh Lossless replay buffer, capture real gameplay and export. Inspect both finalized segments and export, including its requested duration.
6. Restore x264/CRF, replay0/AutoOff and other original settings; quit normally.

Baseline QA167 selected Lossless but all632 codedframes had nonzero base quantizers37–128, despite a successful21.066-second decode. Candidate acceptance must inspect new actual media, not just argument strings or saved toasts. Hardware codecs are not covered by this VP9 test.

## Automated diagnostics
Run the suite with `AKRON_FFMPEG_TESTS=1` when FFmpeg with libx264/libvpx-vp9 is installed. The opt-in bounded CPU regression uses production arguments and compares decoded deterministic input against direct conversion to the same YUV420P format. It also rejects unused-option/default-quality warnings. Other tests cover captured-vs-settings codec disagreement, both capture callers, all Quality presets, and replay export's captured-spec routing. This supports but does not replace native acceptance.
