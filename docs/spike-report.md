# WCALSS Spike 測試報告（進行中）

本文件記錄 WCALSS Spike 測試規格書（`WCALSS Spike 測試規格書.md`）中，Test 01～06 的實測結果與初步判定。Test 07 之後尚未進行，會在後續更新。

測試日期：2026-08-13（Test 01-06、08-10 主體）、2026-08-14（Test 06 補測 day-overcast，見 5.1／5.2 節）
測試機：使用者工作機（Windows）
測試 Webcam：JINPEI 錦沛 1080p（Windows 列舉裝置名稱為 "USB Camera"）

---

## 1. Test 01 — Device Enumeration

| 項目 | 結果 |
|---|---|
| Device Name | USB Camera |
| Vendor ID | 5258 |
| Product ID | 4A55 |
| Interface | USB |
| Camera API | Windows.Media.Capture（`DeviceInformation.FindAllAsync` + `MediaCapture.VideoDeviceController`） |

裝置列舉穩定，僅偵測到 1 台視訊擷取裝置（未見筆電內建鏡頭，代表測試機本身沒有內建鏡頭或未被列入 VideoCapture 類別）。重啟程式後仍能重新找到裝置。USB 拔插情境（Test 13）尚未測試。

---

## 2. Test 02 — Capture Capability Probe

裝置公布 24 個 Capture Mode，實際上是 6 種解析度 × 2 種 FPS × 2 種 Pixel Format 的組合：

| 解析度 | FPS | Pixel Format |
|---|---|---|
| 1920×1080 | 25 / 30 | NV12 / MJPG |
| 1280×720 | 25 / 30 | NV12 / MJPG |
| 640×480 | 25 / 30 | NV12 / MJPG |
| 640×360 | 25 / 30 | NV12 / MJPG |
| 352×288 | 25 / 30 | NV12 / MJPG |
| 352×240 | 25 / 30 | NV12 / MJPG |

沒有出現硬體限制或列舉失敗；至少存在可穩定取得 Frame 的 Capture Mode（後續 Test 04-06 使用 640×480 @30 FPS / NV12 驗證過）。

---

## 3. Test 03 — Camera Control Capability

| Capability | 結果 |
|---|---|
| Auto Exposure | No |
| Exposure | Yes |
| Gain | No |
| White Balance | Yes |
| Auto White Balance | Unknown（WinRT `VideoDeviceController` 無對應獨立 Supported 介面） |
| Backlight Compensation | Yes |
| Low Light Compensation | Unknown（同上） |

這些能力皆視為 Optional，不影響後續测试的可行性。「Auto Exposure = No」指的是軟體端查不到可切換的曝光模式控制介面，不代表硬體韌體沒有在跑自動曝光（見第 5 節，實測行為顯示自動曝光確實在運作）。

---

## 4. Test 04 / 05 — Cold Start / Exposure Convergence

方法：每個環境下重複 Open → Sample（3 秒窗、640×480 @30 FPS / NV12）→ Release 共 5 輪，記錄每個 frame 的 Mean/Median Luminance；穩定判定為「最近 15 個 frame 的 Mean Luminance max−min ≤ 0.01」（門檻與視窗數皆為可調參數，非寫死常數）。

各環境的 Time-to-Stable-Luminance（排除下述已知不受控的例外資料）：

| 環境 | 5 輪收斂時間（ms） |
|---|---|
| dark | 406.19 / 463.75 / 464.23 / 463.94 / 463.94 |
| normal-fixed-angle | 438.74 / 479.95 / 463.96 / 463.55 / 463.49 |
| bright-fixed-angle | 454.49 / 463.73 / 463.99 / 479.71 / 480.24 |
| very-bright-fixed-angle | 440.02 / 464.06 / 464.06 / 463.87 / 463.99 |
| current-room-baseline（早期單一狀態基準） | 456.10 / 464.26 / 464.16 / 463.64 / 464.21 |

**初步結論：這顆 Webcam 的 Exposure 收斂時間穩定落在約 0.44～0.48 秒（多數集中在 0.46～0.48 秒），不同亮度環境下沒有顯著差異。** 規格書原先假設的「可能需要 0.5～1 秒」略為高估，實測比假設略快，且相當一致。此數值已由實測資料取得，不是規格書上的假設值。

已知例外（不計入上表，因為是取樣過程中角度未固定造成，非相機本身行為）：早期一組標記為 `normal`（角度調整中）的資料，5 輪中出現 1512 ms 與 1 輪 3 秒內未收斂，且該組 Mean Luminance 範圍寬達 0.208～0.527，明顯是鏡頭角度/畫面構成在測試過程中被移動所致，因此改用固定角度重測（即 `normal-fixed-angle`），該筆舊資料保留在 CSV 中但不用於正式結論。

---

## 5. Test 06 — Environment Separation

四組固定角度環境的 Mean Luminance 統計：

| 環境 | 樣本數 | 平均 | 範圍 |
|---|---|---|---|
| dark（關燈） | 447 | 0.000536 | 0.000005 ～ 0.008173 |
| normal-fixed-angle（主燈一般亮度） | 446 | 0.459676 | 0.451812 ～ 0.463931 |
| bright-fixed-angle（房間主燈 3 段開關最亮） | 443 | 0.452609 | 0.450036 ～ 0.455573 |
| very-bright-fixed-angle（手電筒/桌燈近照鏡頭） | 444 | 0.482067 | 0.446948 ～ 0.492209 |

### 關鍵發現

1. **Dark 與「有光」狀態區隔非常乾淨**：0.0005 vs 0.45 以上，數量級差距超過 800 倍，這個區分完全可靠。
2. **Normal 與 Bright 測出來是同一個值**（0.4597 vs 0.4526，平均值甚至還反過來），但這件事要先打個問號：`normal`／`bright` 這兩個標籤從一開始就沒有量化定義——規格書明講 Test 06 不要求 Camera → 精確 Lux，操作上兩者就是「房間主燈 3 段開關的其中一段，測試者主觀認定的『一般』跟『最亮』」，沒有 lux 計、沒有校準。所以嚴格來說這不是「相機測不出兩個真正不同的亮度」，而是「`normal` 跟 `bright` 本來就可能不是兩個有意義的不同物理量級，只是同一個『有開燈』狀態的兩個主觀稱呼」。
3. **只有極端直接照光（very-bright）才讓數值明顯上升**（平均 0.482），但該組內部變異也明顯變大（0.447～0.492，5 輪中有 1 輪掉回 0.4635，接近 normal 的範圍），代表反應不完全穩定。

### 判讀

Normal 與 Bright 測不出差異，目前有兩個可能原因，**現有資料無法完全區分兩者**（下面 5.1 節的補測結果，讓天平略微傾向原因 1）：

1. **`normal`／`bright` 本身可能就不是兩個定義清楚的不同亮度等級**：測試房間僅靠主燈 3 段開關調光，沒有其他獨立光源（如可調亮度檯燈、窗外自然光）可以做出真正漸進的中段亮度；「一般」與「最亮」兩個標籤只是測試者對同一顆主燈不同段位的主觀稱呼，沒有校準依據，鏡頭實際收到的光量本來就可能差距不大，甚至根本是同一個「有開燈」狀態下的雜訊範圍內差異。
2. **相機自動曝光/自動增益補償**：把「任何有正常光線的場景」收斂到接近同一個目標亮度（AE Target），只有暗到幾乎無光，或亮到遠超一般室內範圍時，才會脫離這個收斂區間。

