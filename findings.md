# findings.md — 研究發現、技術決策與理由

## D1. 技術選型（2026-09-07 決定）

### 需求約束

1. 全英文介面
2. 嵌入 Windows 桌面像 widget
3. 不出現在 Alt+Tab
4. 新增／刪除／勾選／編輯
5. 拖曳排序
6. 資料存本機 JSON
7. 可製作成 single-file self-contained exe
8. 完全離線
9. **端機不得安裝任何 runtime**
10. 交付為一個執行檔或免安裝資料夾

目標端機：Windows 10 Home 19045。

### 候選比較

| | .NET 10 WPF | .NET 10 WinForms | C++/Win32 + ImGui | Rust + Tauri v2 | Electron |
|---|---|---|---|---|---|
| 端機零安裝 | ✅ self-contained | ✅ | ✅（靜態 CRT） | ⚠️ 依賴 WebView2 | ❌ 需整包資料夾 |
| 產物大小 | 60–95MB（壓縮後） | **與 WPF 相近** | **1–2MB** | 5–10MB（或 fixed WebView2 約 120MB 資料夾） | 150MB+ |
| 拖曳排序成本 | 低 | 中 | **高** | 低（HTML） | 低 |
| 文字編輯／IME | 成熟 | 成熟 | **全部自刻** | 成熟 | 成熟 |
| Alt+Tab 排除 | P/Invoke 直接 | P/Invoke 直接 | 原生 | 需 interop | 需 interop |
| 開發機工具鏈 | **已就緒**（SDK 10.0.300） | 已就緒 | 需裝 MSVC | 需裝 Rust | 需裝 Node |

### 決定：.NET 10 WPF，`win-x64` self-contained

理由，依權重：

1. **端機零安裝只有 self-contained 一條路**，而 .NET self-contained 不需要 .NET Runtime
   也不需要 VC++ Redist（runtime 隨成品帶入）。
2. **WPF 的成熟能力正好覆蓋本專案最花工的部分**：內嵌文字編輯、IME、拖曳排序、
   捲動手感、無障礙。C++/ImGui 這些全部要自己負責，為了省幾十 MB 賠上大量工時不划算。
3. **開發機工具鏈已就緒**，SDK 10.0.300 含 `Microsoft.WindowsDesktop.App 10.0.8`。
4. **換語言逃不掉真正的風險**（見 D2）。桌面附著是 shell 問題，Rust／C++／Tauri
   同樣要面對 `WorkerW`，還額外賠上工具鏈與 WebView2 依賴。

### 更正紀錄（Codex 規劃 review 指出，Claude Code 覆核屬實）

- **不要用 .NET 8／9**：兩者皆於 2026-11 結束支援。選 .NET 10 LTS。
- **WinForms 不會明顯比 WPF 小**。兩者都帶入 Windows Desktop runtime；
  Claude Code 初版聲稱 WinForms 較小是錯的。
- **`PublishTrimmed` 對 WPF 與 WinForms 官方皆不支援**（WPF 靠反射、
  WinForms 靠 COM marshalling）。沒有靠 trim 瘦身的路。
- **.NET 桌面沒有 NativeAOT 路徑**，60–95MB 大致是體積地板。

### 淘汰理由

- **Tauri／Wails**：依賴 WebView2 Evergreen Runtime。開發機實測**已安裝**
  （152.0.4191.66），但那是 Edge 更新推送的產物，不是可依賴的保證——重灌、
  企業政策或不同端機都可能沒有。改用 fixed-version WebView2 會變成約 120MB 資料夾，
  體積優勢消失。與需求 9 直接衝突。
- **Electron**：體積與需求 7／10 衝突。
- **C++/Win32**：唯一能到個位數 MB 的路，**若日後體積成為硬需求可重新評估**；
  目前開發成本不成比例。

---

## D2. 桌面附著：沒有受支援的公開 API（**本專案最大風險**）

### 事實

「嵌入桌面」只有一種接近真實的做法：把視窗 `SetParent` 到
`WorkerW` / `SHELLDLL_DefView`（桌布層）。**這倚賴未文件化的 shell 視窗結構與訊息
（常見的 `0x052C`），Microsoft 沒有任何相容性承諾。**

已知脆弱點：

