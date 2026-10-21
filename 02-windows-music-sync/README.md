# ESP32 音樂同步

以電腦播放音量控制既有 ESP32-S3／8 顆 WS2812。精簡 WinForms 視窗可縮至通知區，音訊及燈光均使用 Windows 系統 API；沒有 WebView、Windows App SDK、NAudio、FFT 或雲端服務。

## 環境與硬體

- C# **10.0** 語言、.NET **10**；SDK 基準 10.0.402。
- x64 Windows 11，套件最低 23H2（22631），目前以 24H2（26100.9711）測試。程式另外檢查實際 LampArray 控制權 API，缺少時要求更新 Windows。
- 只控制唯一符合的 `ESP32-S3 USB Light Strip`，VID 303A、PID 1001、8 顆。
- 沿用 GPIO4、5V、GND 與已燒錄韌體，不新增 COM 通訊或解除韌體低亮度限制。

C# 與 .NET 是不同的版本設定。`LangVersion=10.0` 固定語法；執行階段使用 .NET 10。自包含 MSIX 包含執行階段，使用者不需另裝 .NET，下載／安裝大小與執行時記憶體占用應分別衡量。

## 使用

1. 安裝／註冊封裝版本後，從 Windows 開始功能表開啟「ESP32 音樂同步」。
2. 按「Windows 動態光效」，開啟裝置控制，在「背景光源控制項」將本 App 排到前面。不必將內建效果改為「音效」。
3. 播放 YouTube，按「開始同步」。音量條由綠向黃、紅延伸；末格亮度平滑變化。音樂太弱則提高靈敏度，長時間全滿則降低靈敏度。
4. 暫停／沒有新音訊時漸暗。預覽有反應不等於燈條已有控制權，請同時看燈條狀態。
5. 「縮至通知區」會繼續背景同步；關閉視窗或通知區「結束」即離開。不設定開機自動啟動。

輸入是**預設多媒體播放裝置的混音**，包含其他 App。若 YouTube 指定至非預設輸出，請改用預設輸出；更換預設播放裝置時會重新擷取。程式不開啟麥克風、不建立錄音檔、不建立網路連線；其他 App 已播放出的通話聲仍可能屬於系統混音。

按「停止」會停止分析與送色；Windows 可能恢復原先內建效果。這個版本提供音量同步，不包含節拍辨識或八頻帶頻譜。

## 節省資源

WASAPI 採事件驅動，直接在原生 buffer 計算各聲道能量，避免左右反相抵銷，不複製／保存音訊。燈效約 30 fps，靜音後降至 8 Hz；重複畫面不送 USB，遵守裝置最小更新間隔。預覽每 250 ms 更新，隱藏時不重繪。

## 建置與測試

在儲存庫根目錄執行：

```powershell
dotnet build .\02-windows-music-sync\MusicSync.csproj -c Release -p:Platform=x64
dotnet run --project .\02-windows-music-sync\tests\TestRunner.csproj -c Release
& .\02-windows-music-sync\scripts\build-package.ps1
```

未封裝 EXE 適合開發診斷，背景同步需有套件身分。已具開發人員模式的本機可註冊：

```powershell
& .\02-windows-music-sync\scripts\install-dev.ps1 -LayoutPath '<產生的 layout 完整路徑>'
& .\02-windows-music-sync\scripts\launch-packaged.ps1 -Arguments '--autostart'
```

開發註冊會直接使用該 `layout` 目錄，請保留正在使用的目錄；一般使用者日後以 Store 安裝時由 Windows 管理程式檔案。

短時診斷會自動開始、可能控制燈條，時間到後儲存統計並退出：

```powershell
& .\02-windows-music-sync\scripts\launch-packaged.ps1 `
    -Arguments '--diagnose 30 --report "D:\test-output\music-sync.json" --screenshot "D:\test-output\app.png"'
```

報告分列套件身分、最大 RMS、控制權、送色幀數、靜音黑幀與資源統計。`AudioPackets` 包含合成零音量狀態，不能單獨證明擷取成功；`SteadyCpuPercentOfOneCore` 排除啟動前 5 秒。截圖只繪製 App 視窗，不擷取桌面。診斷不保存原始音訊。

`scripts/play-test-signal.ps1` 在記憶體產生並播放 6 秒、三段振幅的 440 Hz 測試音，不變更系統音量；可協助檢查播放／停止反應，不等於 YouTube 或實體 LED 驗收。

## Microsoft Store

MSIX manifest、圖示、封裝腳本、隱私政策草稿與提交待辦見 [封裝文件](Packaging/README.md)。正式 Identity、Publisher、產品名稱須由 Partner Center 提供，`LocalDev.Esp32MusicSync` 僅供本機測試。MakeAppx、開發註冊、WACK、Store 套件驗證、認證與發布分開記錄。

最新實測狀態見 [handoff.md](../handoff.md)；前一輪設計文件中的「尚未實作」是當時歷史。

| 目錄 | 用途 |
| --- | --- |
| `Audio/` | WASAPI COM、端點變更、PCM／浮點解碼 |
| `Core/` | RMS 平滑、靜音偵測、8 格映射及同步工作 |
| `Lighting/` | LampArray、控制權、USB 重插及送色 |
| `UI/` | WinForms、預覽、通知區與可選診斷 |
| `Packaging/` | Manifest、圖示與上架準備 |
| `tests/` | 音訊解碼及映射檢查，不依賴額外測試框架 |

參考：[WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)、[Windows Dynamic Lighting](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/lighting-dynamic-lamparray)。
