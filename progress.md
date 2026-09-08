# Progress

## 現況總覽（2026-09-08）

| | |
|---|---|
| 技術 | C# / .NET 10 / WPF，`win-x64` self-contained |
| 測試 | **60 passed / 0 failed** |
| 建置 | 零錯誤零警告 |
| 發佈體積 | 141MB（未壓縮） |
| 依賴 | 主專案 **0 個 NuGet 套件**；測試專案 xUnit（不出貨） |
| 端機需求 | 無——不需 .NET Runtime、VC++ Redist、WebView2 |
| 最新 commit | `472f43d` |
| 發佈檔 | `publish\DesktopTodoWidget.exe`（可直接執行） |
| 資料位置 | `%LOCALAPPDATA%\DesktopTodoWidget\`（`todos.json`、`window.json`，各有 `.bak`） |

**功能已全部完成，只剩打包（P1）。**

## 階段狀態

| 階段 | 內容 | 狀態 | commit |
|---|---|---|---|
| S1 | 桌面附著 spike | **完成，結論：workerw 不可交付並移除** | `2ec3f73` `8168af6` |
| D1a | 路徑解析、原子 JSON 儲存 | **完成**（11 tests + mutation probe） | `2ec3f73` |
| D1b | Todo 模型、schema 版本、單一實例 | **完成**（14 tests + mutation probe） | `2ec3f73` |
| W1 | 視窗外殼、拖曳移動、位置持久化 | **完成**（人工 M1–M7 全過） | `de92869` `4fcf5f2` |
| W1b | 消除拖曳閃爍 | **完成** | `e4432db` |
| U1 | Todo UI：新增／刪除／勾選／編輯 | **完成**（人工 U-1～U-8 全過） | `c99e830` |
| U1b | 深色主題貫穿控制項 | **完成** | `047176e` |
| U1c | 選取反白可讀 + 輸入框移到底部 | **完成** | `17ef33f` |
| R1 | 拖曳排序 | **完成**（人工 D1–D9 全過） | `eb6371b` |
| U2 | 每項字型大小與顏色 | **完成**（部分人工驗收未回報，見下） | `81db465` |
| U2b | 色票重調（分辨度） | **完成** | `472f43d` |
| **P1** | **打包** | **未開始，待使用者拍板形態** | — |

---

## S1 的結論（最重要的架構事實）

**`workerw` 不可交付，已從產品移除。`bottommost` 是唯一模式。**

兩個 target 實測都失敗：按鈕點不動、無法輸入，`--target=workerw` **連 Esc 都收不到**。
根因是**桌面圖示層（`SHELLDLL_DefView`）攔走全部輸入**——不是選錯層。
該技術幾乎只被用於動態桌布，正因為桌布不需要互動。

外加獨立缺陷：**行程結束後殘影留在桌布上**，使用者無從判斷程式是否還在跑。

### 誠實的能力邊界（不要在後續階段悄悄改口）

**原始需求「嵌入 Windows 桌面像個 widget」無法達成**，
而且不是實作品質問題——**Windows 沒有提供可互動的桌面層 API**。

實際交付：無邊框、不在 Alt+Tab、**不會浮到最上層**的常駐視窗。
與真 widget 的差別是 **Win+D 顯示桌面時它會跟著被蓋掉**。

**復原手勢是 Win+D，不是雙擊 exe**（2026-09-08 實測，`findings.md` D2.1）。
被蓋住時程式仍在跑，雙擊 exe 會**靜默無反應**——第二個實例直接 `Shutdown()`。
三種「消失」裡雙擊只救得了「真的被關掉」那一種。

詳見 `findings.md` D2、D2.1 與 `docs/s1-manual-gate.md`。

---

## 目前的功能

- 頂部 24px 拖曳條（移動視窗）／清單／底部固定輸入框
- 輸入框打字按 Enter 新增到清單尾端，焦點保留
- 勾選 → 刪除線 + 變淡，**留在原位不重排**
- 雙擊文字就地編輯，Enter 套用、Esc 取消
- 每列 `×` 刪除
- **拖曳項目重新排序**（插入指示線、邊緣自動捲動）
- **每列 ⚙ 設定字型大小（10–28）與顏色（八色色票）**
- 位置與所有內容持久化，關閉前同步落盤
- 單一實例；Esc 或右鍵 `Exit` 關閉

### Esc 有三個語意，優先序不可弄反

**拖曳中取消拖曳 > 編輯中取消編輯 > 否則關閉視窗。**
寫錯會在拖曳或編輯時把程式關掉，使用者剛打的字全沒。

---

## 待使用者處理

### 1. U2 的人工驗收尚未回報

使用者只回報了色票分辨度問題（已修），下列仍未確認：

| # | 測試 |
|---|---|
| P1 | 點 ⚙ 出現 popup，點他處會關閉 |
| P2 | 調大小 → 只有該列變 |
| P4 | Reset → 回到預設 |
| P5 | 關閉再開 → 大小與顏色都記住 |
| **P7** | **字型調大後拖曳排序仍正常**（列高改變會影響插入線計算） |

### 2. P1 打包形態，需使用者拍板

| | 單一 exe | 免安裝資料夾 |
|---|---|---|
| 交付 | 一個檔案 | 一個資料夾 |
| 執行時 | native DLL 解壓到 `%TEMP%\.net` | 不解壓 |
| 體積 | 開 `EnableCompressionInSingleFile` 約 60–95MB（**待實測**） | 141MB |

需求原文寫「single file self-contained exe」，但也允許免安裝資料夾。
**此決策不由實作者自行決定**（findings.md D5）。

### 已定案（2026-09-08）

**開機自啟不做。** 理由見 `findings.md` D2.1：資料每 500ms 落盤，重開機後雙擊即完整還原，
而「開機後手動點一次」正是雙擊唯一有效的情境，不值得為此碰登錄檔或啟動資料夾。
**這也表示最終使用說明必須寫明復原手勢是 Win+D。**

---

## 開發流程的重要事實（下一個 session 必讀）

### Codex 在本機 sandbox 內無法建置

MSBuild 被拒絕寫入 `obj/` 與工作區內新建目錄，即使 `--add-dir` 全給對、
restore 已預先完成也一樣。**後果：Codex 交出的每一份程式碼都是未經編譯的。**

分工因此調整為：

| 工作 | 誰做 |
|---|---|
| 撰寫實作與測試 | Codex |
| `dotnet build` / `test` / `publish` | **Claude Code**（sandbox 外） |
| 回饋真實結果給 Codex | Claude Code |
| 修正編譯錯誤 | Claude Code 可直接修 |

派工 prompt 必須明講「不要嘗試 build」，並要求 Codex 不得偽造測試輸出。
詳見 `findings.md` D7.1。

### 派工的可用配方

```
codex exec --sandbox workspace-write \
  --add-dir "C:\Users\oldli\AppData\Local\Microsoft SDKs" \
  --add-dir "C:\Users\oldli\AppData\Roaming\NuGet" \
  --add-dir "C:\Users\oldli\.nuget" \
  -C "D:/ai/projects/desktop-todo-widget" \
  -o "<scratch>/<task>-report.md" "$(cat <scratch>/<task>-prompt.md)" < /dev/null
