# Windows 音樂同步實作證據

2026-10-21，Asia/Taipei。本目錄根層保留 0.1.1.0 的 60 秒音訊診斷；**目前本機安裝版本是 0.1.2.0**，最新資料在 [0.1.2](0.1.2/)。

| 檔案 | 證據與界線 |
| --- | --- |
| `core-tests.json` | 26 音訊解碼＋17 映射檢查通過。未接觸硬體。 |
| `packaged-runtime.json` | 0.1.1.0 實際封裝啟動、三段自製測試音與靜音；RMS／Level 有反應，但 IsAvailable=false、SentFrames=0。 |
| `initial-steady-resources.json` | 0.1.0.0 的短時常駐資源取樣，已修正時間換算；不是完整同步負載。無效的早期換算檔僅留本機，未收錄。 |
| `package-report.json` | 0.1.1.0 unsigned MSIX 的 MakeAppx 結果，不是目前最新套件。 |
| `development-install-report.json` | 0.1.1.0 開發 layout 註冊，非簽署 MSIX／Store 安裝。 |
| `app-window.png` | 僅 App 視窗的程式內繪圖，不是桌面截圖。 |
| `0.1.2/package-report.json` | 最新 0.1.2.0、自包含 x64、57,610,921 bytes；MakeAppx 正常驗證通過。 |
| `0.1.2/development-install-report.json` | 最新版本開發註冊，未改 Developer Mode 或憑證信任。 |
| `0.1.2/packaged-runtime.json` | 最新版本 15 秒啟動：有套件身分、唯一 8 燈、無啟動錯誤；仍未取得控制權，送色0幀。本輪沒有再播放測試音。 |
| `0.1.2/source-sha256.json` | 31 份 App 原始碼、腳本、文件與資產的雜湊。 |
| `0.1.2/script-validation.json` | 5 份 PowerShell 腳本語法檢查0錯誤。 |
| `*/evidence-hashes.json` | 複製檔案與本機原件 SHA-256 核對相同。 |

音訊與 UI 層已驗證，**實體音樂同步仍待 Windows 背景控制權與使用者觀察**；不能由三段音訊、預覽或已註冊 MSIX 推定燈條已同步。WACK、正式 Store 身分、乾淨機／Store 簽署安裝、認證與公開發布均尚未完成。

本機曾生成 0.1.0.0 與兩個 0.1.1.0 迭代套件，完整產物留在 App 的 `artifacts/`；目前有效 layout 為 `package-development-20261021-133216-749/layout`，因開發註冊直接使用它，需保留。大型 runtime 和 MSIX 不複製進此精簡證據目錄。
