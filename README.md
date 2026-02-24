# BMC Framework

BMC Framework 是一個基於 Unity 引擎開發的模組化遊戲框架。本框架致力於提供高度解耦的系統架構，讓開發者能夠**以 Unity Package Manager (UPM) 的形式，將所需模組直接安裝到自己的專案中**，並整合了強大的資料配置導出工具 [Luban](https://github.com/focus-creative-games/luban)，大幅提升遊戲開發效率。

## 🤝 貢獻指南

本專案供大家學習與參考，但目前**不接受 Pull Request**。如果您有任何建議或發現了 Bug，歡迎發起 [Issues](https://github.com/silverofw/BMC_Framewok/issues) 進行交流與討論。

---

## 📑 目錄
- [貢獻指南](#-貢獻指南)
- [核心特性](#-核心特性)
- [專案結構與模組介紹](#-專案結構與模組介紹)
- [快速開始](#-快速開始)
  - [環境要求](#環境要求)
  - [安裝步驟](#安裝步驟)
  - [Luban 數據導出工具使用方式](#luban-數據導出工具使用方式)
- [第三方依賴 (Third-Party Plugins)](#-第三方依賴-third-party-plugins)
- [授權條款](#-授權條款)

---

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
```

## 🚀 快速開始

### 環境要求

* **Unity**：建議使用 Unity 2021.3 LTS 或更高版本。
* **.NET**：.NET Framework 4.7.1 或 .NET Standard 2.1 以上。

### 安裝步驟

本框架支援透過 Unity Package Manager (UPM) 載入，採用模組化設計，您可以根據專案需求，選擇性地獨立安裝各個模組。

1. 開啟您的 Unity 專案，點選上方選單 `Window` -> `Package Manager`。
2. 點擊左上角的 `+` 號按鈕，選擇 `Add package from git URL...`。
3. 根據您需要的模組，輸入以下對應的網址並點擊 `Add`：

#### 📦 核心模組 (BMC.Core)

框架的基礎核心模組，包含底層架構與通用工具。**（為確保其他模組正常運作，建議優先安裝此模組）**

```
https://github.com/silverofw/BMC_Framewok.git?path=/Assets/Packages/BMC.Core
```

#### 🖼️ UI 模組 (BMC.UI)

提供遊戲中的介面管理與 UI 系統。

```
https://github.com/silverofw/BMC_Framewok.git?path=/Assets/Packages/BMC.UI
```

#### 📖 劇情模組 (BMC.Story)

提供對話系統與劇情邏輯控制。

```
https://github.com/silverofw/BMC_Framewok.git?path=/Assets/Packages/BMC.Story
```

*(註：由於 UPM 只會匯入上述路徑內的程式碼，若您需要使用框架內建的 Luban 導出工具，請額外將本專案原始碼中的 `LubanTool/` 資料夾下載，並放置於您的專案根目錄下。)*

### Luban 數據導出工具使用方式

本框架使用 Luban 作為配置表數據的導出解決方案。

1. 將本儲存庫中的 `LubanTool/` 目錄複製到您的專案中。
2. 在 Excel 表格中定義或修改你的遊戲配置數據（如：角色屬性、道具列表）。
3. 執行對應的生成腳本（例如 `gen.bat` 或 `gen.sh`），工具將會自動生成 C# 數據結構腳本與二進位/JSON 數據檔案，並同步至您的 `Assets/` 目錄中。
4. 在 Unity 中透過框架提供的配置管理器直接讀取數據。

## 🧩 第三方依賴 (Third-Party Plugins)

本框架在開發與運行過程中，整合了以下優秀的開源與第三方套件（未列出 Unity 官方內建套件）：

* [**Luban**](https://github.com/focus-creative-games/luban)：強大的多語言、多格式遊戲配置數據導出與程式碼生成工具。
* [**HybridCLR**](https://github.com/focus-creative-games/hybridclr)：強大且高效的 Unity 原生 C# 熱更新解決方案。
* [**YooAsset**](https://github.com/tuyoogame/YooAsset)：強大穩定的商業級資源管理與資源熱更新系統。
* [**UniTask**](https://github.com/Cysharp/UniTask)：為 Unity 提供高效的零內存分配 async/await 異步解決方案。
* [**UIEffect**](https://github.com/mob-sakai/UIEffect)：為 Unity UI (uGUI) 提供各種視覺特效（模糊、灰階、漸層等）。
* [**SQLite-net**](https://github.com/gilzoide/unity-sqlite-net)：輕量級的跨平台 C# SQLite ORM 資料庫解決方案。
* [**NuGetForUnity**](https://github.com/GlitchEnzo/NuGetForUnity)：讓您能在 Unity 編輯器中直接管理和使用 NuGet 依賴套件。

## 📄 授權條款

本專案採用 [MIT License](LICENSE) 進行開源。
