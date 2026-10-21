# 64 顆 WS2812 USB 實測

## 目前狀態：尚未執行 64 顆實測

2026-10-21 使用者確認目前實體仍為 **8 顆**，供電為 USB；尚未提供電壓／電流量測設備及讀值。因此目前不能將 64 顆實體或耗電列為通過，原有 8 顆韌體與設定保留。

本輪唯讀檢查已確認：

- CH343 位於 COM3，VID/PID 為 1A86:55D3；沒有開啟 COM 或燒錄。
- Windows 找到一個啟用中的 ESP32-S3 USB Light Strip，LampArray **韌體回報 8 顆**。這個數字不會自動偵測實體顆數。
- USB 路徑經板上 1A86:8091 Hub、上游 05E3:0610 Hub 連至 ROOT_HUB30；尚不能由此判定供電額定值、Hub 是否外接電源或實際電流。
- 以預期 8 顆唯讀檢查成功；以預期 64 顆檢查時，在數量驗證階段停止，兩次均未送出燈色。
- 既有 64 顆／80 mA 建置的 Config 來源及產物雜湊相符，仍未燒錄。該預算是軟體模型，並非量測值。

證據見 [USB64 前置檢查](verification/2026-10-21/usb64-readiness/readiness-status.json)。

## 實測所需條件

1. 實際 64 顆、串接方向正確的 WS2812 燈條，以及 GPIO4、5V、GND 接線。
2. 可量測電壓與電流的設備。USB 電表需支援資料透傳，才能同時使用 HID；只有螢幕顯示也可由使用者讀回數值。記錄型號、量程與解析度。
3. 確認電腦 USB 埠或 Hub 的供電規格、線材與接法。USB 供電本身不會讓 ESP32 自動取得實際電流數據。

本輪準備的測試範圍是 **64 顆／LED 支路模型 80 mA 的受限低亮度運作**。模型先扣每顆 1 mA 靜態電流，僅剩 16 mA 動態餘額；全條受限白色的輸出各通道約 1/255、模型約 79.06 mA。這些是依 FrameLimiter 算出的估計，不能代替整板量測，也不是 64 顆全亮測試。詳細設定見 [自訂燈數與 USB 供電](自訂燈數與USB供電.md)。

## 執行順序

1. 先斷 USB，將燈條與開發板分開。板子單獨確認型號、記憶體及 CH343 埠，再燒錄對應 64 顆韌體、量測板子基線。不要讓未供電的燈條仍接著資料輸出。
2. 斷 USB 後接好 64 顆與電表，再通電。舊 8 顆韌體只會更新其宣告範圍，不適合用來控制或估算其餘 56 顆。
3. 唯讀確認 LampArray 回報 64 顆，再依下表逐項測試。App 與測試工具輪流使用，維持 Windows 控制權檢查；不修改系統優先順序來繞過仲裁。
4. 每階段同時記錄 API 結果、使用者實物觀察及電表讀值。USB 入口總電流包含板子／Hub／UART／LED；扣除板子基線只能推估 LED 支路，不能稱為支路直接量測。

| 階段 | 觀察與量測 | 目前結果 |
| --- | --- | --- |
| 板子單獨 | USB 來源、入口電壓、基線電流、啟動穩定度 | 尚未量測 |
| 64 顆全黑 | 全部熄滅、總電流、燈條入口與末端電壓 | 尚未執行 |
| 首尾與逐顆 0～63 | 第 64 顆可獨立控制、每顆位置、RGB／GRB、漏顆或錯色 | 尚未執行 |
| 全紅／綠／藍 | 每個階段的電壓／電流、閃爍、重啟與斷線 | 尚未執行 |
| 受限白色 | 已限定預算下的電壓／電流、穩定度及溫升 | 尚未執行 |
| 音樂／暫停／停止 | 動態反應、漸暗、控制權與正常停止，記錄實際持續時間 | 尚未執行 |
| USB 重插 | 人工重插後重新枚舉與恢復，記錄次數及結果 | 尚未執行 |

量測記錄需包含來源／Hub、線材、電表型號、韌體 SHA、預算、燈數、階段持續時間、USB 電壓與總電流。未量到的欄位保留「尚未量測」，不可填 0 當作實測。

## 測試工具

以下從儲存庫根目錄，以 Windows PowerShell 5.1 執行。第一個命令只讀取屬性，不送色；預期數量不符時會停止：

```powershell
powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64
```

**僅在實體、供電與量測準備完成後**，才使用下列明確送色命令；工具視窗需保持前景並取得 Windows 控制權：

```powershell
# 全黑保持10秒，讀取靜態耗電。
powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64 `
    -RunSequence -TestPattern Black -ColorHoldMilliseconds 10000

# 首尾與逐顆定位，確認包含索引63。
powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64 `
    -RunSequence -TestPattern FirstLast
powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64 `
    -RunSequence -TestPattern Sequential -LampHoldMilliseconds 300

# RGB各保持10秒，再逐顆；白色另測，仍受已燒錄韌體限制。
powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64 `
    -RunSequence -TestPattern RgbAndSequential -ColorHoldMilliseconds 10000
powershell.exe -NoProfile -STA -File .\scripts\test-lamparray.ps1 -ExpectedLampCount 64 `
    -RunSequence -TestPattern White -ColorHoldMilliseconds 10000
```

工具會回報步驟與清理結果，沒有電流感測功能。`ActualLampCount` 是韌體回報值；`PhysicalLampCountVerified`、`UsbCurrentMeasured` 與 `UsbVoltageMeasured` 不會因 API 成功而自動標為 true。測試有總時間上限，關閉視窗會停止後續寫色；最後送黑仍須當時有控制權。

## 停止與驗收界線

若電流超過已確認的來源額定能力、電壓超出實際元件規格，或出現異常發熱、氣味、重啟、USB 斷線、錯色及無法熄滅，立即停止並實際拔除 USB。直連版本沒有硬體斷電功能，軟體停止不等於切斷 5V。

後續若恢復 8 顆，先在仍有控制權時送 64 顆全黑，斷 USB 後換回 8 顆燈條，再使用 8 顆／50 mA 韌體與 App 設定。不能留下 64 顆接著 5V，卻只把韌體宣告改為 8 顆。

只有實際接上 64 顆、包含索引 63 的逐顆觀察通過，且相應供電配置的電壓／電流與運作穩定度有記錄後，才能記為該配置通過。慢速 USB 電表不能證明浪湧、DIN 波形或 USB suspend 電氣合規；這些項目需另行量測，不與基本功能／耗電測試混寫。
