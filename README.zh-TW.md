# Canvas Nudge

[English](README.md) | **繁體中文**

一個小型排程工作：**每 3 小時**抓一次你在 Canvas 上所有**尚未完成**的事項（還沒繳交的作業 + 還沒做的同儕互評），整理成一則摘要發到 Discord 頻道。通知預設是英文，設定 `LANGUAGE=zh-Hant` 可改成繁體中文。

這是週期性提醒，不是「新事項偵測」：每次都會重送完整的待辦清單，等 Canvas 顯示完成後該項目才會消失。沒有本地狀態、也不去重。完整設計見 [`todos/spec.md`](todos/spec.md)。

```
📋 待完成 — 共 4 項 · 2026/06/15 14:17（美東時間）

作業 (3)
• [15-503] Cache Lab — 截止 6/17（週三）23:59（還有 2 天）
• [17-214] Homework 3 — 截止 6/18（週四）09:00（還有 3 天）
• [15-619] Project Phase 1 — 已逾期 1 天 ⚠️

同儕互評 (1)
• [15-513] 互評：「Design Doc」— 待完成 3 件（互評截止 6/17，還有 2 天）
```

## 運作方式

- **執行環境：** GitHub Actions 排程 workflow（`.github/workflows/notify.yml`），cron `17 */3 * * *`（UTC，GitHub 盡力準時）。也可以透過 **workflow_dispatch** 手動觸發。
- **Canvas：** 用 Personal Access Token 讀取（Bearer 認證、自動分頁）。
- **Discord：** 每次執行對 incoming webhook 發一次 POST。
- **無狀態：** 每次只回報當下的待辦，不儲存任何東西。
- **.NET 8 console app**（`src/`），只用 BCL（本地開發另用 `DotNetEnv`）。

## 設定

先 fork 這個 repo（或當成 template 使用），然後：

### 1. 建立 Canvas Personal Access Token

1. 登入 Canvas → **Account → Settings → Approved Integrations → New Access Token**。
2. 填寫用途，並設定 **120 天到期**（token 等同密碼，擁有你帳號的完整 API 權限）。
3. 複製 token（只會顯示一次）。
4. 記下到期日 —— 存成 `CANVAS_TOKEN_EXPIRES_AT`，摘要會在到期前 14 天內提醒你。

### 2. 建立 Discord incoming webhook

1. 在 Discord：**伺服器設定 → 整合 → Webhook → 新 Webhook**。
2. 選擇目標頻道，**複製 Webhook 網址**（同樣是敏感資訊 —— 拿到的人都能發訊息到該頻道）。

### 3. 設定 repo

到 **Settings → Secrets and variables → Actions**：

**Secrets**（加密儲存；即使 repo 公開，也不會出現在 repo 或 log 中）：

| Secret                | 值                     |
| --------------------- | ---------------------- |
| `CANVAS_API_TOKEN`    | 步驟 1 的 Canvas PAT   |
| `DISCORD_WEBHOOK_URL` | 步驟 2 的 webhook 網址 |

**Variables**（非敏感、皆為選填 —— 列出的是預設值）：

| Variable                   | 預設值                   | 說明                                         |
| -------------------------- | ------------------------ | -------------------------------------------- |
| `LANGUAGE`                 | `en`                     | `en` 或 `zh-Hant`（繁體中文）                |
| `CANVAS_BASE_URL`          | `https://canvas.cmu.edu` | 你的 Canvas 網址                             |
| `TIMEZONE`                 | `America/New_York`       | 截止時間與時間戳使用的 IANA 時區             |
| `NOTIFY_WHEN_EMPTY`        | `true`                   | `true` = 沒有待辦時也送一則 🎉 訊息          |
| `NOTIFY_ERRORS_TO_DISCORD` | `true`                   | 失敗時（例如 PAT 過期）也發到 Discord        |
| `CANVAS_TOKEN_EXPIRES_AT`  | _(未設定)_               | `YYYY-MM-DD` PAT 到期日 → 到期前 14 天內提醒 |

### 4. 測試

手動觸發 workflow：**Actions → canvas-outstanding-notifier → Run workflow**。Discord 頻道應該會出現摘要（或 🎉 訊息）。

> **注意：** 公開 repo 若 60 天沒有任何活動，GitHub 會自動停用排程 workflow。如果通知停了，到 **Actions** 頁面重新啟用（或推任何一個 commit）即可。

## 本地開發

```bash
cp .env.example .env     # 填入 CANVAS_API_TOKEN + DISCORD_WEBHOOK_URL
dotnet run --project src/CanvasNudge.csproj
```

`.env` 已被 gitignore。執行測試：

```bash
dotnet test
```

## 安全性

- Secret 只存在 GitHub Actions secrets（CI）或本地 `.env`（已 gitignore）—— 絕對不要 commit。
- 執行 log 中 secret 只會顯示為 `(set, N chars)`，GitHub 也會自動遮蔽 log 裡的 secret 值，所以 repo（以及它的 Actions log）公開是安全的。
- workflow 沒有 `pull_request` trigger，fork 或 PR 都拿不到你的 secrets。
- PAT 透過 API 等同你的完整 Canvas 帳號 —— 每 120 天輪換一次，並保持 `CANVAS_TOKEN_EXPIRES_AT` 為最新，讓摘要提醒你。
