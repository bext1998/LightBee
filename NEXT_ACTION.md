# LightBee — 下一步行動

> 僅保留當前有效前線；明確 closeout 時整體重建，不追加歷史。

## 下一個 Session 目標

完成 M1「雛型轉正」的產品識別、資料遷移與 WinRT 投影轉正。

## 行動（最多 3 項）

1. 處理 M1 的產品命名與 `%AppData%` 資料遷移，執行 `--selftest`。
2. 將 WinRT preview 投影替換為正式 targeting pack，驗證建置與 `--selftest`。
3. 依 `docs/spec.md` §7 規劃 M3 的真機端對端與長時間穩定性驗證。

## 阻塞與待決策

- 雛型 App 執行時會鎖定輸出 DLL；建置前須先停止 App。

## 權威連結

- docs/spec.md §7、§9
- docs/spike-report.md §13.4、§15.4
