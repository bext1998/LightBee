# WCALSS Camera Probe

這個 Windows Console App 實作 Spike 規格書第 6～9 節：

- Test 01：以 `Windows.Devices.Enumeration.DeviceInformation` 列舉 `DeviceClass.VideoCapture`。
- Test 02：以 `Windows.Media.Capture.MediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord)` 列舉裝置公布的 Capture Mode。
- Test 03：以 `VideoDeviceController` 的控制物件 `Supported` 屬性探測控制能力。
- Test 08：以 `coldstart` 的重複 Open→Sample→Release 循環探測 Lazy Acquisition 穩定性。
- Test 09：以 `persistent` 與 `coldstart` 的資料比較 Persistent/Lazy 行為。
- Test 10（部分）：`coldstart`／`persistent` 皆支援 `--sharing-mode exclusive|shared`，並在 `SelectFormat` 找不到 640x480/NV12/30fps 時自動 fallback 到目前可用的最高解析度 NV12 格式，用於跟其他 App 共存時的實測。

本工具也包含 `coldstart`、`analyze` 與 `persistent` 子命令，實作 Test 04～06、08、09、10（部分）的 frame 取樣、亮度計算、Raw CSV、收斂分析、生命週期比較與共存測試。本輪 `roi` 固定為 `full`，不做 ROI 切割；不包含 Test 07 之後的功能，Test 10 也只測了 Windows 相機 App，Discord／瀏覽器／OBS 尚未測試。

## Build

在本目錄執行：

```powershell
dotnet build
```

## Run

```powershell
dotnet run
```

不帶參數會執行 Test 01～03。Console 會印出三個 Test 的結果；同一份完整結果會寫到：

```text
spike/camera-probe/raw-data/camera-probe-<UTC timestamp>.json
```

每次執行會產生新的 JSON，不會覆蓋既有 raw data。

## Cold Start / Exposure Convergence / Environment Separation

使用已列舉且驗證支援的 `USB Camera`、`640x480 @ 30 FPS / NV12`，執行 5 次、每次 3 秒的 cold start：

```powershell
dotnet run -- coldstart --environment current-room-baseline
```

可用參數：

```text
--environment <字串>     必填環境標籤
--duration-ms <整數>     每輪觀測時間，預設 3000
--repeats <整數>         輪數，預設 5
--pause-ms <整數>        每輪釋放後等待時間，預設 1000
--device-name <字串>     裝置名稱，預設 USB Camera
--sharing-mode <字串>    exclusive（預設，對應 MediaCaptureSharingMode.ExclusiveControl）或 shared（對應 SharedReadOnly）
```

每輪會建立新的 `session_id`，在 `MediaFrameReader.FrameArrived` 收集每個 frame。Frame 透過 `MediaFrameSourceGroup` 的 color source、`SetFormatAsync` 設為 640x480/30/NV12，再由 `SoftwareBitmap.CopyToBuffer` 讀取 NV12 buffer 的前 `width * height` 個 Y-plane bytes。Mean 是 Y 平均值 / 255；Median 使用 256-bin histogram 後除以 255。

Raw samples 會 append 到：

```text
raw-data/luminance-samples.csv
```

每次 coldstart 另寫一份 metadata：

```text
raw-data/coldstart-<UTC timestamp>.json
```

`coldstart` 同時會將每個 Open→Sample→Release cycle append 到：

```text
raw-data/lazy-cycles.csv
```

欄位為 `timestamp,session_id,repeat,open_latency_ms,frame_count,capture_success,release_success,error`。metadata 另外記錄該次執行的 `total_cpu_time_ms` 與 `total_wall_time_ms`。Console 會列出每 cycle 的 open latency、前/後 5 輪平均、capture/release/error 統計，以及所有 Mean Luminance 都低於 `0.01` 的疑似黑 frame cycle。

長時間 Lazy 測試範例：

```powershell
dotnet run -- coldstart --environment endurance-lazy --repeats 30 --duration-ms 3000 --pause-ms 1000
```

## Persistent Acquisition

Persistent 模式只 Open 一次、保持 MediaFrameReader 執行指定時間後再 Release；frame 仍 append 到同一份 `luminance-samples.csv`：

```powershell
dotnet run -- persistent --environment endurance-persistent --total-duration-ms 120000
```

可用參數：

```text
--environment <字串>       必填環境標籤
--total-duration-ms <整數>  持續擷取時間，預設 120000
--device-name <字串>       裝置名稱，預設 USB Camera
--sharing-mode <字串>      exclusive（預設）或 shared，同 coldstart
```

Persistent 另寫 metadata：

```text
raw-data/persistent-<UTC timestamp>.json
```

其中記錄單次 `open_latency_ms`、frame count、capture/release/error，以及 `total_cpu_time_ms`、`total_wall_time_ms`。CPU 時間使用 `Process.GetCurrentProcess().TotalProcessorTime`，Wall time 使用 `Stopwatch`；這些數字是整次命令執行期間的量測值，不包含使用者干擾測試。

## Analyze

讀取累積 CSV，依每個 session 計算 Time-to-Stable-Luminance，並依 environment 統計 Mean Luminance 的最小值、最大值與平均值：

```powershell
dotnet run -- analyze
```

預設穩定判定為「最近 15 個 frame 的 Mean Luminance `max - min <= 0.01`」。兩個值都可由 CLI 覆寫，不是判斷邏輯中的固定常數：

```powershell
dotnet run -- analyze --stability-window 15 --stability-threshold 0.01
```

也可指定其他 CSV：`--csv <path>`。`camera_sharing` 欄位目前仍固定輸出 `false`，實際共存狀態請看 CSV 裡 `environment` 標籤是否帶 `coexistence-` 前綴與對應的 raw data，尚未接成自動偵測。

## 實作備註

本輪實測確認 `MediaFrameSourceGroup`、`SetFormatAsync`、`MediaFrameReader` 與 NV12 frame 到達均可運作。原先嘗試直接以 `IMemoryBufferByteAccess` 讀取 `BitmapBuffer`，但此機器的 .NET 8 與舊版 Windows SDK projection 組合在 runtime 發生 cast/QI 失敗；因此改用同一個 `SoftwareBitmap` 的 Windows 公開 `CopyToBuffer(IBuffer)` API 取得 bytes，沒有改用 DirectShow、OpenCV 或 RGB 轉換。

Test 10 實測發現：與其他 App 共存、且 Windows 的 Camera Sharing 系統設定開啟時，`MediaFrameSource.SupportedFormats` 可能被限縮成只剩對方已在用的單一格式，因此 `SelectFormat` 不能再假設 640x480/NV12/30fps 一定存在，加了 fallback 到可用清單中解析度最高的 NV12 格式；同時新增 `--sharing-mode` 讓 `ExclusiveControl`／`SharedReadOnly` 可以分開測試比較。

## 控制能力對應

`Auto Exposure` 對應 `ExposureControl.Supported`，`Exposure` 對應 `Exposure.Supported`，`Gain` 對應 `IsoSpeedControl.Supported`，`White Balance` 對應 `WhiteBalance.Supported`，`Backlight Compensation` 對應 `BacklightCompensation.Supported`。

Windows `VideoDeviceController` 沒有可直接對應的獨立 `Auto White Balance` 或 `Low Light Compensation` Supported 介面，因此工具會如實輸出 `Unknown`。