支持第 2 點的線索是：very-bright-fixed-angle（直接近照鏡頭，光量遠超前兩組）確實讓平均值上升到 0.482，代表輸入光量差異夠大時相機還是有反應；但同一組內部變異也明顯變大（0.447～0.492，5 輪中有 1 輪掉回 0.4635，接近 normal 的範圍），顯示反應不完全穩定、乾脆——如果單純是「房間本來就沒有真正的中段光量差異」，這組理論上不該出現忽高忽低的情況。因此比較合理的判讀是**兩個因素疊加**：房間光源選擇有限，放大了問題，但自動曝光的補償行為也有參與，不是單一原因。要完全切開兩者，需要用可控漸進亮度的光源（例如可調光檯燈）重測，這超出本輪 Spike 範圍。

無論根本原因為何，本輪 Spike 在「這台測試機 + 這個房間 + 這顆相機」的組合下，觀察到的實際結果是：

- Full-frame Mean/Median Luminance 目前被驗證能可靠分辨**三個粗略區間**：「暗」（無光，~0.0005）、「微光」（僅自然光，~0.02，見 5.1 節）、「有開燈」（不論主燈段位，~0.45～0.48）。這已滿足規格書 Gate B 最基本的需求。
- 這不是連續或多級的亮度分級：`normal` 與 `bright` 目前沒有被數據驗證為兩個不同的等級——扣掉本身就不穩定的 `very-bright`，`normal`／`bright` 實質上是同一個「有開燈」區間內的雜訊差異，不應該被當作已驗證的漸進亮度刻度使用。要驗證能不能做更細的漸進分級，需要換一組有校準依據、真正漸進的光源（例如可調光檯燈搭配 lux 計）重測，這超出本輪 Spike 範圍。

### 5.1 補測：day-overcast（自然光，2026-08-14）

規格書 Test 06 明確要求 `Night-Dark` 與 `Day-Curtain-Closed`／`Day-Overcast` 等 `Day-Dim` 場景必須保留不同環境標籤，不可與夜間關燈的 `Night-Dark` 合併分析（規格書「Night-Dark 與 Day-Dim 的區分」一節）。原資料集裡的 `dark` 是夜間關燈量測，缺少純自然光的場景，因此於 2026-08-14 早上補測：房間主燈關閉、窗簾拉開、天氣陰天/多雲、天光很少，相機角度沿用 `normal-fixed-angle`／`bright-fixed-angle` 的固定角度。

| 環境 | 樣本數 | 平均 | 範圍 |
|---|---|---|---|
| dark（夜間關燈） | 447 | 0.000536 | 0.000005 ～ 0.008173 |
| day-overcast（早上關燈、窗簾開、陰天天光） | 446 | 0.022021 | 0.020694 ～ 0.029160 |
| normal-fixed-angle（開主燈一般亮度） | 446 | 0.459676 | 0.451812 ～ 0.463931 |

`day-overcast` 平均 0.022，乾淨地落在 `dark` 與 `normal-fixed-angle` 之間、5 輪範圍窄（0.0207～0.0292）且互不重疊：比 `dark` 亮約 41 倍，比 `normal-fixed-angle` 暗約 21 倍。Time-to-Stable-Luminance 428～464 ms，與其他環境一致，無異常。

這筆資料補上了本節「判讀」提到的缺口之一——測試房間本身缺乏中間亮度梯度、沒有窗外自然光這類獨立光源。現在有了這個獨立的自然光資料點，且它確實形成了乾淨可分辨的第三個量級（不是跟 dark 或 normal 疊在一起），這替**原因 1（測試環境缺乏中間亮度梯度）**提供了較直接的支持：至少在「有真正不同來源、不同量級的光」時，Full-frame Mean Luminance 是能夠區分的；`normal` 與 `bright` 測不出差異，更可能是因為兩者本來就是同一光源（房間主燈）的相近檔位，而不是相機自動曝光把所有正常光線都壓平。但這不能排除**原因 2（自動曝光補償）**仍有部分作用——兩者是否疊加，仍待後續用可控漸進光源（如可調光檯燈）驗證。

### 5.2 附加發現：Camera Sharing 系統設定殘留導致格式異常（2026-08-14）

補測 `day-overcast` 第一次執行時，工具印出「找不到 640x480/NV12/30fps，改用目前可用格式：1920x1080 @ 30/1 FPS / NV12」的警告，但當時 metadata 記錄 `camera_sharing: false`，也就是**沒有任何其他 App 在用相機**，跟 Test 10 共存情境下的格式限縮原因不同。

查證：`Get-Service FrameServer` 顯示該服務為 `Running`；重跑 Test 02（純格式列舉，不受光線影響）只列出 1 種 Capture Mode，跟原本的 24 種差很多。判斷為 Test 10（2026-08-13 深夜）測試共存時手動開啟的「Camera Sharing」系統設定，事後沒有關閉，導致 `FrameServer` 服務持續以共享模式接管相機、殘留鎖定前一個 session 的格式，即使當下完全沒有其他 App 在使用相機。使用者於 Windows 設定關閉 Camera Sharing 後，重跑 Test 02 立即恢復完整 24 種格式，不需重啟服務或拔插裝置即可解決。

**影響與教訓**：Camera Sharing 一旦被手動開啟，即使關掉所有共存的其他 App，相機格式仍可能維持在限縮狀態，直到使用者手動關閉這個系統設定為止——不會因為其他 App 關閉就自動復原。這是 Gate C（Non-Intrusive Operation）值得記錄的操作細節：產品化時若依賴這個設定做共存，需要考慮這個殘留行為對後續（非共存）使用情境的影響。

第一次因格式異常產生的 5 個 session（1920×1080，Mean Luminance 範圍 0.000221～0.263505，且開機曝光有明顯過衝現象）已改標為 `day-overcast-1920x1080-invalid`，保留在 `luminance-samples.csv` 中供追溯，不計入上表結論；`luminance-samples.csv.bak` 為改標籤前的備份。

---

## 6. Test 08 — Lazy Acquisition

方法：`coldstart --repeats 30 --duration-ms 3000 --pause-ms 1000`，觀察 30 輪 Open→Sample→Release→Pause 循環是否穩定。

結果：

- 30/30 cycles 成功取得 frame，`capture_success`／`release_success` 全為 true，0 個 error，0 個疑似黑 Frame（Mean Luminance 全部 ≤0.01 的 cycle）
- Open Latency 範圍 64.158～84.106 ms，前 5 輪平均 71.023 ms、後 5 輪平均 67.458 ms，**沒有隨時間變慢的趨勢**
- 未觀察到 Camera Handle 洩漏或無法重新開啟的情況

**結論：PASS。** Lazy 模式可以長時間穩定反覆執行。

---

## 7. Test 09 — Persistent vs Lazy

方法：Persistent 模式只 Open 一次，持續擷取至總時長對齊 Lazy 那次的總 Wall Time（約 120 秒），與上面 Test 08 的 Lazy 數據直接比較。規格書明講不預設哪個模式較好，以下只列實測數據與取捨，不下優劣結論。