| 情境 | 影響 |
|---|---|
| Explorer 重啟 | **必壞**——父 HWND 消失，必須偵測並重新掛載 |
| 混合 DPI 多螢幕 | **可能壞**——跨行程 `SetParent` 的 DPI awareness 不相容，Win10 1703+ 可能失敗或強制改變 DPI 狀態 |
| Win+D 顯示桌面 | 通常可見，但掛在圖示層之上或之下會影響能否點擊 |
| 桌布輪播 | 未必壞，但 shell 可能重建層級，需偵測失聯後重掛 |
| Windows 更新 | 無承諾，至少按「Explorer 重啟」處理 |

### `SetParent` 的隱含要求（Codex review 指出，v1 遺漏）

`SetParent` **不會自動調整視窗樣式**。附著後必須自行清除 `WS_POPUP`、加上 `WS_CHILD`、
以 `SWP_FRAMECHANGED` 生效，並把座標改為 parent-relative。
跨行程 `SetParent` 還可能**強制重設子行程的 DPI awareness**。

### 替代方案 `bottommost`，以及它自己的矛盾

**無邊框、非 topmost 的 top-level window，以 `SetWindowPos(HWND_BOTTOM)` 壓到最底。**
沒有跨行程 parent 與 DPI 問題，是受支援的做法。

**但它不是免費的安全選項**（Codex review 指出，v1 把它當成穩妥退路是錯的）：

1. `HWND_BOTTOM` **只代表 Z-order 最底部，不保證「一般視窗之下、桌布之上」**。
   這是待驗證的假設，不是已成立的事實。
2. **非作用中視窗一旦取得 activation，Windows 會把它提到前方。**
   「壓在底層」與「可以點擊」本身互相拉扯——而兩者都是需求。

因此 `bottommost` 也必須通過自己的獨立 gate（S1 的 E8），不能因為 `workerw` 失敗就
預設它可用。

### 決定（**2026-09-08 已由 S1 實測定案**）

**`workerw` 不可交付，已從產品移除。唯一模式為 `bottommost`。**

S1 人工驗證結果（`docs/s1-manual-gate.md`）：

| | bottommost | workerw / progman | workerw / workerw |
|---|---|---|---|
| E1 按鈕可點 | **通過** | 失敗 | 失敗 |
| E2 Alt+Tab | **通過** | — | — |
| E7 打字／中文 IME | **通過** | 失敗 | 失敗（**連 Esc 都收不到**） |
| E8 Z-order | **通過**（S1b 修正後） | — | — |

**根因不是選錯層**——兩個候選 target 都位於桌面圖示層（`SHELLDLL_DefView`）之下，
而該層攔走全部輸入，滑鼠與鍵盤皆然。這是該技術的固有性質：
它幾乎只被用於**動態桌布**，正因為桌布不需要互動。

外加一個獨立缺陷：**行程結束後殘影留在桌布上**，使用者無從判斷程式是否仍在執行，
必須重新套用桌布或重啟 Explorer 才會消失。強制結束時無法從行程內修補。

依本節原訂的處置規則，`workerw` **判定不可交付並移除，不保留為使用者可見的實驗選項**。

### R2 的預測獲得證實

「跨行程 `SetParent` 會改變 DPI awareness」已由 `spike.log` 實測確認
（`dpiAwareness` 由 1 變 2）。此風險隨 `workerw` 一併移除。

### 產品定義的調整（**重要**）

原始需求寫「嵌入 Windows 桌面像個 widget」。**這一項無法達成**，
且不是實作品質問題——**Windows 沒有提供可互動的桌面層 API**。

實際交付的是：無邊框、不在 Alt+Tab、**不會浮到最上層**的常駐視窗。
與真 widget 的差別是：**按 Win+D 顯示桌面時它會跟著被蓋掉**，不會留在桌面上。

W1 之後依此定義進行。

### 原始決定（暫定，已被上方取代）

**兩種模式都實作，`bottommost` 為預設。** 預設值必須是穩定的那個。

**失敗處置（Codex review 修正）**：若 `workerw` 未通過 S1 的淘汰門檻，
先做一次**有界的根因確認**（是否選錯層、樣式切換寫錯、失聯偵測失效）；
若仍失敗，判定 `workerw` **不可交付並移除**，**不保留為使用者可見的實驗選項**——
留一個已知會壞的模式給使用者，只會在日後某天無聲失敗。

若 `bottommost` 也未通過 E8，**不要堆疊 workaround**，
應回頭重談「嵌入桌面」這項產品需求本身。

