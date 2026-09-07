# S1 — Spike：桌面附著

> **v2（2026-09-07）**：已納入 Codex 規劃 review 的 6 項（4 BLOCKING / 2 MAJOR），全數採納。
> 逐項理由見 `findings.md` §Review。

> **這張卡的產出不是功能，是一個決策。** 它要回答：`workerw` 是否**可交付**？
> **不要在這張卡裡寫任何 todo 功能、資料儲存或 JSON。**

## Goal

建立最小 WPF 專案骨架，實作**三種**附著目標，以 **self-contained 發佈檔**在真實端機上
完成七項驗證，並依**事先訂好的淘汰門檻**產出「可交付／不可交付」的結論。

| 模式 | 參數 | 做法 |
|---|---|---|
| bottommost（**預設**） | `--mode=bottommost` | 無邊框 top-level window，以 `SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE)` 壓到 Z-order 最底 |
| workerw / progman | `--mode=workerw --target=progman` | `SetParent` 到 `Progman` |
| workerw / workerw | `--mode=workerw --target=workerw` | `SetParent` 到桌布層的 `WorkerW`（見下方選層契約） |

## Files to edit

```
src/DesktopTodoWidget/DesktopTodoWidget.csproj
src/DesktopTodoWidget/App.xaml
src/DesktopTodoWidget/App.xaml.cs
src/DesktopTodoWidget/MainWindow.xaml
src/DesktopTodoWidget/MainWindow.xaml.cs
src/DesktopTodoWidget/Interop/DesktopAttach.cs
```

> 檔案數超過一般 5 個上限，因為這是新專案 scaffolding。**不得再增加其他檔案。**

## Do not modify

`CLAUDE.md`、`AGENTS.md`、`findings.md`、`task_plan.md`、`LICENSE`、`docs/` 下任何檔案。

## Requirements

### R1. 專案設定

TFM `net10.0-windows`、`UseWPF=true`、RID `win-x64`、`Nullable=enable`、
`ImplicitUsings=enable`。**不得**設 `PublishTrimmed` 或 `PublishAot`。**不得引入任何 NuGet 套件。**

### R2. 視窗外觀（**不做透明**）

無邊框（`WindowStyle=None`、`ResizeMode=NoResize`、**`AllowsTransparency=false`**）、
固定 360×420、**不透明**深色背景。

> v1 曾同時要求「半透明」與 `AllowsTransparency=false`，兩者矛盾。
> S1 一律不透明；真正的透明需求延到 W1 再談。

內容需要三個元件，各有明確驗證目的：

| 元件 | 目的 |
|---|---|
| 一行狀態文字 | 顯示目前模式與附著結果 |
| 一個按鈕，點擊後把文字改為 `clicked N`（N 為累計次數） | 驗證附著後仍能接收滑鼠事件 |
| **一個單行 `TextBox`** | **驗證附著後仍能取得鍵盤焦點與輸入法**（見 E7） |

### R3. Alt+Tab 排除

`ShowInTaskbar=false`；在 **`SourceInitialized`** 取得 HWND 後加 `WS_EX_TOOLWINDOW`、
清除 `WS_EX_APPWINDOW`，再 `SetWindowPos(..., SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER)`。
**不得**建立假的 owner window。

`workerw` 模式下視窗成為 child 後本來就不列入 Alt+Tab；`WS_EX_TOOLWINDOW` 仍要保留，
作為退回 top-level 時的保護。

### R4. `WorkerW` 選層契約（**v1 缺這段，實作者會亂猜**）

`--target=workerw` 時，依下列順序取得目標，**每一步都要寫 log**：

1. `FindWindow("Progman", null)` 取得 Progman。
2. 對 Progman 送訊息 `0x052C`（`wParam=0`、`lParam=0`），觸發桌布層 `WorkerW` 生成。
3. `EnumWindows` 找出**含有 `SHELLDLL_DefView` 子視窗**的那個 top-level 視窗。
4. 目標 = 該視窗的**下一個同層 `WorkerW` 兄弟視窗**（`FindWindowEx` 以 `WorkerW` 類別接續列舉）。
5. 找不到就記錄 `workerw target not found` 並依 R7 處理。

`--target=progman` 時直接以步驟 1 的 Progman 為目標。

### R5. Child 樣式切換（**v1 遺漏，`SetParent` 不會自動處理**）

`SetParent` 成功後必須：

1. 清除 `WS_POPUP`、加上 `WS_CHILD`
2. `SetWindowPos(..., SWP_FRAMECHANGED)` 讓樣式生效
3. **座標改為 parent-relative** 重新定位
4. 記錄 `SetParent` 前後的 DPI awareness 與視窗矩形——
   跨行程 `SetParent` 可能強制重設子行程的 DPI awareness

### R6. 失聯偵測與重掛

`workerw` 模式以 2 秒 timer 檢查父 HWND 是否仍 `IsWindow`；失效時重跑 R4 並重掛，
每次事件寫一筆 log（含第幾次重掛）。

### R7. 失敗不得靜默

`workerw` 若找不到目標或 `SetParent` 失敗，**不得靜默退回**：
必須記錄明確原因，且視窗狀態文字顯示 `workerw FAILED (<reason>) — running as bottommost`，
之後才退回 bottommost。

### R8. 診斷 log