| 指標 | Lazy（30 cycles） | Persistent（單次） |
|---|---|---|
| 總 CPU 時間 | 4078 ms | 4672 ms |
| 總 Wall Time | 138449 ms | 120674 ms |
| CPU / Wall 比例 | 2.95% | 3.87% |
| Open Latency 次數 | 30 次 | 1 次 |
| Open Latency 總計 | 約 2042 ms | 82 ms |
| Exposure 重新收斂次數 | 30 次（每輪 ~440-480ms） | 1 次（439ms） |
| 120 秒內 Luminance 穩定度 | N/A | 0.4605～0.4647（變異極小，無漂移） |

**取捨**：

- Persistent 的 CPU/Wall 比例反而比 Lazy 高（3.87% vs 2.95%），因為 Lazy 在每輪 1 秒的 pause 期間完全不佔用相機，把平均值拉低；但這代表 Persistent 換來的是全程佔用相機（`ExclusiveControl`），對 Test 10 的 Camera Coexistence 可能更不利，需要實測才能確認。
- Lazy 的代價是重複的啟動稅：30 輪下來光是重新曝光收斂就佔掉約 13.5 秒（約運行時間的 10%），這段時間內的樣本理論上不該直接拿來用。
- Persistent 一旦收斂就非常穩定，2 分鐘內 Mean Luminance 只在 0.0042 範圍內波動，無明顯長時間漂移。

---

## 8. Test 10 — Camera Coexistence（Gate C，部分完成）

本輪只測了 WCALSS + Windows 相機 App 這一組合，Discord／瀏覽器／OBS 尚未測試；且只在深夜單一時段完成，屬於 Hardware Behavior Spike 的一部分（不受環境光限制，任何時段可測）。

### 方法調整

原始程式碼（Test04-09 沿用的 `ColdStartCommand`）寫死要求 640×480/NV12/30fps、`MediaCaptureSharingMode.ExclusiveControl`。實測時發現這兩點在共存情境下都不成立，因此做了兩個小改動：

1. `SelectFormat` 改成優先選 640×480/NV12，找不到就退而求其次選可用清單中解析度最高的 NV12 格式（並印出警告），而不是直接丟例外。
2. 新增 `--sharing-mode exclusive|shared` 參數，對應 `MediaCaptureSharingMode.ExclusiveControl` / `SharedReadOnly`。

### 結果

| 情境 | WCALSS 抓到 Frame？ | 對方（Windows 相機 App）受影響？ | 備註 |
|---|---|---|---|
| Camera Sharing **OFF** + ExclusiveControl（原始 640×480 設定） | ❌ 0 frames（3/3 輪），無 error、無 crash | 完全正常 | `InitializeAsync`／`reader.StartAsync()` 都回報成功，但 `FrameArrived` 從未觸發——安靜失敗，不會被誤判成功 |
| Camera Sharing **ON** + ExclusiveControl（fallback 到 1920×1080/NV12） | ✅ 76-77 frames/輪（單獨測試時同時長約 89-90 frames） | 完全正常 | 可用 Capture Mode 從 24 種瞬間壓縮成只剩對方在用的那 1 種；Exposure 收斂變慢（511-577ms vs 單獨測試 ~440-480ms）；3 輪中有 2 輪起始 Mean Luminance 異常偏離穩態（0.080、0.685 vs 穩態 0.455） |
| Camera Sharing **ON** + SharedReadOnly | ✅ 76-77 frames/輪 | 完全正常 | 收斂時間相近（544-576ms），但 3 輪起始 Mean Luminance 都明顯更接近穩態（0.497、0.531、0.463），訊號品質比 ExclusiveControl 那組可信很多 |

另有一筆資料（環境標籤 `coexistence-sharingoff-exclusive-v2`）因為測試過程中使用者在 Windows 設定裡把 Camera Sharing 切回 ON、跟原先想做的「Sharing OFF 乾淨對照組」意圖不符，**該筆資料不採用**，保留在 CSV 中僅供追溯，不計入以下結論。

### 判讀

1. **Camera Sharing（Windows 系統層級開關）是共存的硬前提**：關閉時 WCALSS 完全拿不到畫面（雖然也不會 crash 或干擾對方）。這代表若要達成 Gate C，使用者必須先手動開啟這個系統設定，不能假設預設值就會成立——這是產品化必須處理的相依條件。
2. **開啟 Sharing 後，可用 Capture Mode 被大幅限縮**成對方已在用的那一種格式，因此 WCALSS 的擷取邏輯必須做動態格式協商（已修正），不能寫死特定解析度。
3. **`SharedReadOnly` 比目前預設的 `ExclusiveControl` 更適合共存情境**：兩者都能拿到 frame、都不影響對方，但 `SharedReadOnly` 的起始亮度讀數明顯更穩定可信。規格書沒有明講這點，是本輪實測發現的細節。
4. **三個情境下 Windows 相機 App 都完全沒有受到干擾**（使用者目視確認畫面正常、無掉幀、無黑畫面、無 crash）。

### 對 Gate C 的初步影響

「不嚴重干擾其他 Camera App」這個條件，在已測的組合下成立；但「WCALSS 能否取得環境亮度」這個前提條件依賴使用者手動開啟 Windows 的 Camera Sharing 系統設定，不是 WCALSS 自己能控制的。Discord／瀏覽器／OBS 尚未測試，无法排除這些 App 的相機使用方式（例如是否也用 ExclusiveControl、是否鎖定不同格式）會有不同表現。

---

## 9. 目前 Gate 初步判定

| Gate | 判定 | 依據 |
|---|---|---|
| Gate A — Compatibility | **YES** | Test 01-03：裝置可穩定列舉、有可用 Capture Mode、標準 Windows API 可正常存取 |
| Gate B — Signal Quality | **PARTIAL** | Test 04-06：目前驗證可靠分辨三個粗略區間（暗／微光自然光／有開燈，見 5.1 節），滿足最基本需求；但 `normal`／`bright` 兩個標籤本身缺乏校準定義，尚未證實這個系統能做比「有沒有開燈」更細的漸進亮度分級，需要校準過的漸進光源才能進一步驗證 |
| Gate C — Non-Intrusive Operation | **CONDITIONAL** | Test 10（部分）：與 Windows 相機 App 共存時不干擾對方，但前提是使用者需手動開啟 Camera Sharing 系統設定，且需改用 `SharedReadOnly` 與動態格式協商；Discord／瀏覽器／OBS 尚未測試 |

Gate B 的 PARTIAL 判定不代表 Spike 失敗——規格書明確定義「即使最終判定為 No-Go，只要原因有實測資料支持，Spike 仍視為成功」。這個發現本身就是有價值的實測結論，後續可能的方向包括：
- Test 07 ROI 比較：測試小範圍取樣（而非全畫面平均）是否能繞開自動曝光的全域補償，抓到更細緻的亮度差異。
- 改用相機回報的 Exposure/Gain 數值本身（而非畫面像素亮度）作為訊號來源，因為自動曝光调整的幅度本身也隱含了環境光資訊。

---

## 10. 尚待進行

Test 07（ROI 比較）、Test 10（Camera Coexistence，僅測了 Windows 相機 App，Discord／瀏覽器／OBS 尚未測試）、Test 11（Busy Handling）、Test 12（Privacy Permission）、Test 13（USB Hotplug）、Test 14（Sleep/Resume）尚未完成。

