# BMC Framework

BMC Framework 是一個基於 Unity 引擎開發的模組化遊戲框架。本框架致力於提供高度解耦的系統架構，讓開發者能夠**以 Unity Package Manager (UPM) 的形式，將所需模組直接安裝到自己的專案中**，並整合了強大的資料配置導出工具 [Luban](https://github.com/focus-creative-games/luban)，大幅提升遊戲開發效率。

## ✨ 核心特性

* **UPM 模組化安裝**：框架被拆分為多個獨立的 UPM 套件（如 Core、UI、Story 等），不綁架您的專案架構，開發者可依專案需求「隨插即用」。

* **保持專案整潔**：透過 Git URL 載入套件，無須複製繁雜的框架原始碼到您的專案 `Assets` 目錄中，讓您的專案保持乾淨，且後續升級與維護更加便利。

* **強大的數據驅動**：內建 `LubanTool` 工作流，支援 Excel/JSON 等多種格式的遊戲配置數據定義與 C# 程式碼自動生成。

* **高效能與易擴充**：以 C# 為主要開發語言（90%+），底層架構清晰，並包含自定義 ShaderLab 等渲染優化，方便二次開發與擴充。

## 📁 專案結構與模組介紹

本 GitHub 儲存庫本身是一個完整的 Unity 專案，同時也作為各個 UPM 套件的託管來源。主要的模組都放置於 `Assets/Packages/` 目錄下。

```text
BMC_Framewok/
├── Assets/
│   └── Packages/       # 🚀 供外部專案匯入的 UPM 模組存放區
│       ├── BMC.Core/   # 📦 核心模組：提供底層架構、通用工具類 (Utility) 與核心 API 系統
│       ├── BMC.UI/     # 🖼️ UI 模組：提供介面管理系統、視窗開啟/關閉與 UI 生命週期控制
│       └── BMC.Story/  # 📖 劇情模組：提供遊戲內的劇情播放機制、對話系統與邏輯腳本控制
├── LubanTool/          # Luban 數據配置導出工具與執行腳本 (需手動複製到您的專案使用)
├── Packages/           # 本開發專案自身的 Unity Package 依賴設定
├── ProjectSettings/    # 本開發專案的 Unity 設定檔
└── .gitignore          # Git 忽略設定
