# ESP32 S3 USB 燈條與 Windows 動態光效

使用 Waveshare ESP32-S3-DEV-KIT-NXRX N32R16 與 WS2812 燈條，經同一條 USB 線傳輸及供電。韌體提供 USB HID LampArray，基本燈效由 Windows 11「設定 → 個人化 → 動態光效」直接控制。YouTube 音量同步另由 [Windows 音樂同步 App](02-windows-music-sync/README.md) 在背景分析電腦播放音訊並送出燈色。燈具 HID 欄位依據 [USB HID Usage Tables 1.4 第 26 章](https://www.usb.org/sites/default/files/hut1_4.pdf)；Windows 相容性另參照 [Microsoft Dynamic Lighting 裝置規範](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/dynamic-lighting-devices)。

目前依使用者最新決定，採 **8 顆燈條直連**，先不使用 TPS2553 或 74AHCT125。`Config::UsePowerSwitch = false`，燈條由板上 5V 供電、GPIO4 送資料，GPIO9、GPIO10 不需接線。已完成燒錄、Windows 動態光效設定頁辨識與純色控制；使用者確認 **8 顆都能隨設定切換紅、綠、藍**。逐顆索引、DIN 訊號裕量、USB 耗電與睡眠／喚醒仍待量測或驗證。詳見 [直連接線](docs/直連接線.md)、[驗證紀錄](docs/驗證紀錄.md) 與 [交接文件](handoff.md)。

最新直連韌體已編譯成功，24/24 項 PC 協定與電流模型測試通過。2026-10-21 已經 CH343 **COM3** 燒錄，四段資料雜湊驗證成功；ROM 讀回 ESP32-S3 revision v0.2、32MB Octal Flash、16MB PSRAM。Windows LampArray API 與原生設定頁均找到 `ESP32-S3 USB Light Strip`；8 顆紅／綠／藍切換結果來自使用者實物觀察，詳見驗證紀錄。此次燈效測試沒有重刷韌體。

## 使用者需求與原型設定

| 項目 | 設定 |
| --- | --- |
| Windows 控制 | Windows 11 內建動態光效 |
| LED 排列 | 一維燈條，由 DIN 端編號 0 起 |
| 供電 | 同一條 USB 線，不使用外接電源 |
| 開發板照片 | `圖片/61282_0.jpg`、`61283_0.jpg`、`61284_0.jpg` |
| 燈條照片 | `圖片/61285.jpg`，可見 8 顆及入口 +5V / DIN / GND 標示 |
| 原型燈數 | 8 顆，與照片可見顆數相符 |
| 暫定間距 | 16.667 mm，對應每公尺 60 顆，須依實際燈條修改 |
| 資料接腳 | GPIO4 |
| 接線模式 | 直連，`UsePowerSwitch = false` |
| 電源開關接腳 | 目前不使用 GPIO9、GPIO10 |
| LED 支路軟體估算上限 | 50 mA，包含每顆 1 mA 靜態電流假設 |
| 亮度額外上限 | RGB 每色最高對應 24/255，再依電流估算縮放 |

8 顆舊型 WS2812 全白全亮以每顆 60 mA 估算可能需 480 mA，尚未包含開發板。原型採低亮度；Windows 設為 100% 也不會解除韌體上限。估算依實際 WS2812 型號與量測修正。[LED 電源參考](https://learn.adafruit.com/adafruit-neopixel-uberguide/powering-neopixels)

## 硬體與接線

目前使用 [直連接線說明](docs/直連接線.md) 與 [直連接線圖](docs/circuit-direct.svg)：板 5V → 燈條 +5V、共同 GND、GPIO4 → 建議串聯 330Ω → 第一顆 DIN。先使用短資料線與既定低亮度設定。省略電平轉換後，3.3V 能否可靠驅動取決於實際 LED 版本，低亮度限制不能解決邏輯門檻問題。[邏輯電平參考](https://learn.adafruit.com/adafruit-neopixel-uberguide/logic-level)

原先包含 TPS2553 與 74AHCT125 的 [備選電路](docs/電路與供電.md) 及 [圖檔](docs/circuit.svg) 保留供日後需要時參考。只有組裝該電路後才可將 `UsePowerSwitch` 改為 true。

板上 Type-C 經 CH334 USB hub 分別連到 CH343 UART 與 ESP32-S3 原生 USB。CH343 用於燒錄和日誌，原生 USB 用於 HID LampArray，不需再飛線 USB D+/D−。[Waveshare FAQ](https://docs.waveshare.com/ESP32-S3-DEV-KIT-N8R8/FAQ)

GPIO19、20 保留給 USB。依系列原理圖，GPIO38 為板載 RGB；N32R16V 的 GPIO35–37 及 GPIO47、48 有記憶體／電壓限制，本案不使用。照片貼紙為 N32R16；本次實板已讀回 32MB Flash、16MB PSRAM 與 Octal Flash 設定。[Waveshare 系列資料](https://docs.waveshare.com/ESP32-S3-DEV-KIT-N8R8)

## 韌體與建置

第一個 8 顆 WS2812 程式集中在 `01-8_ws2812/`，主程式為 `01-8_ws2812/01-8_ws2812.ino`，以本機既有 Arduino ESP32 core **3.1.1** 為建置基準，不需額外 LED 函式庫。USB HID 使用 Arduino-ESP32 的 TinyUSB 介面，WS2812 以硬體 RMT 傳送 GRB 資料。

韌體目前僅沿用 Microsoft `ArduinoHidForWindows` 的 LampArray **報告描述元**與 MIT 授權；協定解析、ESP32 USB 介面、RMT 與限亮由本專案實作。官方完整函式庫宣告支援 SAMD、使用不同 USB 介面，不能直接安裝後替換此 ESP32-S3 程式；可用性評估見 [交接文件](handoff.md)。

搬移後已重新編譯並再次通過 24/24 測試；未再次燒錄，先前裝置驗證紀錄仍對應當時上傳的映像。

1. 依燈條修改 `01-8_ws2812/Config.h` 的 `LampCount`、`LampPitchUm` 與 `StripWidthUm`。增加燈數前重新核算電源，不能只改數字。
2. 在專案根目錄執行：

   ```powershell
   .\scripts\build.ps1
   ```

3. Arduino IDE 對應設定為：ESP32S3 Dev Module、USB Mode 選 USB-OTG (TinyUSB)、USB CDC On Boot 關閉、CPU 80 MHz、OPI Flash 32 MB、OPI PSRAM、32 MB 分割配置。記憶體選項依系列文件預設，確認實板後才燒錄。
4. 燒錄時選有 `CH343` 名稱的 UART COM 埠；2026-10-21 本次為 COM3。重新連接或換電腦仍須重新辨識，不以其他 COM 埠猜測。原生 COM9 在這次燒錄後轉為 HID，因此不再列為 COM 埠。
5. 建置結果位於 `build/`。`tests/run-tests.ps1` 執行 PC 上的協定與電流估算測試；測試不會寫入 ESP32。

USB 保留 Arduino core 的開發用預設 VID/PID；若將來製作正式產品，須使用合法分配的識別碼。原型以晶片 MAC 產生穩定序號，不在檔案記錄實板 MAC。

## Windows 使用方式

音樂同步App已新增至[02-windows-music-sync](02-windows-music-sync/README.md)。2026-10-21，使用者設定背景控制優先順序後確認：8顆燈會隨YouTube音樂變化，暫停後漸暗。此為0.1.2.0的使用者實物驗收；0.1.3.0另更新[App與Store圖示](02-windows-music-sync/Packaging/Branding/README.md)。

接上已燒錄且接線完成的板子，開啟「設定 → 個人化 → 動態光效」，啟用相容裝置控制，檢查是否出現 `ESP32-S3 USB Light Strip`，再測固定顏色、亮度及 Windows 提供的效果。若其他 RGB 軟體取得控制權，需調整 Windows 中的控制優先順序。[Microsoft 使用說明](https://support.microsoft.com/en-us/windows/hardware/input-devices/control-dynamic-lighting-devices-in-windows)

單純出現 COM 埠或一般 HID 裝置，不代表 Windows 動態光效已識別。應以設定頁出現燈條，並能實際改變 LED 顏色作為功能驗收。效果選項依 Windows 版本與相容應用程式而定；本機設定頁已實際看到「音效」及「純色」。

本機 2026-10-21 再次檢查時，裝置本身也選了「音效」，其詳細設定為效果速度、方向與兩種色彩。使用者確認暫停 YouTube 時，燈條仍持續流動變色；這是自行運作的效果，不能據此認定已隨音樂節奏變化。其後使用者授權實作，[Windows 音樂同步 App](02-windows-music-sync/README.md) 現在提供 WASAPI 播放音訊分析與 LampArray 控制；初版以音量驅動 8 格燈條，不含節拍辨識。設計歷史見 [Windows 音樂同步設計](docs/Windows音樂同步設計.md)，最新實測狀態見 [交接文件](handoff.md)。[Microsoft 內建效果說明](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-devices-lights-winrt-windldefaulteffecttype)、[WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)。

## 韌體行為

- 直連模式在 RMT 初始化後送全黑；未配置 USB、USB 暫停及 Windows 的 AutonomousMode 都維持全黑。燈條仍然接著 5V，這不是切斷電源。
- Windows 關閉 AutonomousMode 並提交完整畫面後：先送全黑，再顯示限制亮度後的畫面。
- 多顆更新及範圍更新先寫暫存畫面，收到 UpdateComplete 才提交，最多約 60 次實體更新／秒。
- USB 暫停時送全黑；恢復後可恢復既有 Windows 畫面。拔除／重新枚舉則清除控制狀態。
- RMT 傳送失敗時停止輸出並嘗試送黑，直連無法硬體斷電；先排除接線／負載問題，再重新連接 USB。若 RMT 本身故障，不能保證全黑命令送達。

備選的 `UsePowerSwitch = true` 才使用 GPIO9 電源門控、30ms 啟動等待及 GPIO10 /FAULT 鎖定；目前直連不具這些硬體功能。

USB 描述元暫設原生 USB 功能 100 mA，這不是整塊板子的耗電宣告或 USB 認證。CH334 hub、CH343、ESP32 與燈條合計電流，包含休眠電流，必須量測及核對 USB hub 描述元；軟體縮放也不是實際電流量測。

## 實板驗收

1. 確認實板型號、Flash／PSRAM、CH343 與原生 USB 拓撲，再燒錄。
2. LED 未接時，先驗證 HID LampArray 可在 Windows 動態光效設定頁出現。
3. 先接 1 顆低亮度測試紅／綠／藍與 GRB 順序，再依實際顆數重新建置。
4. 量測整板靜態、開機、最大白色、熱插拔及 USB 暫停電流，並量測 DIN 波形與燈條端電壓。
5. 測試睡眠／喚醒、USB 重插、關閉動態光效與切換效果，確認直連模式送黑及恢復。/FAULT 只適用日後接上電源開關的備選電路。

沒有實板讀回與量測前，不將編譯或 PC 測試結果視為燈條可用亮度、USB 電氣合規或 Windows 相容性驗證。

## 版本控制與驗證證據

私人 GitHub 儲存庫：[chiangyih/esp32-s3-windows-dynamic-lighting](https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting)，分支 `main`。

原始碼、測試、接線圖、硬體照片與交接文件納入 Git；精簡的建置、燒錄及 Windows LampArray 辨識證據保存在 [docs/verification](docs/verification/README.md)。本機 `build/` 的映像、執行檔與歷史完整封存保留在原處，由 `.gitignore` 排除。
