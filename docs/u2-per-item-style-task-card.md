# U2 — 每個項目的字型大小與顏色

> 使用者需求：每列一個設定按鈕，可各自設定字型大小與文字顏色。
> 兩個做法由使用者拍板：**每列 ⚙ 按鈕**（非右鍵選單）、**預設色票**（非自由調色盤）。

## Goal

每個待辦項目可獨立設定文字大小與顏色，設定隨項目持久化。

## Files to edit

```
src/DesktopTodoWidget/Data/TodoModels.cs
src/DesktopTodoWidget/Data/TodoListViewModel.cs
src/DesktopTodoWidget/MainWindow.xaml
src/DesktopTodoWidget/MainWindow.xaml.cs
src/DesktopTodoWidget/App.xaml
tests/DesktopTodoWidget.Tests/TodoModelsTests.cs
tests/DesktopTodoWidget.Tests/TodoListViewModelTests.cs
```

> 檔案數超過一般上限，因為這是跨資料層與 UI 的功能。不得再增加其他檔案。

## Do not modify

`Data/AppPaths.cs`、`AtomicJsonStore.cs`、`SingleInstanceGuard.cs`、`WindowPlacement.cs`、
`Interop/DesktopAttach.cs`、`App.xaml.cs`、
`CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/`

## Requirements

### R1. 資料格式：新增兩個選用欄位，**並升到 schemaVersion 2**

`TodoItem` 新增：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `fontSize` | `double?` | `null` = 使用預設 |
| `colorKey` | `string?` | `null` = 使用預設；值為色票的鍵（見 R3） |

`TodoDocument.CurrentSchemaVersion` 由 `1` 改為 **`2`**。

**必須同步修正既有測試（否則會誤判）**：

- `TodoModelsTests.cs` 的「版本過新則拒絕載入」測試目前用 `"schemaVersion":2` 當作
  「比目前新的版本」。升版後 2 就是目前版本，**該測試必須改用 `3`**。
- 讀到 `schemaVersion: 1` 的舊檔應**照常遷移**（兩個新欄位留 `null`），不得拒絕。
  請補一條測試驗證「v1 舊檔可正常載入且項目完整」。

### R2. 字型大小

- 範圍 **10–28**，預設 **13**（維持現況外觀）
- UI 用一個小的加減步進器（`−  13  +`），每次 ±1
- **超出範圍要夾回**，不得擲例外

### R3. 顏色：固定色票，八色

在 `App.xaml` 定義具名筆刷，**全部必須在深色底上清楚可讀**：

| key | 用途 |
|---|---|
| `default` | 現有的主要文字色（`#F2F2F2`） |
| `yellow` / `orange` / `red` / `green` / `blue` / `purple` / `grey` | 其餘七色 |

- UI 呈現為一排可點的圓形色塊，目前選中的要有明顯標示
- **未知的 `colorKey`（例如手改 JSON 或未來版本）→ 退回 `default`**，不得擲例外、不得空白

### R4. 設定入口：每列一個 ⚙ 按鈕

- 位置在 `×` 刪除鈕左側
- 點擊開啟一個小 `Popup`，內含字型大小步進器、色票列、以及一個 **Reset** 鈕
  （Reset 把該項目兩個欄位都設回 `null`）
- Popup 開啟時點擊他處即關閉
- **Popup 內的互動不得觸發拖曳排序或視窗移動**

### R5. 套用到顯示

- 項目文字套用各自的 `fontSize` 與 `colorKey`
- **已完成項目的刪除線與變淡仍然要生效**，且與自訂顏色疊加後**仍需可讀**
- 字型變大導致列高改變是預期行為；**拖曳排序的插入指示線與自動捲動必須仍然正確**

### R6. 唯讀模式

schema 版本過新時（`UnsupportedSchemaVersion` 非 null），⚙ 按鈕**一併停用**。

### R7. 持久化

沿用既有的 debounce 500ms + 關閉前同步落盤，**不得新增寫檔路徑**。

## Acceptance tests

### 自動化

- A1 `dotnet build -c Release --no-restore` 零錯誤零警告
- A2 `dotnet test --no-restore` — 既有 51 個測試（含依 R1 修正過的那條）全過，加上新增的
- A3 `src/` 下 `HttpClient`／`WebClient`／`Socket`／`WebView` 零命中

### 新增單元測試（至少）

- S1 設定 `fontSize` 在範圍內 → 值被保存
- S2 設定 `fontSize` 超出 10–28 → **夾回邊界**，不擲例外
- S3 設定合法 `colorKey` → 值被保存
- S4 設定未知 `colorKey` → **解析為 default**，不擲例外
- S5 Reset → 兩個欄位皆回 `null`
- S6 任一樣式異動 → **觸發存檔通知**
- S7 樣式往返：寫檔再讀回，`fontSize` 與 `colorKey` 完全一致
- S8 **v1 舊檔載入** → 項目完整，兩個新欄位為 `null`
- S9 **`schemaVersion: 3`（比目前新）→ 拒絕載入且原檔未被改動**（沿用既有防護，值改為 3）

### 人工驗收（使用者）

| # | 測試 |
|---|---|
| **P1** | 點 ⚙ → 出現設定 popup；點他處會關閉 |
| **P2** | 調字型大小 → 該列文字立即變大／變小，**其他列不受影響** |
| **P3** | 選顏色 → 該列文字變色，**八色在深色底上都讀得清楚** |
| **P4** | Reset → 回到預設大小與顏色 |
| **P5** | **關閉再開 → 每項的大小與顏色都被記住** |
| **P6** | 已完成項目 + 自訂顏色 → 刪除線與變淡仍生效，**且仍讀得出來** |
| **P7** | 字型調大後，**拖曳排序仍正常**（插入線位置正確、自動捲動正常） |
| D5 | 點 checkbox 仍能勾選（迴歸） |
| D6 | 雙擊仍能進入編輯（迴歸） |
| D8 | 頂部拖曳條仍能移動視窗（迴歸） |

## Risk notes

- **R1 的測試修正是必做的**：不改的話「版本過新」那條會拿目前版本當未來版本，
  變成永遠拒絕載入自己寫的檔——**那會讓程式完全無法讀取資料**。
- **⚙ 會再吃掉約 30px 的文字寬度**，這是使用者知情後的選擇；
  不要為了補救而縮小字型或改變版面比例。
- **色票必須全部在深色底上可讀**——這個專案已經因為黑底黑字修過兩輪，
  不要提供會重現該問題的顏色。
- Popup 內的滑鼠事件若沒擋好，會觸發拖曳排序或視窗移動。R4 最後一條就是為此。
- 不要加字型家族選擇、粗體斜體、背景色、每項圖示——都不是需求。
