# MSIX 與 Microsoft Store 準備

本程式使用 C# **10.0 語言版本**與受支援的 **.NET 10** 執行階段。C# 10 不代表必須使用已停止支援的 .NET 6。桌面介面為 WinForms，不使用 Windows App SDK、WebView 或瀏覽器框架。MSIX 將 .NET runtime 一起封裝，使用者不需另裝 .NET；這增加磁碟大小，但避免依賴未宣告的外部執行階段。

目前來源修正版 **0.2.1.0** 保留0.2.0.0加入的蒂芬妮藍／深藍介面、連續光條預覽、明確關閉按鈕及音樂燈效選項，維持C#10／.NET10，不新增音訊或繪圖執行時套件。可選音量、粗低／中／高頻能量分布與高低音比例；Windows輔色、彩虹或2～4種自訂色；呼吸、流動、單顆依序；節奏估測、手動曲風快慢或每秒0.1～4循環的自訂速度。節奏不足以估測時使用每秒0.5循環；曲風快慢不是自動分類。商品頁應依這些實際功能描述，勿宣稱精確音高／FFT頻譜、精準BPM或自動辨識曲風。

單顆依序會依實際更新間隔限制速度，確保8顆均依序呈現；約30fps時有效速度略低於每秒3.75循環，慢幀時更低。因此每秒4循環是可選目標，不保證單顆依序能達到精確4循環。

使用者選擇只存目前使用者的本機設定JSON，正常關閉後還原；診斷覆寫不保存。關閉按鈕、視窗X與通知區退出共用清理流程，與持續同步的「縮至通知區」及保留視窗的「停止」分開。完整操作見 [App說明](../README.md)。

0.2.0.0開發註冊後，使用者已確認三種效果有反應、暫停後漸暗；早期未取得控制權的零送色報告屬調整前歷史。0.2.0.0曾通過115項整合檢查、四種退出路徑及六項UI選擇診斷，但退出檢查未涵蓋取得控制權後的原生回呼互鎖，實機後來發現停止／關閉卡住。0.2.1.0針對此問題調整原生呼叫鎖定與非同步停止／釋放，已重新封裝及開發註冊，並通過115項檢查、四種退出及持續送色時的停止／重啟／關閉；詳見 [handoff.md](../../handoff.md)；不能把舊版檢查列為修正版已驗證。0.1.2.0的使用者實物觀察另保留。正式Store身分、WACK、乾淨機安裝與Store認證／發布仍待完成。

`AppxManifest.template.xml` 宣告 `com.microsoft.windows.lighting` AppExtension，讓 Windows 動態光效能將程式列為背景控制者；只宣告桌面程式需要的 `runFullTrust`，不要求網路或麥克風能力。WASAPI loopback 讀的是播放裝置混音，不是麥克風。最低安裝版本為 Windows 11 23H2（10.0.22631.0）；現場實機為 24H2。背景控制是否成功仍須在已安裝套件和實際硬體上驗證，不能只由 manifest 判定。

本版程式沒有網路傳輸或麥克風輸入擷取程式碼；這項聲明來自實作檢查，並非以 full-trust App 未宣告 `internetClient` 當作網路隔離保證。若其他 App 已將麥克風聲音播放到輸出裝置，它仍可能包含在 loopback 混音中。Store 資料使用宣告應依發行版實際行為填寫。

## 建置開發套件

在此專案根目錄執行：

```powershell
& .\02-windows-music-sync\scripts\build-package.ps1
```

輸出在 `02-windows-music-sync/artifacts/package-development-<時間>/`，保留獨立的 `layout`、未簽署 `.msix` 和 `package-report.json`。需要 Windows SDK 的 **MakePri 與 MakeAppx**。腳本先索引有 scale／targetsize 限定詞的圖示，產生 `resources.pri`，再呼叫 MakeAppx 的正常驗證，不使用 `/nv`。詳細輸出分別保存在 `makepri.log`、`makeappx.log`，設定檔與日誌不放進套件。流程不開啟或安裝 App，不操作音訊／燈條。

圖示的可編輯 SVG 原稿位於 `Packaging/Branding/`，設計時離線輸出 PNG 與多尺寸 ICO，再簽入 `Packaging/Assets/`。一般重建套件**只讀取現成圖檔**，不需 Node.js、Sharp 或額外影像函式庫，也不將這些設計工具加入 App。`generate-assets.ps1` 現在負責驗證基底尺寸、PNG／ICO 檔頭、scale／targetsize 尺寸與複製後雜湊；拒絕將輸出寫回原始 Assets 目錄。

`AppIcon.ico` 供 Win32 EXE／視窗與通知區使用；`StoreLogo`、`Square44x44Logo`、`Square150x150Logo`、`Wide310x150Logo` 與其限定尺寸供 MSIX／Windows 選用。Partner Center 商品頁圖示另行提供，與 manifest 的 `StoreLogo` 檔案用途不同。修改任何限定圖示後需重新建置 PRI，不能只替換 PNG 後沿用舊索引。

此腳本製作單一 MSIX，會移除 MakePri `createconfig` 預設的 `resources/packaging` 自動拆包設定，將所有 scale／targetsize 變體收進同一個根目錄 `resources.pri`；若產生多份 PRI，便停止封裝，不刪除或忽略它們。`package-report.json` 會記錄 PRI 數量、雜湊與停用資源拆包的設定。MakePri 日誌另正規化為 UTF-8，便於直接讀取。

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
2. 在已安裝的封裝版本測試：瀏覽器前景時音樂同步、暫停漸暗、各種依據／配色／動畫／速度組合、Windows輔色變更、設定還原、USB重插、音訊輸出裝置切換、控制權競爭、關閉App釋放裝置，以及沒有硬體時的訊息。`scripts/test-shutdown.ps1`可檢查四種退出路徑，執行前須沒有其他本App程序；腳本不強制終止現有程序。軟體退出檢查與實體燈條觀察分別記錄。
3. 以實際版本執行 Windows App Certification Kit（WACK），另做無開發環境／無預裝 .NET 的乾淨機安裝測試。MakeAppx 通過不等於 WACK 通過。
4. 量測閒置／播放時 CPU、記憶體與封裝大小，不以「原生 WinForms」當作低資源消耗已驗證。
5. 準備真實 App 截圖、功能／硬體限制說明、客服聯絡、商店語系、公開隱私政策網址；`privacy-policy.zh-TW.md` 是待填聯絡與發布網址的草稿。
6. 為 `runFullTrust` 提供具體說明：桌面WASAPI loopback擷取播放混音，在記憶體分析音量、粗頻帶能量與節奏估測，透過Windows LampArray控制外接燈條；不讀取麥克風、不存音訊、不傳輸音訊。本機設定JSON只保存使用者的燈效選擇，不保存音訊內容。
7. 完成 Partner Center 分級、發行市場與價格等真實宣告，再由使用者確認最終送出認證。套件 `Validated`、提交、認證與公開發布是不同狀態。

本輪腳本不建立 Partner Center 產品、不上傳、不提交認證，也不宣稱已通過 Microsoft Store。

## 官方參考

- [Dynamic lighting：背景控制與套件身分](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/lighting-dynamic-lamparray)
- [Microsoft LampArray manifest 範例](https://github.com/microsoft/Windows-universal-samples/blob/main/Samples/LampArray/cs/Package.appxmanifest)
- [以 MakeAppx 建立套件](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- [以 MakePri 建立套件資源索引](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion#generate-a-package-resource-index-pri-file-using-makepri)
- [簽署應用程式套件](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)
- [開發簽署憑證與 LocalMachine TrustedPeople 要求](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)
- [WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
