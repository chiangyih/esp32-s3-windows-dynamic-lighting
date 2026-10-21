# 音樂動態燈光設置

分析電腦播放音訊，控制本專案 ESP32-S3／WS2812 燈條，可自訂 **1～64 顆、預設 8 顆**。精簡 WinForms 視窗採蒂芬妮藍搭配深藍底，可縮至通知區，音訊及燈光均使用 Windows 系統 API；沒有 WebView、Windows App SDK、NAudio、FFT 或雲端服務。

目前送審版本為 **0.3.1.0**，新增內建離線「隱私政策」第三分頁，發行者 Tseng，文字與套件共用同一份嵌入資源，不開啟外部連結。正式產品身分已與 Partner Center 後台讀回核對，套件為 `Validated` 並已成功提交；目前預先處理中，通過認證後將立即發布，尚未認證通過或公開發布。

保留0.3.0.0加入的燈數設定，僅在停止同步並完成清理後可修改，正常關閉後會保存。App 設定必須與韌體回報的 `LampCount` 相同；不符時顯示兩個數值並停止送色。現有實體裝置仍為 8 顆，本輪沒有重刷韌體。設定、韌體與 USB 供電限制見 [自訂燈數與 USB 供電](../docs/自訂燈數與USB供電.md)。64 顆目前只有編譯、純程式及預覽測試，尚未完成 64 顆實體燈條或 USB 供電驗證。

0.2.3.0新增「燈光設置」與「關於與版本」分頁；後者顯示版本及相容硬體需求。0.2.0.0已加入新介面、關閉按鈕與可選音樂燈效，使用者確認「**三種效果都有反應，暫停後漸暗**」。早期未取得控制權的零送色報告屬調整前歷史。0.2.0.0的115項整合檢查、四種退出路徑與六項UI選擇診斷曾通過，但退出檢查未涵蓋取得燈光控制權後的原生回呼互鎖；後續實機發現停止／關閉卡住。0.2.1.0已修正LampArray原生呼叫的鎖定範圍，並以非同步方式停止／釋放；重新通過115項檢查、四種正常退出，以及取得控制權、實際送出211幀時的停止／重啟／關閉，正常退出後工作與清理錯誤均無殘留。完整證據見 [handoff.md](../handoff.md)。

0.1.2.0的YouTube播放／暫停使用者目視驗收，以及0.1.3.0接入[Windows／Store圖示](Packaging/Branding/README.md)的歷史均保留。

## 環境與硬體

- C# **10.0** 語言、.NET **10**；SDK 基準 10.0.402。
- x64 Windows 11，套件最低 23H2（22631），目前以 24H2（26100.9711）測試。程式另外檢查實際 LampArray 控制權 API，缺少時要求更新 Windows。
- 只控制唯一符合的 `ESP32-S3 USB Light Strip`，VID 303A、PID 1001；裝置回報燈數須與 App 設定的 1～64 顆相同。
- 沿用 GPIO4、5V、GND，不新增 COM 通訊或解除韌體低亮度限制。已燒錄的現有裝置仍為 8 顆；修改 App 數值不會改寫韌體或自動增加燈數。

C# 與 .NET 是不同的版本設定。`LangVersion=10.0` 固定語法；執行階段使用 .NET 10。自包含 MSIX 包含執行階段，使用者不需另裝 .NET，下載／安裝大小與執行時記憶體占用應分別衡量。

## 使用

1. 安裝／註冊封裝版本後，從 Windows 開始功能表開啟「音樂動態燈光設置」。
2. 按「Windows 動態光效」，開啟裝置控制，在「背景光源控制項」將本 App 排到前面。不必將內建效果改為「音效」。
3. 停止同步並等待清理完成後，設定 1～64 顆燈數，預設為 8 顆。設定須與韌體 `LampCount` 相同；裝置狀態會顯示回報值，不符時不送色。更換實體燈數須另外調整、建置及燒錄韌體，先核對 USB 供電限制。
4. 播放 YouTube，按「開始同步」。預覽以**整條無段式光條**顯示目前設定燈數的色彩分布與亮度；現有實體仍為 8 顆。音樂太弱則提高靈敏度，反應長時間全滿則降低靈敏度。
5. 暫停／沒有新音訊時漸暗。預覽有反應不等於燈條已有控制權，請同時看燈條狀態。
6. 選擇下表的音訊依據、配色、變化方式與速度。一般操作的燈數與燈效選擇保存在目前使用者的本機設定JSON，正常關閉後下次還原；診斷覆寫不存回日常設定。不設定開機自動啟動。
7. 「縮至通知區」會繼續背景同步。「關閉程式」、視窗右上角X／Alt+F4與通知區「關閉程式」共用退出流程，取消背景工作、停止音訊與送色、釋放LampArray、通知區圖示及視窗資源後結束程序。

「關於與版本」分頁可查看版本號與硬體需求；「隱私政策」分頁提供唯讀、可捲動與複製的離線文字，包含音訊處理、本機偏好與選用診斷說明。未連接相容硬體時，預覽可能變化，實體燈光不會受控制。底部關閉按鈕在三個分頁都可使用；離開燈光設置頁時不更新隱藏預覽。