---

## 11. 原始資料

- `spike/camera-probe/raw-data/luminance-samples.csv`：Test 04-06、09、10 所有 frame 級原始樣本（Test 10 的環境標籤前綴為 `coexistence-`；`day-overcast-1920x1080-invalid` 為 2026-08-14 因 Camera Sharing 殘留設定造成格式異常的資料，保留供追溯，不計入結論，見 5.2 節）
- `spike/camera-probe/raw-data/luminance-samples.csv.bak`：上述標籤修正前的備份
- `spike/camera-probe/raw-data/lazy-cycles.csv`：Test 08 的逐 cycle 診斷資料（open latency / capture / release / error）
- `spike/camera-probe/raw-data/coldstart-*.json`：各次 coldstart（Test 04-06、08、10）執行的 metadata
- `spike/camera-probe/raw-data/persistent-*.json`：Test 09 persistent 執行的 metadata
- `spike/camera-probe/raw-data/camera-probe-*.json`：Test 01-03 的裝置/能力探測結果
- `spike/camera-probe/README.md`：工具使用說明

---

## 12. 背景 App 端對端驗證（2026-09-03）

在上述 Spike 結論的基礎上，另外建了一個實際會跑的產品雛型 `app/WcalssAmbientBrightness/`（WinForms 背景常駐 + 系統匣 + 設定視窗），把 Test 04-06、08、10 驗證過的相機邏輯接成「環境光→分級→自動調螢幕亮度」的完整流程，目的是確認 Spike 測過的東西在真實使用情境下真的能用，不只是「探測工具能跑」。細節與設計理由見該目錄下的 `README.md`。以下是這次串接過程中，在使用者本機（非模擬環境）實測發現、原本 Test 01-10 沒有涵蓋到的新資訊。

### 12.1 SharedReadOnly 在 Camera Sharing 關閉時的失敗方式，跟 ExclusiveControl 不同

Test 10 只測過「Camera Sharing 關閉 + ExclusiveControl」（安靜失敗，0 frame、無 error）與「Camera Sharing 開啟 + SharedReadOnly／ExclusiveControl」，沒測過「Camera Sharing 關閉 + SharedReadOnly」這個組合。這次補上了：

| 情境 | 行為 |
|---|---|
| Camera Sharing 關閉 + ExclusiveControl | 開相機成功（約 90 ms），但完全收不到 frame，無 error（Test 10 既有結論） |
| Camera Sharing 關閉 + SharedReadOnly | `MediaCapture.InitializeAsync` 直接丟出例外，HResult `0x80070020`（Windows `ERROR_SHARING_VIOLATION`），每次取樣都立刻失敗 |

兩種模式在 Camera Sharing 關閉時都拿不到畫面，但 `SharedReadOnly` 的失敗是立即、明確、可被程式偵測到的 exception，`ExclusiveControl` 則是安靜的 0 frame。這個差異值得記錄：如果產品要靠「有沒有收到 exception」判斷是否需要提示使用者開啟 Camera Sharing，`SharedReadOnly` 提供的訊號其實比 `ExclusiveControl` 更明確。

### 12.2 Windows「相機共用」系統設定的實際路徑

Test 10 只確認了這個系統設定是共存的前提條件，沒有記錄它在哪裡。實測環境（Windows 11, build 26200）確認路徑為：

**設定 → 藍牙與裝置 → 相機 → 選擇目標相機 → 進階相機選項 → 編輯 → 打開「允許多個應用程式同時使用相機」**

補充：這是**逐裝置設定**，不是全域開關；切換需要系統管理員權限；預設是關閉的；這個功能是 Windows 11 2025 年 3 月更新（KB5052093）才加入。開啟後，`SharedReadOnly` 模式立即從 12.1 節的立即失敗，變成能正常拿到畫面（47 frames，平均亮度 0.4623，正確落在 Test 06「有開燈」區間），與 Test 10 原本記錄的行為一致，驗證了 Test 10 的結論在另一次獨立操作下可重現。

### 12.3 DDC/CI 外接螢幕亮度控制：新增並完成真機驗證

Spike 報告原先只測了相機端（Gate A/B/C），沒有測過「拿到亮度訊號後能不能真的調整螢幕」這一段。這次補上：透過 `Dxva2.dll` 的 `GetPhysicalMonitorsFromHMONITOR`／`GetMonitorBrightness`／`SetMonitorBrightness`（P/Invoke），對不支援 WMI／ACPI 亮度控制的外接螢幕（本機情況，桌機 + 外接螢幕，WMI 回報「不受支援」）做 DDC/CI 控制，與原本的 WMI 路徑互為備援。

實機驗證（獨立探測腳本 `tools/Probe-DdcCi.ps1`，直接呼叫 DDC/CI API，不經過相機流程）：

```
Generic PnP Monitor: DDC/CI brightness supported, raw=40, range=0-100
Generic PnP Monitor: DDC/CI brightness set to raw=80 (80%)
Generic PnP Monitor: DDC/CI brightness supported, raw=80, range=0-100
```

寫入與回讀一致，確認這台機器的外接螢幕支援 DDC/CI，且程式能實際控制它。

### 12.4 發現並修好一個亮度分級遲滯（hysteresis）的邊界 bug

`BrightnessMapper.Evaluate()` 原本的遲滯計算，往下切換分級時用 `boundary - hysteresis` 當有效邊界。當某個分級的門檻本身小於遲滯量時（本機預設：「暗」分級門檻 0.01，遲滯預設 0.02），算出來的有效邊界會是負數（0.01 − 0.02 = −0.01），而亮度讀數不可能是負值，導致往「暗」這個最低分級的切換在數學上永遠不會發生——即使畫面顯示的分級標籤（無狀態查詢）正確顯示「暗」，實際套用的亮度卻卡在上一段（「微光」45%），不會真的降到「暗」設定的 15%。

**症狀**：使用者實測「用東西遮住鏡頭」時，螢幕確實有變暗（因為先前的分級成功切到「微光」45%），但暗的幅度比預期小很多，且遮住鏡頭當下不會再進一步變暗。

**修法**：往下切換時的有效邊界改成 `Math.Max(boundary / 2, boundary - hysteresis)`，確保永遠是正值、且落在該分級實測讀數範圍內可以被跨過，同時不影響原本已正常運作的其餘轉換（`boundary` 明顯大於 `hysteresis` 時行為不變）。

### 12.5 修好之後的完整端對端驗證（三段雙向轉換，真機、真螢幕）

修好上述 bug、重新編譯後，在使用者機器上實測到完整的雙向轉換，`%AppData%\WCALSS\AmbientBrightness\validation-log.csv` 節錄：

| 時間 (UTC) | 平均亮度 | 判定分級 | 套用亮度 | 結果 |
|---|---|---|---|---|
| 14:37:23 | 0.003944 | 暗（無光） | 15% | 已透過 DDC/CI 套用 |
| 14:37:58 | 0.571815 | 有開燈 | 80% | 已透過 DDC/CI 套用 |
| 14:38:14 | 0.002890 | 暗（無光） | 15% | 已透過 DDC/CI 套用 |
| 14:38:19 | 0.426110 | 有開燈 | 80% | 已透過 DDC/CI 套用 |

