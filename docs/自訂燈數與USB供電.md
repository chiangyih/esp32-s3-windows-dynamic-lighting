# 自訂燈數與 USB 供電

Windows App 可設定 **1～64 顆**；這是軟體支援範圍，沒有把目前8顆實板驗證擴大宣稱為64顆USB供電驗證。

App設定燈數必須與 ESP32 韌體透過 LampArray 回報的燈數一致。App 不會用燈數設定改寫韌體；例如目前實板回報8顆，在App改成16顆後會提示不一致，須先準備對應燈條、重新建置／燒錄16顆韌體並驗證供電。本次仍保留原有8顆實板韌體，沒有重新燒錄。

裝置回報的是韌體設定值，並非自動偵測WS2812實際顆數。韌體必須依實體燈數設定，不能用較小宣告值規避電流模型；未納入輸出範圍的燈也仍然接著5V，可能保留先前狀態及靜態耗電。

## 建置對應韌體

`01-8_ws2812/Config.h` 保留預設8顆、LED支路50mA軟體估算預算、每顆1mA靜態電流假設、RGB每通道24/255上限，以及原有USB宣告100mA。透過建置參數覆寫，不必修改預設原始碼：

```powershell
# 目前8顆原型：保留50mA估算預算。
& .\scripts\build.ps1 -Cli 'C:\Program Files\Arduino CLI\arduino-cli.exe' `
    -LampCount 8 -OutputDirectory '.\build\count-8'

# 16顆的建置範例：仍用原有50mA預算，平均可用亮度會受限制。
& .\scripts\build.ps1 -Cli 'C:\Program Files\Arduino CLI\arduino-cli.exe' `
    -LampCount 16 -OutputDirectory '.\build\count-16'
```

參數會傳入 `CONFIG_LAMP_COUNT` 與 `CONFIG_LED_BUDGET_MA`。`-OutputDirectory` 可保留不同配置的產物，`firmware-build-report.json` 記錄實際建置參數、雜湊及未燒錄／未量測狀態。未指定輸出目錄時延續原先`build/`行為。

## 50mA預算與較大燈數

目前模型預留每顆1mA靜態電流，並要求LED支路預算**嚴格大於**靜態總和。因此保持50mA預算時，50～64顆配置會被編譯檢查拒絕。這不是App燈數限制，也不能藉由降低靜態電流假設來規避。

64顆在此模型下至少需要65mA預算，才有非零動態餘額。以下只示範**建置／軟體檢查**，不代表實際USB供電已足夠：

```powershell
# 64顆、80mA只作明確參數的編譯範例；勿直接視為實板可用配置。
& .\scripts\build.ps1 -Cli 'C:\Program Files\Arduino CLI\arduino-cli.exe' `
    -LampCount 64 -LedBudgetMa 80 -OutputDirectory '.\build\count-64-budget-80'
```

`-LedBudgetMa` 必須明確指定才改變原有50mA預算，允許1～100mA，並保留靜態預算與USB宣告一致性的編譯檢查。數值是軟體模型，並不是硬體限流或實際電流量測；宣告100mA也不是整塊開發板、USB hub、CH343、ESP32與燈條的總耗電認證。

增加燈數前，須核對實際WS2812版本、USB來源／線材與整板耗電，量測運作及暫停電流，確認USB配置是否允許該負載。GPIO4、5V、GND直連仍沒有電源開關或硬體限流器；全黑也不能切斷燈條的靜態耗電。先前省略74AHCT125的資料電平裕量也不會因軟體限亮而改善。

本輪的1～64顆程式測試／編譯結果、目前8顆實物狀態與未完成的電氣驗證，分別記錄在 [handoff.md](../handoff.md)。
