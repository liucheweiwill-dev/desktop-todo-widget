# Agent Working Agreement

給 **Codex（實作工作者）** 的實作規範。架構決策、任務拆分與 review 由 Claude Code 負責，
見 `CLAUDE.md`。本檔與 `CLAUDE.md` 的 tech stack / Safety / commands 必須同步更新。

## 專案

**Desktop Todo Widget** — 貼在 Windows 桌面的待辦清單 widget。全英文 UI、
不出現在 Alt+Tab、可新增／刪除／勾選／編輯／拖曳排序、資料存本機 JSON、完全離線、
端機不需安裝任何 runtime。

技術決策與理由：`findings.md`。階段與任務卡：`task_plan.md`、`docs/`。

## 兩台機器的區別

- **開發機**不限，可裝任何工具。
- **端機**（實際跑 widget 的電腦）**不得要求安裝任何 runtime**。

「不能加依賴」只針對端機。dev 期的測試框架與分析器不隨成品出貨，不受限。

## Tech Stack

| 項目 | 選用 |
|---|---|
| 語言 / UI | C# / .NET 10 / WPF |
| TFM | `net10.0-windows`，RID `win-x64` |
| 發佈 | `SelfContained=true` |
| Win32 互操作 | P/Invoke（`System.Runtime.InteropServices`） |
| JSON | `System.Text.Json` |
| 測試 | xUnit（dev only） |

## 禁用清單

- `PublishTrimmed`（WPF 官方不支援，會產生執行期反射失敗）
- NativeAOT（WPF 不支援）
- 任何第三方 UI 框架／控制項套件（MahApps、DevExpress、Telerik、HandyControl…）
- 任何網路函式庫；任何 ORM／資料庫；任何序列化套件（用內建 `System.Text.Json`）
- 任何需要端機安裝的元件（WebView2、VC++ Redist…）

新增任何 NuGet 套件前必須先確認 BCL 與 Win32 API 無法解決，
並在回報的 Risk 欄位說明理由與「它是否會隨成品出貨」。

## 實作規則

1. **TDD 為預設**：先寫失敗測試再實作。**UI／視窗行為例外**——那類任務改為先寫下
   可重現的人工驗收步驟，並在回報中聲明採用哪種模式。
2. **只做任務卡 scope 內的事**：不得動 `Do not modify`，不得順手重構無關程式碼。
3. **P/Invoke 必須集中**：所有 Win32 宣告放在單一 interop 檔，不得散落各處。
   每個 P/Invoke 要註明對應的 Microsoft 文件頁面。
4. **不做超出當前任務的抽象**：不建 repository / service / DI container。
   這是個單人用的小工具，不是企業應用。
5. **UI 字串一律英文**，且不得寫死在多處；集中管理。

## 建置：你（Codex）不執行，由 Claude Code 執行

**本機 sandbox 拒絕 MSBuild 寫入 `obj/` 與工作區內任何新建的輸出目錄**
（`MC1000` / `MSB3491` / `MSB4018`，皆為 access denied），即使 `--add-dir` 給對、
restore 已預先完成也一樣。詳見 findings.md D7.1。

因此：

- **不要執行 `dotnet build` / `dotnet test` / `dotnet publish`。**
- **不要**為了讓建置通過而改輸出路徑、改專案設定，或寫到工作區外。
- **Claude Code 會在 sandbox 外執行並把真實結果（含編譯錯誤與測試失敗）回饋給你。**
- **不得偽造或推測測試輸出。** 回報的 Tests 段留空或註明「由 Claude Code 執行」。

因為你看不到紅燈也看不到綠燈，回報中**必須**列出：
每個公開型別與方法的簽章、你對測試意圖的假設、以及任何無法自行驗證的地方。

## 測試規範

- 指令：`dotnet test`（**由 Claude Code 執行**）。
- **只測有實質失敗風險的純邏輯**：JSON 序列化往返、schema 版本遷移、原子寫入、
  排序索引重算、資料驗證。
- **不要為 WPF 視窗寫自動化 UI 測試**——成本高、脆弱、且測不到本專案真正的風險
  （桌面附著行為）。那些走人工 exit gate。
- 測試必須驗證行為，不得只驗證方法存在。
- 測試不得依賴網路、不得依賴使用者的真實 `%LOCALAPPDATA%`（用暫存目錄）。

## Safety（不可違反）

- **零網路。** 不得出現 `HttpClient`、`WebClient`、`Socket`、`WebView`、
  任何 telemetry／自動更新／崩潰回報。
- **只准寫入 `%LOCALAPPDATA%\DesktopTodoWidget\`。**
  **唯一例外**：使用者明確啟用的 portable 模式可寫入 exe 同資料夾（findings.md D4）。
  除這兩處外不得有第三個寫入位置。不碰登錄檔（除非任務卡明確要求且使用者已同意）。
- **不得引入需要端機安裝的元件。**
- 不得 `git push`、不得散布成品，未經使用者明確授權。
- UI 字串一律英文。

## 回報格式（固定）

```markdown
## Summary
（做了什麼、動了哪些檔案）

## Tests
（實際執行的指令與真實輸出，不得省略或改寫；UI 任務貼人工驗收結果）

## Risk
（已知風險、未覆蓋情況、對任務卡的任何偏離）
```
