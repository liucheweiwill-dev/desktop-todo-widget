# S1 人工驗證表

自動化部分已由 Claude Code 完成並通過。**以下八項只有在實體桌面上才能驗證。**

## 怎麼跑

發佈檔在 `D:\ai\projects\desktop-todo-widget\publish\DesktopTodoWidget.exe`
（self-contained，141MB，端機不需安裝 .NET）。

三種模式各跑一輪：

```
publish\DesktopTodoWidget.exe --mode=bottommost
```

```
publish\DesktopTodoWidget.exe --mode=workerw --target=progman
```

```
publish\DesktopTodoWidget.exe --mode=workerw --target=workerw
```

視窗是 360×420 的深色方塊，內含狀態文字、一個按鈕、一個輸入框。
關閉方式：工作管理員結束 `DesktopTodoWidget.exe`（spike 沒有做關閉鈕）。

log 在 `%LOCALAPPDATA%\DesktopTodoWidget\spike.log`（append，不會自動清）。

## 已知的環境資訊（Claude Code 實測）

- Windows build：**19045**
- 啟動時 `dpiAwareness=1`、`monitorDpi=96`
- **`workerw` 兩種 target 都能成功附著**：`progman` → `0x101FC`，
  `workerw` → 另一個 HWND（`SHELLDLL_DefView` 之後的那個 `WorkerW`）
- **`SetParent` 後 `dpiAwareness` 由 1 變成 2**——跨行程附著確實改變了 DPI awareness，
  這正是 findings.md R2 預測的風險。**E4 要特別注意這件事的後果。**
- 兩種 target 都能存活超過 8 秒（先前的 crash 已修）

## 結果（2026-09-08 使用者實測）

### bottommost：**可交付**

門檻要求的 E1／E2／E7／E8 **四項全過**。

E8 首測失敗——點一下就蓋過其他視窗。根因是只在啟動時呼叫一次
`SetWindowPos(HWND_BOTTOM)`，點擊取得 activation 後 Windows 就把它提前。
經 S1b 攔截 `WM_WINDOWPOSCHANGING` 修正後複測通過，**且 E7 未受影響**
（該修正刻意不用 `WS_EX_NOACTIVATE`，因為那會拿掉鍵盤焦點）。

### workerw / progman：**E1 與 E7 失敗**

視窗畫得出來，但**按鈕按不動、edit box 也打不進字**——輸入完全無法到達。

根因：桌面圖示層（`SHELLDLL_DefView`）蓋在附著的視窗之上，滑鼠事件在抵達 widget 之前
就被該層攔走。這也解釋了為何這個技術幾乎只被用於**動態桌布**——桌布不需要互動。

### workerw / workerw：**同樣失敗，且鍵盤也收不到**

按鈕無反應、無法輸入，**連 Esc 都無法結束程式**——證實視窗完全收不到任何輸入，
滑鼠與鍵盤皆然。

這完成了 findings.md D2 要求的「有界根因確認」：**不是選錯層的問題**。
兩個候選 target 都在圖示層之下，而圖示層攔走全部輸入。

### workerw 的額外缺陷：行程結束後殘影留在桌面

使用者以工作管理員強制結束後，**視窗的像素仍留在桌布上、位於圖示之下**，
且無法判斷程式是否仍在執行（實際已結束）。

原因是桌布層不會因子視窗消失而重繪。要清除必須重新套用桌布
（`SystemParametersInfo(SPI_SETDESKWALLPAPER, ...)`）或重啟 Explorer。

**這對一個整天常駐的 widget 是嚴重問題**：任何一次崩潰或強制結束都會在使用者桌面
留下清不掉的殘影，而使用者不會知道要怎麼處理。此缺陷與互動性無關，
**即使 `--target=workerw` 可互動，這一條依然成立。**

## 結果矩陣

