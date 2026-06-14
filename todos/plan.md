# Canvas → Discord notifier — implementation plan

> Companion to `spec.md`. Task checklist lives in `todos/todo.md`.
> Status: greenfield (no code, no git repo yet).

## Spec review summary

The spec is complete and buildable. Open decisions baked into the tasks below:

- **Empty + expiry-warning gap (§8.2 vs §5 empty-state):** an active PAT-expiry
  warning (≤14 days) must **force a send even when the outstanding list is empty**,
  otherwise the warning is dropped precisely when needed. → handled in Phase 6.
- **Partial-fetch failure:** keep spec behavior (fail loud, no digest) for MVP.
  Documented as a conscious tradeoff, not silently.
- **Discord webhook 429:** add a single `retry_after`-honoring retry on the POST
  path (the chunking fallback can trip it). → Phase 5.
- **`html_url`:** confirm `assignment.html_url` is populated on todo items at build
  time; fall back to top-level `html_url` if not.

## Dependency graph

```
scaffold (csproj/.gitignore/.env.example)
   └─ config loader (env + DotNetEnv)
        ├─ Models (Canvas + Discord DTOs)
        │     └─ CanvasClient (auth, pagination, retry)
        │           ├─ self id        ─┐
        │           ├─ todo (assignments)│
        │           ├─ courses + map     ├─ Domain assembly (group/sort/relative-time)
        │           └─ peer reviews     ─┘        └─ DiscordNotifier (embed + POST + chunk)
        └─ Program orchestration (fetch → assemble → send → error handling)
                 └─ GitHub Actions workflow + README docs
```

## Vertical slices (each = one complete runnable path)

1. **Discord path proven** — scaffold + config + POST a fixed message to the webhook.
2. **Canvas read path proven** — auth + self id + todo assignments (pagination + retry), printed to console.
3. **Data layer complete** — course map + peer-review fetch & grouping by assignment.
4. **Real digest** — assembly (relative-time zh-Hant, grouping, sorting) wired into the embed, replacing the fixed message.
5. **Edge cases** — empty state + over-length chunking at item boundaries + webhook 429 retry.
6. **Error handling** — 401 fix-it message, `NOTIFY_ERRORS_TO_DISCORD`, expiry warning (incl. force-send-when-empty gap).
7. **Deploy** — GitHub Actions workflow + README setup docs.

## Checkpoints (human review gates)

- **CP-A (after Phase 1):** confirm a message lands in the real Discord channel.
- **CP-B (after Phase 3):** eyeball printed outstanding data vs. actual Canvas state.
- **CP-C (after Phase 4):** verify the rendered digest matches the §5 target shape.
- **CP-D (after Phase 7):** `workflow_dispatch` run succeeds end-to-end in CI.

## Verification strategy

- No formal test framework required by spec; verify each phase by running
  `dotnet run --project src/CanvasNudge.csproj` against a local `.env`.
- Acceptance criteria §9 (1–7) map to CP-A…CP-D + Phase 5/6 manual toggles.
