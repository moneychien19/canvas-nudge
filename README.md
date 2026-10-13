# Canvas Nudge

**English** | [繁體中文](README.zh-TW.md)

A small scheduled job that, **every 3 hours**, fetches all of your currently **outstanding** Canvas work (assignments not yet submitted + peer reviews not yet done) and posts a single digest to a Discord channel. Notifications are in English by default; set `LANGUAGE=zh-Hant` for Traditional Chinese.

It's a recurring reminder, not new-item detection: the full standing list is re-sent each cycle, and an item drops out once Canvas reports it done. No local state, no dedup. See [`todos/spec.md`](todos/spec.md) for the full design.

```
📋 Outstanding — 4 items · 2026/06/15 14:17 (Eastern Time)

Assignments (3)
• [15-503] Cache Lab — due Wed 6/17 23:59 (in 2 days)
• [17-214] Homework 3 — due Thu 6/18 09:00 (in 3 days)
• [15-619] Project Phase 1 — overdue by 1 day ⚠️

Peer Reviews (1)
• [15-513] Peer review: “Design Doc” — 3 pending (review due 6/17, in 2 days)
```

<details>
<summary>Same digest with <code>LANGUAGE=zh-Hant</code></summary>

```
📋 待完成 — 共 4 項 · 2026/06/15 14:17（美東時間）

作業 (3)
• [15-503] Cache Lab — 截止 6/17（週三）23:59（還有 2 天）
• [17-214] Homework 3 — 截止 6/18（週四）09:00（還有 3 天）
• [15-619] Project Phase 1 — 已逾期 1 天 ⚠️

同儕互評 (1)
• [15-513] 互評：「Design Doc」— 待完成 3 件（互評截止 6/17，還有 2 天）
```

</details>

## How it works

- **Runtime:** GitHub Actions scheduled workflow (`.github/workflows/notify.yml`), cron `17 */3 * * *` (UTC, best-effort). Also runnable on demand via **workflow_dispatch**.
- **Canvas:** read via a Personal Access Token (Bearer auth, paginated).
- **Discord:** one outbound POST to an incoming webhook per run.
- **Stateless:** reports the current outstanding set each run; nothing persisted.
- **.NET 8 console app** (`src/`), BCL only (+ `DotNetEnv` for local dev).

## Setup

Fork this repo (or use it as a template), then:

### 1. Create a Canvas Personal Access Token

1. Log in to Canvas → **Account → Settings → Approved Integrations → New Access Token**.
2. Set a purpose and a **120-day expiry** (treat the token like a password — it has full API access to your account).
3. Copy the token (shown once).
4. Note the expiry date — you'll store it as `CANVAS_TOKEN_EXPIRES_AT` so the digest warns you ≤14 days before it lapses.

### 2. Create a Discord incoming webhook

1. In Discord: **Server Settings → Integrations → Webhooks → New Webhook**.
2. Pick the target channel, **Copy Webhook URL** (also sensitive — anyone with it can post to the channel).

### 3. Configure the repo

Under **Settings → Secrets and variables → Actions**:

**Secrets** (encrypted; never visible in the repo or logs, even if the repo is public):

| Secret                | Value                       |
| --------------------- | --------------------------- |
| `CANVAS_API_TOKEN`    | the Canvas PAT from step 1  |
| `DISCORD_WEBHOOK_URL` | the webhook URL from step 2 |

**Variables** (non-sensitive; all optional — defaults shown):

| Variable                   | Default                  | Notes                                            |
| -------------------------- | ------------------------ | ------------------------------------------------ |
| `LANGUAGE`                 | `en`                     | `en` or `zh-Hant` (Traditional Chinese)          |
| `CANVAS_BASE_URL`          | `https://canvas.cmu.edu` | your Canvas URL                                  |
| `TIMEZONE`                 | `America/New_York`       | IANA tz for due dates + timestamp                |
| `NOTIFY_WHEN_EMPTY`        | `true`                   | `true` = send 🎉 line when nothing's outstanding |
| `NOTIFY_ERRORS_TO_DISCORD` | `true`                   | also post failures (e.g. expired PAT) to Discord |
| `CANVAS_TOKEN_EXPIRES_AT`  | _(unset)_                | `YYYY-MM-DD` PAT expiry → warns ≤14 days out     |

### 4. Test it

Trigger the workflow manually: **Actions → canvas-outstanding-notifier → Run workflow**. A digest (or 🎉 line) should appear in your Discord channel.

> **Note:** On public repos, GitHub disables scheduled workflows after 60 days without repository activity. If notifications stop, re-enable the workflow from the **Actions** tab (or push any commit).

## Local development

```bash
cp .env.example .env     # fill in CANVAS_API_TOKEN + DISCORD_WEBHOOK_URL
dotnet run --project src/CanvasNudge.csproj
```

`.env` is gitignored. Run tests with:

```bash
dotnet test
```

## Security

- Secrets live only in GitHub Actions secrets (CI) or your local `.env` (gitignored) — never commit them.
- The run log prints secrets only as `(set, N chars)`, and GitHub additionally masks secret values in logs, so it's safe for the repo (and its Actions logs) to be public.
- The workflow has no `pull_request` trigger, so forks and PRs can't access your secrets.
- The PAT acts as your full Canvas account via the API — rotate it every 120 days and keep `CANVAS_TOKEN_EXPIRES_AT` current so the digest reminds you.
