# 交接文件

> 建立日期：2026-09-04
> 交接方：Claude（Claude Code session 63a41053）
> 接收方：下一個接手的人 / agent

---

## TL;DR（5 分鐘讀完）

LightBee（repo 代號 WCALSS）是把「用網路攝影機當環境光感測器、自動三段調整螢幕亮度」的 Windows 系統匣工具從 spike 推向產品。今天做了三件事：對 `app/WcalssAmbientBrightness/` 雛型做穩定性 code review 並修掉 4 個高信心 bug（selftest 19/19 綠燈）、敲定技術選型（C#/.NET + WinForms v1 + 預留 WebView2/Palladio 遷移路徑）、產出規格書 `docs/spec.md` v0.1（草稿）。規格書的五個關鍵決策已由使用者拍板。下一步是建立專案定位錨點（`maze-project-init`）與補強／審查規格（`maze-spec-hardening` / `maze-spec-review`），使用者表示改天再做。

---

## 專案概述

- **專案名稱**：LightBee（對外名稱，今天定案）；WCALSS 為 repo / 命名空間 / 雛型代號
- **目標**：用一般網路攝影機當粗略環境光感測器，依環境明暗把螢幕亮度自動調到三個檔位之一（給沒有硬體環境光感測器的 Windows 桌機／外接螢幕使用者）
- **技術棧**：C# / .NET 8（產品目標 net10 LTS）、WinForms、WinRT `MediaCapture` / `MediaFrameReader`、WMI（`root\wmi` 亮度）、`dxva2.dll` P/Invoke（DDC/CI）；設定與紀錄存 `%AppData%`（JSON + CSV）
- **Repo**：`D:\AgentCoding\WCALSS`（本機；非 git repo）

---

## 當前狀態

**開發階段**：spike 完成 → 規格草稿階段（尚未進入 v1 開發）

**已完成**：
- **Spike 與端對端雛型**（今天之前）：`spike/camera-probe/` + `app/WcalssAmbientBrightness/`，完整記錄在 `docs/spike-report.md`（§1–§15）。Gate A = YES、Gate B = PARTIAL（只分得出三段）、Gate C = CONDITIONAL（需 Windows 相機共用設定）。§12.5 有使用者主觀 + 客觀雙重驗證的關燈調光成功紀錄。
- **穩定性 code review + 修補**（今天，透過 codex pane 執行）：
  1. `ValidationLog.cs` — `File.AppendAllText` 加 try/catch（CSV 被 Excel 鎖住 / 磁碟問題不再讓 timer 的 async void 逸出例外崩潰）
  2. `AmbientLightSensor.cs` — `samples` list 讀取改成在 `lock (gate)` 內快照再做 LINQ（消除與 `FrameArrived` threadpool 執行緒併發的集合列舉例外，這是「長時間跑會偶發卡住」的可能主因）
  3. `TrayContext.cs` — `SampleOnceAsync` 加外層 catch-all（未預期例外寫狀態列 + 驗證紀錄後吞掉，不崩潰）
  4. `AppConfig.cs` — `Load()` 對數值欄位做範圍 clamp（壞掉 / 舊版 config.json 不再讓設定視窗 `NumericUpDown.Value` 崩潰）
  - `dotnet run --project app/WcalssAmbientBrightness -- --selftest` → 19/19 通過，exit 0
- **技術選型定案**（今天，與使用者討論）：見下方「重要技術決策」
- **規格書 `docs/spec.md` v0.1（草稿）**（今天）：透過 `maze-idea-to-spec` 產出，9 個 section 全填滿。使用者已拍板 5 個關鍵決策：
  1. 產品名 = LightBee
  2. 自適應取樣（`SamplePacing`）納入 v1，但發布前須補真機端對端驗證
  3. 「微光」中段保留現值（使用者無筆電、難以營造純自然光環境測試）
  4. 使用者手動調亮度時，自動調整須讓步（偵測螢幕亮度偏離最後寫入值 → 暫停寫入、持續取樣；分級變動或冷卻期滿或使用者操作後恢復）
  5. 多螢幕一律套用相同亮度百分比，不做逐螢幕控制

**進行中（未完成）**：
- 無程式碼正在改。規格書停在草稿，等補強 / 審查。

**已知問題**：
- 目前無法在此環境 `dotnet build` 成功——雛型 App（PID 會變）正在使用者機器上執行，鎖住輸出 DLL（`winrt.runtime.dll`）。selftest 是用 `dotnet run` 跑的，另一條路徑。要 build 需先關掉正在跑的 App。
- 雛型仍用 preview 版 `Microsoft.Windows.SDK.NET 10.0.18362.6-preview`，build 有 `WinRT.Runtime` 版本衝突警告（規格已列為要改正的清理項）。
- spike 未跑完：Test 07（ROI）、Test 10 其餘 App（Discord/瀏覽器/OBS 共存）、Test 11（Busy）、Test 12（Privacy UX）、Test 13（USB 熱插拔）、Test 14（睡眠/喚醒）。
- 相機能看到螢幕自身光 / 窗戶反光時訊號會被污染（spike §14）——§14.7 只驗證了單一新角度有效，未系統性測試。
- 系統層級的 Windows 相機共用（FrameServer）在高頻搶相機下會卡死，App 內自動重連救不了，只能使用者手動關開設定或重插 USB（spike §13.4）。

