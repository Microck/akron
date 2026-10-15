# Golden Transparency verification

## Baseline

QA58 exact Release 3b26388695d222718bd1ff6cd622222ed08db8f1220719e5425adfdd8033ed2e reproduced #282 in native Linux Celeste/Everest. A real neutral jump collected the original City room1 golden berry. The native Golden Start popup changed First room to Unsafe here while Used this session remained No, corroborating attachment without running the helper. Explicit Freeze Gameplay stabilized the follower and camera for matched controls.

On100 matched Off. On55 appeared washed/brighter; at On0 the complete berry sprite remained bright and visible, not merely independent particles. Off restored the original appearance. Reset restored55; the toggle was left Off and gameplay thawed. Native Restart Chapter restored First room with no helper use. Twelve original screenshots and an independent audit are retained. Settings differed only by backup timestamp. Save totals changed4→5 deaths (unassigned incidental),13→14 jumps (real pickup),0dashes. Native exit0.

## Correction

Remove the update-time alpha-only writer. Hook the concrete Image.Render path inherited by Sprite, filtered to Sprite components owned by Golden Strawberry entities. Enabled/allowed opacity below100 temporarily multiplies all premultiplied RGBA channels from the existing Color, calls the original renderer even at0, and restores the exact Color in finally. No persistent baseline cache or cumulative modulation. Off/100 leave the renderer untouched. Symmetric Load/Unload registration follows the existing module lifecycle.

Native/custom update-time tint and ghostberry opacity are preserved. Separate particles, light/bloom and non-Sprite renderers remain outside the previous sprite-only scope. A custom renderer that never calls Image.Render is not covered; an inner render hook that persistently writes Color may conflict with scoped restoration.

## Candidate acceptance

QA59 completed native acceptance on exact Release744b8bf2d2b8ceeb3f2e4630b835de92f94dea2578c61feab7c3e49db740d04f. The initial Release and Debug suites passed 2,308 tests with full runtime references, including six color-behavior cases. GitHub CI uses intentionally stripped FNA method bodies, so those six cases failed there with InvalidProgramException despite successful production compilation. The test-only follow-up replaces them with one emitted-IL check for clamp, division by 100, and native premultiplied Color multiplication. The final Release and Debug suites pass 2,303 tests locally. Structural checks also cover hook registration, sprite/entity filters, finally restoration, and removal of the old update writer. Structural checks do not execute rendering or establish ghostberry behavior in-game; they supplement the native acceptance. Production source and the live-tested Release artifact are unchanged by this test-only follow-up.


Twenty-one original screenshots document the genuine attached follower, frozen Off/100/55/0 controls, repeated55→Off restoration, and an uncarried berry control after native Restart Chapter. At55 the sprite visibly fades toward the background; at0 its body and crown disappear while separate light/particles remain. Off and100 restore the original appearance. The selected fixed berry-region decoded pixels match exactly between the original Off reference, On100, Off-at0, Off-at55 and repeated-Off frames; both55 frames also match. This is a matched visual control, not a direct calibrated opacity measurement.

Native restart restored Golden Start status First room / Used No. The uncarried berry also hid at0 and returned when Off. Reset returned55, the toggle was left Off, and explicit gameplay freeze was released. Settings differ only in backup timestamp; save counters match the baseline changes (one real pickup jump and one unassigned incidental death). The suite does not certify third-party render hooks, all custom sprites, ghostberry behavior in-game, exception injection, or policy-denied rendering; pure/structural tests and source review cover only their stated scope.

Native Save and Quit followed by Exit0; launch/exit receipts and window absence retained.
