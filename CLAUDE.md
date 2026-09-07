# CLAUDE.md

## 專案

**Desktop Todo Widget** — 一個貼在 Windows 桌面上的待辦清單小工具。
全英文介面、不出現在 Alt+Tab、可新增／刪除／勾選／編輯／拖曳排序，
資料存本機 JSON，**完全離線**，端機不需安裝任何 runtime。

| 文件 | 內容 |
|---|---|
| `findings.md` | 技術決策與理由（選型、已定案決策、風險） |
| `task_plan.md` | 階段拆分、依賴、阻擋項、驗證指令 |
| `docs/<phase>-task-card.md` | 各階段任務卡 |
| `progress.md` | 進度日誌、測試結果、事故（第一個階段完成時建立） |

## 兩台機器的區別（最容易搞錯的前提）

| | 限制 |
|---|---|
| **開發機** | 不限，可裝任何工具（.NET SDK 10.0.300 已就緒） |
| **端機**（實際跑 widget 的電腦） | **不得要求安裝任何 runtime**——不裝 .NET Runtime、VC++ Redist、WebView2、Python、Node |

所有「不能加依賴」的判斷都只針對**端機**。開發期的測試框架、分析器等不隨成品出貨，不受此限。

## 角色分工

**Claude Code = 架構負責人**：做架構決策、寫階段任務卡、review Codex 的實作與 diff、
做安全審查。**不直接寫 feature 程式碼**，除非任務本身就是拆任務／寫任務卡這類架構性工作。

**Codex = 實作工作者**，讀根目錄 `AGENTS.md`。

**環境造成的分工調整（2026-09-07，見 findings.md D7.1）**：Codex 在本機 sandbox 內
**無法執行任何建置**（MSBuild 被拒絕寫入 `obj/` 與工作區內新建目錄）。因此：

| 工作 | 誰做 |
|---|---|
| 撰寫實作與測試 | Codex |
| `dotnet build` / `test` / `publish` | **Claude Code**（sandbox 外） |
| 把真實建置與測試結果回饋給 Codex | Claude Code |
| 修正 Codex 因無法編譯而產生的編譯錯誤 | Claude Code 可直接修 |

派工 prompt 必須明講「不要嘗試 build」，並要求 Codex 不得偽造測試輸出。
這是 TDD 的實質降級，補償方式是要求 Codex 回報公開簽章與對測試意圖的假設。

## 開發流程（Plan → Review → Implement → Review → Commit）

沿用 NewsSearch 專案驗證過的迴圈，不可跳步：

1. Claude Code 提出規劃
2. Codex review 規劃可行性（`codex exec --sandbox read-only`，不寫 code）
3. Claude Code 逐項判讀後定案，產出任務卡
4. Claude Code 拆小任務給 Codex（固定格式，見下）
5. Codex 依定案任務卡實作，TDD 為預設，回報用 Summary / Tests / Risk
6. Claude Code review：scope、Safety、測試是否真的驗證行為
7. 通過才 commit（git 指令經使用者權限確認）

**未經使用者授權不得 push。**

呼叫 Codex 的實務細節（`< /dev/null` 必要、`--add-dir`、本機 cp950 編碼坑）
與 NewsSearch 的 `.claude/skills/social-phase-loop/SKILL.md` 相同，該檔可直接參考。

## 一般行為準則

1. **先驗證最可能推翻設計的假設**，不要先做好做的部分。本專案該假設是「嵌入桌面」——
   它沒有受支援的公開 API，見 findings.md D2。
2. **驗收條件必須包含功能的目的本身，不只是它的機制。**
   「視窗有出現」不等於「它真的待在桌面層且可點擊」。
3. 架構有多種合理做法時列出來讓使用者選，不要偷偷挑一個。
4. 手術式修改：review diff 時確認改動對應任務卡 scope，沒動到 `Do not modify`。
5. 不盲目採納 review 意見，逐項判讀並記錄理由。

## Tech Stack

| 項目 | 選用 | 備註 |
|---|---|---|
| 語言 / UI | **C# / .NET 10 (LTS) / WPF** | 理由與被否決的選項見 findings.md D1 |
| 平台目標 | `net10.0-windows`，`win-x64` | |
| 發佈 | `SelfContained=true` | 端機零安裝的唯一手段 |
| Win32 互操作 | `System.Runtime.InteropServices`（P/Invoke） | Alt+Tab 與桌面附著需要 |
| JSON | `System.Text.Json`（內建） | 不加序列化套件 |
| 測試 | xUnit（**僅 dev 期，不隨成品出貨**） | 只測純邏輯，UI 走 exit gate 人工驗證 |

**明確禁止**：`PublishTrimmed`（WPF 官方不支援）、NativeAOT（WPF 不支援）、
任何 UI 框架或控制項套件（DevExpress／Telerik／MahApps 等）、
任何網路函式庫、任何 ORM／資料庫。

## Safety（不可違反）

- **零網路。** 這個程式不得有任何 outbound 網路呼叫——沒有 telemetry、沒有自動更新、
  沒有崩潰回報、沒有字型／圖示 CDN。review 時要能在整個 codebase 搜不到 `HttpClient`、
  `Socket`、`WebClient`、`WebView`。
- **不寫入安裝目錄以外的系統位置。** 資料只寫 `%LOCALAPPDATA%` 下的專案資料夾，
  或（portable 模式）exe 同資料夾。不碰登錄檔（開機自啟另議，需使用者明確同意）。
- **不得引入需要端機安裝的元件。**
- **UI 字串一律英文。**
- 未經使用者授權不得 push 或散布成品。

## 拆任務給 Codex 的格式

```markdown
## Task ID
### Goal
### Files to edit
### Do not modify
### Requirements
### Acceptance tests
### Commands to run
### Risk notes
```

原則：每個任務盡量少於 5 個檔案且可獨立測試；`Commands to run` 必須包含完整
`dotnet build` 與 `dotnet test`；純邏輯一律要求補測試，UI 行為改用可重現的人工驗收清單。

## 跟 AGENTS.md 的關係

`AGENTS.md` 是給 Codex 的實作規範。tech stack、Safety、commands 或角色分工變動時，
兩份要一起改。
