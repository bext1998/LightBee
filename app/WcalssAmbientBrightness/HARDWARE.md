# 相機硬體相容性

這個 App 每次取樣都會 Open → 讀幾個 frame → Release（Lazy 模式，對應 spike-report Test 08）。
這種頻繁開關對**韌體不穩定的廉價相機**特別不友善：實測某些通用晶片會進入「安靜無 frame」
狀態，時好時壞、逐步惡化，只有靠裝置自己重新從 USB 列舉才恢復，程式內重連（同程序重建
MediaCapture）救不回來。在取樣設計改為常駐串流之前，請用韌體穩定的相機。

## 已驗證可用

| 相機 | 列舉名稱 | 備註 |
|---|---|---|
| Logitech C270 HD WEBCAM | `C270 HD WEBCAM` | 驗證中（2026-09-08 換上，待一整晚 `camera-diagnostics.csv` 無整片 `no-frames` 才轉正式） |

## 已知有問題

| 晶片 / 相機 | VID&PID | 症狀 |
|---|---|---|
| Sonix 通用晶片（常見於無牌「1080p」webcam，如 JINPEI 錦沛 1080p，Windows 列舉為 `USB Camera`） | `VID_5258 & PID_4A55` | 使用中進入安靜無 frame，時好時壞、逐步惡化；靠裝置重新列舉才恢復，程式內重連無效。相機只是溫溫的，非發熱問題。 |

換相機不要只看「1080p / 60fps」這種規格——這類問題出在韌體與晶片，不是規格。

## 2 分鐘自我檢查（換相機或遇到問題時）

1. **能不能鎖曝光**
   `dotnet run --project app/WcalssAmbientBrightness -- --probe-metadata`
   看有沒有 `ExposureControl: available` 或 legacy `Exposure: available`。
   沒有 → 相機的自動曝光會把「任何有光的室內」壓到接近同一個亮度（spike-report §8），
   分級門檻會較難拉開。

2. **裝置樹有沒有髒**
   裝置管理員 → 檢視 → 顯示隱藏的裝置 → 「相機」和「影像裝置」底下，
   把灰色（Status: Unknown）的舊相機項目右鍵解除安裝 → 拔相機 → 等 10 秒 → 重插。

3. **USB 供電 / 省電**
   插主機板後方的 USB 2.0 孔，不走 hub、不走前面板；換一條線。
   該相機與其 USB Root Hub 的內容 → 電源管理 → 取消「允許電腦關閉這個裝置以節省電源」。

4. **連續跑一晚**
   看 `%AppData%\WCALSS\AmbientBrightness\camera-diagnostics.csv` 會不會出現整片
   `failed_step=no-frames`。開頭的 `phase=device-info` 那列有記錄相機的 `hwid=VID_xxxx&PID_xxxx`。

## 回報規則

清單外的相機屬 best-effort 支援。開 issue 前，請先確認在「已驗證可用」清單內的相機上
也能重現同樣問題，以便區分是程式的 bug 還是特定相機的韌體行為。

## 程式的自我提示

- 啟動時 `camera-diagnostics.csv` 會記一列 `phase=device-info`，含相機 VID/PID。
- 連續多次重連仍取不到 frame 時，工作列會跳通知並降頻到每 60 秒慢速重試（螢幕亮度維持
  最後一次有效判定），恢復取樣後自動回正常頻率。
