# U1b — 深色主題貫穿所有控制項

> 起因：2026-09-08 人工驗證 U-1～U-8 全數通過，但使用者回報
> **黑底黑字，要滑鼠移過去才看得到字**。

## 根因

視窗設了 `Background="#1E1E1E"` 與 `Foreground="#F2F2F2"`，但 WPF 的
`TextBox`／`CheckBox`／`ListBoxItem`／`MenuItem` 等控制項**各自帶預設樣板**，
樣板內的前景色來自系統預設（黑色），不會繼承視窗的 `Foreground`。

「hover 才看得到」是決定性線索：hover 狀態套用了系統高亮筆刷才讓文字浮現，
代表**非 hover 的常態配色是錯的**。

## Goal

讓**所有文字在不需要 hover 的情況下都清楚可讀**，且各種狀態
（一般／hover／選取／焦點／停用／已完成）都維持足夠對比。

## Files to edit

```
src/DesktopTodoWidget/App.xaml
src/DesktopTodoWidget/MainWindow.xaml
```

僅在確實必要時才可動 `MainWindow.xaml.cs`，並說明理由。

## Do not modify

`Data/` 下任何檔案、`Interop/`、`tests/`、`App.xaml.cs`、
`CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/`

## Requirements

### R1. 色彩集中定義在 `App.xaml`

以具名 `SolidColorBrush` 資源定義調色盤，**不得散落在各處寫死色碼**。
至少需要：視窗背景、主要文字、次要／提示文字、已完成文字、
輸入框背景、輸入框邊框、focus 邊框、hover 背景、選取背景、分隔線。

沿用既有深色基調（背景 `#1E1E1E`、主要文字 `#F2F2F2`）。

### R2. 逐一覆寫會渲染文字的控制項

**每一個**都要明確設定 `Foreground` 與 `Background`，不得依賴繼承：

| 控制項 | 位置 |
|---|---|
| 頂部新增用的 `TextBox` | 含 placeholder 提示文字 |
| 就地編輯的 `TextBox` | 雙擊項目後出現的那個 |
| `CheckBox` 的文字內容 | 清單每一列 |
| 清單項目的 `TextBlock` | 含已完成的刪除線樣式 |
| 空清單提示 | `No tasks yet.` |
| 唯讀提示 | schema 版本過新時顯示的訊息 |
| `ContextMenu` 與 `MenuItem` | 右鍵選單的 `Exit`、以及刪除選單（若採此做法） |
| `ScrollBar` | 深色底上的捲軸不得是亮色系統預設 |

### R3. 狀態配色

- **hover**：背景略亮，文字維持可讀。**不得依賴系統高亮筆刷**
- **選取／焦點**：`TextBox` 需明確設定 `CaretBrush` 與 `SelectionBrush`
  （深色底上的黑色游標看不見）
- **停用**（唯讀模式）：文字要變淡但**仍需可讀**，不得變成幾乎不可見
- **已完成項目**：刪除線 + 降低不透明度，但**仍需清楚可讀**

### R4. 不得引入主題框架或控制項套件

不裝 MahApps、HandyControl 或任何第三方樣式庫（CLAUDE.md 禁用清單）。
只用內建的 `Style`／`ControlTemplate`／`Setter`。

### R5. 不改變任何行為

本卡**純外觀**。不得更動新增／刪除／勾選／編輯／拖曳／存檔的任何邏輯，
不得改動 `TodoListViewModel`。

## Acceptance tests

### 自動化（Claude Code 於 sandbox 外執行）

- A1 `dotnet build -c Release --no-restore` 零錯誤零警告
- A2 `dotnet test --no-restore` **44 個測試全數通過**（本卡不應影響任何測試）
- A3 `MainWindow.xaml` 與 `App.xaml` 之外的檔案無變更（除非已說明理由）

### 人工驗收（使用者）

| # | 測試 |
|---|---|
| **C1** | **不移動滑鼠的情況下，清單所有項目文字都清楚可讀** ← 本卡的目的 |
| C2 | 頂部輸入框的提示文字可讀；打字時文字與**游標**都看得見 |
| C3 | 已完成項目：刪除線 + 變淡，但**仍讀得出來寫什麼** |
| C4 | 滑鼠移過項目時不會變得更難讀 |
| C5 | 雙擊編輯時，`TextBox` 內的文字、游標、選取範圍都清楚 |
| C6 | 右鍵選單文字可讀（不是黑底黑字） |
| C7 | 項目多到需要捲動時，捲軸在深色底上不突兀 |
| C8 | U-1～U-8 的行為**完全未變**（迴歸） |

## Risk notes

- **最容易漏掉的是狀態配色**：常態修好了，但 hover／選取／停用仍用系統預設，
  換個 Windows 佈景主題就又壞掉。每個狀態都要明確設定。
- **`CaretBrush` 很容易忘**——深色背景上預設的黑色游標等於看不見，
  使用者會以為輸入框壞了。
- 不要為了省事把整個視窗改成淺色底，使用者選的是深色 widget。
- 不要順手調整版面、間距、字體大小——本卡只處理顏色可讀性。
