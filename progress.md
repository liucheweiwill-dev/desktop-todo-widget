# Progress

## 現況總覽（2026-09-07）

| | |
|---|---|
| 語言 / UI | C# / .NET 10 / WPF，`win-x64` self-contained |
| 測試 | **25 passed / 0 failed** |
| 發佈體積 | 141MB（未壓縮，self-contained） |
| 依賴 | 主專案 **0 個 NuGet 套件**；測試專案 xUnit（不隨成品出貨） |
| 端機需求 | 無——不需 .NET Runtime、VC++ Redist、WebView2 |

## 階段狀態

| 階段 | 內容 | 狀態 |
|---|---|---|
| **S1** | 桌面附著 spike | **自動化部分完成，等待人工驗證** |
| **D1a** | 路徑解析、原子 JSON 儲存 | **完成**（11 tests，含 mutation probe） |
| **D1b** | Todo 模型、schema 版本、單一實例 | **完成**（14 tests，含 mutation probe） |
| W1 | 視窗外殼 | **被 S1 人工驗證阻擋** |
| U1 / R1 / P1 | Todo UI / 拖曳排序 / 打包 | 未開始 |

---

## S1 — 桌面附著 spike（自動化部分）

### 已通過

| # | 驗收 | 結果 |
|---|---|---|
| A1 | `dotnet build -c Release` | 0 錯誤 0 警告 |
| A2 | `src/` 無 `HttpClient`／`WebClient`／`Socket`／`WebView` | 零命中 |
| A3 | P/Invoke 只在 `Interop/DesktopAttach.cs` | 15 個宣告全部在該檔 |
| A4 | `dotnet publish --self-contained` | 成功，141MB |

### 實測拿到的早期證據

以發佈檔實跑三種模式，`spike.log` 顯示：

```
windowsBuild=19045
啟動時 dpiAwareness=1 monitorDpi=96
--target=progman  → SetParent success=True  parent=0x101FC (Progman)
--target=workerw  → SetParent success=True  parent=0x3907A8 (WorkerW)
style 0x6080000 → 0x46080000    （WS_CHILD 正確加上）
dpiAwareness 1 → 2               （跨行程 SetParent 改變了 DPI awareness）
```

**兩種選層都能成功附著**，三步驟契約（Progman → `0x052C` → `SHELLDLL_DefView` host
→ 下一個 `WorkerW`）可用。

**`dpiAwareness` 由 1 變 2 是 findings.md R2 預測的風險，現在有實證。**
E4（混合 DPI 多螢幕）要特別注意這件事的後果。

### 待人工驗證

E1–E8 共八項，只有在實體桌面上才能做。清單與淘汰門檻見
`docs/s1-manual-gate.md`。其中 **E7（IME）是 `workerw` 的生死題**，
**E8（Z-order／activation）是 `bottommost` 的生死題**。

---

## Review 抓到的缺陷

### S1：`IsWindowNative` 指向不存在的匯出（BLOCKING）

```csharp
[DllImport("user32.dll")]              // 沒有 EntryPoint
private static extern bool IsWindowNative(IntPtr window);
```

user32.dll 沒有 `IsWindowNative` 這個匯出，runtime 會照受管方法名去找而擲
`EntryPointNotFoundException`。它由 2 秒的失聯偵測 timer 呼叫，
**workerw 附著成功後約 2 秒程式必定崩潰**——而 bottommost 模式碰不到這條路徑，
所以不會在一般啟動時顯現。

**沒有憑閱讀下判斷**：以發佈檔實跑 `--mode=workerw`，7 秒後程序已結束、
exit code `-532462766`（`0xE0434352`，CLR 未處理例外），log 停在 `workerw attach complete`。
修正後兩種 target 都能存活超過 8 秒。

### S1：`WsPopup` sign-extend（MINOR）

`private const nint WsPopup = -2147483648;` 在 x64 會 sign-extend 成
`0xFFFFFFFF80000000`，使 `RestoreTopLevelStyle` 的 `| WsPopup` 設到無關的高位元。

Codex 的修正 `unchecked((nint)0x80000000)` **本身編譯不過**（`CS0133`：
`nint` 大小依平台而定，不是編譯期常數），由 Claude Code 改為 `static readonly`。

---

## 事故：Codex 在此 sandbox 內無法建置（已改變角色分工）

即使 `--add-dir` 全給對、restore 也已預先完成，MSBuild 仍被拒絕寫入
`obj/`（`MC1000`／`MSB4018`）與工作區內新建的 `.build-tmp/`（`MSB3491`／`MSB3191`）。

**後果：Codex 交出的每一份程式碼都是未經編譯的。** 實際造成四個編譯錯誤：

| 錯誤 | 檔案 |
|---|---|
| `CS0051` 可及性不一致 | `MainWindow.xaml.cs` |
| `CS0133` 非編譯期常數 | `DesktopAttach.cs` |
| `CS0103` 缺 `using System.IO;` | `AppPaths.cs`、`AtomicJsonStore.cs` |
| `CS0246` 缺 `using Xunit;` | 兩個測試檔 |

處置：角色分工調整為 **Codex 撰寫、Claude Code 建置與執行測試並回饋結果**，
編譯錯誤由 Claude Code 直接修。詳見 findings.md D7.1，
`CLAUDE.md` 與 `AGENTS.md` 已同步。

**這是 TDD 的實質降級**——Codex 看不到紅燈也看不到綠燈。補償方式是要求它在回報中
明列公開簽章與對測試意圖的假設，讓 review 能在跑測試前先比對意圖。

### 環境配方（findings.md D7）

四種 `--add-dir` 組合逐一實測後的結論：`C:\Program Files\...` **不能加**
（會讓 sandbox setup 失敗），使用者設定檔的三個路徑**必須加**；
restore 一律由 Claude Code 事前在 sandbox 外完成，Codex 所有指令加 `--no-restore`。

---

## D1a / D1b — 資料層

25 個測試全數通過，且**兩個關鍵防護都做過 mutation probe**：

| 防護 | 拿掉後 | 判定 |
|---|---|---|
| `AtomicJsonStore` 的首次寫入分支（`File.Exists` → `File.Move`） | 11 個測試中 5 個失敗 | 測試真的守得住 |
| `TodoModels` 的「版本過新則拒絕載入」 | 多個測試失敗 | 測試真的守得住 |

兩次探測後都以內容比對確認還原為 byte-identical（不是看 diffstat——等長置換不會改變它）。

### 兩個刻意的設計決定

**排序用陣列位置，不加 `Order` 欄位。** 多一個排序欄位就多一個真實來源，
會產生重複索引、跳號、以及「陣列順序與欄位不一致」三種 bug。拖曳排序直接搬移元素即可。

**讀到較新的 `schemaVersion` 時拒絕載入且不覆寫原檔。**
若使用者用新版寫過資料、又用舊版開啟，靜默覆寫會直接毀掉他的待辦清單。
測試以逐位元組比對確認原檔未被改動。

---

## 下一步

**W1 被 S1 的人工驗證阻擋。** 需要在實體桌面上跑完 `docs/s1-manual-gate.md` 的 E1–E8，
結果會決定：

- `workerw` 可交付 → 可考慮設為預設
- `workerw` 不可交付 → **從產品移除**（不保留為「實驗選項」），走 `bottommost`
- 兩者都不可交付 → 停下來重談「嵌入桌面」這項需求本身

不論結果如何都**不要改用 Rust／Tauri／ImGui**——問題在 shell 不在語言。