使用者主觀回饋（房間關燈情境下）：「真的會變暗，這次暗很多……眼睛總算感覺比較舒服了，不會覺得刺眼，也不用調螢幕亮度了」。這是本輪 Spike 最初想確認的產品目的——「環境光自動調螢幕亮度」——第一次有主觀體驗＋客觀紀錄雙重確認的完整驗證。

### 12.6 尚未驗證的部分（誠實記錄，不算完成）

- **「微光」分級（僅自然光, ~0.02）的自動調光行為未在這輪確認**：目前只確認到「暗」與「有開燈」兩端的雙向轉換，中間那一段有被正確判定（見 12.4 症狀描述時的紀錄），但沒有使用者主觀回饋確認調整後的螢幕亮度是否合理。
- **意外的 USB 熱插拔事件未被正式記錄為 Test 13**：測試過程中相機一度故障、需重插兩次才恢復正常，但發生當下沒有監控程序在跑，沒有留下時間戳、錯誤訊息或裝置列舉狀態變化的資料，只能算軼事，不構成正式的 Test 13 資料點。
- **本節引用的 `validation-log.csv` 是使用者本機 `%AppData%` 下的執行期資料，不在本專案版本控制範圍內**，之後若要長期保存這類端對端證據，需要另外規劃匯出或歸檔方式。
- 相關程式碼與詳細設計說明見 `app/WcalssAmbientBrightness/README.md`。

---

## 13. 漸進亮度控制與自動重連（2026-09-03，同日稍晚）

第 12 節端對端驗證通過後，使用者提出兩個後續需求：讓螢幕亮度變化像手機/平板一樣漸進、不要一次跳階；以及在偶發的相機取樣中斷時能自動恢復，不用使用者自己發現「怎麼都不會動」才手動重啟。這兩項都已實作並在使用者本機實測，過程中又發現兩個新問題。

### 13.1 漸進亮度控制器（新增功能）

新增 `BrightnessRamp.cs` 跟 `TrayContext.cs` 裡的 `rampTimer`，把「分級判定出來的目標亮度」跟「實際寫入螢幕的亮度」拆開：

- 取樣頻率不變（沿用 Test 08 驗證過的 Lazy 取樣，預設每 5 秒一次），只負責判定分級、設定目標值。
- 另一個 200ms 週期的計時器負責往目標值前進一小步，變亮每秒最多 15%，變暗每秒最多 8%（速度不對稱，變暗刻意慢一點，仿照手機螢幕的體感）。
- 驗證紀錄不會每 200ms 洗一筆，只在「開始漸進調整」與「調整完成」各留一筆。

**實測驗證**：故意把螢幕手動設到 80%、讓環境維持暗態，觀察程式自動判定並漸進調整：

| 時間 | 事件 |
|---|---|
| 14:48:35.19 | 判定「暗」，開始漸進調整，目標 15% |
| 14:48:43.57 | 漸進調整完成，達成 15% |

耗時 8.38 秒，跟「差距 65% ÷ 每秒 8%」的理論值 8.13 秒幾乎一致，確認螢幕是真的逐步降下去，不是瞬間跳掉又補記錄。

### 13.2 發現並修好一個 EMA 平滑係數過度保守的 bug

為了減少相機自動曝光單次抖動造成的誤判，在分級判定前加了一層 EMA（指數移動平均）平滑。第一版用 0.15（配合原本設想的「每秒取樣」），但實際取樣間隔預設是 5 秒，兩者沒對齊：0.15 配 5 秒間隔，要 6-7 次取樣（30 秒以上，環境從亮到全暗的落差大時甚至接近 100 秒）才追得上一次真實的環境劇烈變化。

**症狀**：使用者實測「把房間關到全暗」時，原始亮度讀值早就掉到 0.005 以下（明確是「暗」的範圍），但因為平滑值還停在 0.06～0.13 左右（尚未追上），程式判定一直卡在「微光」不會再往下切，螢幕亮度停在 45% 不動，使用者反應「感覺還是亮，沒有肉眼可見的亮度調整」。這不是漸進控制器的問題（漸進控制器本身運作正常），是判定分級的輸入值本身就沒有正確反映當下環境。

**修法**：平滑係數從 0.15 調整為 0.5，兩三次取樣（約 10-15 秒）內就能追上持續性的真實變化，仍能撫平單次讀值的突波，跟既有的遲滯（hysteresis）機制不重複疊加防抖動的效果。修好後重新實測，確認能正確追到「暗」並套用 15%。

### 13.3 新增自動重連機制，並用真實情境驗證

**背景**：13.2 節修好後的一次長時間執行中（約 7-8 分鐘、70 輪取樣後），取樣忽然開始連續超過一分鐘「拿不到任何 frame」，螢幕因此卡在某個亮度不動。獨立用探測工具測試，相機立刻正常拿到畫面，代表卡住的是這個 App 自己的相機工作階段，不是裝置或系統層級故障。

**新增功能**：`TrayContext.cs` 新增連續失敗計數，連續 3 輪（約 15 秒）取樣都失敗時，自動捨棄舊的 `AmbientLightSensor`、重新建立一個乾淨的相機工作階段，不用重啟整個 App。重連失敗也不會卡死——會在下一輪再連續失敗 3 次後自動再試一次。

**驗證方式與結果**：用另一個獨立程序（`coldstart --sharing-mode exclusive`）反覆搶相機，人為製造連續取樣失敗，實際觀察到驗證紀錄在連續 3 輪失敗後自動記下一筆「已自動重建相機工作階段並恢復」，機制確實如設計觸發。

### 13.4 意外發現：高頻率搶相機會讓 Camera Sharing 陷入系統層級的卡死，且無法從單一 App 內自我恢復

13.3 節的驗證測試本身，卻意外把系統的相機共用狀態測到卡死：反覆用 `ExclusiveControl` 搶奪一個正在用 `SharedReadOnly` 讀取的相機幾輪之後，**連全新、獨立啟動的程序都再也拿不到任何 frame**，即使沒有任何程序還在跟它搶。這代表卡住的不是任何單一 App 的工作階段，而是 Windows 相機共用（FrameServer）本身的狀態壞掉了——跟 5.2 節記錄的「Camera Sharing 殘留鎖定」是同一類問題，但這次是被高頻率的搶奪行為直接測出「壞死」而非「格式被限縮」。

13.3 節新增的 App 內自動重連機制，對這種情況**沒有用**：它只能重開 App 自己的工作階段，治不了系統層級的卡死。

**復原方法**（跟 5.2 節記錄的做法一致，這次也確認有效）：使用者在 Windows 設定裡把「相機共用」關掉再重新打開一次，相機立刻恢復正常（獨立探測工具當場測到 51 個 frame）。拔插 USB 相機應該也有效，但這次是用系統設定開關解決的，沒有另外測拔插。

**對 Gate C 的補充**：「非侵入性」的既有結論仍然成立（沒有讓其他相機 App 崩潰或卡死），但這次發現了一個新的風險面——高頻率、短時間內反覆切換 Sharing Mode 或反覆搶奪相機控制權，可能讓 Windows 的相機共用機制本身進入需要使用者手動介入才能恢復的壞狀態。產品化時如果會有多個 App 或多個模式快速切換相機存取，需要考慮加入某種節流或間隔限制，避免使用者端也遇到同樣的情況。

