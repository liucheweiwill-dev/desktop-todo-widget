# U1 — Todo UI：新增／刪除／勾選／編輯

> 依賴 D1a／D1b（資料層）與 W1／W1b（視窗外殼）。
> **本卡不做拖曳排序**（那是 R1）。

## 使用者已決定的兩件事

| 項目 | 決定 |
|---|---|
| 完成的項目 | **留在原位，文字加刪除線並變淡**。可取消勾選。刪除要手動做 |
| 新增方式 | **視窗頂部固定一個輸入框，打字按 Enter 新增** |

## Goal

把佔位文字換成可用的待辦清單：新增、刪除、勾選／取消勾選、編輯既有項目，
異動即存檔。

## Files to edit

```
src/DesktopTodoWidget/MainWindow.xaml
src/DesktopTodoWidget/MainWindow.xaml.cs
src/DesktopTodoWidget/App.xaml
src/DesktopTodoWidget/Data/TodoListViewModel.cs
tests/DesktopTodoWidget.Tests/TodoListViewModelTests.cs
```

## Do not modify

- `Data/AppPaths.cs`、`AtomicJsonStore.cs`、`TodoModels.cs`、
  `SingleInstanceGuard.cs`、`WindowPlacement.cs`
- `Interop/DesktopAttach.cs`
- 既有 32 個測試
- `CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/`

## Requirements

### R1. `TodoListViewModel`（**純邏輯，這是本卡的可測核心**）

所有清單操作都放這裡，**不得依賴 WPF 型別以外的東西**——
它可以用 `ObservableCollection`，但**不得碰視窗、對話框或檔案 I/O**。
持久化由呼叫端注入（見 R5）。

需提供的操作：

| 操作 | 行為 |
|---|---|
| `Add(text)` | 前後空白去除；**空字串或全空白不新增**；新項目加到**清單尾端** |
| `Remove(item)` | 移除指定項目 |
| `ToggleDone(item)` | 切換 `IsDone`，**可雙向** |
| `Rename(item, text)` | 去除前後空白；**空字串視為取消，保留原文字不變** |

- 文字長度上限 **500 字元**，超過截斷（避免單筆撐爆 UI 與檔案）
- 不得自動去除重複——使用者可能真的要兩筆一樣的

### R2. 新增：頂部固定輸入框

視窗頂部一個常駐 `TextBox`，`Enter` 新增並清空輸入框，焦點留在輸入框
（方便連續輸入多筆）。

- placeholder 提示文字用英文，例如 `Add a task…`
- 空白輸入按 Enter **不新增、不報錯、不清空**

### R3. 清單呈現

- 每列：`CheckBox` + 文字
- **已完成**：文字加**刪除線**並降低不透明度（建議 0.5），**留在原位不移動**
- 清單為空時顯示英文提示，例如 `No tasks yet.`
- 清單超出視窗高度要能**捲動**（`ScrollViewer`）

> W1b 已確保捲動容器不會擋住視窗拖曳——`ScrollViewer` 不在互動控制項清單裡，
> 但 `ScrollBar` 在。**不要為了這張卡去改那個判定集合。**

### R4. 編輯既有項目

**雙擊文字**進入編輯狀態（就地換成 `TextBox`）：

- `Enter` 或失焦 → 套用
- `Esc` → **取消編輯並還原原文字**，且**不得關閉視窗**
- 空字串 → 視為取消（見 R1）

> `Esc` 目前是關閉視窗的快捷鍵。**編輯狀態必須攔截 `Esc` 不讓它傳到視窗**，
> 否則使用者想取消編輯卻把程式關掉。這是本卡最容易出錯的地方。

### R5. 持久化

- 用 D1b 的 `TodoDocumentStore`，檔名 `todos.json`，**不得自行寫檔**
- 啟動時載入；檔案不存在 → 空清單，不得失敗
- **版本過新**（`UnsupportedSchemaVersion` 非 null）→ 顯示英文唯讀提示
  並**停用所有編輯操作**，避免覆寫使用者的資料
- 從 `.bak` 復原時（`RecoveredFromBackup`）→ 顯示一次英文提示
- 寫入時機：任何異動後 **debounce 500ms**，以及**關閉前同步落盤**

### R6. 刪除

每列提供刪除方式（右鍵選單 `Delete` 或列尾的 `×` 皆可，擇一實作並說明理由）。
**不需要確認對話框**——這是個人小工具，且誤刪成本低於每次確認的干擾。

### R7. UI 字串一律英文，集中在 `App.xaml`

## Acceptance tests

### 自動化

- A1 `dotnet build -c Release --no-restore` 零錯誤零警告
- A2 `dotnet test --no-restore` — 既有 32 個測試全過，加上本卡新增的
- A3 `src/` 下 `HttpClient`／`WebClient`／`Socket`／`WebView` 零命中

### `TodoListViewModel` 單元測試（至少）

- T1 `Add` 正常文字 → 項目加到尾端
- T2 `Add` 空字串／全空白 → **不新增**
- T3 `Add` 前後有空白 → 儲存的是去除空白後的文字
- T4 `Add` 超過 500 字元 → 截斷為 500
- T5 `Add` 兩筆相同文字 → **兩筆都在**（不去重）
- T6 `ToggleDone` 兩次 → 回到原狀態
- T7 `Remove` → 該項目消失，其餘順序不變
- T8 `Rename` 正常 → 文字更新
- T9 `Rename` 空字串 → **原文字保留不變**
- T10 已完成的項目**不改變在清單中的位置**（驗證使用者的決定）
- T11 任一異動都會通知呼叫端需要存檔（用注入的假 store 驗證被呼叫）

### 人工驗收（使用者）

| # | 測試 |
|---|---|
| U-1 | 頂部輸入框打字按 Enter → 新增到清單尾端，輸入框清空且焦點還在 |
| U-2 | 空白按 Enter → 沒事發生，不報錯 |
| U-3 | 勾選 → **文字加刪除線變淡、留在原位**；再勾一次 → 恢復 |
| U-4 | 雙擊文字 → 可編輯；Enter 套用；**Esc 取消編輯且視窗不關閉** |
| U-5 | 刪除某列 → 該列消失 |
| U-6 | 新增十幾筆 → 清單可捲動 |
| U-7 | 關閉再開啟 → **所有項目與完成狀態都在，順序不變** |
| U-8 | 中文與 emoji 可正常輸入、顯示、存檔後讀回 |
| **F1** | **拖曳時不再閃爍**（W1b 的驗收） |
| **M4** | **點一下視窗後仍不會浮到其他視窗之上**（迴歸項） |
| M1 | 拖曳後位置仍被記住（迴歸項） |

## Risk notes

- **R4 的 `Esc` 是最大的坑**：編輯中按 Esc 應該取消編輯，但目前 Esc 綁定關閉視窗。
  沒處理好會變成「想取消編輯卻關掉程式」，而且使用者剛打的字就沒了。
- **不要為了讓清單好看而自動排序**（把完成的沉到底部）——使用者已明確選擇「留在原位」，
  而且那會與 R1 階段的拖曳排序打架。
- **不要加確認對話框、動畫、優先度、到期日、分類、搜尋**——都不是目前的需求。
- 資料異動要走 debounce，但**關閉前必須同步落盤**，否則最後幾秒的輸入會遺失。