---

## 下一步行動

1. （使用者說改天做）`maze-project-init` — 建立 `MAZE_PROJECT.md` 定位錨點；目前不存在。
2. `maze-spec-hardening` 或 `maze-spec-review` — 補強 `docs/spec.md` 的工程契約、邊界與驗收條件，或做唯讀審查。
3. `maze-spec-to-issues` — 把 §5 未打勾的功能項與 §8 的 8 個開放問題拆成可追蹤的 issue。
4. M1「雛型轉正」：改名 LightBee、`%AppData%\WCALSS\AmbientBrightness\` → `%AppData%\LightBee\` 遷移、WinRT 投影改正規（移除 preview 套件）。
5. 產品開發時把 repo 初始化為 git（目前不是）。

---

## 重要技術決策

| 決策 | 原因 |
|---|---|
| 語言留在 C# / .NET（產品對 net10 LTS，保留 `-windows10.0.19041.0` TFM） | 相機（WinRT `MediaCapture`）、亮度（WMI + DDC/CI）、格式協商全綁 Windows API 且 spike 已跑通；換 Go/Rust 等於把唯一被證明可行的 WinRT interop 重做、驗證歸零 |
| GUI v1 = WinForms；設定視窗做成薄的可替換層 | 表面小（托盤 + 一個設定視窗），WinForms 托盤 `NotifyIcon` 內建、有 `ShowBalloonTip`、啟動快；spike 已有實作 |
| 預留 WebView2（或 Photino.NET）+ Palladio 遷移路徑，托盤殼維持原生 | Palladio（`github.com/bext1998/palladio-design-language-system`）是 CSS token 系統，web UI 可直接 `@import palladio.css`；使用者要所有 AI 協作產出共用一套美學 |
| 拒絕 Wails v2 | Wails = Go 後端，會強制重做 WinRT 相機 interop，與「留 C#、不丟 spike」直接衝突 |
| 開機自啟 = 啟動資料夾 `.lnk`（每次啟動校正指向） | 使用者偏好；無需管理員權限、使用者可在工作管理員看到並停用 |
| 散布 = 單一 self-contained 單檔 exe，不簽章 | 使用者選擇；EV 憑證貴。接受 SmartScreen 首次執行警告 + 防毒誤判，靠下載頁說明 + SHA256 緩解。日後可評估 SignPath.io（OSS 免費）/ Azure Trusted Signing（~US$10/月） |
| WinRT 投影改用正規 targeting pack，移除 preview 版 `Microsoft.Windows.SDK.NET` | 消除 `WinRT.Runtime` 版本衝突警告 |
| 相機共用模式預設 `SharedReadOnly` | spike Test 10：起始讀數比 `ExclusiveControl` 穩定；Camera Sharing 關閉時 `SharedReadOnly` 會丟可偵測的 `0x80070020`，`ExclusiveControl` 則安靜收不到 frame |

---

## 注意事項（地雷與陷阱）

- **改 code 前先確認雛型 App 沒在跑**，否則 `dotnet build` 會因 DLL 被鎖失敗。用 `dotnet run -- --selftest` 可繞過（不同輸出路徑）。
- **不要對疑似故障或被搶占的相機連續高頻開關**（spike §13.4 教訓）——`SamplePacing` 的快模式已有 30 輪上限與失敗退避，改動取樣節奏時要維持這個約束。
- **驗證紀錄的完整性是這個專案的核心價值**：每筆 `validation-log.csv` 都應能回溯到 spike 報告的某個 Test/Gate。加新行為時要一併標 `ValidatedBy`。
- **Gate B 是 PARTIAL 是產品的永久限制**，不是待修項——規格明講只做三段、不假裝連續調光。
- **相機擺放角度是外部變因**，App 只能引導不能強制；規格的成功指標假設「§14.7 已驗證的角度」。
- codex pane（`wN:p2`，Herdr workspace `wN`）今天執行了修補，其 session 內有完整 diff 脈絡；同名 `codex` agent 在 `wM` 也有一個（Palladio 專案），用 pane id 區分。
- 記憶檔在 `C:\Users\tiger\.claude\projects\D--AgentCoding-WCALSS\memory\`：`wcalss-tech-stack-decisions.md`、`palladio-unified-aesthetic.md`。

---

## 重要文件位置

| 文件 | 路徑 |
|---|---|
| 規格書 | `docs/spec.md`（v0.1 草稿） |
| Spike 報告 | `docs/spike-report.md`（§1–§15，含所有實測數據與 Gate 判定） |
| 交接文件（本檔） | `docs/HANDOFF.md` |
| 雛型 App 設計說明 | `app/WcalssAmbientBrightness/README.md` |
| DDC/CI 探測工具 | `tools/Probe-DdcCi.ps1` |
| 決策紀錄 | （尚無獨立 DECISIONS 檔；技術決策見本檔與 spec §6） |
| 專案定位錨點 | （尚無 `MAZE_PROJECT.md`，待 `maze-project-init`） |
