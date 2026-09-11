# LightBee 相機斷連問題 — 待辦

建立：2026-09-08
背景：實驗程式 `app/WcalssAmbientBrightness/` 的相機（Sonix 通用晶片，`VID_5258 & PID_4A55`）
在使用中會進入「安靜無 frame」狀態，時好時壞、會逐步惡化，靠裝置自己重新列舉才恢復。
程式內重連（同程序重建 MediaCapture）對此無效。相機只是溫溫的 → 排除發熱。

---

## 進度更新 2026-09-08（晚）

已做完：§4 重連退避、§5 整段（HARDWARE.md + 啟動記 VID/PID + 退避通知）、
§2 曝光鎖測試（C270 legacy Exposure 可鎖）、新增 `--sensitivity-probe` 給 §3 校正用、
`--selftest` 綠燈驗證、git 全部整理提交。commit `92c6e1b`…`bf803dc`。

已做完（追加）：五條件感光實測完成、C270 暫定門檻（暗<0.06 / 微光<0.28）已套進
`DefaultBands` + `config.json` + SelfTest，commit `b600dc0`。自助校正流程開了 issue #16。

還沒做（要你）：
- **重開 App**：`config.json` 的 C270 + shared + 新門檻都要關掉 App 再開才生效。
- §2 連續跑一晚看 csv 會不會整片 `no-frames`。
- §3 正式校正（鎖曝光重跑，之後）。
- §4「Persistent 常駐串流」那項還在等你拍板。

---

## 1. 硬體（先做，最高優先）

- [x] 買 **Logitech C270**（約 NT$800）。不要再買無牌「1080p」相機——病因是韌體/晶片，不是規格。
- [x] **清掉幽靈裝置**：裝置管理員 → 檢視 → 顯示隱藏的裝置 → 「相機」和「影像裝置」底下，
      把灰色那個 "USB Camera"（Status: Unknown，`...7&2C3EB27F&0&0000`）右鍵解除安裝 →
      拔相機 → 等 10 秒 → 重插。
- [x] 換 **USB 孔**：插主機**後方主機板** USB 2.0 孔，不走 hub、不走前面板。換一條線。
- [x] **關省電**：裝置管理員 → 該相機 + 它的 USB Root Hub → 內容 → 電源管理 →
      取消勾「允許電腦關閉這個裝置以節省電源」。

## 2. 新相機到手後的驗證

- [x] 曝光鎖測試（`--probe-metadata` 已加入實際 SetAuto(false) 測試）：**C270 現代 ExposureControl
      不支援、legacy `Exposure` 可鎖**（`TrySetAuto(false)` OK）。→ §3 走「能鎖曝光」路線。
- [x] 連續跑一整晚（2026-09-10 14:19 ~ 09-11 09:15，18h55m，無重啟）：
      `camera-diagnostics.csv` 0 reconnect、0 sample 失敗、0 `stop_async_timed_out`、
      `failed_step` 全 `none`，找不到 `no-frames`；`validation-log.csv` 分級切換與燈光時間軸吻合。
- [x] 已把 C270 從 HARDWARE.md 的「驗證中」移到「已驗證可用」正式清單。

## 3. 重新校正（Gate B）— 換相機後本來就要做  →  GitHub issue #13

**新工具**：`WCALSS.AmbientBrightness.exe --sensitivity-probe 12`（先關 App）——用產品的 Lazy 取樣
路徑連跑 12 次，印出每次 raw luminance + min/max/mean/stddev/range，最後接曝光/ISO 能力。

- [x] 五種條件都跑了（2026-09-09）：暗 ~0.02 / 微光 ~0.15 / 開燈 ~0.44；相機朝亮螢幕 0.188±0.09（廢）。
      完整數據在 issue #13 comment。
- [x] **暫定門檻已套進軟體**（commit `b600dc0`）：暗 `<0.06`、微光 `<0.28`。改了 `DefaultBands`
      + 本機 `config.json` + SelfTest（37/37）。**重開 App 生效**。
- [x] 路線已定：**能鎖曝光** → 用 `controller.Exposure.TrySetValue()`（driver-defined 單位），
      不是 `ExposureControl.Value`（C270 現代 API 不支援）。
- [ ] （正式校正，之後）鎖曝光重跑五條件、相機角度固定不對螢幕、檢查能否分 normal/bright、
      `HysteresisMargin`（現 0.02）可能要配合微光段 range ~0.035 加大。
- [ ] 把校正做成設定內的自助流程 → **issue #16**（你提議的）。

## 4. 程式碼（與相機無關，仍然要做）

- [x] SamplePacing 暗房永久快模式修正 — 邏輯層 selftest 通過（commit `3fcced6`），**未經真機驗證**。
- [x] Log 時間改本地時間（`+08:00`），CSV 欄名 `timestamp_utc` → `timestamp_local`
      （commit `2277bec`）。**已驗證**：隔離目錄 `dotnet build` + `--selftest` 37/37 綠。
- [x] **重連退避**（commit `acddf64`）：連續 3 次重連仍取不到 frame → 降到每 60 秒慢速重試、
      工作列警告一次、螢幕維持最後有效讀數；任何一次取樣成功即回正常頻率。停掉了「每 5 秒取樣
      + 每 15 秒重連」的緊迴圈。`TrayContext` 一貫不進 selftest，需靠 §2 整晚真機跑驗證。
- [ ] （**待你拍板**）Lazy 每次取樣重建 → Persistent 常駐串流（開一次、讀最新 frame）。
      **未證實是根因**；若與發熱相關可能更糟；但能把每小時 ~720 次相機開關降到接近 0。
      要證實需要 Persistent vs Lazy 對照跑一輪。
- [ ] （延後）`camera-diagnostics.csv` 加 `interval_ms` / `fast_mode` 欄（需改 TrayContext 呼叫點）。

## 5. 文件 — HARDWARE.md（已建立） + README 指標段  ✅ 全部完成

- [x] **已驗證可用清單**（C270，標「驗證中」） + **已知有問題**（Sonix `VID_5258` 通用晶片，點名晶片）。
- [x] **2 分鐘自我檢查**：`--probe-metadata` 看 `ExposureControl`；裝置管理員看灰色幽靈相機；
      先連續跑一晚看 csv 會不會整片 `no-frames`；USB 供電 / 省電。
- [x] **回報規則**（中性）：「非清單內的相機屬 best-effort；開 issue 前請先確認在清單內相機上也能重現。」
- [x] **程式自我提示**：啟動時 `camera-diagnostics.csv` 記 `phase=device-info` 含 VID/PID；
      退避時工作列通知「疑似相機韌體問題，見 HARDWARE.md」。
- [x] **caveat**：「目前取樣設計對廉價相機較不友善，在改為常駐串流前，需要韌體穩定的相機。」
      （寫在 HARDWARE.md 開頭。）

## 6. 收尾

- [ ] App 關掉再開（讓 `config.json` 的 C270 + shared 生效）。
- [x] `dotnet build` 過（隔離目錄驗證；主目錄因 App 在跑 DLL 被鎖，屬正常）。
- [x] `dotnet run -- --selftest` 全綠（37/37）。
- [x] git：本次 session 已把前次未提交變更 + 本次工作全部整理提交（`92c6e1b`…`d3401fd`，9+ commits）。

---

### 這次調查的教訓

只要 bug 沾到爛硬體，**先戳裝置層**（什麼晶片、會不會從 USB 掉、裝置樹有沒有髒、換機能不能重現）
再挖程式碼。這次深度讀碼排在前面，`Get-PnpDevice` 看 VID + 幽靈裝置是 10 分鐘的事卻排在後面。
