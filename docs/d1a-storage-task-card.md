# D1a — 路徑解析與原子 JSON 儲存

> D1 拆成 D1a／D1b 兩張卡，因為單張的檔案數會超過上限、且原子寫入這塊值得獨立測試。
> **本卡不碰 todo 模型與 UI**，只做可重用的儲存基礎。

## Goal

建立測試專案，並實作兩個純邏輯元件：資料目錄解析、原子 JSON 讀寫。
兩者之後由 todo 資料（D1b）與視窗狀態（W1）共用。

## Files to edit

```
tests/DesktopTodoWidget.Tests/DesktopTodoWidget.Tests.csproj
tests/DesktopTodoWidget.Tests/AppPathsTests.cs
tests/DesktopTodoWidget.Tests/AtomicJsonStoreTests.cs
src/DesktopTodoWidget/Data/AppPaths.cs
src/DesktopTodoWidget/Data/AtomicJsonStore.cs
```

若測試專案需要參照主專案而必須調整 `DesktopTodoWidget.csproj`，
**允許只加 `<ItemGroup>` 的必要設定**，不得改動既有 property。

## Do not modify

- `src/DesktopTodoWidget/MainWindow.*`、`App.*`、`Interop/DesktopAttach.cs`
- `CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/` 下任何檔案

## Requirements

### R1. 測試專案

`net10.0-windows`，xUnit。**套件已由 Claude Code 事前還原至本機快取**，
所有指令請加 `--no-restore`；若出現 `NU1301` 或任何網路錯誤，**停下來回報**，
不要改用其他測試框架或修改 NuGet 設定。

### R2. `AppPaths`

解析資料目錄，優先序如下：

1. **Portable 模式**：exe 同資料夾存在名為 `portable.marker` 的檔案 → 用 exe 同資料夾
2. **預設**：`%LOCALAPPDATA%\DesktopTodoWidget\`

規則：

- 必須提供可注入的方式讓測試指定「exe 所在目錄」與「LocalAppData 根目錄」，
  **測試不得碰使用者真實的 `%LOCALAPPDATA%`**。
- 目錄不存在時要能建立。
- **Portable 目錄無寫入權限時，必須退回預設位置並回報這件事**（不得靜默失敗、
  也不得直接拋例外讓程式掛掉）。回退行為要可被測試觀察到。
- 只回傳這兩種位置之一，**不得有第三個寫入位置**（AGENTS.md Safety）。

### R3. `AtomicJsonStore`

泛型的 JSON 讀寫，使用 `System.Text.Json`，UTF-8 無 BOM。

**寫入序列**：

1. 寫入同資料夾的暫存檔（例如 `<name>.tmp`）
2. flush 到磁碟
3. **目標檔已存在** → 以同 volume 的原子替換取代，並保留 `<name>.bak`
4. **目標檔不存在（首次寫入）** → 直接移動就位，不嘗試替換

> 第 4 點是關鍵：`File.Replace` 在目標檔不存在時會擲例外，
> **必須有獨立的首次建立路徑**（findings.md D4）。

**讀取行為**：

- 檔案不存在 → 回傳預設值（不擲例外）
- 內容毀損／無法解析 → **嘗試讀 `.bak`**；`.bak` 也不行才回傳預設值，
  並且要能讓呼叫端知道發生過回復（不要靜默）
- 讀取不得修改磁碟上的檔案

### R4. 不得引入任何 NuGet 套件到主專案

測試專案的 xUnit 相關套件是唯一例外，且它**不隨成品出貨**。

## Acceptance tests

xUnit，**必須測行為不是測方法存在**。至少涵蓋：

**`AppPaths`**

- A1 無 marker 檔 → 回傳 LocalAppData 下的 `DesktopTodoWidget` 路徑
- A2 有 marker 檔 → 回傳 exe 同資料夾
- A3 目錄不存在時會被建立
- A4 portable 目錄不可寫 → **退回預設位置且該事實可被觀察**

**`AtomicJsonStore`**

- A5 **首次寫入**（目標檔不存在）成功，且內容可正確讀回 ← 最容易漏的一條
- A6 覆寫既有檔成功，且 **`.bak` 內含前一版內容**
- A7 讀取不存在的檔 → 回傳預設值、不擲例外
- A8 目標檔內容毀損但 `.bak` 完好 → 從 `.bak` 成功回復，且呼叫端可得知
- A9 目標檔與 `.bak` 皆毀損 → 回傳預設值、不擲例外
- A10 寫入後暫存檔 `.tmp` **不殘留**
- A11 中文與 emoji 內容往返後完全一致（UTF-8 正確性）

所有測試**必須使用暫存目錄**，不得觸及使用者真實的 `%LOCALAPPDATA%`，
且測試結束要清理。

## Commands to run

```
dotnet build -c Release --no-restore
```

```
dotnet test --no-restore
```

兩者都必須貼真實輸出。`dotnet test` 需顯示通過數。

## Risk notes

- **首次寫入路徑是本卡最可能出錯的地方**（`File.Replace` 對不存在的目標會失敗）。
  A5 必須真的驗證這條，不要只測覆寫。
- 不要為了讓測試好寫而把檔案 I/O 抽象成介面再 mock——
  **直接用暫存目錄測真實檔案行為**，那才是這段程式碼的風險所在。
- 原子替換能降低斷電毀損風險，但不能宣稱絕對安全；`.bak` 是第二道防線。
- 不要在本卡引入 todo 模型、UI 或任何 Win32 呼叫。
