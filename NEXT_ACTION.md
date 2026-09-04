# LightBee — 下一步行動

> 僅保留當前有效前線；明確 closeout 時整體重建，不追加歷史。

## 下一個 Session 目標

完成 M1「雛型轉正」的產品識別、資料遷移與 WinRT 投影轉正。

## 行動（最多 3 項）

1. 完成 [#1](https://github.com/bext1998/LightBee/issues/1)：產品命名與 `%AppData%` 資料遷移，執行 `--selftest`。
2. 完成 [#2](https://github.com/bext1998/LightBee/issues/2)：將 WinRT preview 投影替換為正式 targeting pack，驗證建置與 `--selftest`。
3. 完成 M1 後，依 [#7](https://github.com/bext1998/LightBee/issues/7) 與 [#8](https://github.com/bext1998/LightBee/issues/8) 規劃真機端對端與長時間穩定性驗證。

## 阻塞與待決策

- 雛型 App 執行時會鎖定輸出 DLL；建置前須先停止 App。

## 權威連結

- docs/spec.md §7、§9
- docs/spike-report.md §13.4、§15.4
- https://github.com/bext1998/LightBee/issues/1
- https://github.com/bext1998/LightBee/issues/2
