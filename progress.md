# Progress

## 現況總覽（2026-09-09）

| | |
|---|---|
| 技術 | C# / .NET 10 / WPF，`win-x64` self-contained |
| 測試 | **60 passed / 0 failed** |
| 建置 | 零錯誤零警告 |
| 依賴 | 主專案 **0 個 NuGet 套件**；測試專案 xUnit（不出貨） |
| 端機需求 | 無——不需 .NET Runtime、VC++ Redist、WebView2 |
| 最新 commit | `7528330` |
| 資料位置 | `%LOCALAPPDATA%\DesktopTodoWidget\`（`todos.json`、`window.json`，各有 `.bak`） |
| 遠端 | `origin/main` 已同步（公開 repo） |
| 已發布 | **v1.0.0、v1.0.1**（v1.0.1 為 Latest） |

**所有階段完成，已對外發布。專案於 2026-09-09 收尾，使用者切往其他專案。**

**唯一未結的事：端機（他人電腦）的趨勢科技擋下執行，使用者決定暫不處理。**
細節與可行方向見下方「散布時的防毒問題」。

**收尾時的結論（`findings.md` D8）：Windows 內建「便利貼」覆蓋本專案八成以上功能，
且端機零安裝、不觸發防毒。** 本專案僅存的不可取代之處是「每列獨立的字型大小與顏色」
與「保證離線無帳號」。原本值得自建的理由是「嵌進桌面圖示層」，而 D2 證明那做不到。

### 已發布的兩種成品（同一支程式，只有打包形態不同）

| 檔案 | 體積 | 內容 | 用途 |
|---|---|---|---|
| `DesktopTodoWidget-win-x64-folder.zip` | 60.1 MB | 256 檔案，exe 僅 162KB | **預設推薦**，防毒觀感正常 |
| `DesktopTodoWidget-win-x64.zip` | 56.4 MB | 6 檔案，壓縮 bundle | 整潔，但**容易被防毒啟發式誤判** |

理由見下方「散布時的防毒問題」與 `findings.md` D5。

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
| **H1** | **標題列說明按鈕 `?`** | **完成**（驗收見下） | `f694a11` `7528330` |
| **P1** | **打包與發布** | **完成**，形態＝免安裝資料夾 | `31bb752` |

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
- **標題列右端 `?` 開啟使用說明彈窗**（英文，內容含「看不見時怎麼辦」）
- 位置與所有內容持久化，關閉前同步落盤
- 單一實例；Esc 或右鍵 `Exit` 關閉

### Esc 有四個語意，優先序不可弄反

**拖曳中取消拖曳 > 說明開啟時關說明 > 編輯中取消編輯 > 否則關閉視窗。**
寫錯會在拖曳、編輯或閱讀說明時把程式關掉，使用者剛打的字全沒。

第二階是 H1 加的。注意 `Popup` 是獨立 HWND，焦點在 popup 內時鍵盤事件**不會**冒泡到
`MainWindow_PreviewKeyDown`；目前是靠把 popup 與其 `ScrollViewer` 設為 `Focusable="False"`
讓焦點留在主視窗，這條路才成立。

---

## 已定案

| 決策 | 結論 | 依據 |
|---|---|---|
| 開機自啟 | **不做** | `findings.md` D2.1：資料每 500ms 落盤，重開機後雙擊即完整還原，而「開機後手動點一次」正是雙擊唯一有效的情境，不值得碰登錄檔或啟動資料夾 |
| P1 打包形態 | **免安裝資料夾** | 見下方防毒段落；`findings.md` D5 有三種形態的實測數字 |
| 說明彈窗位置 | 錨定 `WindowDragStrip` 而非 `HelpButton` | 錨在靠右的按鈕上會有 336px 掛在視窗外；改錨拖曳條後 overhang 為 −6px |

## 待使用者處理

### U2 的人工驗收仍未回報

使用者只回報了色票分辨度問題（已修），下列仍未確認：

| # | 測試 |
|---|---|
| P1 | 點 ⚙ 出現 popup，點他處會關閉 |
| P2 | 調大小 → 只有該列變 |
| P4 | Reset → 回到預設 |
| P5 | 關閉再開 → 大小與顏色都記住 |
| **P7** | **字型調大後拖曳排序仍正常**（列高改變會影響插入線計算） |

這批是 U2 留下的唯一缺口。**注意 v1.0.0／v1.0.1 是在這些未驗的情況下發布的。**

---

## 差點跟著 v1.0.0 出貨的當機缺陷（2026-09-08 修復，`94d6dfc`）

**清單長到需要捲軸就當掉，而且每次啟動都當。**

```
System.InvalidCastException: Unable to cast object of type
'System.Double' to type 'System.Windows.GridLength'
```

`TodoVerticalScrollBarStyle` 用 `{x:Static SystemParameters.VerticalScrollBarButtonHeight}`
指定 `RowDefinition.Height`。前者是 `double`，後者要 `GridLength`。
**寫字面值 `"17"` 時型別轉換器會轉，`{x:Static}` 直接塞物件進去則不會。**

### 為什麼兩個月都沒被發現

**ControlTemplate 只在控制項真的需要時才展開。** 捲軸只在清單長到需要捲動時才出現，
而開發期間的測試資料只有 4 筆。實測放 30 筆 → **啟動即崩潰**；因為待辦存在 JSON，
**之後每次啟動都崩潰**，使用者除了手動編輯 JSON 沒有別的救法。

修法：row 改 `Auto`，把系統尺寸移到 RepeatButton 的 `Height`（那本來就收 double）。

### 教訓（比修法本身重要）

1. **延遲建立的 UI 不會在啟動時暴露錯誤。** `Popup` 內容、`ControlTemplate`、
   `DataTemplate` 都是用到才建。「程式跑起來了」完全不代表這些沒問題。
2. **它是被 H1 的說明彈窗意外踩出來的**——那個彈窗內容夠長，需要捲軸。
   一個看似無關的小功能，才讓兩個月的地雷現形。
3. **人工驗收清單要包含「資料量大」的情境。** 之前所有人工驗收都只用 4 筆資料跑。

---

## 合成滑鼠事件打不到這個視窗（驗證方法的硬限制）

**`SendInput` / `mouse_event` 的點擊與拖曳，這個 bottommost 視窗收不到。**

對照實驗確立的，不是猜的：對第一列勾選框送出合成點擊後，`todos.json` 的 `isDone`
**完全沒變**，而勾選框是 U1 已人工驗證可用的功能。鍵盤事件（Esc）則正常送達。

### 因此可自動化與不可自動化的界線

| 可自動化 | 手段 |
|---|---|
| 視窗是否存在、位置、大小、z-order | `EnumWindows` + `GetWindowRect` |
| 畫面長相 | **`PrintWindow`（含 `PW_RENDERFULLCONTENT`＝2）**，被其他視窗蓋住也能擷取 |
| 是否被蓋住 | `WindowFromPoint` 打中心點問最上層是誰 |
| 按鈕觸發、彈窗開啟與幾何 | **UI Automation `InvokePattern`** |
| 鍵盤行為（Esc 各階） | `keybd_event` |

| 不可自動化 | 只能人工 |
|---|---|
| 滑鼠點擊命中測試 | 拖曳條可拖、點他處關彈窗、拖曳排序 |

**跑 UIA 或截圖驗證時務必設對照組**（例如已知可用的 ⚙ 彈窗）。這個 session 曾因為
沒設對照組，把「工具打不到」誤判成「功能壞掉」，來回三次。

另外兩個實測踩過的坑：**滑鼠停在按鈕上會冒出 tooltip，那是一個 77x23 的獨立視窗，
很容易被誤認成彈窗**（量測前先把游標移開）；以及 `CopyFromScreen` 擷到的是螢幕內容，
視窗被蓋住時拍到的是別人的畫面，**一律改用 `PrintWindow`**。

---

## 散布時的防毒問題（2026-09-09，**未解決，使用者決定暫停**）

實際發生的事：**端機的趨勢科技（Trend Micro，企業版介面）擋下執行。**
使用者提供的日誌欄位：

| 欄位 | 值 |
|---|---|
| 類型 | **行為監控**（不是病毒碼掃描） |
| 違規 | **新發現的程式** |
| 風險 | **低** |
| 目標 | Download from email |
| 作業／處理行動 | 執行／**拒絕** |

### 這推翻了本節原本的推論

原本判斷是「壓縮 bundle 像 packer，所以被啟發式擋」，並據此把 P1 定為資料夾版。
**實際攔截的是普及率規則**——「新發現的程式」看的是全世界有多少人跑過這支 exe，
**與打包形態無關**。因此資料夾版**大概率同樣會被擋**，改包裝解決不了這一條。

打包形態的判斷本身仍然成立（壓縮 bundle 確實容易被啟發式盯上，見 `findings.md` D5），
只是它**沒有解決端機實際遇到的問題**。兩者是不同的規則，不要混為一談。

另一個被推翻的假設：端機上 zip 按右鍵**沒有「解除封鎖」核取方塊**，
代表檔案**不帶 `Zone.Identifier`**。所以 MotW 與 SmartScreen 在端機上根本不是障礙，
唯一在擋的是趨勢科技。

### 三道關卡（修正版）

| 關卡 | 成因 | 對本專案是否為障礙 |
|---|---|---|
| SmartScreen | 未簽章、雜湊零信譽 | 端機上**未觸發**（無 MotW） |
| Mark of the Web | 下載會加 `Zone.Identifier` | 端機上**不存在** |
| 防毒**啟發式**（packer 形態） | 壓縮 bundle | 資料夾版可避開 |
| 防毒**普及率**（新發現的程式） | 沒人跑過這支 exe | **實際的障礙，打包形態無效** |

### 可行方向（依當時評估）

1. 換傳輸管道（USB，避開「下載」歸類）——最省事，未實測
2. 在趨勢加**單一資料夾**例外（企業版須由 IT 操作）
3. 向趨勢回報誤判（免費，1–3 工作天，可附原始碼公開這點佐證）
4. **程式碼簽章憑證＝唯一根治**。簽章後普及率規則改看憑證信譽，
   之後每次改版不必重新累積。OV 約 US$200–400/年，EV 約 US$300–600/年。**目前未購買。**

### 這件事早就被列為阻擋項

`task_plan.md` 的阻擋項寫過「**端機歸屬**：端機是使用者本人的另一台電腦，還是他人的？
影響 R3（SmartScreen）的處理」。當時沒有答案就繼續往下做，最後在散布階段踩到。
**若當初就知道端機是他人的受管電腦，打包與簽章策略從一開始就會不同。**

若端機由公司 IT 管控，這個攔截是政策正常運作，正確路徑是請 IT 加例外，
不是設法繞過。

### 發布的成品雜湊（回報誤判或驗證完整性時使用）

```
DesktopTodoWidget.exe（資料夾版，162,304 bytes）
SHA256 1ACD36A738EB29C1CFD27959E40192315904F7FA39857DA3771D230AEF3E89D2

DesktopTodoWidget-win-x64-folder.zip（62,967,312 bytes）
SHA256 5FE77D1F5360312C01D7F8B069CC5CC7F224D5E53B42D7EA32838C054803E2A0

DesktopTodoWidget-win-x64.zip（59,118,430 bytes）
SHA256 3A821D8C42E0193130902A8E8789905059B8658871FED2598688DD7AE1A907EC
```

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

發布用的**資料夾版**（P1 定案形態，256 檔案）：

```
dotnet publish src/DesktopTodoWidget/DesktopTodoWidget.csproj -c Release -r win-x64 --self-contained true -p:Version=1.0.1 --no-restore -o publish
```

6 檔案的壓縮 bundle 版（整潔但易被防毒誤判）：

```
dotnet publish src/DesktopTodoWidget/DesktopTodoWidget.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:Version=1.0.1 --no-restore -o publish
```

**出貨前一定要刪掉 `publish/DesktopTodoWidget.pdb`**——成品不需要，而且它會洩漏
開發機的原始碼路徑。

發布（**需使用者明確授權**才能 push 或散布）：

```
gh release create v1.0.1 <zip> --title "Desktop Todo Widget 1.0.1" --notes "..."
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