**S1 spike 的結果決定這條怎麼改。**

---

## D3. Alt+Tab 排除

`WS_EX_TOOLWINDOW` 是正確且足夠的手段——Microsoft 明確定義帶此樣式的視窗
不出現在 taskbar 與 Alt+Tab。

實作細節（時機是關鍵）：

1. XAML／建構期設 `ShowInTaskbar="False"`
2. 在 **`SourceInitialized`**（不是 `Loaded`）取得 HWND
3. 加上 `WS_EX_TOOLWINDOW`、清除 `WS_EX_APPWINDOW`
4. `SetWindowPos(..., SWP_FRAMECHANGED)` 讓樣式生效

**不要**建立假的 owner window——owner 會改變最小化／啟用／關閉行為，
且不能取代 tool-window style。在 `Loaded` 才設會讓視窗短暫出現在切換器裡。

---

## D4. 資料儲存

| 決策 | 內容 |
|---|---|
| 預設位置 | `%LOCALAPPDATA%\DesktopTodoWidget\todos.json` |
| Portable 模式 | exe 同資料夾（需處理無寫入權限的情況） |
| 寫入方式 | 同資料夾寫 `.tmp` → flush → **同 volume 原子替換** → 保留 `.bak` |
| 格式 | UTF-8，含 `schemaVersion` 欄位 |
| 落盤時機 | 異動後 debounce，**關閉前必須同步落盤** |
| 多開 | 每使用者 session 的 **named mutex**，只允許一個實例 |

用 `%LOCALAPPDATA%` 仍完全符合「免安裝、離線」。不要用目前工作目錄——
從捷徑啟動時它可能是任何地方。

原子替換降低斷電毀損風險，**但不能宣稱絕對防硬體或檔案系統故障**，`.bak` 是第二道。

### 兩個實作細節（Codex review 指出，v1 規格不完整）

1. **`File.Replace` 在目標檔不存在時會失敗。** 必須另有一條「首次建立」路徑。
   測試要涵蓋：首次寫入、覆寫、備份產生、以及模擬替換失敗。
2. **未加前綴的 named mutex 預設是目前 Terminal Services session 範圍**，
   符合「每 session 一個實例」的意圖，但**不具使用者 ACL**。
   若要防止其他使用者干擾，需明確指定 current-user scope 或設定 ACL。

### Portable 模式是經授權的例外（Codex review 指出的文件矛盾）

