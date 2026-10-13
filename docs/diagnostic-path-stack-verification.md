# Diagnostic-path stack depth (#283)

Unmodified main `2cee797dd3d7f07abed74bd09fd6a16ebf4e4ec2` aborts the
existing `DeepGraphPersistencePreservesValuesAndCyclesOnASmallWorkerStack`
test in Debug on Debian 13 / .NET 8.0.24. Its depth-1024 graph runs on a
requested 256 KiB stack; restore recursively materializes parent paths.
The same baseline test passes in Release. No live-game crash is established.

The proposed fix builds the uncached parent chain in a temporary stack, then
materializes it from ancestor to descendant. Existing path construction and
length validation remain in use. The existing small-stack test is retained
unchanged; the worker stack is not enlarged and the test is not excluded.

The candidate passes the unchanged targeted Debug test and all 2045 tests in
each of Debug and Release, with no exclusions or skips. These checks were
repeated after formatting the changed property. This qualifies the reported
automated Debug regression only. Native StartPos capture/restore
and real deep-map behavior require their own evidence before claiming gameplay
acceptance. No human review or human testing has been claimed.

## Ordinary native compatibility control, 2026-10-12

A separate AI-operated comparison used the supplied Celeste 1.4.0.0-fna,
Everest 6580-azure-082e2-stable and .NET 8.0.24. On both unchanged main and
the #283-only candidate, one owned slot capture published a valid v12 gzip;
after visible movement away, one Load restored the initial visible position,
and fresh Right input moved the player again. Both processes exited normally.

The twelve starting save/settings files were byte-identical. The canonical
snapshot root was initially absent; each run created its own slot1 artifact,
and the baseline artifacts were preserved before preparing a fresh candidate
root. Final settings differed only by automatic backup timestamp. Total deaths
increased from 4 to 5 and Normal-mode deaths from 3 to 4 in both runs, with no
attribution or preservation claim for those counters.

- Candidate DLL SHA-256:
  `dcea3dc8e367544376e9cc87527723fc442bf3f111f8d05fa5b389552acd81ed`.
- Candidate package SHA-256:
  `e400170889a438523f6c619ad4932fc661ed966121aaf5b6a208fcfaa916dd3b`.
- Evidence: `Akron-283-Native-StartPos-Compatibility-20261012.zip`, SHA-256
  `f1406ec4c44ca7493a07dab3b490f9bea222ab05bf24e971ecdd4c01844b54b3`.

This is ordinary capture/publication/warm-load compatibility. It neither
reproduces the Debug small-stack failure nor proves full state identity,
clock/stat preservation, cold reload or real deep-map behavior. The separate
automated baseline/candidate comparison establishes the reported test fix.