### 13.5 這幾輪測試共同呈現的教訓

- **相機共用（Camera Sharing）這個機制本身比預期脆弱**：從最早 Test 10 的格式限縮（5.2 節）、到今天單一長時間執行後的偶發卡住（13.3 節）、再到高頻率搶奪導致系統層級卡死（13.4 節），三次獨立觀察到的現象程度不同，但方向一致——這個機制在非典型使用模式（長時間、高頻率、快速切換）下的穩定性，比官方文件呈現的樣子更需要留意，值得在正式產品化前額外規劃監控與復原流程，不能假設「開了就會一直正常」。
- **App 層級的自動重連是有用但有限的補強**：能處理「這個 App 自己的工作階段卡住」，處理不了「系統層級的相機共用狀態壞掉」，兩者需要分開看待、分開設計復原路徑。

---

## 14. 螢幕內容回饋迴路（使用者實測發現，2026-09-03）

### 14.1 使用者發現：開啟全白網頁會讓螢幕「突然變亮」

在 13.2 節那次「單一離群值造成螢幕忽亮忽暗」的異常之後，使用者自己動手測試，發現只要開一個背景全白的網頁，螢幕就會被判定成「有開燈」而跟著變亮。這代表 13.2 節當時看到的離群值（0.076），很可能不是相機感光元件的隨機雜訊，而是當下畫面上真的出現了亮的內容，被相機看到、誤判成環境變亮。

### 14.2 量化驗證：螢幕自己的光，足以讓判定結果整段跳級

用瀏覽器全螢幕開啟 Google（大片白底頁面），跟沒有開啟時的基準值直接比較：

| 情境 | Mean Luminance |
|---|---|
| 基準（無白頁面，房間暗） | ~0.0000（暗） |
| 全螢幕白色網頁 | 0.31（收斂後 0.12） |

房間實際光線完全沒變，讀值卻從「暗」的範圍直接跳到逼近「有開燈」（門檻 0.20 以上），單靠螢幕自己顯示的內容就做到了。這不是量測誤差或單次雜訊，是螢幕的光確確實實被相機收進了取樣範圍。

### 14.3 這是一個結構性問題，不是可以靠參數調校解決的 bug

第 13.2 節修的 EMA 平滑係數、跟這次為了濾除單次離群值新增的「連續兩次確認才切換」機制（見 `BrightnessMapper.cs`），都只能處理「單次、短暫的異常讀值」。**這兩層防護都擋不住「螢幕持續顯示亮內容超過兩次取樣間隔（約 10 秒）」的情況**——因為就演算法的角度，這時候连續兩次讀值都支持「變亮」，會被判定為真實、持續的環境變化，程式沒有任何依據能分辨「這是房間真的變亮」還是「使用者剛好開了一個白色網頁」。

更嚴重的是這可能構成一個真正的回饋迴路：使用者開啟亮色內容 → 相機讀到更亮 → 分級可能被推到「有開燈」（80%）而不是原本使用者可能期望的較暗設定 → 螢幕整體亮度提高（如果原本不是 80%）→ 螢幕更亮的畫面內容被相機讀到的量會更多。目前三段分級的最高只到 80%、不會無限循環放大到 100%，所以不是失控的正回饋，但「因為使用者切換了視窗內容，就被誤判成環境變亮而調整螢幕」這件事本身，已經違背了這個系統的核心前提——用環境光偵測來源自動調整螢幕亮度，前提是這個訊號要獨立於螢幕自己顯示的內容,而這台測試機的相機擺放角度顯然沒有滿足這個前提。

### 14.4 對 Gate B 的重大補充

第 9 節「Gate 初步判定」把 Gate B 標為 PARTIAL，原因是分級精細度不足；**這個發現指出一個更根本的問題：即使分級精細度足夠，這套「用相機讀取環境光」的架構本身，在相機能看到螢幕自身光線（直視、反射、或房間夠小造成整體環境亮度受螢幕顯著影響）的擺放方式下，訊號來源並不乾淨**。這不是三段分級夠不夠細的問題，是輸入訊號本身混雜了它想控制的輸出（螢幕亮度／內容）造成的自我干擾。

本輪 Spike 與後續 App 開發都沒有規劃或測試相機的擺放位置／角度對這個問題的影響程度，這是一個尚待驗證的新變因，值得列入之後測試規劃：例如相機朝向使用者臉部（背對螢幕）跟相機能直視螢幕兩種擺放，訊號乾淨程度應該會有明顯差異。

### 14.5 本輪已做的部分緩解，以及尚未解決的部分

- **已做**：`BrightnessMapper.cs` 新增「連續兩次都判定要切換，才真的切換」的確認機制，能濾掉一次性的短暫亮度突波（例如畫面上短暫出現一個通知視窗），不會再像 13.2 節那樣單一離群值就造成螢幕忽亮忽暗。
- **未解決**：只要亮色內容在螢幕上停留超過約 10 秒（兩次取樣間隔），現有機制完全無法分辨這是真實環境變化還是螢幕自己的內容，會被當成真的環境變亮處理。要真正解決，可能需要：
  - 硬體層面：確保相機擺放角度看不到螢幕本身或其明顯反射光（最直接但需要使用者配合擺放，App 無法強制）。
  - 軟體層面：把「目前螢幕實際輸出的亮度／畫面平均亮度」當作已知變因，從相機讀值中做某種扣除或校正，這需要額外的螢幕內容取樣機制，超出本輪範圍。
  - 這兩個方向都还没有在本輪實作或驗證，只停留在問題被發現、量化、記錄的階段。

### 14.6 相關且未測試的污染來源：窗戶等反光表面

使用者提出另一個尚未驗證的風險：如果相機後方或視野內有窗戶，玻璃反光可能把螢幕自己的光反射回相機（等於間接放大 14.2 節的回饋迴路，即使相機本身沒有直視螢幕），也可能把完全不受控、跟房間實際照明無關的戶外光線（車燈、路燈、天色變化、對面建築物的燈光）一起反射進畫面，讓判定結果混入使用者既沒有意圖也無法預期的變因。

這點跟 14.4 節的核心問題同一個性質——訊號被相機視野內的間接光路污染，不限於螢幕直接入鏡這一種情況。本輪沒有測試不同背景（有窗戶／無窗戶／窗簾開關）對讀值穩定度的影響，這也應該列入後續測試規劃，跟 14.4 節提到的相機擺放角度一併考慮。

### 14.7 使用者調整相機角度後重新驗證：問題確實被解決

使用者根據 14.4／14.6 節的發現，把相機實際往上抬、調整角度避開螢幕與窗戶，之後用同一套方法（開瀏覽器全螢幕白底頁面 vs. 不開）重新測了一次。第一次重測誤用了深色模式的 Google 首頁（實際背景是黑色，不是白色），數字看起來混亂，發現問題後改用保證白底的 `https://example.com`，並排除頁面載入與全螢幕切換過程中的曝光過衝，只取收斂後的樣本平均：

| 情境（新角度） | 收斂後平均亮度 |
|---|---|
| 無白頁面（基準） | 0.000210 |
| 全螢幕白底頁面 | 0.000208 |

