# W1b — 消除拖曳時的閃爍

> 起因：2026-09-08 人工驗證 M1–M5 全數通過，但使用者回報**拖曳時會閃爍**。
> 不影響使用，但成因是 W1 自己加的機制，屬 W1 範圍內的缺陷。

## 根因

`WM_WINDOWPOSCHANGING` hook 對**每一次**位置變更都把 `hwndInsertAfter` 改成
`HWND_BOTTOM` 並清除 `SWP_NOZORDER`。`DragMove()` 期間每次滑鼠移動都會觸發該訊息，
於是每一格移動都讓 Windows 重新計算一次 Z-order——閃爍來自這裡。

## Goal

拖曳過程中不重寫 Z-order，拖曳結束後再壓回底層一次。**不得讓 M4 回歸**。

## Files to edit

```
src/DesktopTodoWidget/MainWindow.xaml.cs
```

## Do not modify

`Interop/DesktopAttach.cs`、`Data/` 下任何檔案、`tests/`、`MainWindow.xaml`、
`App.xaml*`、`CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/`

## Requirements

### R1. 拖曳期間停用 Z-order 改寫

以一個旗標標示「拖曳進行中」。hook 在該旗標為 true 時**原樣放行**
`WM_WINDOWPOSCHANGING`，不改 `hwndInsertAfter`、不清 `SWP_NOZORDER`。

`DragMove()` 是同步阻塞呼叫，回傳即代表拖曳結束——旗標在呼叫前設定、
回傳後清除即可，**不需要另外攔截 mouse up**。

### R2. 拖曳結束後重新壓回底層

旗標清除後呼叫一次 `MoveToBottommost`，確保拖曳期間若被提前也會回到底層。

### R3. 例外安全

`DragMove()` 在特定狀況下會擲例外（例如滑鼠鍵已被放開）。
旗標的清除與 R2 的重壓必須放在 `finally`，不得因例外而永久停用 Z-order 維持。

## Acceptance tests

### 自動化（Claude Code 於 sandbox 外執行）

- A1 `dotnet build -c Release --no-restore` 零錯誤零警告
- A2 `dotnet test --no-restore` 32 個測試全數通過

### 人工（使用者，與 U1 同一輪一起驗）

| # | 測試 |
|---|---|
| F1 | 拖曳時**不再閃爍**（或明顯減輕） |
| M2 | 拖曳仍然順暢、放開後位置正確 |
| **M4** | **點一下視窗後仍不會浮到其他視窗之上** ← 最重要的迴歸項 |
| M1 | 位置仍然被記住 |

## Risk notes

- **M4 是這張卡最可能弄壞的東西。** 拖曳期間放行 Z-order 意味著視窗可能短暫浮起，
  R2 的重壓就是為此存在。若拖曳結束後沒有回到底層，等於用閃爍換了一個更嚴重的問題。
- 不要改用 timer 或延遲重壓來「平滑化」——那只是把問題往後推。
- 不要動 `DesktopAttach.cs`：hook 的行為由 `MainWindow` 的旗標控制即可。
