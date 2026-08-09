## Summary

-

## Verification

- [ ] The diff is scoped to one goal.
- [ ] Public docs are updated when user-facing behavior changes.
- [ ] `CHANGELOG.md` is updated for notable user-facing changes.
- [ ] Feature policy docs and registry tests are updated when feature classification changes.
- [ ] Tests are added or updated when behavior, persistence, archive shape, policy, or setup defaults change.
- [ ] Live Celeste/Everest verification evidence is included when unit tests cannot prove the behavior.
- [ ] Screenshots or video are included for visible, input-driven, timing-sensitive, rendering-sensitive, capture-related, or gameplay-facing changes.
- [ ] The pull request does not commit secrets, local tokens, personal config files, local captures, or machine-specific paths.
- [ ] I have the right to submit this contribution and agree to the contribution license in `CONTRIBUTING.md`.

## Checks run

Delete commands you did not run. Add the exact commands for any other checks.

```text
dotnet format Akron.sln --include <changed-csharp-files>
dotnet build Source/Akron.csproj
dotnet test tests/akron-tests.csproj --nologo
```

## Live verification

- Map SID:
- Akron setup or ruleset state:
- Steps:
- Evidence:

## AI assistance disclosure

Complete this section when AI assistance materially helped produce the change. Delete it only if AI assistance did not materially contribute.

- `agent_name`:
- `agent_version`:
- `model_used`: exact provider model identifier, or `model_not_exposed` with the reason
- `human_testing`:
- `contribution_summary`:
