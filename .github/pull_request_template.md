## Summary

- 

## Verification

- [ ] The diff is scoped to one goal.
- [ ] Public docs are updated when user-facing behavior changes.
- [ ] Feature policy docs and registry tests are updated when status classification changes.
- [ ] Tests are added or updated when behavior, persistence, archive shape, policy, or setup defaults change.
- [ ] Live Celeste/Everest verification evidence is included when unit tests cannot prove the behavior.
- [ ] Screenshots or video are included for visible, input-driven, timing-sensitive, rendering-sensitive, capture-related, or gameplay-facing changes.
- [ ] The pull request does not commit secrets, local tokens, personal config files, local captures, or machine-specific paths.
- [ ] I have the right to submit this contribution and agree to the contribution license in `CONTRIBUTING.md`.

## Checks Run

```text
dotnet format Akron.sln --include <changed-csharp-files>
dotnet build Source/Akron.csproj
dotnet test tests/akron-tests.csproj --nologo
```

## Live Verification

- Map SID:
- Akron setup or ruleset state:
- Steps:
- Evidence:

## AI Assistance Disclosure

Complete this section when AI assistance materially helped produce the change. Delete it only when no AI assistance was used.

- `agent_name`:
- `agent_version`:
- `model_used`:
- `human_testing`:
- `contribution_summary`:
