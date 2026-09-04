# LightBee — 專案說明

> 建立日期：2026-09-04
> 最後更新：2026-09-04

## 一句話說明

LightBee 用一般網路攝影機作為粗略環境光感測器，依環境明暗以三段式自動調整 Windows 螢幕亮度。

## 核心問題

沒有硬體環境光感測器的 Windows 桌機與外接螢幕使用者，必須手動調整螢幕亮度。LightBee 將已驗證的相機讀值、分級與漸進調光流程產品化。

## 技術棧

- **語言**：C# / .NET 8，產品目標 .NET 10 LTS
- **框架 / 主要套件**：WinForms、WinRT MediaCapture / MediaFrameReader、System.Management
- **資料存儲**：%AppData% 下的 JSON 設定與 CSV 驗證紀錄
- **目標平台**：Windows 10 1809+、Windows 11

## Coding Agent 工具

- **主要工具**：Codex
- **備用工具**：Claude Code

## 相關文件

- 規格書：docs/spec.md
- Spike 報告：docs/spike-report.md
- 下一步：NEXT_ACTION.md
- 決策紀錄：DECISIONS.md

## 重要限制

- 僅提供三段式亮度調整，不模擬連續調光。
- 系統層級的相機共用卡死無法由 App 修復，必須提供使用者復原指引。
- 相機取樣節奏必須維持低相機佔用與失敗退避。
