# Contributing

## Scope

Akron is an Everest code mod for Celeste. Contributions should stay focused on the current mod surface, docs, tests, live verification tooling, and release packaging needed to support that surface.

Akron does not preserve old local states by default. Prefer one current, canonical implementation with clear errors over compatibility shims unless compatibility support is explicitly requested.

## Local Setup

Build the mod project:

```bash
dotnet build Source/Akron.csproj
```

Run the test project:

```bash
dotnet test tests/akron-tests.csproj --nologo
```

Format changed C# project files before opening a pull request:

```bash
dotnet format Akron.sln --include Source/Core/AkronFeatureRegistry.cs tests/feature-registry-tests.cs
```

Replace the example file paths with the C# files changed by your pull request. Run the full formatter only when intentionally normalizing repository-wide style:

```bash
dotnet format Akron.sln
```

Target the smallest useful test when the change is narrow:

```bash
dotnet test tests/akron-tests.csproj --nologo --filter FeatureRegistryTests
dotnet test tests/akron-tests.csproj --nologo --filter ModuleSettingsTests
dotnet test tests/akron-tests.csproj --nologo --filter ArchiveTests
```

Set `CelestePrefix` if the checkout is not inside the normal Everest `Mods/Akron/Source` layout or if the Celeste references live elsewhere.

GitHub Actions uses a private reference archive instead of committing `lib-stripped/`. Set `AKRON_CELESTE_REFS_URL` to a zip or tar.gz archive containing a complete `lib-stripped` reference directory. Set `AKRON_CELESTE_REFS_TOKEN` only when the archive URL requires bearer-token auth.

CI fails when `AKRON_CELESTE_REFS_URL` is not configured because a green run must prove the mod can build and test. Release packaging also requires that archive and fails fast when it is missing.

## Local Tooling

Canonical verification commands are documented in the docs site:

- [Development setup](docs/contributing/development-setup.mdx)
- [Contribution workflow](docs/contributing/contribution-workflow.mdx)
- [Testing and verification](docs/contributing/testing-and-verification.mdx)

Use live Celeste/Everest verification when behavior depends on rendering, input timing, screen transitions, camera state, hitboxes, overlay layout, capture output, external integrations, or map-specific runtime state.

## Pull Requests

Before submitting, confirm that:

- [ ] The diff is scoped to one goal.
- [ ] Public docs are updated when user-facing behavior changes.
- [ ] Feature policy docs and registry tests are updated when status classification changes.
- [ ] Tests are added or updated when behavior, persistence, archive shape, policy, or setup defaults change.
- [ ] Changed C# project files were formatted with `dotnet format Akron.sln --include <changed-csharp-files>`.
- [ ] New options or features include screenshot or video proof when the behavior is visible, input-driven, timing-sensitive, rendering-sensitive, capture-related, or gameplay-facing.
- [ ] Fixes include after evidence for the corrected behavior when the behavior is visible or runtime-observable.
- [ ] Fixes include before evidence when practical, especially for visual, overlay, capture, or gameplay regressions.
- [ ] Live Celeste/Everest verification evidence is included when unit tests cannot prove the behavior.
- [ ] The pull request does not commit secrets, local tokens, personal config files, local captures, or machine-specific paths.

Screenshots or video should show the smallest surface that proves the change. Use screenshots for static UI, overlay, path, policy, or layout behavior. Use video for animation, input timing, recording, screen transitions, camera movement, hitboxes, or gameplay state changes.

## Feature Pull Requests

Feature PRs should follow the [Feature adding runbook](docs/contributing/feature-adding-runbook.mdx). The runbook covers expected feature shape, tab placement, ordering, tooltip style, implementation surfaces, tests, and live verification.

For feature PRs, confirm that:

- [ ] The feature type is identified: simple action button, runtime toggle, HUD rendering feature, or other with explanation.
- [ ] The feature row has one primary behavior, and independent behaviors are split into separate rows or separate PRs.
- [ ] The row is in the tab users would check first and ordered according to that tab's existing convention.
- [ ] Overlay row behavior, tooltip/search copy, and optional submenu controls are complete.
- [ ] Settings, clamps, setup persistence, and defaults are covered if the feature stores configuration.
- [ ] Command/status output is added when useful for automation or verification.
- [ ] `AkronFeatureRegistry` and policy tests/docs are updated when the feature affects clean/cheat status.
- [ ] Public docs are updated for user-facing behavior.
- [ ] Tests cover settings, persistence, policy, command contracts, or rendering-adjacent logic where applicable.
- [ ] Live Celeste verification evidence is included for visible, runtime, input, rendering, or gameplay behavior.

