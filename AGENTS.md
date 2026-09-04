# LightBee — Coding Agent 指令

## 專案概述

LightBee 是以網路攝影機偵測環境明暗、三段式調整螢幕亮度的 Windows 系統匣工具。

技術棧：C# / .NET + WinForms + WinRT、WMI、DDC/CI

## 工作原則

1. 只實作任務要求的功能，不添加額外功能或重構。
2. 先閱讀 `MAZE_PROJECT.md`、`NEXT_ACTION.md` 與相關規格章節。
3. GitHub Issue／PR 與 Git 是工作狀態權威；只有明確 closeout 才重建 `NEXT_ACTION.md`。
4. 修改相機取樣節奏時，保留快速模式 30 輪上限與取樣失敗退避。
5. 修改前確認雛型 App 未執行，避免輸出 DLL 被鎖定。

## 重要文件

| 文件 | 用途 |
|---|---|
| `docs/spec.md` | 功能規格與驗收標準 |
| `docs/spike-report.md` | Spike 測試數據與 Gate 判定 |
| `NEXT_ACTION.md` | 下一步行動 |
| `DECISIONS.md` | 有效重大決策索引 |

## 禁止行為

- 不得 force push 到 `main`。
- 不得在使用者未確認前 commit 或 push。
- 不得修改 `docs/spec.md` 的功能範圍，除非使用者明確要求。
