# S1c — 關閉時自動恢復桌面顯示

> 起因：2026-09-08 人工驗證期間，以工作管理員強制結束 `workerw` 模式後，
> **視窗像素仍留在桌布上、位於圖示之下**，且無法判斷程式是否仍在執行。
> 使用者要求關閉時自動恢復顯示。

## 根因

視窗 `SetParent` 到 Progman／WorkerW 之後成為桌布層的子視窗。
該層**不會因子視窗消失而自動重繪**，因此像素殘留。

## Goal

`workerw` 模式在**正常關閉**與**退回 bottommost** 時，主動讓桌布層重繪，不留殘影。

## Files to edit

```
src/DesktopTodoWidget/MainWindow.xaml.cs
src/DesktopTodoWidget/Interop/DesktopAttach.cs
```

## Do not modify

`App.xaml`、`App.xaml.cs`、`MainWindow.xaml`、`src/DesktopTodoWidget/Data/`、
`tests/`、`CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/`

## Requirements

### R1. 新增 `DesktopAttach.RequestDesktopRepaint(IntPtr parent, Action<string> writeLog)`

以 `RedrawWindow` 讓桌布層重繪：

```
RedrawWindow(parent, IntPtr.Zero, IntPtr.Zero,
             RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW)
```

常數：`RDW_INVALIDATE=0x0001`、`RDW_ERASE=0x0004`、
`RDW_ALLCHILDREN=0x0080`、`RDW_UPDATENOW=0x0100`。

- **不要**用 `SystemParametersInfo(SPI_SETDESKWALLPAPER, ...)`——那會改寫使用者的桌布設定，
  副作用太大。
- `parent` 為 `IntPtr.Zero` 或已失效時直接跳過並寫 log，不得擲例外。
- 成功與否都寫一筆 log。

### R2. 呼叫時機

在**已從 parent 卸離之後**才呼叫（順序：`SetParent(hwnd, IntPtr.Zero)` → 重繪舊 parent）：

1. **視窗關閉時**（`Closed`，含 Esc 關閉）——若當時處於 workerw 附著狀態
2. **`WorkerwFailed` 退回 bottommost 時**——`RestoreTopLevelStyle` 之後

bottommost 模式從未附著到桌布層，**不得**呼叫。

### R3. 必須記住曾附著的 parent

關閉時 `_workerwParent` 可能已被清空。請保留一份「最後一次成功附著的 parent」
供關閉時重繪使用。

### R4. 誠實的限制說明

在 `RequestDesktopRepaint` 上方加註解說明：**此機制只在行程正常結束時有效**，
被強制結束（工作管理員／崩潰）時程式碼不會執行，殘影仍會留下。
**這是此技術的固有限制，不得宣稱已完全解決。**

## Acceptance tests

### 自動化（Claude Code 於 sandbox 外執行）

- A1 `dotnet build -c Release --no-restore` 零錯誤
- A2 `dotnet test --no-restore` 既有 25 個測試全數通過
- A3 `DllImport`／`LibraryImport` 仍只在 `Interop/DesktopAttach.cs`

### 人工（使用者）

- M1 `--mode=workerw --target=<任一>` 啟動後按 **Esc** 關閉 → **桌面無殘影**
- M2 bottommost 模式行為不變（E1／E2／E7／E8 不得回歸）

## Risk notes

- **強制結束仍會留殘影**，這條無法從行程內解決，不要嘗試。
- `RedrawWindow` 對整個桌布層重繪可能造成一次閃爍，可接受。
- 不要為此引入 timer 或背景執行緒定期重繪——那是為了掩蓋問題而增加複雜度。
