# MSIX 與 Microsoft Store 準備

本程式使用 C# **10.0 語言版本**與受支援的 **.NET 10** 執行階段。C# 10 不代表必須使用已停止支援的 .NET 6。桌面介面為 WinForms，不使用 Windows App SDK、WebView 或瀏覽器框架。MSIX 將 .NET runtime 一起封裝，使用者不需另裝 .NET；這增加磁碟大小，但避免依賴未宣告的外部執行階段。

`AppxManifest.template.xml` 宣告 `com.microsoft.windows.lighting` AppExtension，讓 Windows 動態光效能將程式列為背景控制者；只宣告桌面程式需要的 `runFullTrust`，不要求網路或麥克風能力。WASAPI loopback 讀的是播放裝置混音，不是麥克風。最低安裝版本為 Windows 11 23H2（10.0.22631.0）；現場實機為 24H2。背景控制是否成功仍須在已安裝套件和實際硬體上驗證，不能只由 manifest 判定。

本版程式沒有網路傳輸或麥克風輸入擷取程式碼；這項聲明來自實作檢查，並非以 full-trust App 未宣告 `internetClient` 當作網路隔離保證。若其他 App 已將麥克風聲音播放到輸出裝置，它仍可能包含在 loopback 混音中。Store 資料使用宣告應依發行版實際行為填寫。

## 建置開發套件

在此專案根目錄執行：

```powershell
& .\02-windows-music-sync\scripts\build-package.ps1
```

輸出在 `02-windows-music-sync/artifacts/package-development-<時間>/`，保留獨立的 `layout`、未簽署 `.msix` 和 `package-report.json`。腳本呼叫 Windows SDK MakeAppx 的正常驗證，不使用 `/nv`。不開啟或安裝 App，不操作音訊／燈條。

開發 identity `LocalDev.Esp32MusicSync`、publisher `CN=ESP32 Music Sync Development` 都是**本機測試佔位值**，不代表已在 Partner Center 保留產品或有權上架。

## 明確執行本機開發安裝

```powershell
& .\02-windows-music-sync\scripts\install-dev.ps1 `
    -PackagePath '<完整的 development.msix 路徑>' -Launch
```

這個命令會在明確呼叫時產生或重用 CurrentUser 個人憑證存放區的開發程式碼簽署憑證，複製並簽署開發 MSIX。私鑰設定為不可匯出，不寫 PFX 或密碼；`.cer` 只有公開憑證。Windows 要求自簽 MSIX 的憑證已受 **LocalMachine TrustedPeople** 信任；只有在該信任已存在時，命令才以 `Add-AppxPackage` 安裝至目前使用者。命令不改任何憑證信任、開發人員模式、執行原則或其他 Windows 安全設定。若只需產生簽署的測試套件與公開憑證，可加 `-SignOnly`，不會安裝。

若開發憑證尚未受機器信任，腳本會停止並保留簽署產物與錯誤；應由有權限的管理者評估測試憑證信任，或在**已啟用開發人員模式**的環境使用標準開發套件註冊：

```powershell
& .\02-windows-music-sync\scripts\install-dev.ps1 `
    -LayoutPath '<完整的 package-development-時間\layout 路徑>' -Launch
```

`-LayoutPath` 先檢查既有開發模式，再呼叫 `Add-AppxPackage -Register`；不自行啟用模式或跳過 Windows 檢查。這兩種方式都不是 Store 安裝；Store 將以自己的簽署流程交付通過認證的產品。

安裝後，打開 Windows「設定 → 個人化 → 動態光效 → 背景光源控制項」，將本 App 放在適當優先順序。若有其他相容前景 App 接管，應依 Windows 設定調整；程式不會繞過系統控制權。Windows 設定頁的內建效果不需要選「音效」。

## 帶參數啟動已安裝套件

```powershell
& .\02-windows-music-sync\scripts\launch-packaged.ps1 -Arguments '--autostart'

& .\02-windows-music-sync\scripts\launch-packaged.ps1 `
    -Arguments '--diagnose 20 --report "D:\test-output\music-sync.json"' -PassThru
```

腳本用 `IApplicationActivationManager` 與已安裝套件的 AUMID 啟動，保留 package identity，回傳啟動 PID。診斷報告的實際欄位由 App 定義；音量數值、成功送色與使用者看到實體燈條反應應分別記錄。診斷可能驅動目前接上的 8 顆燈，請在已授權的硬體測試時執行。

## 建置 Store 候選套件

先在 Partner Center 保留正式產品，將產品身分頁的值逐字帶入：

```powershell
& .\02-windows-music-sync\scripts\build-package.ps1 -PackageKind Store `
    -IdentityName '<Package/Identity/Name>' `
    -Publisher '<Package/Identity/Publisher，完整 CN=...>' `
    -DisplayName '<正式保留的產品名稱>' `
    -PublisherDisplayName '<正式發行者名稱>' -Version '1.0.0.0'
```

Store 模式拒絕預設開發身份，並要求四項身分參數全部明確提供。輸出為未經開發憑證簽署的 x64、自包含 MSIX，可作 Partner Center 套件候選；Store 產品身分、架構需求與最新提交規則仍須實際核對。無需為 Store 候選套件匯出或保存本機開發私鑰。

## 上架前待辦

1. 以正式身份重新建置；核對套件顯示名稱、版本、Publisher 和保留產品一致。
2. 在已安裝的封裝版本測試：瀏覽器前景時音樂同步、暫停漸暗、USB 重插、音訊輸出裝置切換、控制權競爭、關閉 App 釋放裝置，以及沒有硬體時的訊息。
3. 以實際版本執行 Windows App Certification Kit（WACK），另做無開發環境／無預裝 .NET 的乾淨機安裝測試。MakeAppx 通過不等於 WACK 通過。
4. 量測閒置／播放時 CPU、記憶體與封裝大小，不以「原生 WinForms」當作低資源消耗已驗證。
5. 準備真實 App 截圖、功能／硬體限制說明、客服聯絡、商店語系、公開隱私政策網址；`privacy-policy.zh-TW.md` 是待填聯絡與發布網址的草稿。
6. 為 `runFullTrust` 提供具體說明：桌面 WASAPI loopback 擷取播放混音、在記憶體分析音量，透過 Windows LampArray 控制外接燈條；不讀取麥克風、不存音訊、不傳輸音訊。
7. 完成 Partner Center 分級、發行市場與價格等真實宣告，再由使用者確認最終送出認證。套件 `Validated`、提交、認證與公開發布是不同狀態。

本輪腳本不建立 Partner Center 產品、不上傳、不提交認證，也不宣稱已通過 Microsoft Store。

## 官方參考

- [Dynamic lighting：背景控制與套件身分](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/lighting-dynamic-lamparray)
- [Microsoft LampArray manifest 範例](https://github.com/microsoft/Windows-universal-samples/blob/main/Samples/LampArray/cs/Package.appxmanifest)
- [以 MakeAppx 建立套件](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- [簽署應用程式套件](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)
- [開發簽署憑證與 LocalMachine TrustedPeople 要求](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)
- [WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
