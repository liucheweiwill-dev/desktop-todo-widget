# D1b — Todo 資料模型、schema 版本、單一實例

> 依賴 **D1a**（`AppPaths`、`AtomicJsonStore`）。本卡只做資料層，**不碰 UI 與 Win32**。

## Goal

定義 todo 的資料模型與 schema 版本策略，並實作單一實例保護。

## Files to edit

```
src/DesktopTodoWidget/Data/TodoModels.cs
src/DesktopTodoWidget/Data/SingleInstanceGuard.cs
tests/DesktopTodoWidget.Tests/TodoModelsTests.cs
tests/DesktopTodoWidget.Tests/SingleInstanceGuardTests.cs
```

## Do not modify

- `src/DesktopTodoWidget/MainWindow.*`、`App.*`、`Interop/DesktopAttach.cs`
- D1a 產出的 `AppPaths.cs`、`AtomicJsonStore.cs`（若確有必要調整，先停下來說明理由）
- `CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/` 下任何檔案

## Requirements

### R1. 資料模型

```
TodoDocument
  schemaVersion : int      // 目前為 1
  items         : TodoItem[]

TodoItem
  id        : Guid
  text      : string
  isDone    : bool
  createdUtc: DateTimeOffset
```

### R2. 排序用陣列順序，**不得加 order 欄位**

**項目順序 = `items` 陣列中的位置。** 不要新增 `Order`／`Index`／`Position` 欄位。

理由：多一個排序欄位就多一個真實來源，會產生重複索引、索引跳號、
以及「陣列順序與欄位不一致」三種 bug，而它們都得靠額外的正規化程式碼去修。
拖曳排序直接在陣列裡搬移元素即可。**這條是刻意的簡化，不是疏漏。**

### R3. schema 版本策略

- 存檔時一律寫入目前的 `schemaVersion`。
- 讀到**較舊**版本 → 執行遷移後照常使用（目前只有版本 1，先留好遷移進入點即可）。
- 讀到**較新**版本 → **拒絕載入，且不得覆寫該檔案**。
  必須讓呼叫端明確得知是「版本過新」而不是「檔案毀損」。

> 第三點是防資料遺失：若使用者用新版寫過資料、又用舊版開啟，
> 靜默覆寫會直接毀掉他的待辦清單。

- 缺漏或為 null 的欄位要容錯（給合理預設），不要因為多一個未知欄位就整份拒絕。

### R4. `SingleInstanceGuard`

以 named mutex 實作，session 範圍（預設的 `Local\` 前綴即可）。

- 取得成功 → 可執行；已被佔用 → 回報「已有實例在執行」，**不得擲例外當作流程控制**。
- 必須可釋放，且釋放後可重新取得。
- 必須 `IDisposable`，且 dispose 時確實釋放。
- **已知限制**：未設 ACL，同機其他使用者可能干擾。單人自用工具接受此限制，
  在程式碼註解中寫明即可（findings.md D4）。

### R5. 不得引入任何 NuGet 套件

## Acceptance tests

xUnit，測行為不測方法存在。至少涵蓋：

**`TodoDocument` / `TodoItem`**

- A1 往返：寫出再讀回，items 內容與**順序**完全一致
- A2 存檔一定含 `schemaVersion`
- A3 **讀到較新版本 → 拒絕載入，且原檔內容未被改動**（讀完再比對檔案位元組）
- A4 讀到含未知額外欄位的 JSON → 仍能載入，不擲例外
- A5 缺漏欄位（如缺 `isDone`）→ 用合理預設載入
- A6 中文與 emoji 的 `text` 往返後完全一致
- A7 空的 items 陣列可正常往返
- A8 在陣列中搬移元素後儲存再讀回，順序符合預期（驗證 R2 的排序方式可用）

**`SingleInstanceGuard`**

- A9 第一個取得成功
- A10 同一 mutex 名稱的第二個取得失敗，且**不擲例外**
- A11 第一個釋放後，第二個可成功取得
- A12 dispose 後確實釋放（可再次取得）

測試一律使用暫存目錄與**隨機 mutex 名稱**，不得干擾實際執行中的程式。

## Commands to run

```
dotnet build -c Release --no-restore
```

```
dotnet test --no-restore
```

`dotnet test` 必須跑**完整測試專案**（含 D1a 的測試），不得只跑本卡新增的測試檔，
並貼出真實的通過數。

## Risk notes

- **A3 是本卡最重要的一條**：版本過新時靜默覆寫會直接毀掉使用者資料。
  必須真的比對檔案未被改動，不能只檢查回傳值。
- 不要為了「更完整」而自行加入 `Order`、`ModifiedUtc`、`Tags`、`DueDate` 等欄位。
  需求沒有要，多加就要多維護與多測（CLAUDE.md：不做超出當前任務的抽象）。
- named mutex 的 ACL 限制是已知且接受的，不要為此設計複雜的跨使用者機制。