`AGENTS.md` 的 Safety 規定只准寫入 `%LOCALAPPDATA%\DesktopTodoWidget\`，
與本節允許 portable 模式寫入 exe 同資料夾**互相矛盾**。

裁定：**portable 模式是明文授權的例外**，且僅在使用者明確啟用時生效。
`AGENTS.md` 已同步修正。D1 階段實作前不得再有第三個寫入位置。

---

## D5. 打包形態（決策延後至 P1）

需求 7 寫「single file self-contained exe」，需求 10 又允許免安裝資料夾。兩者的實質差別：

要真正只產出一個 exe，必須開 `IncludeNativeLibrariesForSelfExtract=true`，
而**那會讓 native runtime DLL 在執行時解壓到 `%TEMP%\.net`**。
那不是安裝 runtime，但也不能宣稱「完全不在端機留下檔案」，且不保證立刻清除。

| 形態 | 取捨 |
|---|---|
| 免安裝資料夾 | 不解壓到 TEMP、無 single-file 相容性邊角、可觀察 |
| 單一 exe | 交付最簡單，接受 `%TEMP%\.net` 解壓行為 |

`EnableCompressionInSingleFile` 可把體積壓到 60–95MB，代價是啟動時解壓到記憶體、
啟動變慢。**體積不是框架常數，必須以實際成品量測。**

**此決策由使用者在 P1 階段拍板**，不由實作者自行決定。

---

## D6. 測試策略

- **純邏輯用 xUnit**：JSON 往返、schema 遷移、原子寫入、排序索引重算、資料驗證。
- **UI 與視窗行為不寫自動化測試**：成本高、脆弱，且測不到本專案真正的風險。
  改用可重現的人工 exit gate 清單。
- xUnit 是 dev-only 依賴，**不隨成品出貨**，因此不違反端機零安裝。

---

## D7. 建置環境與 Codex sandbox 的配方（2026-09-07 實測）

Codex 的 `workspace-write` sandbox **沒有網路**，且會擋掉部分使用者設定檔路徑。
四種 `--add-dir` 組合逐一實測的結果：

| 組合 | 結果 |
|---|---|
| `C:\Program Files\dotnet` + `.nuget` | **sandbox 直接掛掉**：`helper_unknown_error: setup refresh had errors` |
| 完全不加 | sandbox 正常、`dotnet --version` 可執行，**但 build 失敗**：`Access to the path 'C:\Users\oldli\AppData\Local\Microsoft SDKs' is denied` |
| `AppData\Local\Microsoft SDKs` + `AppData\Roaming\NuGet` | access denied 消失，改成 **`NU1301` 連不到 nuget.org** |
| 再加 `C:\Users\oldli\.nuget` | **仍然 `NU1301`**——restore 一定會連 source，套件在快取裡也一樣 |

**`C:\Program Files\...` 不能加**（會讓 sandbox setup 失敗）；
**使用者設定檔路徑必須加**（否則讀不到 SDK／NuGet 設定）。

### 可用配方

1. **Claude Code 先在 sandbox 外 `dotnet restore`**（產生 `obj/project.assets.json`）
2. Codex 用三個 `--add-dir`：`AppData\Local\Microsoft SDKs`、`AppData\Roaming\NuGet`、`.nuget`
3. **所有 dotnet 指令一律加 `--no-restore`**
4. prompt 明講「出現 `NU1301` 就停下來回報」，否則 Codex 會嘗試改用
   framework-dependent 或動 NuGet 設定來繞過

**任何需要下載的套件都必須由 Claude Code 事前還原**：
self-contained 的 runtime packs、xUnit 測試套件皆然。

### 實測數據

空白 `dotnet new wpf` 專案 `--self-contained` 發佈（未壓縮）：**141MB**。
與 D1 預估的 130–160MB 相符。壓縮後的實際值待 P1 量測。

### D7.1 — **Codex 在此 sandbox 內無法建置**（2026-09-07 確認，已改變角色分工）

即使 `--add-dir` 全部給對、restore 也已預先完成，MSBuild 仍被拒絕寫入：

| 嘗試的輸出位置 | 錯誤 |
|---|---|
| 預設 `obj/Release/...` | `MC1000` / `MSB4018`：`Access to the path '...DesktopTodoWidget_MarkupCompile.cache' is denied` |
| 工作區內新建的 `.build-tmp/` | `MSB3491` / `MSB3191`：同樣 access denied |
| 工作區外（`.nuget` 下） | 可寫，但違反「不得寫到工作區外」 |

**後果很嚴重：Codex 交出的每一份程式碼都是未經編譯的。** 實際發生兩次：

1. S1 第一版有 `CS0051`（可及性不一致）
2. S1 修正版把 `WsPopup` 改成 `unchecked((nint)0x80000000)`，觸發 `CS0133`
   ——`nint` 大小依平台而定，該表達式不是編譯期常數。必須用 `static readonly`

### 因此本專案的角色分工調整如下

| 工作 | 誰做 |
|---|---|
| 撰寫實作與測試 | **Codex** |
| 執行 `dotnet build` / `dotnet test` / `dotnet publish` | **Claude Code**（sandbox 外） |
| 把真實建置與測試結果回饋給 Codex | **Claude Code** |
| 修正 Codex 無法自行驗證而產生的編譯錯誤 | Claude Code 可直接修（CLAUDE.md 的環境例外） |

派工的 prompt 必須**明講「不要嘗試 build，Claude Code 會在 sandbox 外執行」**，
否則 Codex 會花大量時間嘗試繞過輸出路徑，甚至寫到工作區外。

同時必須要求 Codex **不得偽造測試輸出**，Tests 段留空或註明由 Claude Code 執行。

**這是 TDD 的實質降級**：Codex 看不到紅燈也看不到綠燈。
補償方式是要求它在回報中明列公開簽章與對測試意圖的假設，讓 Claude Code 能在
執行測試前先比對意圖是否對齊。

---

## R. 已知風險

| 編號 | 風險 | 緩解 |
|---|---|---|
| R1 | `WorkerW` 附著無官方支援，可能隨 Windows 更新失效 | 預設走 `bottommost`；`workerw` 需具備失聯偵測與自動重掛 |
| R2 | 混合 DPI 多螢幕下跨行程 `SetParent` 可能失敗 | S1 spike 必測；失敗即維持 `bottommost` |
| R3 | 未簽章 exe 在端機首次執行會被 SmartScreen 攔 | 端機若非本人所有需事先告知；不在本專案範圍內解決（簽章需憑證） |
| R4 | 產物體積 60–95MB，無法用 trim 或 AOT 縮減 | 已知且接受；若成為硬需求需重新評估 C++/Win32 |
| R5 | single-file 會解壓 native DLL 到 `%TEMP%\.net` | 見 D5，由使用者決定形態 |
| R6 | Windows 10 Home 19045 的一般支援已結束（Consumer ESU 可能仍適用） | 記錄端機的實際 build 與 ESU 狀態，不要籠統稱「不在支援範圍」。相容性只能實機驗證 |

---

## Review — 2026-09-07 Codex 規劃可行性 review 採納紀錄

Codex 以 read-only 模式 review 了 CLAUDE.md / AGENTS.md / findings.md / task_plan.md /
S1 任務卡，提出 6 項（4 BLOCKING / 2 MAJOR），結論是「S1 暫不應進入實作」。
逐項判讀如下（CLAUDE.md 準則 5）。

| # | 嚴重度 | 議題 | 判讀 |
|---|---|---|---|
| 1 | BLOCKING | S1 任務卡不足以做出決策：缺 `WorkerW` 選層契約、缺 `WS_POPUP → WS_CHILD` 樣式切換、E1–E6 只「觀察記錄」而無淘汰門檻、用 `dotnet run` 測而非 self-contained 發佈檔、缺 IME 測試 | **全數採納**。任務卡改版為 v2：新增 R4 選層契約（三步驟＋兩個 target）、R5 樣式切換、E7 鍵盤與 IME、明文淘汰門檻，並改為**必須以發佈資料夾實測**。`dotnet run` 那項是關鍵——端機沒有 .NET runtime，用它測到的不是真實條件 |
| 2 | BLOCKING | `WS_EX_TOOLWINDOW` 與 `SetParent` 不衝突，但 child window 的 WPF `TextBox`／TSF／IME 行為不可假設；且 **`bottommost` 有自己的 activation／Z-order 矛盾** | **採納**。E7 是 `workerw` 的生死題（滑鼠可點不代表能打字）；E8 是 `bottommost` 的生死題。**後者是 Claude Code 的實質錯誤**——v1 把 `bottommost` 當成穩妥退路，但「壓在底層」與「可以點擊」本身互相拉扯 |
| 3 | MAJOR | 階段順序應改為 `S1 → D1 → W1 → U1 → R1 → P1` | **採納**。W1 含位置／大小持久化與 single instance，但那些的路徑、原子寫入、mutex 都由 D1 定義；原順序會逼實作者寫暫時性實作，且 W1 無法獨立驗證 |
| 4 | MAJOR | findings.md 主張大致正確，但 D4 規格不完整、R6 措辭過度 | **採納**。D4 補上 `File.Replace` 首次寫入路徑與 mutex ACL；R6 改為記錄實際 build 與 ESU 狀態。D1／D3／D5 經覆核無誤 |
| 5 | BLOCKING | 「半透明」與 `AllowsTransparency=false` 矛盾；`HWND_BOTTOM` 不保證層級；不應聲稱 `AllowsTransparency=true` 必然關閉 GPU | **採納**。**矛盾是 Claude Code 寫錯的**，S1 改為一律不透明，透明需求延到 W1。`AllowsTransparency=true` 現行 WPF 文件明載 layered window 可硬體加速，只是重繪成本較高——先前的說法過時 |
| 6 | MAJOR | AGENTS.md 的「只准寫 LocalAppData」與 D4 的 portable 模式矛盾；人工 gate 應記錄環境而非只有 pass/fail | **採納**。D4 補上「portable 是明文授權的例外」，AGENTS.md 同步修正；S1 回報格式改為必須記錄 Windows build、螢幕與 DPI 組態、publish 指令與發佈資料夾大小 |

另有一項 Codex 對失敗處置的修正（原列於第 7 題）：v1 寫「`workerw` 失敗就降級為實驗選項」，
Codex 主張應**判定不可交付並移除**。**採納**——留一個已知會壞的模式給使用者，
只會在日後某天無聲失敗。已寫入 D2。

結論：6 項全數採納，任務卡改版為 v2，階段順序調整，四份文件同步修正。**S1 可以開始實作。**