兩者幾乎完全相同，差距在雜訊範圍內，跟調整前的對照（基準 ~0.0000 vs. 白頁面 0.31／收斂後 0.12，見 14.2 節）差異非常明顯。**這代表相機擺放角度確實是這個問題的關鍵變因，把相機抬高、避開直視螢幕與窗戶之後，14.2～14.6 節描述的螢幕內容回饋現象在這台機器上已經測不出來了。**

這不代表問題在所有擺放情境下都解決了——本輪只驗證了「這一個新角度」有效，14.6 節提到的窗戶／反光表面對讀值的影響仍未系統性測試（例如刻意打開窗簾、調整不同反光角度做對照）。但至少證明了 14.4 節建議的「硬體層面：確保相機擺放角度看不到螢幕本身或其明顯反射光」這個方向是可行、且用實測數據驗證過的解法，不是紙上談兵的建議。

### 14.8 擺放不限於「螢幕上方朝使用者」：朝牆壁也可行（使用者觀察，2026-09-04）

使用者發現相機不一定要架在螢幕上方對著自己，直接對著一面牆壁也能運作。牆面讀到的是室內反射／間接光，本身就是「房間有多亮」的合理代理量，而且因為完全看不到螢幕，天生迴避了 14.1～14.4 節的螢幕內容回饋問題。代價是這面牆上的內容會混進訊號：局部的燈光熱點、有人走動造成的移動陰影、或牆色本身，都會影響讀值；而且照不到那面牆的光源不會被感知到。這是一個尚未系統性測試的新擺放選項，可與 §5 Nice-to-have 的「相機擺放引導」一併考慮（引導文案不必假設只有「架在螢幕上」一種擺法）。

---

## 15. 自適應取樣與平滑的實作補強（2026-09-04，尚待真機端對端驗證）

第 13 節的實測確認漸進亮度控制器本身可用，但也指出固定 5 秒取樣搭配固定 EMA 的反應時間會影響使用體感。為了讓「持續且明確的環境變化」更快進入既有的三段分級，同時不破壞第 13.4 節已知的 Camera Sharing 穩定性限制，App 新增以下純軟體層補強：

### 15.1 自適應 EMA

`SampleSmoother.cs` 保留小幅讀值擾動使用 `alpha = 0.5` 的行為；當 `|raw - smoothed| >= 0.1` 時改用 `alpha = 0.9`，並在下一次取樣保留一次強係數尾窗。這讓開／關燈這類大幅持續變化可以在兩三次取樣內追上，而單次突波仍會受到 `BrightnessMapper` 的「連續兩次確認」與遲滯保護。

這不是增加新的亮度級距，也不是將相機讀值當作連續 Lux 訊號；Gate B 的三段限制維持不變。

### 15.2 自適應取樣節奏與安全上限

`SamplePacing.cs` 在讀值與平滑值差距至少 `0.03`、或任一值距分級邊界不超過 `0.05` 時，將下一輪取樣間隔從穩定態的 5 秒縮短為 500ms。讀值回穩後立即回到慢間隔。

為避免第 13.4 節觀察到的高頻率相機操作風險，快模式最多連續 30 輪，之後強制一輪慢取樣；任何取樣失敗都會立刻退回慢間隔。這是節流措施，不代表已證實 FrameServer 能安全承受長時間快取樣。

### 15.3 收斂後提前結束取樣窗

`AmbientLightSensor.cs` 每次 Lazy 取樣仍先略過 550ms 曝光收斂期。之後每 100ms 檢查最近至少 6 個 frame 是否符合 Test 04／05 的既有穩定判定（max-min <= 0.01）；符合時提早結束，不必固定等待滿 1.2 秒。收斂慢時仍維持原本 1.2 秒上限，避免縮短取樣而取得未收斂讀值。

### 15.4 邏輯自檢與目前證據邊界

新增 `SelfTest.cs`，可執行：

```powershell
dotnet run -- --selftest
```

2026-09-04 執行結果為 **19/19 通過**（同日稍晚 §16 加入 3 項 `AsyncGuard` 自檢後為 **22/22**），涵蓋：

- 三段分級的首次判定、雙重確認、遲滯與第 12.4 節最低分級邊界修復；
- 自適應 EMA 的小擾動、強變化、尾窗與突波回復；
- 自適應取樣的快／慢切換、失敗退避與快模式上限；
- 取樣窗的收斂判定；
- （§16）`AsyncGuard`：卡住的 `StopAsync` 逾時後不阻擋後續 `Dispose`。

這些自檢不會開啟相機或寫入 DDC/CI，不能取代真機證據。本節新增的自適應平滑、快取樣與提前結束功能尚未完成新的「相機取樣 → 分級 → 漸進調光」端對端實測，也沒有改變 Gate A／B／C 的既有判定。後續真機驗證應同時確認：

1. 關燈／開燈後能在可接受時間內到達既有三段目標，且不因短暫突波誤切換；
2. 快模式結束後會退回慢間隔，長時間執行不引發相機共享卡死；
3. 第 14.7 節已驗證的相機角度下，螢幕白色內容仍不會污染輸入訊號。

---

## 16. 釋放路徑加固與相機診斷紀錄（2026-09-04，讀碼發現，尚待真機 Test 13 佐證）

### 16.1 觸發：使用者觀察到「一直取樣失敗、關掉 App 相機才恢復」

使用者實測回報兩個現象：(a) 這顆便宜 USB 網路攝影機在電腦開機後可能沒被正確初始化，未使用本 App 前處於 disconnect 狀態；(b) 一旦本 App 進入連續取樣失敗，**把 App 關掉之後相機就恢復正常**。現象 (b) 跟 §13.3 記錄的「App 自己的相機工作階段卡住、獨立探測工具卻正常」是同一類——卡住的是本 App 持有的工作階段，而 process 結束會強制釋放。

### 16.2 讀碼發現的可疑點（尚未 runtime 驗證）

`AmbientLightSensor.SampleOnceAsync()` 原本的 `finally` 依序執行 `await reader.StopAsync()` → `mediaCapture.Dispose()`。已知 `MediaFrameReader.StopAsync()` 在 FrameServer 打嗝時可能長時間不返回；一旦它卡住，後面的 `mediaCapture.Dispose()` 就永遠跑不到，這個 `MediaCapture` 的相機控制代碼會被 App 持有到 process 結束，正好對應現象 (b)。此外 `reader`（`MediaFrameReader`）從頭到尾沒有被 `Dispose()`，只 `StopAsync()`。次要放大因素：`TryReconnectSensorAsync` 每次重連都會透過 `PrepareAsync → FindColorSourceAsync` 額外 `new MediaCapture()` 一次只為讀格式，在「已經連續失敗」的當下製造更密集的相機開關 churn（§13.4 記錄過這類模式會把 Camera Sharing 測到系統層級卡死）。

### 16.3 已做的加固

- **`SampleOnceAsync` 的 `finally` 改為不可被卡住**：事件處理器一定先解除；`StopAsync()` 以 `AsyncGuard`（新檔）包 2 秒 timeout，卡住就不再等它；`reader` 與 `mediaCapture` 各自獨立 `try/catch` Dispose，彼此不受影響、也不受 `StopAsync` 影響。`reader` 現在會被 `Dispose()`。
- **`TryReconnectSensorAsync` 換手前先 `Dispose()` 舊 sensor**（原本直接捨棄）。
- **`AsyncGuard`**：逾時後不取消底層工作（WinRT `StopAsync`／`IClosable` 不保證可取消），只是停止等待，確保呼叫端的釋放一定執行得到。3 項自檢見 §15.4（22/22）。

