# Canvas Nudge

A small scheduled job that, **every 3 hours**, fetches all of your currently **outstanding** Canvas work (assignments not yet submitted + peer reviews not yet done) and posts a single Traditional-Chinese digest to a Discord channel.

It's a recurring reminder, not new-item detection: the full standing list is re-sent each cycle, and an item drops out once Canvas reports it done. No local state, no dedup. See [`todos/spec.md`](todos/spec.md) for the full design.

```
📋 待完成 — 共 4 項 · 2026/06/15 14:17（台北時間）

作業 (3)
• [15-503] Cache Lab — 截止 6/17（週二）23:59（還有 2 天）
• [17-214] Homework 3 — 截止 6/18（週三）09:00（還有 3 天）
• [15-619] Project Phase 1 — 已逾期 1 天 ⚠️

同儕互評 (1)
• [15-513] 互評：「Design Doc」— 待完成 3 件（作業截止 6/14）
```

## How it works

- **Runtime:** GitHub Actions scheduled workflow (`.github/workflows/notify.yml`), cron `17 */3 * * *` (UTC, best-effort). Also runnable on demand via **workflow_dispatch**.
- **Canvas:** read via a Personal Access Token (Bearer auth, paginated).
- **Discord:** one outbound POST to an incoming webhook per run.
- **Stateless:** reports the current outstanding set each run; nothing persisted.
- **.NET 8 console app** (`src/`), BCL only (+ `DotNetEnv` for local dev).

## Setup

### 1. Create a Canvas Personal Access Token

1. Log in to Canvas → **Account → Settings → Approved Integrations → New Access Token**.
2. Set a purpose and a **120-day expiry** (treat the token like a password — it has full API access to your account).
3. Copy the token (shown once).
4. Note the expiry date — you'll store it as `CANVAS_TOKEN_EXPIRES_AT` so the digest warns you ≤14 days before it lapses.

### 2. Create a Discord incoming webhook

1. In Discord: **Server Settings → Integrations → Webhooks → New Webhook**.
2. Pick the target channel, **Copy Webhook URL** (also sensitive — anyone with it can post to the channel).

### 3. Configure the repo (keep it **private**)

Under **Settings → Secrets and variables → Actions**:

**Secrets** (encrypted):

| Secret                | Value                       |
| --------------------- | --------------------------- |
| `CANVAS_API_TOKEN`    | the Canvas PAT from step 1  |
| `DISCORD_WEBHOOK_URL` | the webhook URL from step 2 |

**Variables** (non-sensitive; all optional — defaults shown):

| Variable                   | Default                  | Notes                                            |
| -------------------------- | ------------------------ | ------------------------------------------------ |
| `CANVAS_BASE_URL`          | `https://canvas.cmu.edu` | your Canvas URL                                  |
| `TIMEZONE`                 | `Asia/Taipei`            | IANA tz for due dates + timestamp                |
| `NOTIFY_WHEN_EMPTY`        | `true`                   | `true` = send 🎉 line when nothing's outstanding |
| `NOTIFY_ERRORS_TO_DISCORD` | `true`                   | also post failures (e.g. expired PAT) to Discord |
| `CANVAS_TOKEN_EXPIRES_AT`  | _(unset)_                | `YYYY-MM-DD` PAT expiry → warns ≤14 days out     |

### 4. Test it

Trigger the workflow manually: **Actions → canvas-outstanding-notifier → Run workflow**. A digest (or 🎉 line) should appear in your Discord channel.

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

- Keep the repo **private** — it references a Canvas PAT and a webhook.
- Never commit secrets; `.env` is gitignored and CI reads from Actions secrets.
- The PAT acts as your full Canvas account via the API — rotate it every 120 days
  and keep `CANVAS_TOKEN_EXPIRES_AT` current so the digest reminds you.
