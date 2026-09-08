# W1 — 視窗外殼

> 依賴 D1a（`AppPaths`／`AtomicJsonStore`）、D1b（`SingleInstanceGuard`）與 **S1 的定案**。
> **S1 結論：`workerw` 不可交付並移除，`bottommost` 為唯一模式**（findings.md D2）。

## Goal

把 spike 換成產品的視窗外殼：無邊框、不在 Alt+Tab、維持在最底層、
可拖曳移動、**位置會被記住**、只允許單一實例。

**本卡不做 todo 功能**（新增／刪除／勾選／編輯是 U1）。
視窗內容維持一個佔位區塊即可。

## Files to edit

```
src/DesktopTodoWidget/MainWindow.xaml
src/DesktopTodoWidget/MainWindow.xaml.cs
src/DesktopTodoWidget/App.xaml.cs
src/DesktopTodoWidget/Data/WindowPlacement.cs
tests/DesktopTodoWidget.Tests/WindowPlacementTests.cs
```

`Interop/DesktopAttach.cs` 允許**刪除** workerw 相關成員（見 R1），不得新增功能。

## Do not modify

- `src/DesktopTodoWidget/Data/AppPaths.cs`、`AtomicJsonStore.cs`、
  `TodoModels.cs`、`SingleInstanceGuard.cs`
- 既有的 25 個測試
- `CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/`

## Requirements

### R1. 移除 workerw

刪除 `--mode` / `--target` 參數與所有 workerw 相關程式碼：
`TryResolveDesktopParent`、`TryAttachToParent`、`RestoreTopLevelStyle`、
失聯偵測 timer、`0x052C`、`SetParent`／`ScreenToClient`／`EnumWindows`／
`FindWindow`／`FindWindowEx` 等只為 workerw 存在的 P/Invoke。

**保留**：`ConfigureToolWindow`、`MoveToBottommost`、`WM_WINDOWPOSCHANGING` hook、
`WindowPos`、`GetWindowClassName`／DPI 診斷（若仍用於 log）。

> S1 已證明 workerw 不可用，留著只會讓後續維護者以為那是可選路徑。

### R2. 視窗外觀與行為

- 無邊框（`WindowStyle=None`、`AllowsTransparency=false`、不透明深色背景）
- `ShowInTaskbar=false`；`SourceInitialized` 時設 `WS_EX_TOOLWINDOW`、
  清 `WS_EX_APPWINDOW`、`SWP_FRAMECHANGED`（沿用 spike 的做法，已驗證）
- 以 `WM_WINDOWPOSCHANGING` hook 維持 `HWND_BOTTOM`（沿用 S1b，已驗證）
- **尺寸固定 360×420，本卡不做縮放**（需求未要求，YAGNI）

### R3. 拖曳移動

在視窗**空白處**按住左鍵可拖曳移動整個視窗。

- 不得干擾之後 U1 的內容互動：**拖曳只在非互動元素上生效**
  （實作上建議在容器層處理，並在來源是 `Button`／`TextBox`／`CheckBox` 等控制項時放行）
- 拖曳結束時把新位置寫入設定（見 R4）

### R4. 位置持久化

以 D1a 的 `AtomicJsonStore<T>` 與 `AppPaths` 儲存，檔名 `window.json`，
與 `todos.json` 同目錄。**不得自行實作檔案讀寫。**

- 內容至少含 `left`、`top`（`double`）與 `schemaVersion`
- 啟動時套用；**沒有設定檔時用預設位置**，不得因缺檔而失敗
- 寫入時機：拖曳結束、以及關閉前
- **異動要 debounce**（建議 500ms），避免拖曳過程狂寫檔

### R5. 位置合法性（**純邏輯，要測**）

`WindowPlacement` 提供一個**純函式**，輸入「儲存的視窗矩形」與「目前所有螢幕的工作區矩形」，
輸出「安全的視窗矩形」：

- 視窗與任一螢幕工作區有足夠重疊 → 原樣回傳
- 完全或幾乎不在任何螢幕上（例如螢幕被拔掉、解析度變小）→ **夾回主螢幕的可見範圍**
- 「足夠重疊」的判準要寫死並測（建議：至少 `100 x 30` 像素落在某個工作區內）

**這個函式不得依賴 WPF 或 Win32**——只吃數值、吐數值，才能測。

### R6. 單一實例

用 D1b 的 `SingleInstanceGuard`，在 `App.OnStartup` 取得。

- 取得失敗 → **安靜結束**（不要跳錯誤對話框騷擾使用者），並寫一筆 log
- guard 必須在程式結束時釋放

### R7. 關閉方式

無邊框視窗沒有標題列，必須提供關閉途徑：

- **在視窗上按右鍵顯示 context menu，含一個 `Exit` 項目**
- 保留 **Esc 關閉**

> 兩者都保留是刻意的：Esc 快，右鍵選單則是使用者找得到的正規途徑。
> 若日後要做系統匣圖示，那是另一張卡。

### R8. UI 字串一律英文，集中管理

沿用 spike 的 `App.xaml` resource 做法。

## Acceptance tests

### 自動化（Claude Code 於 sandbox 外執行）

- A1 `dotnet build -c Release --no-restore` 零錯誤、**零警告**
  （順帶修掉既有的 `TodoModels.cs` `CS8619`）
- A2 `dotnet test --no-restore` — 既有 25 個測試**全部仍通過**，加上本卡新增的
- A3 `src/` 下 `HttpClient`／`WebClient`／`Socket`／`WebView` 零命中
- A4 `DllImport`／`LibraryImport` 只在 `Interop/DesktopAttach.cs`
- A5 `src/` 下 **`SetParent`、`0x052C`、`WorkerW`、`SHELLDLL_DefView` 零命中**（R1 已完成）

### `WindowPlacement` 的單元測試（至少）

- P1 視窗完全在主螢幕內 → 原樣回傳
- P2 視窗完全在螢幕外（負座標）→ 夾回可見範圍
- P3 視窗大部分在螢幕外、重疊小於門檻 → 夾回
- P4 視窗跨兩個螢幕但重疊足夠 → 原樣回傳
- P5 只有一個螢幕且視窗比它大 → 夾到工作區左上角
- P6 螢幕清單為空 → 回傳預設位置，不擲例外

### 人工驗收（使用者）

| # | 測試 |
|---|---|
| M1 | 拖曳視窗到新位置，關閉再開啟，**位置被記住** |
| M2 | 拖曳時不會卡頓、放開後位置正確 |
| M3 | Alt+Tab 看不到它；工作列沒有它 |
| M4 | 點擊視窗後**不會浮到其他視窗之上**（S1b 的行為未回歸） |
| M5 | 右鍵選單出現且 `Exit` 可關閉；Esc 也可關閉 |
| M6 | 開第二個實例 → 安靜結束，不出現第二個視窗、不跳錯誤 |
| M7 | 關閉後桌面無殘影（bottommost 從未附著桌布層，理應無此問題） |

## Risk notes

- **拖曳與 U1 的內容互動會衝突**。本卡就要把「控制項上不觸發拖曳」處理好，
  否則 U1 做內嵌編輯時會發現點不到 TextBox。
- **不要為了記住位置而自行寫檔**——D1a 的原子寫入已經處理了斷電與毀損，繞過它等於白做。
- 儲存的位置在螢幕組態改變後可能失效，這正是 R5 存在的理由；
  **不要用 try/catch 吞掉，要用夾回**。
- 不要在本卡加入系統匣圖示、開機自啟、透明度、縮放——那些都不是目前的需求。
