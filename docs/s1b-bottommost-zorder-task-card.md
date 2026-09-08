# S1b — 修正 bottommost 的 Z-order 矛盾

> 起因：2026-09-07 人工驗證 **E8 失敗**——點擊 widget 後它會蓋過其他視窗。
> 依 findings.md D2 的處置規則，判定「不可交付」前先做一次有界的根因確認，
> 本卡即是該確認的產物。

## 根因

`MoveToBottommost` 只在 `SourceInitialized` 呼叫**一次** `SetWindowPos(HWND_BOTTOM)`。
使用者點擊視窗會使其取得 activation，Windows 隨即把它提到 Z-order 前方——
這是正常且文件化的行為，不是缺陷。**缺的是「每次 Z-order 要變動時把它壓回去」。**

E1／E2／E7 在 bottommost 模式**已通過**（按鈕可點、Alt+Tab 看不到、可打字含中文 IME），
所以問題只在 Z-order 維持，不在互動能力。

## Goal

讓 bottommost 模式的視窗在取得 activation 之後仍然回到 Z-order 底部，
**且不得破壞已經通過的 E1／E2／E7**。

## Files to edit

```
src/DesktopTodoWidget/MainWindow.xaml.cs
src/DesktopTodoWidget/Interop/DesktopAttach.cs
```

## Do not modify

- `App.xaml`、`App.xaml.cs`、`MainWindow.xaml`
- `src/DesktopTodoWidget/Data/` 下任何檔案
- `tests/` 下任何檔案
- `CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/` 下任何檔案

## Requirements

### R1. 攔截 `WM_WINDOWPOSCHANGING`（0x0046）

以 `HwndSource.AddHook` 掛上訊息處理，在 `WM_WINDOWPOSCHANGING` 時把
`WINDOWPOS.hwndInsertAfter` 設為 `HWND_BOTTOM`（值為 1），並**清除 `SWP_NOZORDER`**
（否則系統會忽略我們設定的 `hwndInsertAfter`）。

- `WINDOWPOS` 結構要宣告在 `Interop/DesktopAttach.cs`，與其他 P/Invoke 一起。
- 需要以 `Marshal.PtrToStructure` 讀出、修改後 `Marshal.StructureToPtr` 寫回。

### R2. 只在 bottommost 模式生效

`workerw` 附著成功時**不得**掛這個 hook（child window 的 Z-order 由 parent 管，
強壓底層可能造成不可預期的行為）。

`workerw` 失敗退回 bottommost 時**要**掛上。

### R3. 不得改用 `WS_EX_NOACTIVATE`

那會讓視窗無法取得 activation，雖然也能解 Z-order，但會破壞已通過的 E7
（鍵盤焦點與輸入法）。**本卡的目的是修 E8 而不弄壞 E7。**

### R4. log

hook 掛上與卸下時各寫一筆 log 到既有的 `spike.log`。
**不要**在每次 `WM_WINDOWPOSCHANGING` 都寫 log——那個訊息非常頻繁，會把 log 灌爆。

## Acceptance tests

### 自動化（由 Claude Code 在 sandbox 外執行）

- A1 `dotnet build -c Release --no-restore` 零錯誤
- A2 `dotnet test --no-restore` 既有 25 個測試仍全數通過
- A3 `DllImport`／`LibraryImport` 仍只出現在 `Interop/DesktopAttach.cs`

### 人工（使用者執行）

重跑 `--mode=bottommost`，**四項都要通過才算修好**：

| # | 測試 | 判定 |
|---|---|---|
| E8 | 點擊 widget 後**不會**蓋過其他視窗 | 本卡的目的 |
| E1 | 按鈕仍可點、計數仍會增加 | 不得因修正而破壞 |
| E2 | Alt+Tab 仍看不到它 | 不得因修正而破壞 |
| E7 | edit box 仍可打字、中文 IME 仍可組字 | **最重要的迴歸項** |

**只修好 E8 但弄壞 E7 等於沒修。**

## Risk notes

- **不要在 hook 裡呼叫 `SetWindowPos`**——那會遞迴觸發 `WM_WINDOWPOSCHANGING`。
  正確做法是修改傳入的 `WINDOWPOS` 結構後放行。
- 這個修正只保證「不會浮到最上面」，**不保證「在桌布之上、圖示之下」**——
  後者是 `workerw` 才做得到的事。若使用者要的是後者，E8 通過也不代表需求滿足。
- 若修正後 E7 失效，**停下來回報**，不要為了兩者兼顧而引入更複雜的 activation 操作。