## AI-Assisted Contributions

AI-assisted contributions are allowed. A pull request may contain code, tests, documentation, screenshots, or other changes that were partially or substantially generated with AI assistance.

AI use must be disclosed. The contributor remains responsible for the full contribution, including all AI-assisted portions. AI-generated work must be reviewed and tested by a human before submission.

### Your Pull Request Must Include

Each field exists for contributor verification and project auditing. Fill in every field completely. Partial disclosures may be returned for revision.

Disclosure fields must be factual. Do not claim human review, testing, verification, or approval unless it actually happened. False testing claims make the disclosure invalid.

- `agent_name`: What AI tool, coding assistant, or agent was used?
  Name the specific tool or service.
- `agent_version`: What version of the tool, extension, CLI, or agent was used?
  Provide the exact version shown by the tool when available.
- `model_used`: What model was used?
  Provide the exact model identifier as exposed by the tool, CLI, or API.
  This is the identifier the provider uses to route requests — not the
  product name, marketing label, or abbreviation.
  Examples of valid identifiers: `openai/gpt-5.5`, `anthropic/claude-sonnet-5`,
  `zhipuai/glm-5.2`, `google/gemini-3-pro`, `deepseek/deepseek-v4`.
  Use the `lab/model-name` convention. If you are unsure of the canonical
  identifier, check https://models.dev or the provider's API documentation.

  These values are NOT valid: `unknown`, `default`, `auto`, `latest`, `GPT-5`,
  `Claude`, `Codex`, blank, or any guessed or abbreviated name.

  If the tool does not expose the underlying model identifier (for example,
  a tool that auto-routes or hides the model), state the exact tool name and
  version, and write `model_not_exposed` in this field with an explanation
  of why the model could not be identified. A PR with `model_not_exposed`
  may be returned for revision unless the tool genuinely provides no way to
  determine the model.
- `human_testing`: What tests, checks, screenshots, live Celeste verification, or manual review did a human perform?
  This must describe real human testing that actually happened. If no human testing was performed, the pull request is not ready.
- `contribution_summary`: One sentence describing what changed.

Before submitting, confirm that:

- You understand the proposed changes.
- A human ran the relevant tests, checks, or live verification.
- The disclosure accurately describes the AI tool and model used.
- No field contains fabricated, guessed, or placeholder information.
- The contribution does not include secrets, private data, copied code, or material that violates licensing requirements.
- Incorrect, unsafe, unnecessary, or unverifiable AI output has been corrected or removed.

Pull requests will not be rejected solely because AI was used. They may be rejected or returned for revision if the disclosure is incomplete, inaccurate, fabricated, unverifiable, or if the contribution appears to have been submitted without real human testing.

## Auth, Data, And Test Safety

- Do not commit `.env` files, tokens, local session state, personal Celeste paths, or private mod archives.
- Prefer unit tests, fixtures, and local fake data over live authenticated or network-dependent tests.
- If a change requires live verification, document the exact map, visible policy/status state, setup state, command, and manual steps in the pull request.
- Keep screenshots and captures focused on evidence. Do not include unrelated local overlays, usernames, or private paths.
- New options or features need visual proof when the result is visible to players. Fixes need after evidence for the corrected behavior; before evidence is preferred when it is practical to capture or reproduce.

## Feature Policy

Akron classifies behavior as `GoldberryHardlistClean`, `RegularClean`, or `Cheat`. Do not introduce new policy words or hidden status categories.

When a change affects policy:

1. Classify the smallest meaningful behavior, not only the parent UI row.
2. Update `Source/Core/AkronFeatureRegistry.cs`.
3. Add or update registry tests.
4. Regenerate or update public feature status docs when needed.
5. Explain what the feature does, not whether it is "for practice" or "for cheating."

## Release Notes

For notable user-facing changes, update the relevant docs page. If a future release-note workflow is added, call out breaking overlay, `.akr`, or file-location changes explicitly.

## Review Policy

This repository may be maintained by a small or solo team. Required status checks should stay green even when formal approving reviews are not enforced.

Contributors should still open pull requests with focused diffs, verification details, and honest disclosure of AI assistance when applicable.