| # | 測試 | bottommost | workerw / progman | workerw / workerw |
|---|---|---|---|---|
| E1 | 可見、在一般視窗之下、**按鈕可點且計數增加** | **通過** | **失敗**（按鈕無反應） | **失敗**（按鈕無反應） |
| E2 | Alt+Tab **看不到它**，taskbar 也沒有 | **通過** | | |
| E3 | Win+D 後仍可見**且按鈕仍可點** | 未測（門檻未要求） | | |
| E4 | 雙螢幕混合 DPI（100% / 150%）、拔插螢幕、改解析度 | 未測 | | |
| E5 | 工作管理員重啟檔案總管後自動恢復 | 不適用 | | |
| E6 | 桌布輪播切換後仍在（沒開就填「不適用」） | 未測 | | |
| E7 | **點輸入框後能打字、中文 IME 能組字、Ctrl+V 能貼上、切走再切回仍可輸入** | **通過** | **失敗**（無法輸入） | **失敗**（無法輸入，連 Esc 都收不到） |
| E8 | 點擊視窗使其取得焦點後，**是否被提到前景**？開關／切換／最小化其他視窗後是否仍在正確層級、仍可點、重繪正常 | **通過**（S1b 修正後） | | |

每格填 **通過 / 失敗 / 部分**，失敗請附上當時的現象描述。

## 兩題是生死題

**E7 決定 `workerw` 的生死。** 滑鼠可點不代表能打字——跨行程重設 parent 之後，
WPF `TextBox` 的 TSF／IME 行為不可假設。E7 失敗代表 `workerw` 做不出可編輯的 todo，
那條路就沒有意義了。

**E8 決定 `bottommost` 的生死。** `HWND_BOTTOM` 只代表 Z-order 最底，
不保證「一般視窗之下、桌布之上」；而且非作用中視窗一旦取得 activation，
Windows 會把它提到前方——「壓在底層」與「可以點擊」本身互相拉扯。

## 淘汰門檻（事先訂好，不事後放寬）

`workerw`（任一 target）符合下列**任一項**即判定**不可交付**：

1. E1、E3、E7 任一項失敗
2. 需要人工重啟程式或手動重掛才能恢復
3. 同一項測試重複三次結果不一致

`bottommost` 須獨立通過 E1、E2、E7、E8 才算可交付。

## 結果出來之後

- **`workerw` 可交付** → 可考慮把它設為預設模式
- **`workerw` 不可交付** → 從產品移除（不保留為「實驗選項」——
  留一個已知會壞的模式只會在日後無聲失敗），走 `bottommost`
- **兩者都不可交付** → 停下來重談「嵌入桌面」這項需求本身，不要堆 workaround

E4 或 E5 失敗時，先做一次有界的根因確認（是否選錯層、樣式切換寫錯、失聯偵測失效），
再下判定。**但不論結果如何都不要改用 Rust／Tauri／ImGui**——問題在 shell 不在語言。


---

## S1 結論（2026-09-08 定案）

### `workerw`：**不可交付，從產品移除**

兩個 target 均在 E1 與 E7 失敗，且 `--target=workerw` 連鍵盤都收不到。
根因不是選層錯誤，而是**桌面圖示層攔走全部輸入**——這是該技術的固有性質，
它之所以幾乎只被用於動態桌布，正是因為桌布不需要互動。

外加一個獨立缺陷：**行程結束後殘影留在桌布上**，且使用者無從判斷程式是否仍在執行。

依 findings.md D2 的處置規則，**判定不可交付並移除，不保留為使用者可見的實驗選項**——
留一個已知會壞的模式，只會在日後某天無聲失敗。

### `bottommost`：**可交付，成為唯一模式**

門檻要求的 E1／E2／E7／E8 四項全過（E8 經 S1b 修正後複測通過）。

### 誠實的能力邊界

**bottommost 不是「嵌入桌面」，是「一個不會浮到最上層的普通視窗」。**

- 按 Win+D 顯示桌面時，它會跟著被蓋掉，不像真的 widget 留在桌面上
- 它在桌布之上、但也在所有一般視窗之上的最底層，不在桌面圖示之下

原始需求寫的是「嵌入 Windows 桌面像個 widget」。**這一項無法達成**，
而且不是實作問題——Windows 沒有提供可互動的桌面層 API。
W1 之後的產品定義依此調整。
