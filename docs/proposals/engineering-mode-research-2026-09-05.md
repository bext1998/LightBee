# LightBee 環境光階梯研究與 Engineering Mode — 討論提案（未批准）

> 狀態：**提案 / 討論用**，尚未取代 `docs/spec.md`。任何 Agent 不得將本文視為已核准需求。
> 2026-09-05 更新：經 codex／pi 兩邊審查（見 §21），並修正手機角色定義（見 §7 修正說明）。

## 0. 文件定位
需區分兩類資訊：
- **現行規格**：`docs/spec.md` 已定義、具有現有 spike／雛型依據的內容。
- **本次提案**：針對環境光場景覆蓋不足提出的新研究方向，尚未取代現行規格。

## 1. LightBee 現行規格基線
Windows 系統匣常駐工具，Webcam 作為粗略環境光感測器，依環境明暗調整螢幕亮度。

```
Webcam → 環境亮度讀值 → EMA 平滑 → 三段式分類 → 目標亮度 → 速率限制器 → WMI/DDC-CI → Monitor Brightness
```

現行三段：

| 分級 | 預設條件 | 目標亮度 |
|---|---:|---:|
| 暗 | `< 0.01` | 15% |
| 微光 | `< 0.20` | 45% |
| 有開燈 | 其餘 | 80% |

門檻與目標值可於設定修改。配套：遲滯、連續兩次確認、自適應 EMA、`SamplePacing`、漸進亮度調整、使用者手動亮度讓步。Lazy 相機取樣，預設約 5 秒一輪，每輪 Open→Sample→Release。

## 2. 現行規格與本次討論的衝突
`docs/spec.md` 目前將「連續或多級調光」列為非目標——原因是 spike 資料不足（Gate B 對 `normal`/`bright` 的區分只得到 PARTIAL），不是架構禁止。三段式仍是**已驗證基線**；6～8 段或其他有限階梯屬於**新研究假設**，資料收集完成前不應直接修改 Runtime 為多階梯。

## 3. 新發現的問題
現有三種測試環境無法涵蓋真實環境變化（全暗/微弱室內光/一般室內燈/強照明/窗戶自然光/側光/背光/角度差異/局部亮暗/距離反射條件）。

> **問題陳述：LightBee 現有三段式模型是否因原始測試樣本不足，而低估 Webcam 可辨識的有效環境光階梯數？** 這是需要資料回答的問題，不是已知結論。

## 4. 新研究假設：有限階梯式亮度
`有限環境光階梯 → 有限螢幕亮度階梯`，暫不研究無極／連續映射（Webcam 非 lux sensor、自動曝光干擾、資料不足、LightBee 定位是粗略感知工具）。研究目標：判斷 3 段是否有證據支持擴增到 4/5/6/8...段。階梯數不得先寫死；先前討論的「6～8 階」僅供實驗設計參考。

## 5. 分級模型設計原則
若實驗支持多階梯，保留 `Sensor Value → Light Level → Target Brightness` 兩段映射（而非 `Sensor Value → Brightness` 一步到位），使門檻與目標值可分別調整。現有 EMA、遲滯、連續確認、SamplePacing、速率限制器作為基線保留，除非實驗證明需要修改。

## 6. 新一輪資料收集目的
不是直接產生正式亮度設定，而是回答：
1. Webcam 能穩定區分多少個環境光區間？
2. 哪些讀值區域具可重複性？
3. 哪些區域受自動曝光影響而無法分辨？
4. 現有 `0.01`/`0.20` 門檻是否需修改？
5. 多階梯是否比三段式增加實際使用價值？
6. 階梯增加後，遲滯與 SamplePacing 是否仍適用？
7. 不同光源方向是否造成分類錯誤？

## 7. 手機作為實驗載體（2026-09-05 修正）
> **修正說明**：先前版本誤將手機定位為「顯示灰階畫面的測試光源」。正確設計是：手機**不是**光源，而是 **Mobile Reference Sensor**——手機 Camera 與桌機 Webcam 在同一真實環境光條件下同步收集資料，兩者觀察的是同一個真實光源，不是手機螢幕。因此「手機同時是光源又是控制器，切換 Web UI 會改變光源條件」這項疑慮不成立，已撤回。

修正後資料流：

```
真實環境光
   ├─→ 桌機 Webcam ──→ LightBee raw / EMA / classifier data（正式 Sensor）
   └─→ 手機 Camera ──→ reference brightness data（參考訊號，非正式 Sensor）
```