```

- **`C:\Program Files\...` 不能加**，會讓 sandbox setup 失敗
- 所有 dotnet 指令**一律加 `--no-restore`**；restore 由 Claude Code 事前在外面做
- prompt 寫成檔案再 `$(cat ...)` 帶入，避免中文在 shell 引號裡出事
- `< /dev/null` 必要，否則背景執行會卡住

---

## Review 抓到的缺陷（Codex 無法編譯而產生）

| 缺陷 | 影響 |
|---|---|
| `IsWindowNative` P/Invoke 指向不存在的匯出 | workerw 附著成功後 2 秒必崩潰 |
| `CS0051` 可及性不一致、`CS0133` 非編譯期常數 | 建不起來 |
| 缺 `using System.IO;` / `using Xunit;` | 建不起來 |
| `IsInsideControl` 往上走訪撞到 `Window`（`Window` 繼承自 `Control`） | **整個視窗都拖不動** |
| 為取得多螢幕而開 `UseWindowsForms` | 與 WPF 型別大量撞名 |
| `VerticalScrollBarStyle` 屬性不存在 | 建不起來 |

### 兩個「測試綠燈但功能壞掉」的險境

**U2 的 schemaVersion 升版**：升到 2 時，「版本過新則拒絕載入」的測試剛好用
`schemaVersion: 2` 當未來版本。不改的話程式會**拒絕載入自己寫的檔**，
而測試仍是綠的（它本來就斷言「應該拒絕」）。已改用 `3` 並補 v1 遷移測試，
且**以手寫 v2 檔實跑驗證**。

**W1 的規格自相矛盾**：R5 說「重疊夠就原樣回傳」，驗收 P5 又說「視窗比螢幕大就夾到左上角」。
實作是對的、規格錯了，已更正卡片與測試（理由寫在測試註解裡）。

---

## 驗證指令

```
dotnet build src/DesktopTodoWidget/DesktopTodoWidget.csproj -c Release --no-restore
```

```
dotnet test tests/DesktopTodoWidget.Tests/DesktopTodoWidget.Tests.csproj --no-restore
```

```
dotnet publish src/DesktopTodoWidget/DesktopTodoWidget.csproj -c Release -r win-x64 --self-contained true --no-restore -o publish
```

UI 與視窗行為不寫自動化測試（findings.md D6），走各卡片的人工驗收清單。

**視覺類問題可由 Claude Code 自行截圖驗證**：以 `EnumWindows` 依 PID 找到視窗
（`MainWindowHandle` 因 `WS_EX_TOOLWINDOW` 為 0，不可用），再用 `PrintWindow` 擷取。
本專案的深色主題、色票、版面都是這樣驗的，不必每次都麻煩使用者。

**截圖量測的兩個坑（2026-09-08 實測踩過，都會導致錯誤結論）**：

1. **一個 WPF 行程有多個 `HwndWrapper[...]` 頂層視窗**，多數不可見。
   取「第一個 class 符合的」會拿到 136x39 的隱藏視窗，量出 `visible=False`。
   **正確條件：`IsWindowVisible` 為真且寬高皆 > 50。**
2. **啟動任何新行程都會解除 show-desktop 狀態。** 涉及 Win+D 的量測，
   Win+D、截圖、啟動第二實例**必須全部收在同一個行程內**，否則量測工具自己
   就把待測狀態破壞掉了。

判斷「是否被蓋住」用 `WindowFromPoint` 打視窗中心點問最上層是誰，
比只看截圖可靠——`IsWindowVisible` 為真不代表使用者看得見。
