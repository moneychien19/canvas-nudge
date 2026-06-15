# Canvas → Discord "outstanding work" notifier — spec

## 1. Goal

A small scheduled job that, **every 3 hours**, fetches all of _my_ currently
**outstanding** Canvas work (assignments not yet submitted + peer reviews not yet
done) and posts a single digest message to a Discord channel.

The point is a recurring reminder, not new-item detection — I miss notifications,
so I want the full standing list re-sent each cycle. An item disappears from the
digest once I complete it (Canvas reports it as done), so **no local state / dedup
is needed**.

## 2. Architecture (decided)

| Concern          | Decision                                                                        |
| ---------------- | ------------------------------------------------------------------------------- |
| Runtime          | GitHub Actions scheduled workflow (cron)                                        |
| Cadence          | Every 3 hours                                                                   |
| Repo visibility  | **Private** (PAT + course data must not be public)                              |
| Canvas auth      | Personal Access Token (PAT), stored as a GitHub Actions **secret**              |
| Discord delivery | **Incoming Webhook** (outbound only — no bot, no gateway, no always-on process) |
| State            | **None.** Digest model = re-emit current outstanding set each run               |
| Language         | **C# / .NET 8 (LTS)** — console app (swappable, but this spec assumes C#)        |

Why webhook not bot: we only push messages outward, never read commands. A webhook
is a single POST per run, which fits the stateless cron model perfectly.

