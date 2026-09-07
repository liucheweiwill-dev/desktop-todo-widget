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

## 結果矩陣

| # | 測試 | bottommost | workerw / progman | workerw / workerw |
|---|---|---|---|---|
| E1 | 可見、在一般視窗之下、**按鈕可點且計數增加** | | | |
| E2 | Alt+Tab **看不到它**，taskbar 也沒有 | | | |
| E3 | Win+D 後仍可見**且按鈕仍可點** | | | |
| E4 | 雙螢幕混合 DPI（100% / 150%）、拔插螢幕、改解析度 | | | |
| E5 | 工作管理員重啟檔案總管後自動恢復 | | | |
| E6 | 桌布輪播切換後仍在（沒開就填「不適用」） | | | |
| E7 | **點輸入框後能打字、中文 IME 能組字、Ctrl+V 能貼上、切走再切回仍可輸入** | | | |
| E8 | 點擊視窗使其取得焦點後，**是否被提到前景**？開關／切換／最小化其他視窗後是否仍在正確層級、仍可點、重繪正常 | | | |

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