### 16.4 新增 `camera-diagnostics.csv`（Test 13 證據來源）

新增 `CameraDiagnosticsLog`，寫到獨立於 `validation-log.csv` 的：

```text
%AppData%\WCALSS\AmbientBrightness\camera-diagnostics.csv
```

每次取樣與每次 init／reconnect 各記一列，欄位含：`initialize_ms`、`start_status`、`frames_arrived`、`sample_window_ms`、`stop_async_ms`、`stop_async_timed_out`、`reader_dispose_ms`、`media_capture_dispose_ms`、`failed_step`（none／initialize／start／post-init／no-frames），以及 prepare 階段的 `device_enum_ms`、`enumerated_devices`、`target_device_found`、`resolved_format`。這幾欄就是回答以下問題的直接資料：`stop_async_timed_out=True` → §13.3 那種卡死的直接證據；`enumerated_devices` 是否為空 → 開機後相機到底有沒有被 Windows 列舉（現象 a）。

### 16.5 真機擷取結果（2026-09-04，`camera-diagnostics.csv`）

用加固後的版本執行，04:06–04:09 的一段紀錄如實推翻了 §16.2 的主要假設：

- **釋放路徑沒有卡住**：所有成功取樣的 `stop_async_ms` 穩定在 255–262ms、`stop_async_timed_out` 全為 `False`、`media_capture_dispose_ms` 每次都有值（~205ms）。2 秒 timeout 一次都沒觸發。「`StopAsync` 卡住 → `Dispose` 跑不到 → 控制代碼洩漏到 process 結束」在這次擷取中沒有發生。
- **真正的失敗是 USB 裝置掉線**：04:06:49 起 `InitializeAsync` 在 0–4ms 內炸出 `COMException 0xC00D36B2`（`MF_E_INVALIDREQUEST`，錯誤文字帶 `deviceActivateCount`）；接著約 70 秒間，`reconnect` 的 `enumerated_devices` 一直是空的、`DeviceInformation.FindAllAsync(VideoCapture)` 回傳零裝置，之後錯誤升級為 `0xC00DABE0`（`MF_E_NO_CAPTURE_DEVICES_AVAILABLE`）。裝置在 OS 層級整個從匯流排上消失，不是被佔用。`deviceActivateCount` 是 Media Foundation 對「活著的 activation 底下裝置突然被拔掉」的反應。
- **自動重連自己救回來了**：04:08:00 `reconnect` 成功（裝置重新列舉為 `USB Camera`，格式從 1920×1080 變回 640×480，順帶清掉 §5.2 的 Camera Sharing 殘留鎖），之後恢復健康取樣。這一輪沒有需要「關掉 App 才恢復」。

結論：§16.3 的加固無害且正確（未來若 `stop_async_timed_out=True` 會立刻現形），但這次真機資料**沒有**證實它曾是病因。這台機器上觀察到的是便宜相機的硬體掉線，軟體層無法修復掉線本身。§16.1 現象 (b)（舊版「關 App 才恢復」）無診斷紀錄可比對，暫時無法歸因。這仍值得正式化為 Test 13（USB 熱插拔／disconnect），補記開機後 `enumerated_devices` 狀態與「什麼動作讓相機恢復」。這不改變 Gate A／B／C 的既有判定。

### 16.6 裝置離線前置檢查（回應 §16.5 的實際發現）

裝置已從列舉消失時，原本的行為是每 5 秒仍對 Media Foundation 硬送一次 `InitializeAsync`（0–4ms 秒炸）＋每 15 秒一次完整重連。§13.4 警告過這種對 MF／裝置的高頻 thrash 可能把 Camera Sharing 推進系統級卡死（這次沒發生，但風險面相同）。

加上：`AmbientLightSensor.CheckTargetDevicePresenceAsync()` 只做裝置列舉、不碰 `MediaCapture`。`TrayContext` 在**已經處於連續失敗狀態**（`consecutiveSampleFailures > 0`）時先跑這道檢查；目標裝置不在列舉中就略過本輪取樣、不觸碰相機 API，`camera-diagnostics.csv` 記一列 `phase=device-check`。健康路徑（首次失敗前）不受影響、零額外開銷；掉線後最多只多吃一次 0–4ms 的 `InitializeAsync` 失敗，之後就完全不戳 MF。`PrepareAsync`（重連用）本來就會在裝置缺席時 fail-fast 且不建立 `MediaCapture`，維持不變。

這段只能在真機的裝置掉線情境驗證（即 §16.5 重現的那種），`--selftest` 不涵蓋。

### 16.7 待處理：啟動時相機不在，之後插回 USB 不會被自動偵測（2026-09-04，使用者回報 + log 佐證）

使用者回報：USB 相機拔掉後再插回去，App 不會自己重新偵測到、不會恢復。`camera-diagnostics.csv` 佐證了機制：

- 04:10–04:15 一段運作中，出現數次 `failed_step=no-frames` 的間歇失敗，且 `initialize_ms` 逐次升高（1259 → 1129 → 1642 → 3386ms），每次下一輪自行恢復——相機在完全掉線前已經在劣化。
- 04:15:26 最後一筆取樣後中斷約 90 秒（App 被關閉）。
- 04:16:56 一列 `phase=initialize, success=False, InvalidOperationException (0x80131509)`，`enumerated_devices` 為空、`target_device_found=False`——**App 重啟時 `PrepareAsync` 找不到相機**。
- log 到此為止，之後沒有任何列。

**識別到的缺口**：兩條恢復路徑不對稱。

| 情境 | 目前行為 |
|---|---|
| 相機在**運作中**掉線 | `sampleTimer` 持續 tick → 取樣連續失敗 → 3 次後 `TryReconnectSensorAsync` → §16.6 前置檢查生效 → 裝置回來時恢復（§16.5 的 04:08:00 已驗證有效） |
| 相機在**啟動時／設定變更 re-init 時**不在 | `TrayContext.InitializeAsync` 的 `PrepareAsync` 拋例外 → `sensorReady=false` → **`sampleTimer` 從未 `Start()`** → `SampleOnceAsync` 因 `!sensorReady` 永遠 early-return → 沒有任何東西在輪詢 → 之後插回 USB 也不會被發現，App 卡死直到手動重啟 |

**修法方向（下班後處理，尚未實作）**：`InitializeAsync` 失敗時不要就停住——起一個慢速重試（例如每 15–30 秒再跑一次 `PrepareAsync`，成功才 `sensorReady=true` 並 `sampleTimer.Start()`），或乾脆一律 `Start()` 讓 `SampleOnceAsync` 在 `sensor is not null && !sensorReady` 時自行嘗試 `PrepareAsync`。重試也要沿用 §16.6 的「裝置不在就只列舉、不戳 MF」原則，避免對 MF 高頻 thrash。`--selftest` 不涵蓋，需真機驗（拔 USB 啟動 App → 插回 → 應在一個重試週期內自動恢復）。