手機 Web UI 同時負責：顯示手機 Camera 資料、標記實驗條件、控制資料記錄（Session Start/Stop/Label）。手機 Camera 資料是參考訊號，不是 lux 真值，也不取代桌機 Webcam 作為正式環境光 Sensor。

手機 Camera 亦可作為 P0 pilot 的**對照來源**：若手機 Camera 能反映環境變化，但桌機 Webcam raw mean 被壓縮到狹窄區間，可協助判斷瓶頸來自桌機 Camera pipeline／Auto Exposure，而非環境光本身缺乏變化（見 §21）。

## 8. 真實環境光測試的限制
真實環境光源受房間本底光疊加、日照角度隨時間變化、非受控的多重光源干擾，資料須明確記錄光源條件（燈具/自然光/方向/距離）以利事後歸類，不能假設單一乾淨變因。控制環境測試（固定單一光源條件，建立可重複資料）與真實使用環境測試（多重光源、日常場景，驗證泛化能力）應分層收集、分開分析。

## 9. Engineering Mode 定位
非正式一般使用者功能，用途：Calibration、Dataset Collection、Runtime Diagnostics、Classifier Validation、Spike/演算法驗證。候選啟動：`LightBee.exe --engineering`。不改變一般 Runtime 預設行為。

## 10. Engineering Mode 與 Runtime 的關係
避免建立第二套演算法：

```
LightBee Runtime
├─ AmbientLightSensor / EMA / Classifier / SamplePacing / Brightness Controller / Manual Override
└─> Engineering Mode (Web API / Web UI / Diagnostics / Dataset Recorder)
```

Engineering Mode 只**讀取**正式 Runtime 的 Raw Sensor Value、EMA Value、Candidate/Confirmed Level、Target/Actual Brightness、SamplePacing 狀態、Manual Override 狀態、Camera 錯誤/重連狀態，不得複製 Classifier 或 Sensor 實作。

## 11. 現有 Validation Log 與新 Dataset 的關係
`%AppData%\LightBee\validation-log.csv` 記錄「取樣→判定→套用」，是正式 Runtime 行為紀錄。Engineering Dataset 只增加實驗所需 metadata（Session ID、Experiment Level、手機參考值、光源條件、人工標記、備註），可引用/合併同時間的 Runtime validation data，避免兩套 logger 各自重新取得 Sensor 狀態。

## 12. Engineering Web UI
Session（Start/Stop/Label）、Desktop Runtime（Raw/EMA/Current Level/Target/Actual/Sample State）、Experiment（Record/Light Source Label/Notes）、Phone Reference（Camera/Brightness Statistics）。第一版不需完整管理後台。

## 13. 手機 Camera 參考資料
手機瀏覽器取像、手機端計算統計值（mean/median/p10/p90），不傳完整照片回桌機。手機 Camera 參考值屬輔助資料，不作為分級真值（不同手機 Camera pipeline/Auto Exposure/ISP/擺放位置皆不同）。

## 14–16. Tailscale 決策與架構邊界
遠端連線採 **Tailscale**（先前考慮的 Tailcat 不採用：介面穩定性不足、不想自行維護 P2P transport、核心工作是資料收集不是網路技術研究）。使用者已用 GitHub 帳號建立 Tailscale 帳號。

```
手機 → Tailscale → Tailscale Serve → 127.0.0.1:<port> → LightBee Engineering Server
```

Server 預設只監聽 localhost（例：`127.0.0.1:8765`，port 待實作決定），不監聽 `0.0.0.0`。Tailscale 負責裝置身分/加密/遠端連線/Tailnet 存取；LightBee 不實作 VPN/NAT traversal/公開 endpoint/帳號管理。**架構邊界**：LightBee Runtime 不依賴 Tailscale；Engineering Mode 可搭配 Tailscale Serve，但移除 Tailscale 後仍應可透過 localhost 使用。不使用 Tailscale Funnel（工程模式無公開 Internet 服務需求）。

## 17. Engineering API 原則
只提供固定工程功能，概念 API：`GET /status`、`POST /session`、`POST /experiment-label`、`GET /dataset`（實際路由待實作決定）。禁止 `/exec` `/shell` `/powershell` 等任意程式執行介面。

> **修正（codex 審查）**：原提案含 `POST /sample`（觸發桌機取樣），但這不符合「唯讀 Runtime」的邊界——會改變相機存取時機、干擾既有 Lazy 取樣與 SamplePacing 行為。已移除；若未來確實需要手動觸發取樣，應明確列為控制命令，另案評估對 Runtime 的影響。

