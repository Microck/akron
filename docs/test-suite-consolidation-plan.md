# Test-suite consolidation plan

Status: final planning recommendation, September 5, 2026. Implementation has not started.

Remove five tests directly. Correct the known-wrong restore contract, strengthen the specific weak fixtures below, and replace selected source checks only when the replacement observes the same behavior. Keep the remaining tests. There is no target percentage reduction and no claimed CI speedup.

## Evidence and scope

- [Discussion #189](https://github.com/Microck/akron/discussions/189): broad review and removal proposal.
- [Discussion #190](https://github.com/Microck/akron/discussions/190): per-method audit, corrections, and replacement recommendations.
- Both discussion bodies and all 28 catalog comments were retrieved. The catalog contains 1,173 unique method records. Recommendations were reconciled across those records; disputed decisions were checked against current method bodies and relevant production code. This consolidation is not a second independent audit of every assertion.
- Both audits inventory `826bbf509a82`, which is also this documentation PR's base. The initial planning review used local revision `7ebde9ee6827`, which adds the snapshot bundle and updates its integration tests. That unpublished work and the pre-existing local audit are excluded from this PR.
- At local revision `7ebde9ee6827`, a fresh Release build/test run passed **1,898 cases, 1,180 methods, zero failures, zero skips**, using .NET SDK `8.0.418`. The runner reported 21 seconds. TRX timestamps span 13:42:43 to 13:43:10 UTC. This is one run, not a benchmark.
- On this PR's base, a separate Release run passed **1,888 cases, 1,173 methods, zero failures, zero skips** with the same SDK. The runner reported 19 seconds. The four failures in the audits did not reproduce on either revision.
- The local snapshot work uses portable attachment `startpos/snapshots.bin.br`; local reconstruction snapshots remain gzip. Its `docs/reference/snapshot-bundle.md` contract and `tests/snapshot-bundle-tests.cs` are absent from this PR's base. If that work lands before implementation, preserve its seven additional methods and ten cases: they check byte identity, independent decoding input, bounds, incomplete consumption, cancellation, and failed writes. Use the compiled format and contract on the actual implementation base, rather than copying either audit's description of unfinished per-slot Brotli work.

Initial local baseline command:

```bash
dotnet test tests/akron-tests.csproj --configuration Release --no-restore --nologo \
  --logger 'trx;LogFileName=consolidation-baseline.trx' \
  --results-directory /tmp/akron-consolidation-baseline
```

PR-base verification commands:

```bash
dotnet restore Akron.sln --locked-mode --nologo
dotnet test tests/akron-tests.csproj --configuration Release --no-restore --nologo \
  --logger 'trx;LogFileName=baseline.trx' \
  --results-directory /tmp/akron-pr-baseline
```

Neither run started Celeste, contacted the remote test machine, exercised native Windows sharing, rendered a browser, or encoded media.

## Decision rule

An addition must name a failure the existing assertions do not detect. A removal must identify either the surviving assertions that cover the same inputs and outcome, or the precise guard being deliberately retired. A replacement must reach the relevant caller, state transition, or output; calling a helper elsewhere is insufficient.

For each replacement, temporarily remove or alter the protected operation in an isolated local copy and confirm the replacement fails for the intended reason. This is a targeted sensitivity check, not a new permanent mutation-testing system. A green test alone does not prove equivalence. Keep mixed tests' behavioral assertions when replacing their source portion.

Use real implementations, temporary files, in-memory streams, and explicit synchronization. Keep tests in their existing owner files. Do not introduce a generic test framework, a second implementation, production settings for test control, or additional validation in inner loops. A narrow internal observation point is justified only when the specified transition cannot otherwise be observed.

## Ordered changes

### 1. Establish the current baseline

Already completed for this plan. At implementation time, check `jj status` and record the code revision again. Rerun the baseline only if code or dependencies changed.

Preserve the format-name, per-snapshot-size, aggregate-size, and changelog-contract tests that failed during the audits. There is no remaining repair to schedule based on those historical failures. Do not revert their fixtures to the old attachment layout.

**Reason:** mixing old working-copy failures with the current suite would create unnecessary changes and could restore an obsolete wire contract.

### 2. Correct the restore ownership contract before cleanup

Files: `docs/feature-guide/startpos.mdx`, `tests/startpos-reconstruction-tests.cs`, the relevant reconstruction code in `Source/SaveLoad/akron-reconstruction-graph.cs`, and `CHANGELOG.md`.

1. Clarify the existing exact-state contract: successful restoration must leave entity membership, component membership, and owner references consistent. A displaced fresh entity must not retain ownership claims that contradict the restored graph.
2. Preserve the fixture in `AnUnpairedGhostStillTakesItsSceneEdgeOnStructuralBudgetAloneAndRestoresWrongly`. Replace its assertions that demand inconsistent ownership with assertions for the intended graph and rename it accordingly.
3. Fix the general ownership/reconciliation behavior. The saved population must restore coherently when the map still places the saved entity but the fresh session did not instantiate it. Do not special-case `PlayerPlayback`, discard the fixture, or make every unpaired entity fail to avoid the problem.
4. Preserve `AnUnpairedGhostIsRefusedWhenTheMapNoLongerPlacesIt` and the valid missing-fresh-entity reconstruction cases. An actual map change and a different fresh runtime population remain different conditions.
5. Add a short changelog entry describing the ownership fix once implemented.

**Objective justification:** the current test explicitly asserts successful restoration alongside a component listed by one entity but owned by another, and a displaced entity that still points to the level. Its own comments identify this as wrong. The existing exact-state contract requires coherent references, so these passing assertions contradict the product contract.

**Acceptance:** the corrected fixture fails on the current implementation and passes with the fix; valid reconstruction and map-change refusal cases still pass. Verify same-process and restarted-game restoration on the remote Linux Mint test machine before calling the gameplay behavior verified. If the general fix cannot preserve valid reconstruction, report that blocker rather than broadening refusals.

### 3. Delete only these five tests directly

These are five method deletions, not permission to remove neighboring tests or fixtures.

| Remove | Objective reason | Coverage retained or deliberately lost |
|---|---|---|
| `LogTests.DiagnosticSuppressesVerboseAndTrace` | Its four `ShouldWrite` assertions are identical to `ModuleSettingsTests.DiagnosticLevelSuppressesVerboseAndTraceLines`. | All four input/output pairs remain in the latter test. |
| `LogTests.VerboseSuppressesTrace` | Its three assertions are a subset of `VerboseLevelWritesDiagnosticAndVerboseButNotTraceLines`. | All three pairs remain; the surviving test also checks Normal. |
| `LogTests.DiagnosticAggregatesPolicyChecksAndFeatureUses` | Its four decision pairs occur in `PolicyChecksAggregateUntilTrace` and `FeatureUsesAggregateAtDiagnosticAndEmitFromVerbose`. | Decision coverage remains. Keep accumulator, expiry, and output tests separately. Preserve the useful explanation of the aggregation boundary beside the surviving theories. |
| `FeatureRegistryTests.TheLabelClassificationTableIsGone` | It only requires the old public method name `TryClassifyUiLabel` to remain absent. That name is not the current classification contract. | Deliberately retire the historical API-name ban. Keep `RowsClassifyThroughTheFeatureKindThatRecordsThem` and the independent feature-classification table. These do not prove the old method stays absent. |
| `StartPosReconstructionTests.RegisteredActionIdentityIsStableAcrossProcesses` | It passes the literal `core-runtime-0` to a constructor and reads `Id`; it performs no derivation, serialization, or second-process work. | Deliberately retire this literal constructor pass-through check. Keep registration-order and stable-identity guards. This does not establish genuine cross-process registration-ID coverage, and no such coverage is claimed. |

**Acceptance:** inspect the before/after assertions and run `LogTests`, `ModuleSettingsTests`, `FeatureRegistryTests`, and `StartPosReconstructionTests`. The method inventory falls by exactly five in this step. Do not add a test that asserts these names disappeared.

### 4. Repair misleading names and incomplete fixtures

| Existing test | Exact change | Missing evidence and acceptance |
|---|---|---|
| `HitboxLineThicknessDefaultsToOneNativePixel` | Rename to `HitboxLineThicknessDefaultsToFive`; retain the constructor assertion of `5f`. | Its name disagrees with its assertion. Resetting a modified instance does not test construction. No behavior change. |
| `ShowTrajectoryLineThicknessDefaultsToFive` | Keep unchanged, including both settings and setup-state constructors. | The clamp theory does not cover either property initializer. No removal is justified. |
| `JsonStringsPassNonAsciiThrough` | Use actual accented text, non-Latin text, and a supplementary Unicode character. Parse the emitted JSON string and compare the decoded value; retain the direct pass-through assertion the name promises. | The current `Glyph/1-Forsaken` input is entirely ASCII. The replacement must fail if Unicode is lost or emitted as invalid JSON. Keep the separate escaping cases. |
| `FilterSearchesAuthorDescriptionAndTags` | Add independent queries matching only author, only description, and only tags, plus a nonmatch. Keep map/category filters intact. | The current query can pass without searching description. Removing any one searchable field must fail its corresponding case. |
| `EveryQueuedSlotIsWarmedInsteadOfStoppingAtTheBudget` | Rename to describe warming every queued slot **within** the budget. Preserve its five-slot progress and consume-once assertions. | Five small fixtures establish queue progress, not permission to exceed the memory budget. |
| `DeepCloneReadonlyFieldSetterDoesNotThrowWhenRuntimeRejectsWrite` | Rename to state that the helper completes on the tested runtime. Keep the no-throw fixture and document that the rejected-write branch remains unproven. | `ForceSetField` can return before attempting a write if runtime reflection metadata is unavailable. Neither success nor absence of an exception proves that rejection occurred. Do not manufacture a rejection by rewriting vendored code for this cleanup. |

The readonly rejected-write recommendation is deferred until a real supported runtime/fixture reliably reaches that branch. At that point, the required assertions are preservation of the original value and restoration of field attributes after rejection. This is a stated coverage gap, not completed replacement coverage.

### 5. Replace serialization and logging source checks with output checks

Files: `tests/value-object-tests.cs`, `tests/performance-tests.cs`, `tests/log-tests.cs`, `tests/module-settings-tests.cs`; production changes only in the corresponding existing owners if a narrow seam is necessary.

| Replace | Add or strengthen | Why this addition earns its cost; when removal is allowed |
|---|---|---|
| `ProofSidecarBlocksEndWithoutATrailingComma` | Invoke the real `AkronProof.BuildSummaryJson` with real settings/session fixtures. Parse the complete result, check required top-level and nested fields, and cover `MapVersionStamp` on/off and empty/nonempty feature lists. | The source loop can pass without recognizing any block. Parsing detects malformed output; required-field assertions prevent `{}` from passing. Retire the source scan after both shapes execute the production serializer. |
| `RecordingWindowStartsCleanAndFlushesBeforeTheWriterIsDetached` | Exercise two consecutive telemetry recordings using real files or the existing stream boundary. Record a partial window, stop, parse all JSONL records, and assert the partial sample appears exactly once. Assert the second recording starts with fresh counts and its own label. | The existing disposed-writer test covers failure containment, not normal flush or reset. Retire the source-order check only after both flush and restart isolation are observed. Keep collision and writer-failure tests. |
| `RecordPolicyCheckAggregatesAllowedChecksAndEmitsDenialsAtDiagnostic` | Call `AkronLog.RecordPolicyCheck` with Diagnostic enabled. Observe an allowed check entering the accumulator without an individual output line, and a denied check producing the correct output without being counted as allowed. | Private accumulator tests never execute the denial-output branch. Retire the source check after both public-path outcomes are observed. Restore static state in cleanup. |
| Source portions of `LoggingLevelChoicePersistsImmediately` and `LogLevelCommandDrivesTheSameApplierAsTheOverlayChoice` | Exercise the actual command handler and the action used by the overlay choice. Read persisted settings and confirm the selected level survives. Preserve existing command-generation assertions. | Calling `ApplyLoggingLevel` directly proves the applier, not that each entry point uses it. Remove only the assertions whose entry-point behavior the replacement actually exercises. Retain any unexercised overlay wiring guard. |

Do not add a parallel serializer or logging implementation for tests. Use the existing shared-state collection and restore settings, writers, and counters in `finally` blocks. The serialization tests should also use decimal values under a non-default culture, because valid JSON numbers are part of the same output contract.

### 6. Add the missing admission-boundary cases

Files: `tests/module-settings-tests.cs`, `tests/community-pack-tests.cs`; relevant owners are `Source/Automation/akron-automation-service.cs` and `Source/Community/akron-community-packs.cs`.

1. Extend `AutomationCommandFilesRequireOptInTokenCapsAndAllowlistedCommands` with independently valid fixtures at the limit and just above each existing cap: UTF-8 byte size, total lines, command length, and commands per run. Include multibyte input so byte count cannot silently become character count. Keep opt-in, token, and allowlist cases. Exercise the real capped file reader as well as the parser for the byte limit; parser-only checks do not prove bounded file reading.
2. Strengthen `RefreshRejectsUnsafeCatalogSources` to observe the production validation decision before I/O. Retain its disallowed-source cases and add an allowed-source control. A controlled transport boundary may record whether it was invoked, but tests must not contact the listed external destinations. Testing a newly written validator that the refresh path never calls is insufficient.

**Objective justification:** the automation test currently has no oversized input, despite naming caps. The catalog test accepts a generic `Connection failed:` result that could also arise after a network attempt. Both need observations at the existing admission boundary, without changing the policy or adding redundant downstream validation.

**Acceptance:** each over-limit case reaches its intended boundary rather than failing earlier for an unrelated malformed fixture. The at-limit control remains accepted. Rejected catalog sources produce zero transport calls. Preserve all archive, checksum, accepted-input, and preview dimension tests.

### 7. Make worker, cancellation, and pacing observations deterministic

Files: `tests/community-pack-tests.cs`, `tests/startpos-hotpath-cache-tests.cs`, `tests/startpos-persistence-tests.cs`; only the corresponding existing worker/pacing owners as necessary.

| Existing test or group | Exact replacement observation | Objective reason |
|---|---|---|
| `BeginRefreshLoadsFileIndexWithoutBlockingCaller` | Hold the worker at its read boundary, observe that `BeginRefresh` returned while the worker remains held, then release it and retain completion/search assertions. | Waiting for eventual completion does not establish nonblocking initiation. A generous timeout only prevents a hung test; it is not the performance oracle. |
| `ThePrewarmWorkerMakesNoProgressWhileThePlayerIsInControl` | Signal that the worker reached the closed gameplay gate; inspect progress while held, then release it and observe completion. | A sleep followed by zero cached entries can pass because the worker never ran. |
| `ThePrewarmWorkerReadsItsQueueAndStopsWhenCancelled` | Establish the first generation reached work, cancel it, enqueue the new generation, and assert only eligible new-generation work publishes. | Queue recovery and stale-generation rejection are separate observable outcomes. |
| `AParkedPrewarmLetsGoOfItsReadWhenTheQueueIsReplaced` | Establish a held read, replace/cancel the queue while gameplay remains gated, and observe that the read is released before reopening the gameplay gate. Retain later worker recovery. | The existing recovery path can pass even if the old handle remains held until the gate opens. Windows handle semantics require a Windows check; Linux file deletion alone is not evidence of closure. |
| `ACancelledPrewarmStoresNothing` | Keep immediate cancellation and cold-load availability. Add deterministic cancellation after a successful read but before cache publication. | Immediate cancellation and worker restart do not cover cancellation at final publication. Replace only the source portion once this second case observes zero stored documents and bytes. |
| `PacingRedistributesASnapshotsCostWithoutManufacturingAnyMore` | Retain the allocation comparison; replace sleep-based sequencing and elapsed lower bounds with observed gate arrival/wait/release. | Allocation growth and whether work actually waited are different properties. |
| `PacingIsInertOnAnyThreadThatIsNotRunningAPacedJob` | Observe zero wait-path entries when no paced job is active, including a separate thread while another thread owns a paced job. Replace its one-second assertion. | The contract is thread-local nonblocking behavior. Merely returning the correct value does not protect it. |

Add synchronization at existing I/O or wait boundaries, with cleanup that releases gates even after assertions fail. Do not introduce per-iteration validation or instrumentation into every graph node visit. If observing a specific boundary would require a broad production redesign, keep that existing guard and record the unmet replacement condition instead.

### 8. Replace the selected cache guards with mutation and publication tests

Files: `tests/startpos-hotpath-cache-tests.cs`, existing setup/persistence tests where they already invoke the relevant operations, and the owning StartPos code only when necessary.

| Guard selected for replacement | Required cases before its removal | Distinct failure protected |
|---|---|---|
| `EveryRuntimeSlotMutationAdvancesTheRuntimeStateRevision` | Exercise clear-all, slot publication, canonical removals, and park/restore/release of the previous slot state. For each actual operation, observe a changed revision and refreshed consumer result. | A stale list after a real slot mutation. Calling `MarkRuntimeSlotsChanged` directly tests only the counter. |
| `EveryInPlaceStartPosCatalogMutationMarksTheCatalogChanged` | Publish/replace and remove entries through their actual operations; observe the next catalog lookup. | In-place changes do not change the collection reference and can leave cached views stale. |
| `GetStartPositionsChecksEveryCacheKeyComponentBeforeReusingItsList` | Independently vary session identity, map SID, catalog revision, runtime revision, and snapshot revision. Keep an unchanged-input reuse case and verify the returned collection cannot be mutated. | Each omitted key can reuse the wrong list; read-only ownership is an additional assertion that must not be dropped. |
| `WarmAndPendingSlotsAreNeverQueuedForPrewarm` | Observe queue selection for the loaded slot, a usable warm slot, a stale-session warm slot, pending persistence, a missing snapshot, and a cold eligible slot. | Presence of warm memory alone does not prove it is usable in the current session. |
| `ChangingMapOrSaveFileReleasesEveryPrewarmedDocument` | Change map, save slot, and profile incarnation during blocked work; prove stale work cannot publish and obsolete cached documents/bytes are released. | Cache clearing without invalidating in-flight work permits stale publication. |
| `APrewarmReadThatRacesAWriteIsRejectedByTheWriteRevision` | Strengthen `ASnapshotRewrittenDuringItsPrewarmReadIsNotCached` so the read is known to succeed, a real write advances the revision, and publication rejects the stale document. Verify an unchanged-file control stores successfully. | Its current callback rewrite can also produce zero entries because reading failed. Zero cache size alone does not identify revision enforcement as the cause. |
| `EveryPrewarmedDocumentIsDroppedWhenItsPathIsInvalidated` | Keep the existing write/delete cases; add staged install, successful import, failed import/rollback, and restored-previous-file cases at their owning operations. Check the next read's content as well as cache accounting. | Write/delete tests do not cover all mutation callers or prove rollback restores the correct document. |
| Source portion of `PrewarmingIsBoundedByAFiniteMemoryBudget` | Keep the finite configured-budget checks and existing exact-fit/one-byte-short test. Add a successful blocked read whose remaining budget is consumed before publication; prove final admission respects the cap. | Admission before reading does not establish admission at publication. Existing estimated RSS arithmetic is historical sizing evidence, not measured process memory. |

Use the existing `HoldPrewarmedSnapshotBytesForTests` facility for bounded fixtures. Do not allocate gigabytes to test the cap or add a configurable production budget for tests. Keep `AFullPrewarmCacheReportsBudgetExhaustionRatherThanAFailedRead`, which already distinguishes exact fit from one byte too little, and the separate oversized-cold-read refusal.

Retain `ASavestateLoadRebuildsTheSessionStartPosCatalog`, `TheFirstColdStartPosLoadBuildsEveryRuntimeSlotBeforeReturning`, and `PrewarmReadsAreStoppedWhileThePlayerIsInControl` until their actual broker/load/gameplay callers are exercised. Generic cache tests cannot replace those integration checks.

### 9. Improve performance and IL checks without dropping their contracts

Files: `tests/engine-gc-tests.cs`, `tests/performance-tests.cs`.

1. Keep debt-state assertions in `DeferringADeathCollectionNeverCollects`. Add direct observation of induced collection events in an isolated process running the real deferral path, using a deliberate collection as an observation control. Distinguish induced collections from incidental runtime collections. Remove the five-second assertion only after the observation detects an accidental direct collection. The `PaidCollections` counter alone does not observe every possible `GC.Collect` call.
2. Replace the source predicate in `RetainedFullCollectionsReconcileDebtOnlyAfterTheyRun` with executable IL fixtures for both `call` and `callvirt`. Retain assertions that reconciliation follows the actual collection. The existing IL fixture is the right owner; no new IL-testing framework is needed.
3. Replace the absolute 400 ms oracle in `FeatureClassificationStaysConstantTimeForAllFeatures` with a warmed production/reference comparison using the existing measurement helper and median sampling. Use the same feature inputs and consume both outputs. Rename it to describe measured classification cost. Record baseline ratios and select a threshold that separates the current lookup from a deliberately linear test reference before retiring the old alarm. Do not claim asymptotic complexity from a fixed workload or invent an unmeasured threshold.
4. Keep `ActiveCheatContributorScanStaysCheapWithManyEnabledOptions`, the allocation-growth reconstruction test, and the real cross-process hash test. Their distinct measurements are not supplied by correctness tables.

**Objective justification:** wall time mixes implementation cost with host scheduling, but deleting the alarm without a replacement loses the only relevant performance observation. A same-process reference reduces shared host effects; it does not eliminate all noise. If the new measurement cannot distinguish the intended regression reliably, retain the current alarm and document the limitation.

### 10. Limit UI consolidation to behavior the available model exposes

Files: `tests/overlay-tests.cs` and `tests/module-settings-tests.cs`. No UI redesign is part of this plan.

The named targets are `EntityInspectorRowUsesSettingForActiveColorAndPopupBindings`, `OptionsPopupsBindRegisteredSuboptionsFromTheirControls`, `CursorFeaturePopupsExposeCustomCursorBindings`, and `StartPosPopupBindingsUseNativeButtonBindingFields`.

For each, move only row availability, action identity, toggle state, popup routing, and binding/default-value assertions that can actually execute through the existing entry/action model. Extend an existing behavioral test when it owns the same case. Remove a corresponding source assertion only after the new assertion fails when that row, route, or binding is broken.

**Objective justification:** these are observable model contracts that can survive source rearrangement. However, `BuildDisplayEntriesForTab` builds rows and the `HasOverlayOptionsPopup` helpers report popup availability. They do not execute every popup control, render layer, keyboard hook, or input interaction. Therefore a row test cannot justify deleting an entire large source test merely because it contains some row assertions.

Retain these tests and remaining assertions in this consolidation:

- `InspectorPinPopupUsesAkronImGuiPanelTheme` and `UploadPackWindowUsesCompactAlignedSingleColumnForm`: theme/layout checks need rendered evidence, not row presence.
- `AkronDotBindingsSuspendEverestDebugConsoleBinding`: helper truth tables do not prove the Everest hook is installed or released.
- `AkronOverlayStaysOnFinalRenderPassWhileWorldDebugGeometryUsesSplitPasses` and `SpeedrunToolStateTransitionsSuppressAkronRenderSurfacesBriefly`: render order and lifecycle integration require the actual runtime paths.
- `BrowserProvidesLargePreviewAndDiscordActions` and `BrowserRendersCatalogAuthorAvatarInsteadOfPrintingItsUrl`: weak source alarms, but no equivalent action/render observation has been established. Their removal is not included in the five direct deletions.
- `RemovedRowsAreNotShownInOverlay`: it evaluates the built entry lists, unlike the removed API-name test. No complete surviving inventory was shown to subsume its three negative assertions.
- `RenderPassHasNoSettingsSurface`: its substring bans are a poor proxy, but retiring them would also retire a settings-surface restriction without an established replacement. Keep it in this cleanup; current render-output tests do not prove absence of controls.

This deliberately rejects #189's blanket Part 3B deletions across overlay, scanner, module settings, persistence, and hotpath tests. The specific replacements in steps 5, 7, 8, and this step are the only exceptions. A function compiling, a helper passing, or an old screenshot existing does not establish that its current caller or render path is correct.

### 11. Separate the release contract check without duplicating it

Files: `tests/startpos-persistence-tests.cs`, `Makefile`, `.github/workflows/ci.yml`, and the existing testing documentation.

1. Keep `TheNewestChangelogContractMentionNamesTheContractsThisBuildActuallyWrites` and its comparisons against compiled format constants. Give it a `ReleaseContract` category.
2. Add a `release-contract-check` Make target that runs that category. Include it in `preflight-release` before packaging.
3. Run the ordinary test target with the release category excluded, and add an explicit release-contract check to CI using the same built test assembly. Existing release workflows that run all tests continue to include it; inspect any filtered release test command before changing it.
4. Check a current document passes and a deliberately stale format token fails the release target. Confirm the category selects the intended test and CI cannot skip it accidentally.

**Objective justification:** release-document consistency belongs in a named preflight check, while comparing the real compiled constants avoids a second script with its own format literals. This is a relocation of enforcement, not removal of coverage. Adding a standalone parser or making the check optional is unnecessary.

### 12. Document the rule and verify the final change set

Update `docs/contributing/testing-and-verification.mdx` with the decision rule: prefer assertions on state, output, and real operations; retain a source guard when it protects an otherwise unobserved contract; replace it only with named equivalent coverage. Do not adopt #189's absolute rule that every new test must call code. Release artifacts, resource inventories, and narrowly justified structural checks remain legitimate test subjects.

Add concise explanations only beside retained structural guards whose current comments do not already state their purpose and limitation. Prioritize persistence commit/rollback ordering, shutdown draining, GC load ordering, Windows held-handle behavior, and runtime caller wiring. Do not churn all 229 catalog entries marked `Replace after coverage` just to add uniform comments.

For each implementation slice, record the changed methods/assertions, the failure each addition detects, the surviving coverage for each removal, and the targeted verification. Keep the source audits as historical evidence; this document supplies the consolidated decisions rather than rewriting those audits retroactively.

Final verification:

1. Run the relevant test classes after each logical slice. For each replacement, verify sensitivity to the specific missing operation before deleting the source assertion.
2. Run the full Release suite and the separate release-contract check after all settled changes. Reconcile method/case deltas against the five deletions, renames, replacement theories, and added cases. A lower count is not itself success.
3. Preserve CI's package existence, PDB exclusion, and exact license/notice checks. No package-manager or dependency changes are needed.
4. Apply `ce-simplify-code` to settled implementation changes before final review. Do not apply it to this planning-only document.
5. Verify affected restore/input/render behavior on the remote Linux Mint test machine. A native Windows held-read/write/rotation check is required before replacing the corresponding sharing guards. If that host is unavailable, leave those guards intact and report the missing verification.
6. Identify any helpers, imports, constants, or fixtures made unused by the actual diff. List them and obtain removal approval under repository rules; do not include speculative dead-code cleanup. `GetPerformanceTelemetrySourcePath` is a likely candidate only after its sole source-based test is replaced. Other source-reading helpers still have callers.

## What stays outside this change set

| Proposal or area | Final disposition and objective reason |
|---|---|
| Delete roughly 120 source-driven tests to reduce churn | Rejected. Neither audit supplies equivalent observable coverage for that whole set. Deletion would trade away unique caller, ordering, or lifecycle alarms. |
| Rewrite every source check immediately | Rejected. Most entries offer only a general aspiration, not a reachable replacement boundary. The finite work above targets proven gaps and existing ownership boundaries. |
| Remove `DeloadSimulationDoesNotMutatePlayerTimeStats` because a one-shot claim test exists | Keep. Claim state and cumulative game clocks are different outputs. |
| Remove `GameplayCommandsDoNotDereferenceSessionStateDirectly` because code compiles | Keep. Compilation does not establish safe behavior without loaded session state. |
| Remove `NativeStartPosRuntimeRestoreSuppressesLagPauserSpike` because the grace helper is tested | Keep. The helper test does not prove runtime restore invokes it. |
| Trim scanner/source halves because collage screenshots exist | Keep. Old images do not prove current reservation cleanup, partial-map selection, marker exclusion, output completion, or cancellation wiring. |
| Drop the readonly test as meaningless or force its rejection branch through a mock | Keep and clarify its claim. Runtime completion is still an observed property; a mocked reflection failure would not verify runtime behavior. |
| Add a new cross-process registration-ID harness just to redeem a misleading test name | Not included. Remove the trivial test and state the gap. Add such coverage with identity-generation work when independent derivation inputs and a real failure are established. The existing cross-process hash test stays. |
| Drop Windows/source ordering guards after Linux headless tests | Rejected. Linux success is not evidence of native Windows sharing behavior. |
| Remove source-reading utilities and old constants opportunistically | Not included. Only actual newly unused elements are candidates, subject to repository approval rules. |
| Change wire formats, dependencies, UI layout, policy tables, or unrelated production code | Not included. The audits do not establish a need for those changes. The ownership fix in step 2 is the specific behavioral correction in scope. |

## Completion criteria

The implementation is complete when the five direct deletions have their surviving assertions verified, the wrong restore contract is corrected, the accepted fixture improvements pass, and every removed source assertion has the corresponding acceptance evidence above. A replacement whose boundary cannot be observed remains an explicit retained guard, not an unexplained deletion or a claimed coverage improvement. Record any such unmet replacement condition individually.

All retained data-loss, reconstruction, cross-process hashing, import, policy, and package checks must continue to pass. Runtime-dependent fixes need the stated live verification. Report the final additions/removals, tests run, and remaining coverage limits without claiming a measured speedup or blanket equivalence that was not established.
