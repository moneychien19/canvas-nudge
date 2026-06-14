# Canvas Nudge — task list

> Derived from `spec.md` + `plan.md`. Tasks are ordered; each is a vertical slice
> with acceptance criteria (AC) and a verification step (V). Check off as you go.

## Phase 0 — bootstrap

- [ ] **0.1 git init + repo hygiene**
  - AC: `git init` done; first commit excludes secrets.
  - V: `git status` clean of `.env`, `bin/`, `obj/`.

## Phase 1 — Discord path proven  ▸ CP-A

- [x] **1.1 Project scaffold**
  - Create `src/CanvasNudge.csproj` (net8.0, `Nullable=enable`, `ImplicitUsings=enable`,
    NO `InvariantGlobalization`), `.gitignore` (`.env`, `bin/`, `obj/`), `.env.example`
    documenting every var from spec §3.
  - AC: `dotnet build` succeeds; `.gitignore` covers secrets.
  - V: `dotnet build -c Release` exits 0.
- [ ] **1.2 Config loader**
  - Read all env vars (§3); optional `DotNetEnv` guarded so missing `.env` is a no-op.
  - AC: all 7 config keys load with documented defaults; secrets required, others optional.
  - V: run with a dummy `.env`, print resolved config (no secret values).
- [ ] **1.3 DiscordNotifier — POST a fixed message**
  - `HttpClient` POST a hardcoded embed to `DISCORD_WEBHOOK_URL`.
  - AC: a test message appears in the channel; non-2xx → log body+status, exit non-zero.
  - V: `dotnet run` → message visible in Discord.  **→ CP-A**

## Phase 2 — Canvas read path proven

- [ ] **2.1 CanvasClient core (auth + pagination + retry)**
  - Bearer auth on every request; `?per_page=100`; follow `Link rel="next"`;
    on `401` fail loud; on `403`/`429` sleep ~5s (honor `Retry-After`) retry once.
  - AC: pagination exhausts; retry fires once then fails loud.
  - V: hit a paginated endpoint, confirm all pages fetched.
- [ ] **2.2 Models — Canvas DTOs**
  - `System.Text.Json` DTOs for `/users/self`, todo item + `assignment`, course,
    assignment, peer_review (§4).
  - AC: deserialization handles null `due_at`.
- [ ] **2.3 Fetch self id + todo assignments**
  - `GET /users/self`; `GET /users/self/todo` → keep `type=="submitting"`; pull
    `assignment.name/.due_at/.html_url` + `course_id` (confirm `assignment.html_url`
    populated, else top-level `html_url`).
  - AC: outstanding assignments printed to console with name/due/course_id/url.
  - V: `dotnet run` prints a list matching Canvas "to-do".

## Phase 3 — data layer complete  ▸ CP-B

- [ ] **3.1 Courses + course-name map**
  - `GET /courses?enrollment_state=active&per_page=100` → `{id: name||course_code}` map;
    reuse to label Phase 2.3 assignments.
  - AC: every assignment line resolves to a course label.
- [ ] **3.2 Peer reviews fetch + grouping**
  - Per course → assignments where `peer_reviews==true && published==true` →
    per assignment list peer_reviews → keep `assessor_id==self.id && workflow_state=="assigned"`.
    Group by assignment, carry count `N`.
  - AC: one group per assignment with pending count N (not one line per review).
  - V: printed peer-review groups match Canvas.  **→ CP-B**

## Phase 4 — real digest  ▸ CP-C

- [ ] **4.1 Domain assembly (grouping, sorting, relative time)**
  - Build outstanding model; group by category (作業 / 同儕互評); sort each by `due_at`
    asc, undated (`無截止日`) last; render due in `TIMEZONE` with zh-Hant relative hints
    (`還有 N 天`/`還有 N 小時`/`今天截止`/`已逾期 N 天`) + weekday.
  - AC: matches §5 target shape; Canvas names verbatim English, chrome zh-Hant.
  - V: unit-spot-check borderline cases (today / overdue / undated).
- [ ] **4.2 Wire assembly into the embed**
  - Replace 1.3 fixed message: one embed, header `📋 待完成 — 共 N 項 · <ts>（台北時間）`,
    grouped description with `[name](html_url)` markdown links.
  - AC: real digest posts as a single message matching §5.
  - V: compare posted message to spec example.  **→ CP-C**

## Phase 5 — edge cases

- [ ] **5.1 Empty state**
  - Zero items + `NOTIFY_WHEN_EMPTY=true` (**default**) → send `恭喜，目前沒有未完成事項 🎉`
    (also carries any active §8.2 expiry warning); `=false` → send nothing.
  - V: toggle var, run both ways.
- [ ] **5.2 Over-length chunking (defensive)**
  - If description > 4096, split into sequential POSTs at item boundaries (never truncate).
  - AC: long synthetic list splits cleanly; normal list = single message.
- [ ] **5.3 Webhook POST 429 retry**
  - On webhook `429`, honor `retry_after` and retry once.

## Phase 6 — error handling

- [ ] **6.1 401 fix-it message (§8.1)**
  - On Canvas 401, emit the exact zh-Hant fix-it block to log; exit non-zero.
- [ ] **6.2 NOTIFY_ERRORS_TO_DISCORD (§8)**
  - On failure, if enabled, POST a short zh-Hant error line to Discord too.
  - V: simulate 401 with a bad token → fix-it message in log + Discord.
- [ ] **6.3 Expiry warning + empty-send gap (§8.2)**
  - If `CANVAS_TOKEN_EXPIRES_AT` set: >14d silent; ≤14d prepend
    `⚠️ Canvas PAT 將於 N 天後（YYYY-MM-DD）過期…`; ≤0d still warn.
  - **Gap fix:** with `NOTIFY_WHEN_EMPTY=true` default, the celebratory empty message
    (5.1) already carries the warning. If `NOTIFY_WHEN_EMPTY=false`, an active warning
    must still force a send.
  - V: set expiry date ≤14d with empty list → warning still delivered.

## Phase 7 — deploy  ▸ CP-D

- [ ] **7.1 GitHub Actions workflow**
  - `.github/workflows/notify.yml` per §6 (cron `17 */3 * * *`, `workflow_dispatch`,
    `permissions: contents: read`, concurrency `notifier`, env wiring).
  - AC: workflow file valid; secrets/vars referenced correctly.
- [ ] **7.2 README setup docs**
  - Create PAT (120-day expiry), create webhook, set secrets/vars, local `.env` run.
- [ ] **7.3 End-to-end CI run**
  - V: `workflow_dispatch` run succeeds and posts a digest.  **→ CP-D**

## Acceptance criteria coverage (§9)

| AC | Covered by |
|----|-----------|
| 1 local run posts digest | CP-C / 4.2 |
| 2 workflow_dispatch posts | 7.3 |
| 3 scheduled ~3h | 7.1 |
| 4 completed item drops out | stateless design; verify post-CP-C |
| 5 no secrets in repo | 0.1 / 1.1 |
| 6 empty-state behavior | 5.1 |
| 7 single msg / split not truncate | 5.2 |