## 18. 對現行 spec.md 的影響
原始順序：現行三段式 Runtime → 建立 Engineering Mode → 擴充資料收集能力 → 控制環境測試 → 真實環境測試 → 分析可辨識階梯 → 決定是否修改分級模型 → 有證據後更新 spec.md。**第一階段任務不是「實作八段式自動亮度」**，而是「建立足以驗證多階梯假設的資料收集與診斷能力」。

> **待決（pi 審查）**：pi 建議把順序反過來——先用現成工具（既有 coldstart/camera-probe + CSV + 人工筆記）跑 P0 pilot，證明「中間地帶確實有可分離、且真實使用中會出現的階梯」之後，再決定要不要投入 Engineering Mode（Web UI + Tailscale + Dataset 平台，估 5–8 工作日）。理由：pilot 幾乎零程式碼、1–2 天可跑完，能在投入平台前先排除「三段式就是這顆相機的天花板」這個 No-Go 結果。此順序調整尚未拍板，見 §21。

## 19. AI Agent 實作邊界
第一階段避免修改：現行三段 Classifier 行為、現有亮度目標值、EMA 參數、SamplePacing 邏輯、Manual Override、WMI/DDC-CI 控制、正式 WinForms 設定流程。集中於：Engineering Mode + Runtime Observability + Experiment Dataset + Web UI + Tailscale 使用說明/整合邊界。若現有 Runtime 缺少可供 Engineering Mode 讀取的狀態，可做最小介面抽取，不應為此重構整個 Runtime。

## 20. 本次討論的核心決策

**已確定：**
1. 現有三段式仍是規格基線。
2. 真實環境複雜度超過既有三組測試條件。
3. 研究有限階梯式亮度模型。
4. 無極調光不在本輪研究範圍。
5. 使用手機建立可控制資料收集流程。
6. 建立 LightBee Engineering Mode。
7. Engineering Mode 使用 Web UI。
8. 使用 Tailscale 提供遠端連線。
9. Tailscale 不成為正式 Runtime 依賴。
10. 正式環境光 Sensor 仍是桌機 Webcam。

**尚未確定：**
1. 最終階梯數。
2. 新分級門檻。
3. 各階梯目標亮度。
4. 手機 Camera 是否需要納入正式實驗資料（作為對照來源，見 §7 修正）。
5. 手機 Camera 的統計方法。
6. Engineering Dataset 最終 schema。
7. Engineering Web API 最終路由。
8. 是否修改 `spec.md` 的「多級調光非目標」。
9. 桌機 Webcam 的 exposure/gain metadata 是否可讀取（見 §21，可能是比像素均值更有價值的訊號）。
10. 是否採 pilot-first 順序，把 Engineering Mode 建置延後到 P0 pilot 有結果之後（見 §18、§21）。

## 21. 審查紀要（codex / pi，2026-09-05）

本節彙整兩個角度的審查結論，供後續決策參考；不代表已核准的修改。

**codex（實作可行性）**：
- Runtime 唯讀觀察層可行，建議建立 `RuntimeObservationStore`（Runtime 只 `Publish(snapshot)`，Web 層只 `GetLatest()`），現有資料分散在 `TrayContext`/`BrightnessMapper`/`SamplePacing` 的私有欄位與區域變數，需最小介面抽取。
- `POST /sample` 不符合唯讀邊界，已移除（見 §17）。
- 內嵌 ASP.NET Core Minimal API + Kestrel、只綁 `127.0.0.1`，成本中等；主要風險是 threadpool（Kestrel handler）與 UI thread（WinForms/TrayContext）的邊界，handler 不得碰 UI 控制項或直接呼叫相機。
- Tailscale Serve 架構正確，但有實作陷阱：需要 tailnet 啟用 MagicDNS + HTTPS 憑證（機器名會進公開憑證透明度紀錄）；仍受 Tailnet ACL/grants 管控，不能假設「同一 Tailscale 帳號」等於有權限，手機裝置要被明確授權；LightBee 不應自行執行/持久化 Tailscale CLI，交由使用者手動設定。
- Phase 1（觀察層 + Dataset + localhost API + Web UI 骨架，不含手機 Camera 統計）估 **5–8 工程工作日**；提醒 M1（改名 LightBee、`%AppData%` 路徑遷移）尚未完成，Dataset 不應先寫死新舊路徑。

