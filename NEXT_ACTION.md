# LightBee — 下一步行動

> 僅保留當前有效前線；明確 closeout 時整體重建，不追加歷史。
> 最後更新：2026-09-09

## 下一個 Session 目標

推進 [#11](https://github.com/bext1998/LightBee/issues/11) P0 pilot 的資料收集，回答「三段是否為這顆相機的天花板」。換相機（C270）相容性修正已完成並併入 `main`。

## 行動（最多 3 項）

1. **A（被動）**：讓 LightBee 正常執行 5–7 天累積 `validation-log.csv`，之後拉出讀值分布，看落在中間帶（暫定 0.06–0.28）的頻率。
2. **C（條件因子）**：跑 2–3 個 session，**鎖曝光**，約 12 種光照條件、每種 ≥8 次採集。桌機用 `--sensitivity-probe`，手機用工程模式採集頁（`啟動工程模式伺服器.cmd` → `https://<主機名>.ts.net/`）當參考相機。
3. **D（分析）**：相鄰階可分離性（Cohen's d ≥ 2、95% 區間不重疊、≥3 session 一致）＋真實使用頻率 → Go（研究 N 段）／轉向 exposure 訊號／No-Go（三段是天花板）。

## 相機後續待辦（併入 main，未驗證項見 issue）

- [#12](https://github.com/bext1998/LightBee/issues/12) §17 修正的冷啟動真機驗證（運作中重連已驗）
- [#13](https://github.com/bext1998/LightBee/issues/13) per-camera 光度校正（C270 暫定門檻 0.06/0.28，正式校正待鎖曝光重做）
- [#15](https://github.com/bext1998/LightBee/issues/15) 自動重連要確認 frame 實際抵達，不只 PrepareAsync 成功
- [#14](https://github.com/bext1998/LightBee/issues/14) 泛用多 `BitmapPixelFormat` luminance 讀取（僅在 #12 發現 MF 轉不動 NV12 時啟動）
- [#16](https://github.com/bext1998/LightBee/issues/16) 設定內建自助校正流程（把 `--sensitivity-probe` 包成引導式 GUI）

## M1（未完成，目前次於 P0）

- [#1](https://github.com/bext1998/LightBee/issues/1) 產品命名與 `%AppData%` 資料遷移
- [#2](https://github.com/bext1998/LightBee/issues/2) WinRT preview 投影替換為正式 targeting pack

## 阻塞與待決策

- 雛型 App 執行時會鎖定輸出 DLL；建置前須先停止 App，或建置到隔離輸出目錄。
- §4「Persistent 常駐串流 vs Lazy」尚待使用者拍板（見 spike-report §17.7、桌面待辦）。

## 權威連結

- docs/spec.md §7、§9；docs/spike-report.md §17
- docs/proposals/engineering-mode-research-2026-09-05.md（P0 提案 + 審查紀要）
- https://github.com/bext1998/LightBee/issues/11