| 選項 | 行為 |
| --- | --- |
| 依據音量 | 以播放混音的能量改變配色位置及亮度。 |
| 依據聲頻 | 以低／中／高頻能量分布改變配色位置；這是粗頻帶分析，不是FFT頻譜、精確音高或樂器辨識。 |
| 依據高低音 | 以低音和高音的能量比例改變配色位置；音量仍控制整體亮暗，靜音時漸暗。 |
| Windows佈景主題 | 以目前Windows輔色產生同色系深淺配色，並追蹤輔色更新。 |
| 彩虹／自訂顏色 | 彩虹連續漸層，或選擇2～4種顏色並在色彩間插值。 |
| 呼吸／流動／單顆依序 | 整條同色呼吸、色彩沿燈條流動，或依目前設定燈數逐顆循環。 |
| 音樂節奏速度 | 依音訊估測BPM改變速度；資訊不足時顯示估測中，以每秒0.5循環的慢速運行。估測可能出現半拍／倍拍差異，並非精準節拍同步或曲目BPM資料庫。 |
| 曲風快慢 | 手動選慢／中／快的反應速度，不自動辨識曲風。 |
| 自訂速度 | 每秒0.1～4循環；呼吸／流動以完整動畫為一循環，單顆依序以走完目前設定的全部燈為一循環。單顆依序依燈數及實際更新間隔限制速度，確保按順序走過每顆；約30fps時，8顆上限約每秒3.75循環，64顆約每秒0.47循環，實際上限略低，慢幀時會進一步降低。 |

輸入是**預設多媒體播放裝置的混音**，包含其他 App。若 YouTube 指定至非預設輸出，請改用預設輸出；更換預設播放裝置時會重新擷取。程式不開啟麥克風、不建立錄音檔、不建立網路連線；其他 App 已播放出的通話聲仍可能屬於系統混音。

按「停止」會停止分析與送色，程式視窗仍保留；Windows可能恢復原先內建效果。「關閉程式」則完全退出。

## 節省資源

WASAPI採事件驅動，在原生buffer分析各聲道能量，避免左右反相抵銷；頻帶和節奏分析使用可重用的濾波／統計狀態，不建立錄音。燈效依目前設定燈數重用色彩緩衝，約30 fps，靜音後降至8 Hz；重複畫面不送USB，遵守裝置最小更新間隔。原生送色陣列僅在停止後變更燈數時重建，不逐畫面配置。預覽每250 ms更新，隱藏時不重繪。新選項不增加執行時套件；新版CPU／記憶體占用須以新版實測，不沿用早期版本數字。

## 建置與測試

0.3.1.0 已重新建置正式身分候選並本機註冊，本輪148項純程式檢查通過。以套件AUMID擷取主頁、關於與版本、隱私政策三張1366×900真實畫面，目視檢查及清理回報通過，三個測試PID均消失；沒有回讀到退出碼，不宣稱退出碼0。這是介面／生命週期驗證，沒有新增實體燈條音樂驗收。

0.3.0.0 的 148 項純程式檢查及 6 項預覽檢查已通過；韌體預設 8 顆／LED 支路估算上限 50 mA，以及 64 顆／80 mA 設定均完成 Arduino 全量建置，0 warnings。這些結果不代表已驗證 64 顆實體燈條、USB 耗電或亮度；現有 8 顆裝置未重刷。

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

確認沒有其他「ESP32音樂同步」程序執行後，可測試一般關閉按鈕、啟動中關閉、通知區關閉與視窗關閉：

```powershell
& .\02-windows-music-sync\scripts\test-shutdown.ps1
```

腳本只啟動本App的測試程序，檢查退出報告及原PID確實消失；不強制終止既有程序、不播放測試音、不調整系統音量或燈光優先順序。測試輸出寫入新的`artifacts/ui-shutdown-<時間>/`；清理檢查通過不代替實體燈色、長時間效能或Store驗證。

`scripts/play-test-signal.ps1` 在記憶體產生並播放 6 秒、三段振幅的 440 Hz 測試音，不變更系統音量；可協助檢查播放／停止反應，不等於 YouTube 或實體 LED 驗收。

## Microsoft Store

MSIX manifest、圖示、封裝腳本、內建政策與提交狀態見 [封裝文件](Packaging/README.md)及[上架資料索引](Packaging/StoreSubmission/README.md)。正式產品身分已在 Partner Center 核對相符；`LocalDev.Esp32MusicSync`仍僅為開發測試身分。

2026-10-21，產品 `9NKRN1KCR738` 的 Submission 1（`1152921505702190045`）已提交。使用者手動上傳的 0.3.1.0 X64 套件在後台顯示 `Validated`，裝置系列限定 Windows.Desktop，最低版本 10.0.22631.0。繁中商品頁、三張截圖與圖示、[公開隱私政策](https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting/blob/main/02-windows-music-sync/Packaging/privacy-policy.zh-TW.md)、支援網址及認證備註均已保存。App 仍可離線閱讀同一政策。

後台概觀顯示認證中；Publishing Status 的細部進度為步驟 2/4、提交完成及預先處理進行中，認證和發布尚未啟動。已設定通過認證後立即發布；目前尚未認證通過或公開發布。證據見[提交狀態](../docs/verification/2026-10-21/store-submission/0.3.1/submission-status.json)及[後台畫面](../docs/verification/2026-10-21/store-submission/0.3.1/submission-in-certification.jpg)。先前工具與自動上傳錯誤保留於交接歷史；WACK、乾淨機安裝及 64 顆實體 USB 測試仍未完成。

最新實測狀態見 [handoff.md](../handoff.md)；前一輪設計文件中的「尚未實作」是當時歷史。

| 目錄 | 用途 |
| --- | --- |
| `Audio/` | WASAPI COM、端點變更、PCM／浮點解碼 |
| `Core/` | 音量平滑、靜音偵測、配色／動畫／速度設定及同步工作 |
| `Lighting/` | LampArray、控制權、USB 重插及送色 |
| `UI/` | WinForms、預覽、通知區與可選診斷 |
| `Packaging/` | Manifest、圖示與上架準備 |
| `tests/` | 音訊解碼、特徵分析與燈效檢查，不依賴額外測試框架 |

參考：[WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)、[Windows Dynamic Lighting](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/lighting-dynamic-lamparray)。