**pi（研究設計與資料科學）**：
- **關鍵缺口**：原提案只讀桌機 Webcam 的像素均值（raw sensor value），但 spike 已證明 AE 自動曝光會把中高亮場景壓縮到 0.45–0.48 附近，像素亮度在此區間幾乎不攜帶資訊；真正有資訊量的是 exposure/gain metadata。不收集此欄位，§6 問題 3（哪些區域受自動曝光影響）無法回答。
- 手機雙重角色的疑慮已隨 §7 修正撤回（手機是同步觀察真實環境的參考感測器，不是操作者改變的光源）。
- 列出一批需在實驗協定中明文控制的混淆變數：真實光源疊加/方向未受控、PWM 調光（若涉及任何自體發光校準源）、AE 隨時間漂移（建議插入參考階偵測）、相機/手機擺放距離角度、螢幕反光回饋等。
- 「3 段 vs N 段」判準建議：相鄰階 95% 區間不重疊 + Cohen's d ≥ 2 + ≥3 個 session 一致成立；量級約 12–15 階梯 × 8–12 重複 × ≥3 session ≈ 300–500 筆，成本低（每 session 1–2 小時）。可分離性只是必要條件，還需第四條件：該區間在真實日常使用中實際出現——這點可直接用現有 `validation-log.csv` 的讀值分布來看，零成本。
- §20「尚未確定」清單中，手機 Camera 統計方法、Dataset schema、API 路由都應等 pilot 結果出來後再定，先設計是過度工程化；階梯數/門檻/目標亮度本身是研究產出，不是輸入。
- 建議 **pilot-first**：用現成工具（camera-probe 加 exposure metadata 讀取、coldstart 工具跑真實環境對數階梯序列）先跑 P0（約 2 個半天、近乎零新程式碼），確認「中間地帶是否有可分離且會實際出現的階梯」，再決定是否投入 Engineering Mode 全平台。三種可能結果（值得投資 N 段 / 轉向 exposure-based 訊號 / 誠實 No-Go）都算成功的 spike。

**尚待使用者拍板**：是否採 pilot-first 順序（§18/§10）、exposure/gain metadata 探測是否列為立即的下一步。

## 22. 範圍二次修正：Engineering Mode 不是平台，是薄型採集工具（2026-09-05）

> 本節為使用者對 §9–§17 的正式範圍澄清，**覆蓋**前述章節中任何暗示「建置完整平台」的描述。保留 §21 的風險提醒（AE/ISP 壓縮、metadata 可行性），但**拒絕**把 P0 轉成受控 lux 實驗。

### 22.1 核心定位修正
「Engineering Mode／工程模式」先前用詞造成範圍誤解。正確定位：

> **一個薄型、臨時、供開發者使用的 Web 資料採集介面**，核心價值是降低資料收集操作成本，**不是**建立新的軟體平台。

用途：手機瀏覽器查看參考 Camera 資料／查看 Runtime 既有必要狀態／標記環境條件／記錄時間戳與實驗資料／讓使用者離開桌機位置仍能收集資料／透過 Tailscale 從手機連回桌機。

### 22.2 第一版規模

```
手機瀏覽器
│
├─ Phone Camera preview / brightness stats
├─ Environment label
├─ Record
└─ Desktop LightBee status
        ↓
極薄 HTTP API
        ↓
CSV / 現有 validation data
```

單頁 Web UI，後端只有少量固定 endpoint，不需要完整管理後台。

### 22.3 明確排除（第一版不做）
`RuntimeObservationStore` 完整架構、Event Bus、完整 telemetry framework、資料庫、完整 Session 管理系統、使用者帳號系統、權限管理後台、完整診斷 Dashboard、即時圖表平台、WebSocket、任意遠端命令執行、Tailscale CLI 自動管理／登入／帳號管理、Funnel、Android App、重新實作 Classifier／Camera Sensor、重新設計正式 Runtime。若現有 Runtime 狀態無法取得，只抽取第一版介面**所需的最小資料**——不建通用觀察層。

### 22.4 Tailscale 角色（修正 §14–17）
LightBee **不管理** Tailscale，只提供 `127.0.0.1:8765` 這樣的 localhost Web Server；由使用者自行透過 Tailscale Serve 暴露給自己的 tailnet。Tailscale 是開發環境工具，不是 LightBee Runtime 依賴——與 §16 原則一致，但強調 LightBee 連「引導使用者跑 Tailscale CLI」都不做。