寫入 `%LOCALAPPDATA%\DesktopTodoWidget\spike.log`（UTF-8，append）。
每次啟動至少記錄：時間戳、模式與 target、Windows build、主視窗 HWND、
找到的父 HWND 與其類別名、`SetParent` 成功與否與 `GetLastError`、
樣式切換前後的 style/exstyle、DPI awareness 與目前螢幕的 DPI、以及每次重掛事件。
**不得寫入其他任何位置。**

## Acceptance tests

### 自動化

- **A1** `dotnet build -c Release` 無錯誤。
- **A2** `src/` 下 `HttpClient`、`WebClient`、`Socket`、`WebView` **零命中**（Safety：零網路）。
- **A3** `DllImport`／`LibraryImport` **只**出現在 `Interop/DesktopAttach.cs`。
- **A4** `dotnet publish -c Release -r win-x64 --self-contained true` 成功產出資料夾。

### 人工 exit gate（**這才是本卡重點**）

**必須以 A4 產出的 self-contained 發佈資料夾執行，不得用 `dotnet run`**
——端機沒有 .NET runtime，`dotnet run` 測到的不是真實條件。

三種模式**各跑一輪**，逐項記錄。每項需記錄：通過／失敗／部分、實際觀察現象、
以及失敗時的 `spike.log` 片段。

| # | 測試 | 判定重點 |
|---|---|---|
| **E1** | 基本可見與可互動 | 視窗可見、位於一般應用視窗之下、**按鈕可點且計數增加** |
| **E2** | Alt+Tab | widget **不出現**在切換器與 taskbar |
| **E3** | Win+D | 顯示桌面後 widget 仍可見**且按鈕仍可點**（不可點代表掛錯圖示層） |
| **E4** | 多螢幕與混合 DPI | 雙螢幕（一台 100%、一台 150%），拔插螢幕或改解析度後位置、大小、可點性是否正常。**最可能失敗的一項** |
| **E5** | Explorer 重啟 | 以工作管理員重啟檔案總管後是否自動恢復附著（`workerw` 應觸發重掛並寫 log） |
| **E6** | 桌布輪播 | 等一次切換後 widget 仍在（未開輪播記「不適用」） |
| **E7** | **鍵盤焦點與輸入法** | 點 `TextBox` 後：英文輸入、**中文 IME 組字與候選字**、`Ctrl+V` 貼上、切到別的視窗再切回來後仍可輸入。**Explorer 重啟後與 DPI 切換後各重做一次** |
| **E8** | **bottommost 的 Z-order 矛盾** | 點擊 widget 使其取得 activation 後，**它是否被 Windows 提到前景**？開啟／切換／最小化一般視窗後，widget 是否仍在正確層級、仍可點、重繪是否正常 |

> **E7 是 `workerw` 的生死題。** 滑鼠可點不代表能打字——跨行程重設 parent 後，
> WPF `TextBox` 的 TSF／IME 行為不可假設。E7 失敗代表 `workerw` 做不出可編輯的 todo，
> 整條路白走。
>
> **E8 是 `bottommost` 的生死題。** `HWND_BOTTOM` 只代表 Z-order 最底，
> **不保證**「一般視窗之下、桌布之上」；而且非作用中視窗一旦取得 activation，
> Windows 會把它提到前方——「壓在底層」與「可以點」本身就互相拉扯。

### 淘汰門檻（**事先訂好，不得事後放寬**）

`workerw`（任一 target）符合下列**任一項**即判定**不可交付**：

1. E1、E3、E7 任一項失敗
2. 需要人工重啟程式或手動重掛才能恢復
3. 同一項測試重複三次結果不一致

`bottommost` 同樣須獨立通過 E1、E2、E7、E8 才算可交付。

## Commands to run

```
dotnet build -c Release
```

```
dotnet publish -c Release -r win-x64 --self-contained true
```

然後從發佈資料夾執行三輪：

```
.\DesktopTodoWidget.exe --mode=bottommost
```

```
.\DesktopTodoWidget.exe --mode=workerw --target=progman
```

```
.\DesktopTodoWidget.exe --mode=workerw --target=workerw
```

（本階段尚無測試專案，`dotnet test` 自 D1 起納入。）

## 回報格式

除標準 Summary / Tests / Risk 外，Tests 段必須包含：

- 端機的 **Windows build 號**、螢幕組態與各螢幕 DPI 縮放比例
- 實際使用的 publish 指令與發佈資料夾大小
- E1–E8 × 三種模式的結果矩陣
- 每個失敗項的 `spike.log` 片段
- **依淘汰門檻得出的結論**：每種模式「可交付／不可交付」，以及理由

## Risk notes

- **`workerw` 無官方支援**，倚賴未文件化的 shell 視窗結構與 `0x052C` 訊息。
  **失敗是合法且有價值的結果**，不要為了讓它通過而堆疊 workaround。
- 混合 DPI 下的跨行程 `SetParent` 可能失敗或強制改變 DPI awareness。E4／E7 若失敗直接記錄。
- **不要因為結果不好就改用 Rust／Tauri／ImGui**——問題在 shell 不在語言。
- 若兩種模式都不可交付，**不要繼續堆 workaround**，停下來回報；
  那代表「嵌入桌面」這項產品需求本身要重談。
