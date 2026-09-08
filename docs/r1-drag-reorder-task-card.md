# R1 — 拖曳排序

> 依賴 U1／U1b／U1c。這是功能面的最後一張卡（P1 只做打包）。

## 必須先解決的衝突

W1b 的規則是「在非互動元素上拖曳 = 移動視窗」，而**項目文字正是非互動元素**。
R1 要讓拖曳項目變成排序，兩者會直接打架；而且清單塞滿時，
視窗將完全沒有可供拖曳移動的區域。

### 裁定

| 區域 | 拖曳行為 |
|---|---|
| **頂部 24px 的拖曳條**（本卡新增，永遠存在） | **移動視窗** |
| 清單項目 | **排序** |
| 清單下方的空白（清單短時才有） | 移動視窗 |
| 底部輸入框 | 不觸發任何拖曳（文字選取） |

拖曳條需與清單有可辨識的視覺區隔（略深或略淺的背景），
但**不加標題文字、不加按鈕**——它只是抓手。

## Goal

以滑鼠拖曳重新排列待辦事項，順序即時持久化。

## Files to edit

```
src/DesktopTodoWidget/MainWindow.xaml
src/DesktopTodoWidget/MainWindow.xaml.cs
src/DesktopTodoWidget/App.xaml
src/DesktopTodoWidget/Data/TodoListViewModel.cs
tests/DesktopTodoWidget.Tests/TodoListViewModelTests.cs
```

## Do not modify

`Data/AppPaths.cs`、`AtomicJsonStore.cs`、`TodoModels.cs`、`SingleInstanceGuard.cs`、
`WindowPlacement.cs`、`Interop/DesktopAttach.cs`、`App.xaml.cs`、
`CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`docs/`

## Requirements

### R1. `TodoListViewModel.Move(int fromIndex, int toIndex)`（**純邏輯，要測**）

- 使用 `ObservableCollection.Move`，不得重建集合（否則 UI 會整個閃）
- 索引越界 → **安靜忽略，不擲例外**
- `fromIndex == toIndex` → 不動作，**且不觸發存檔**
- 移動後觸發存檔通知（與其他異動一致）

### R2. 拖曳互動

- 在項目上按住左鍵並**垂直移動超過 5px** 才開始排序拖曳
  （避免單純點擊 checkbox 或雙擊編輯時誤觸）
- 拖曳中顯示**插入位置指示線**（項目之間的一條細線）
- **不需要**半透明殘影（adorner ghost）——指示線已足夠，殘影成本高且易出錯
- 放開左鍵 → 套用移動
- **拖曳中按 `Esc` → 取消排序，項目回到原位**，且**不得關閉視窗、不得進入編輯**

### R3. 邊緣自動捲動

拖曳到清單頂部或底部 **20px** 範圍內時自動捲動，讓長清單可以跨畫面移動項目。
捲動速度固定即可，不需要加速度曲線。

### R4. 不得破壞既有互動

以下全部必須維持正常：

- 點 `CheckBox` 勾選／取消
- 點 `×` 刪除
- **雙擊文字進入編輯**（拖曳判定的 5px 門檻就是為了這個）
- 底部輸入框打字與 Enter 新增
- **頂部拖曳條可移動視窗**
- 點擊視窗後不會浮到其他視窗之上（M4 迴歸）

### R5. 持久化

移動後沿用既有的 debounce 500ms + 關閉前同步落盤，**不得新增寫檔路徑**。

### R6. 已完成項目一併參與排序

已完成的項目**可以被拖曳、也可以被拖到任何位置**。
不得因為 `IsDone` 而限制其位置——使用者已明確選擇「完成的留在原位」。

## Acceptance tests

### 自動化

- A1 `dotnet build -c Release --no-restore` 零錯誤零警告
- A2 `dotnet test --no-restore` — 既有 44 個測試全過，加上本卡新增的
- A3 `src/` 下 `HttpClient`／`WebClient`／`Socket`／`WebView` 零命中

### `Move` 的單元測試（至少）

- V1 由前往後移動 → 其餘項目順序正確遞補
- V2 由後往前移動 → 同上
- V3 `fromIndex == toIndex` → 集合不變**且不觸發存檔**
- V4 索引為負或超出範圍 → 安靜忽略，集合不變，不擲例外
- V5 移動已完成的項目 → 可正常移動，`IsDone` 不變
- V6 移動後觸發存檔通知
- V7 移動第一項到最後 → 順序完全正確（邊界）

### 人工驗收（使用者）

| # | 測試 |
|---|---|
| **D1** | 拖曳項目上下移動 → 出現插入指示線 → 放開後順序改變 |
| **D2** | 關閉再開啟 → **新順序被保留** |
| **D3** | 拖曳中按 Esc → **回到原位，視窗不關閉** |
| **D4** | 清單長到需要捲動時，拖到邊緣會**自動捲動** |
| **D5** | **點 checkbox 仍能勾選**（不會誤判成拖曳） |
| **D6** | **雙擊仍能進入編輯**（不會誤判成拖曳） |
| **D7** | 點 `×` 仍能刪除 |
| **D8** | **頂部拖曳條可以移動視窗** |
| **D9** | 已完成的項目也能拖曳 |
| M4 | 點擊視窗後不會浮到其他視窗之上（迴歸） |

## Risk notes

- **5px 門檻是 D5／D6 的關鍵**。太小會讓點擊 checkbox 變成拖曳，
  太大會讓拖曳手感遲鈍。做完務必實測這兩項。
- **Esc 現在有三個語意**：關閉視窗、取消編輯、取消拖曳。
  優先序必須是「拖曳中 > 編輯中 > 關閉視窗」，寫錯會在拖曳時把程式關掉。
- 不要用 `ObservableCollection` 的移除再插入來實作移動——
  那會讓 UI 閃爍且可能讓正在編輯的項目失去狀態，一律用 `Move`。
- 不要加動畫、不要加「拖曳把手」圖示、不要做跨視窗拖放——都不是需求。