### 22.5 手機角色（維持 §7 修正，重申邊界）
手機不是受控光源，用途是 **Mobile Reference Camera + Web UI Controller**。手機 ALS 可作**選配輔助資料**，不作架構前提（修正 §21 pi 提出的「ALS 取代 Camera 當主要參考」建議——保留 ALS 作為可選手段，但不強制、也不因此改變手機 Camera 的定位）。

```
真實環境光
├─ Desktop Webcam → LightBee
└─ Phone Camera   → Reference data
```

手機 Camera 不是真實 lux ground truth，也不取代桌機 Webcam。

### 22.6 資料收集原則
第一版只收研究需要的資料，例如：`timestamp`、`environment_label`、`phone_camera_value`、`desktop_raw_value`、`desktop_ema_value`、`desktop_level`、`target_brightness`、`actual_brightness`——實際欄位由 P0 探測結果調整，**不需要**先建立通用 Dataset schema。

### 22.7 與 P0 Pilot 的關係
Engineering Mode 不是 P0 的前置大型工程。**P0 仍先確認**：現有 `validation-log` 分布、Desktop Webcam raw value 的資訊量、exposure/gain metadata 是否可取得、手機 Camera 作為參考訊號的可用性。**若資料收集操作造成明顯摩擦，才實作「薄型 Web Collector」**——這個 Collector 本身可視為 Engineering Mode v0，不代表已承諾建立完整工程平台。

**修正 §21 pi 的建議**：不採「可調光檯燈/距離平方反比法製造受控照度階梯」這條路線作為 P0 的架構前提——**第一輪資料以真實使用環境為主，必要時加入少量可重複光源條件**（例如固定房間燈開關組合），不把 P0 整個轉型成受控 lux 實驗室。

### 22.8 B 階段實測結果（2026-09-05，codex 執行）

已新增獨立探測路徑 `dotnet run --project app\WcalssAmbientBrightness -- --probe-metadata`（不影響既有 Runtime，`--selftest` 22/22 仍全過）。實機結果：

```
Device: USB Camera
Source: 640x480 / NV12
ExposureControl: unavailable（無標準曝光時間，讀不到）
Exposure (legacy): available; Value=-7; Auto=True（舊式控制可讀，但單位由驅動自訂，無法換算成有意義的量）
IsoSpeedControl: unavailable（無標準 ISO/增益）
```

**結論：這顆桌機 Webcam 讀不到可用的 exposure/gain metadata。** 舊式 `Exposure=-7` 可留作弱對照欄位（之後在不同真實光線下重複讀取，觀察是否隨環境變化），但現階段不能當作 Q3（AE 造成訊號壓縮）的乾淨答案來源。

依 pi 訂的備援規則（§21）：桌機 metadata 已確認讀不到 → 下一步需確認**手機瀏覽器能否鎖曝光**（`MediaStreamTrack.getCapabilities()` 查 `exposureMode`/`exposureTime`）。若手機也鎖不了，Q3 就沒有 AE-free 的訊號來源，此時才啟用手機 ALS lux app 作為備援參考（§22.5，非架構前提，僅此情境下觸發）。

**手機端結果（2026-09-05，實機測試，Android/Chrome）：可以鎖定曝光。**

```
exposureMode capabilities: ["continuous", "manual"]
exposureTime range: 0.4166 – 2067.7776 ms（step 0.1）
manualSupported: true
exposureTimeApplied: true（applyConstraints 成功套用，非僅宣稱支援；exposureTime 從自動 333ms 實際變為指定的 1034ms）
```

額外可控：ISO（50–3200）、色溫（2850–7000K）、曝光補償（±2 EV）。

**結論：不需要啟用 ALS 備援。** 手機相機鎖定曝光後可作為 AE-free 的乾淨參考訊號，解決「雙邊塌縮則無法區分」的限制（pi §21）——只要 C 階段條件 session 開始前先在手機端套用 `exposureMode: manual` 並固定 exposureTime，其讀值變化就不會被自動曝光吃掉，能拿來對照桌機 Webcam 的塌縮情形。B 階段結束。

### 22.9 實作原則
能直接讀現有資料，就不新增狀態層。能寫 CSV，就不建資料庫。能用 HTTP request，就不加 WebSocket。能由 Tailscale 處理，就不在 LightBee 內實作。能用單頁 Web UI 完成，就不建立前端框架平台。**Engineering Mode 的規模應保持在「資料採集工具」，而不是「內部開發平台」。**
