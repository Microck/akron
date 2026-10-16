# Retired text widgets (#223 / #161)

This implements the maintainer's existing removal decision: the legacy Stamina Widget, Speed Widget and Dash Widget text rows are removed. Stamina Bar, Speed Number, Dash Bar and Dash Number remain. The previous PR185 label-stack change retained the unwanted widgets; it did not implement their removal.

Remove their settings, menu/search entries, renderer branches, registry identities, setup state fields, proof output and stale dialog/documentation rows. Feature IDs 1–3 remain unused; InputViewer stays 4 and every surviving ID is unchanged. Existing attempt status/reason is not cleared.

Compatibility: Everest ignores removed YAML properties. Exactly the old v10 HUD/Whole setup state schema, with all three boolean widget members, is accepted and those members discarded before the unchanged strict current validation. Partial/duplicate/mistyped/unknown fields remain invalid. Archive integrity, timestamps, attachments and transaction checks still apply. Retired label row/action-binding entries are removed on normalization and export; no replacement feature is silently enabled.

Native baseline QA179 shows all three legacy text lines while the status popup falsely says no active cheats. A modern Speed Number control is rendered and correctly listed. The baseline also exports genuine HUD/Whole archives and preserves its enabled legacy YAML profile. Candidate acceptance should start with that original profile, show no legacy rows/text, import the old archives through the actual UI, and verify retained modern controls. Retain the exported baseline archives unchanged. Restore settings and exit normally.
