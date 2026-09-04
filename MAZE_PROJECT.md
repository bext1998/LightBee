# MAZE_PROJECT — LightBee 定位與工作流設定

> 由 `maze-project-init` 建立。Agent 讀取規格前必須先由此取得實際路徑。

## 專案資訊

- 專案名稱：LightBee
- 目標工具：Codex、Claude Code
- 建立日期：2026-09-04

## 文件

- Spec：docs/spec.md
- Project Brief：PROJECT_BRIEF.md
- Next Action：NEXT_ACTION.md
- Decisions：DECISIONS.md

## 自適應 Guidance

- Default profile：minimal
- Model overlay：gpt-5.6
- Host capabilities：PowerShell、Git、GitHub CLI、Codex 子代理與平行工具
- Profile escalation evidence：僅在具體失敗時記錄

## GitHub

- Repository：bext1998/LightBee
- Issue tracking：enabled
- Spec to Issues：enabled
- Priority label convention：P1、P2、P3、P4
- Category label convention：feature、testing、infrastructure、documentation
- Default assignee policy：none
- Allow label creation：yes

## 備注

- `app/WcalssAmbientBrightness/` 與 `spike/camera-probe/` 是既有雛型；M1 才會進行產品命名與 WinRT 投影轉正。