Why no state file: we only ever report _current_ outstanding items, so there is
nothing to remember between runs. (Note: the GitHub "scheduled workflow disabled
after 60 days inactivity" behavior is documented only for **public** repos; this is
a private repo, so dropping the state file does not reintroduce that problem. If it
ever does silently stop, add a keepalive action — but don't pre-optimize for it.)

## 3. Secrets & config

### Secrets (encrypted — set under repo → Settings → Secrets and variables → Actions → Secrets)

- `CANVAS_API_TOKEN` — Canvas personal access token
- `DISCORD_WEBHOOK_URL` — Discord channel webhook URL (also sensitive: anyone with
  it can post to the channel)

### Variables / env (non-sensitive — repo Variables or hardcoded defaults in workflow)

- `CANVAS_BASE_URL` — default `https://canvas.cmu.edu` (**verify** by checking the
  URL when logged into CMU Canvas; make it configurable, don't hardcode blindly)
- `TIMEZONE` — IANA tz for rendering due dates. Default **`Asia/Taipei`**. (CMU is
  `America/New_York`; switch back to that after moving if desired.) All due dates and
  the digest timestamp are rendered in this tz.
- `NOTIFY_WHEN_EMPTY` — **default `"true"`**: when nothing is outstanding, still
  send a short celebratory line so I can confirm the job is alive **and so any PAT
  expiry warning (§8.2) still reaches me even on an empty day**. Set `"false"` only
  if I ever want silence on empty runs (note: that re-opens the warning-drop gap).
- `NOTIFY_ERRORS_TO_DISCORD` — `"true"` (default) = on failure (e.g. expired PAT)
  also POST a short Chinese error line to Discord, so breakage is visible without
  opening the Actions tab; `"false"` = only fail the Actions run. (Default on because
  I miss GitHub notifications — see §8.)
- `CANVAS_TOKEN_EXPIRES_AT` — optional `YYYY-MM-DD` date the current PAT was set to
  expire (the token is created with a **120-day** expiry). When set, the job warns
  in the digest as the date approaches (see §8). Leave empty to disable the warning.
  Store as a repo **Variable** (it's a date, not a secret).

### Message language (localization)

- **Template / chrome text is Traditional Chinese (zh-Hant)**: headers, category
  labels, relative-time hints, overdue text, empty-state line, error line.
- **Canvas-supplied content is shown verbatim, untranslated**: course name /
  course code, assignment name, peer-review assignment name. Do **not**
  machine-translate these — they're English and stay English.
- No translation API is involved; the Chinese is hardcoded template strings.

Never commit any of these. `.env` for local runs must be gitignored.

## 4. Canvas API

Base: `{CANVAS_BASE_URL}/api/v1`
Auth header on every request: `Authorization: Bearer {CANVAS_API_TOKEN}`
Always request `?per_page=100` and **follow pagination** via the `Link` response
header (`rel="next"`) until exhausted.
Handle non-200 responses: on `401` the PAT is expired/revoked → fail loudly. On
`403` (Canvas signals throttling here) **or** `429`, sleep a few seconds (~5s; honor
`Retry-After`/`X-Rate-Limit-Remaining` if present) and retry **once**; if it still
fails, fail loud (non-zero exit). Volume here is tiny (a handful of courses every
3h) so rate limits won't normally bite.

### 4.1 Resolve self id

`GET /users/self` → keep `id`. (No longer required for peer reviews — see §4.3 —
since GraphQL scopes to the current user. Kept as an optional auth sanity check.)

### 4.2 Outstanding assignments — via the to-do list

`GET /users/self/todo`

This endpoint already returns _actionable, not-yet-done_ items, so it does the
"is it complete?" filtering for us. Keep items where `type == "submitting"`.

Each item of interest contains an `assignment` object; pull:

- `assignment.name`
- `assignment.due_at` (ISO 8601, UTC; may be null)
- `assignment.html_url` (direct link)
- `course_id` (and resolve course name — see 4.4)

Known limitation (acceptable for MVP): the to-do list may omit assignments with no
due date or far-future items. That's fine for now; broadening to per-course
`bucket=unsubmitted` is a v2 option.

### 4.3 Outstanding peer reviews — via **GraphQL** (REST does not work for students)

> **Why not the REST `peer_reviews` endpoint?** The original plan was
> `GET /courses/{id}/assignments/{aid}/peer_reviews` filtered by
> `assessor_id == self.id`. **Verified against canvas.cmu.edu, this does not work
> for a student account:** that endpoint only returns peer reviews of *your own*
> submission (`asset_id` == your submission), with **no `assessor_id` field** at
> all — a student can't list other students' submissions, which is where the
> reviews-you-must-do live. It surfaces "who is reviewing me," not "what I owe."
> `include[]=assessment_requests` / `assigned_assessments` are also absent.

Use Canvas **GraphQL** (`POST {CANVAS_BASE_URL}/api/graphql`, same Bearer token).
The field `assessmentRequestsForCurrentUser` returns exactly the reviews the
current user must complete, correctly scoped — no self-id filter needed.

1. List active courses (still REST): `GET /courses?enrollment_state=active&per_page=100`
   (also used for the course-name map, §4.4).
2. For each course, run:
   ```graphql
   query($courseId: ID!) {
     course(id: $courseId) {
       assignmentsConnection {
         nodes {
           _id
           name
           dueAt
           peerReviews { dueAt }
           assessmentRequestsForCurrentUser { workflowState }
         }
       }
     }
   }
   ```
   For each assignment node, count `assessmentRequestsForCurrentUser` entries with
   `workflowState == "assigned"` (`"completed"` = done). GraphQL returns 200 even
   on failure — check the `errors` array and fail loud if present.

One assignment can assign you **multiple** reviews (e.g. review 3 classmates →
3 entries). **Group by assignment** and carry a count `N` of still-`assigned`
reviews — don't emit one digest line per review. Each group → display the parent
assignment's `name` plus the pending count `N`, with the **peer-review due date**
(`peerReviews.dueAt`) as context, falling back to the assignment `dueAt` when the
peer-review date isn't configured. Render it with the same relative hint / overdue
treatment as assignments (labelled `互評截止`).

### 4.4 Course names

Build a `{course_id: course_name}` map from the §4.3 step-1 course list and reuse it
to label assignment items from 4.2. Use `name` (fall back to `course_code`).

## 5. Discord output

POST JSON to `DISCORD_WEBHOOK_URL`.

### Format

One digest message. Group by category, sort each group by due date ascending
(soonest / most overdue first). Render due dates in `TIMEZONE` with a relative
hint. **Template text in Traditional Chinese; Canvas content (course/assignment
names) verbatim in English** (see §3 "Message language").

Target shape:

```
📋 待完成 — 共 4 項 · 2026/06/15 14:17（台北時間）

作業 (3)
• [15-503] Cache Lab — 截止 6/17（週二）23:59（還有 2 天）
• [17-214] Homework 3 — 截止 6/18（週三）（還有 3 天）
• [15-619] Project Phase 1 — 已逾期 1 天 ⚠️

同儕互評 (1)
• [15-513] 互評：「Design Doc」— 待完成 3 件（互評截止 6/14，還有 2 天）
```

The peer-review category counts **assignments with pending reviews**, not the raw
review count. The `N 件` suffix shows how many reviews remain for that assignment
(omit ` N 件` wording if N == 1, or always show it — pick one and be consistent;
default: always show `待完成 N 件`).

- Prefix each line with the course code/name in brackets (English, verbatim).
- Relative-time hints in Chinese: `還有 N 天` / `還有 N 小時` / `今天截止` /
  `已逾期 N 天`. Compute relative to "now" in `TIMEZONE`.
- Mark overdue items distinctly (⚠️ + `已逾期 N 天`). **Caveat:** whether an overdue
  unsubmitted assignment appears at all depends on the `/users/self/todo` endpoint
  (§4.2 known limitation) — it may drop past-due or undated items. MVP relies on
  todo only; if overdue items go missing, switching to per-course
  `bucket=unsubmitted` is the v2 fix. So `已逾期` rendering is best-effort.
- An assignment with no `due_at` → render `無截止日` instead of a date/relative hint,
  and sort it last within its group.
- Link each item to its `html_url` (use Discord markdown links inside an embed
  description: `[name](url)`).
- Prefer **embeds** over raw content for cleaner rendering (one embed, description
  holds the grouped list; or one embed per category).

### Discord limits — handle, don't exceed

- `content` ≤ 2000 chars; embed `description` ≤ 4096; ≤ 25 fields/embed; ≤ 10
  embeds/message; total embed payload ≤ 6000 chars.
- **Expected case: one run → one message.** Realistic outstanding lists (a handful
  of courses) fit comfortably in a single embed, so don't build elaborate chunking.
- **Defensive fallback only:** if the assembled `description` would exceed 4096
  chars, split into multiple sequential POSTs **at item (bullet) boundaries** — never
  cut a single item in half, never truncate. This is a safety net, not an expected
  path; a simple "if too long, start a second embed" is enough.

### Empty state

If there are zero outstanding items: send a one-line
"恭喜，目前沒有未完成事項 🎉" when `NOTIFY_WHEN_EMPTY=true` (**default**) — this
doubles as a liveness ping and carries any §8.2 expiry warning. Send nothing only
when `NOTIFY_WHEN_EMPTY=false`. If an expiry warning is active (§8.2), prepend it
to the celebratory line.

## 6. GitHub Actions workflow

`.github/workflows/notify.yml`

```yaml
name: canvas-outstanding-notifier

on:
  schedule:
    # every 3 hours at :17 past (avoid top-of-hour congestion). Cron is UTC.
    - cron: "17 */3 * * *"
  workflow_dispatch: {} # manual trigger for testing

permissions:
  contents: read # least privilege — no state commit, so no write needed

concurrency:
  group: notifier
  cancel-in-progress: false

jobs:
  notify:
    runs-on: ubuntu-latest
    timeout-minutes: 5
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"
      - run: dotnet run --project src/CanvasNudge.csproj -c Release
        env:
          CANVAS_API_TOKEN: ${{ secrets.CANVAS_API_TOKEN }}
          DISCORD_WEBHOOK_URL: ${{ secrets.DISCORD_WEBHOOK_URL }}
          CANVAS_BASE_URL: ${{ vars.CANVAS_BASE_URL || 'https://canvas.cmu.edu' }}
          TIMEZONE: ${{ vars.TIMEZONE || 'Asia/Taipei' }}
          NOTIFY_WHEN_EMPTY: ${{ vars.NOTIFY_WHEN_EMPTY || 'true' }}
          NOTIFY_ERRORS_TO_DISCORD: ${{ vars.NOTIFY_ERRORS_TO_DISCORD || 'true' }}
          CANVAS_TOKEN_EXPIRES_AT: ${{ vars.CANVAS_TOKEN_EXPIRES_AT }}
```

Notes:

- Cron is **UTC only** and **best-effort** (10–30 min delays are common at peak;
  occasional skips). Fine for this use case.
- `workflow_dispatch` lets me run it on demand to test.

## 7. Project structure

```
<repo>/
├─ .github/workflows/notify.yml
├─ src/
│  ├─ CanvasNudge.csproj  # net8.0 console app; deps below
│  ├─ Program.cs          # entry point: orchestrate fetch → assemble → send
│  ├─ CanvasClient.cs     # API client: auth, pagination, the endpoint calls in §4
│  ├─ DiscordNotifier.cs  # build digest message + POST to webhook, chunk if needed
│  └─ Models.cs           # DTOs for Canvas + Discord JSON (System.Text.Json)
├─ .env.example           # documents env vars; real .env is gitignored
├─ .gitignore             # .env, bin/, obj/
└─ README.md              # setup: create PAT, create webhook, set secrets
```

A single-file `Program.cs` (top-level statements) is acceptable for the MVP, but
the above split is preferred.

### Dependencies

.NET 8's BCL already covers almost everything — no third-party HTTP/JSON/timezone
libs needed:

- `HttpClient` — Canvas API calls + Discord webhook POST.
- `System.Text.Json` — parse Canvas responses, serialize Discord embeds.
- `TimeZoneInfo` — renders IANA tz names (`America/New_York`, `Asia/Taipei`)
  natively on Linux (the GitHub Actions `ubuntu-latest` runner) and macOS. On
  Windows, .NET 6+ also accepts IANA names via ICU, so local dev works too.

Optional NuGet:

- `DotNetEnv` — load a local `.env` for `dotnet run` during dev. In CI the env
  vars come from Actions secrets/vars, so this is dev-only; guard it so a missing
  `.env` is a no-op.

`.csproj` notes:

- Do **not** set `<InvariantGlobalization>true</InvariantGlobalization>` — that
  would break `TimeZoneInfo` IANA lookups and zh-Hant culture formatting. Leave it
  default (false).
- Target `net8.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.

## 8. Error handling

- Canvas/network failure or `401` → log clearly and exit non-zero so the Actions
  run shows red (visible in the Actions tab). Do **not** silently swallow.
- When `NOTIFY_ERRORS_TO_DISCORD` is on (default), also POST a short Chinese error
  line to Discord so breakage is visible without opening GitHub.
- Discord POST failure → log response body + status; exit non-zero. (Can't notify
  via Discord if Discord itself is down — just fail loud in the log.)

### 8.1 Expired-PAT message (the 120-day case)

A `401` from Canvas almost always means the PAT expired (it's created with a 120-day
expiry) or was revoked. Do **not** print a raw stack trace — emit this exact,
self-explanatory message to **both** the run log and (if enabled) Discord:

```
❌ Canvas PAT 失效（HTTP 401）
原因：Personal Access Token 可能已過期（本 token 設定 120 天到期）或被撤銷。
修復：
  1. 登入 Canvas → Account → Settings → Approved Integrations → New Access Token
  2. 產生新 token（記得再設一次到期日）
  3. 更新 GitHub repo → Settings → Secrets → CANVAS_API_TOKEN
  4. 若有設 CANVAS_TOKEN_EXPIRES_AT，把它改成新的到期日
```

Then exit non-zero. The point: the message alone tells me what broke and how to fix
it, in Chinese, without diagnosing anything.

### 8.2 Proactive expiry warning (before it breaks)

If `CANVAS_TOKEN_EXPIRES_AT` is set, compute days-until-expiry (in `TIMEZONE`):

- **> 14 days:** silent.
- **≤ 14 days:** prepend one warning line to the normal digest (don't fail the run):
  `⚠️ Canvas PAT 將於 N 天後（YYYY-MM-DD）過期，請及早更新，否則通知會停止。`
- **≤ 0 days (past the stored date):** still send the warning, since the token may
  already be dead — the next `401` then triggers §8.1.

This makes a known, dated event visible *in the digest I already read*, so I'm not
relying on noticing a red Actions run after it's already broken.

## 9. Acceptance criteria

1. Local run (`dotnet run --project src/CanvasNudge.csproj` with a local `.env`)
   posts a correctly formatted digest to the Discord channel.
2. `workflow_dispatch` run succeeds and posts the same.
3. Scheduled run fires roughly every 3 hours.
4. Completing an item in Canvas makes it drop out of the next digest.
5. No secrets in the repo; `.env` is gitignored; PAT and webhook are GitHub secrets.
6. Empty-state behavior matches `NOTIFY_WHEN_EMPTY`.
7. A normal digest sends as a single message; only an unusually long one splits
   (at item boundaries) instead of truncating.

## 10. Security notes

- Private repo only.
- A Canvas PAT acts as the full account via the API (read/submit work, grades,
  inbox, profile) — treat it like a password. Create it with a **120-day expiry** and
  record that date in the `CANVAS_TOKEN_EXPIRES_AT` repo Variable; the job then warns
  in the digest ≤14 days out (§8.2) and, if it lapses, prints the fix-it message
  (§8.1) — so you don't depend solely on noticing a red Actions run.
- Webhook URL is sensitive too — keep it in secrets.

## 11. Out of scope (v2 ideas, do not build now)

- State-based "something NEW appeared" push (would reintroduce a state file).
- Quiet hours / only notify during waking hours; dedupe identical consecutive
  digests.
- Grade-posted / feedback notifications.
- Per-course routing to different channels.
- Configurable "due within N days" window; including undated/far-future assignments.
