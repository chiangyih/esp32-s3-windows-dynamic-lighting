# ESP32 S3 Windows 動態光效專案交接

建立日期：2026-10-21，Asia/Taipei。

專案根目錄：`D:\00_Codex_project\esp32-s3-windows動態光效`。

後續工作先讀本檔最新日期章節，再讀 README、當前 Config.h 與驗證資料。新進度以日期追加，保留既有操作與驗證歷史；若需要壓縮，先封存並確認歷史可讀，再整理主交接檔。不得把編譯成功或 PC 測試通過寫成實板驗證成功。

## 2026-10-21 需求與目前決定

使用者已明確選擇：ESP32-S3 控制 WS2812 一維燈條，透過同一條 USB 線傳輸及供電，直接由 Windows 11「設定 → 個人化 → 動態光效」控制。

最新接線決定為 **先不使用 TPS2553，包括 TPS2553DBV，也不使用 74AHCT125**。目前韌體預設 `Config::UsePowerSwitch = false`，改為板 5V、GND 與 GPIO4 直連；GPIO9、GPIO10 不需接線。

目前為功能原型。尚未燒錄 ESP32-S3，尚未讀回實板 USB 描述元、記憶體與 COM 埠，未驗證 Windows 實際辨識、燈效、資料訊號或電流。使用者沒有指定外接電源、自製常駐 Windows 控制程式或無線控制；目前不擴充這些範圍。

## 2026-10-21 照片與硬體依據

| 資料 | 來源與可確認內容 | 仍待確認 |
| --- | --- | --- |
| 開發板正反面 | 使用者專案照片 `圖片/61282_0.jpg`、`61283_0.jpg`、`61284_0.jpg`；可見 Waveshare ESP32-S3-DEV-KIT-NXRX、N32R16 貼紙與 WROOM-2 模組。 | 實際硬體修訂、Flash／PSRAM 容量及可用記憶體設定。 |
| 燈條 | 使用者指明採用資料夾圖檔；`圖片/61285.jpg` 可見 8 顆 LED，入口標有 +5V、DIN、GND，箭頭朝末端。 | WS2812 的具體版本、燈距、寬度與實際耗電。 |
| USB 拓撲 | Waveshare 官方系列文件及 FAQ：USB-C → CH334 hub → CH343 UART 與 S3 原生 USB。 | 實板枚舉、Hub 電源模式及描述元，不能只看 COM 埠就判定 LampArray 已辨識。 |
| 接腳 | 系列原理圖選 GPIO4 作外接資料；GPIO19/20 保留 USB，GPIO38 為板載 RGB。 | 系列圖為依據，尚非實板測量。N32R16V 相關記憶體／電壓限制下，本案避用 GPIO35–37、47、48。 |

官方依據：[Waveshare 系列說明](https://docs.waveshare.com/ESP32-S3-DEV-KIT-N8R8)、[USB FAQ](https://docs.waveshare.com/ESP32-S3-DEV-KIT-N8R8/FAQ)、[系列原理圖](https://files.waveshare.com/upload/e/e1/ESP32-S3-DEV-KIT-N8R8-schematic.pdf)。

## 2026-10-21 目前直連接線

| 開發板 | 燈條入口 |
| --- | --- |
| 5V 排針 | +5V，使用 USB 供電，不能改接 3.3V 排針。 |
| GND 排針 | GND，共地。 |
| GPIO4 | DIN，建議串聯 330Ω，電阻靠近第一顆 DIN。 |

接線前拔除 USB。以燈條印刷接點及箭頭確認方向；不只依線色判斷，也不從 DO／DOUT 端輸入。資料線先用約 10–20cm 作工程測試起點，這不是規格保證。GPIO9、10 不接；GPIO19、20 不作燈條資料線。

照片不能證明 3.3V 資料與這條 5V 燈條必然相容。不同 WS2812 版本的 DIN 門檻不同；低亮度能降低耗電，不能提高資料電壓或解決邏輯門檻。直連先驗證是否不亮、閃爍或顏色錯誤，再判斷訊號、時序、共地與電源。[Adafruit 邏輯電平說明](https://learn.adafruit.com/adafruit-neopixel-uberguide/logic-level)

目前不額外直接掛 100µF／1000µF 大電容到 USB 5V，先沿用成品燈條現有去耦；新增電容前須考慮熱插拔浪湧。直連沒有硬體限流、電源故障回報或支路斷電。初始化、停用與 USB suspend 只嘗試送全黑，仍有靜態耗電；RMT 故障時也不能保證黑幀送達。

詳見 [直連接線說明](docs/直連接線.md)、[PNG 接線圖](docs/circuit-direct.png) 與 [SVG 原稿](docs/circuit-direct.svg)。

## 2026-10-21 程式架構與設定

USB 使用 HID LampArray，目標讓 Windows 原生動態光效接管，不是一般 COM 命令控制。Microsoft descriptor 固定來源為 `ArduinoHidForWindows` commit `deb7e5e20d96bab5571ee2629d634375002f5513`，已保留 MIT 授權。

| 設定 | 目前值 |
| --- | --- |
| UsePowerSwitch | false，直連模式。 |
| LampCount | 8，與照片可見顆數相符。 |
| LampPitchUm / StripWidthUm | 16667 / 10000，即 16.667mm 間距與 10mm 寬；均為待量測的幾何預設。 |
| MinUpdateUs | 16667，動畫更新間隔約 60Hz；啟停黑幀另計。 |
| DataPin / OnboardRgbPin | GPIO4 / GPIO38。 |
| LED 電流估算 | 50mA 支路預算；每顆靜態假設 1mA、每色通道全量假設 20mA。 |
| MaxChannelValue | 24/255，整幀共同縮放，必要時再降低以符合估算預算。 |
| NativeUsbPowerMa | 100，僅 USB 描述元設定，不能當成整板實測或總電流限流值。 |

50mA 是 LED 支路的軟體模型，不含 ESP32-S3、CH334、CH343 等整板耗電；Windows 亮度 100% 不會解除韌體上限。USB-C 接頭不代表已取得 USB PD 大電流，實際預算需核對 Hub 描述元並量測。

韌體的重要行為：

1. 原生 USB Report 1–6 處理陣列屬性、燈具屬性查詢、多顆更新、範圍更新與 AutonomousMode。Feature payload 不含 Report ID，部分短型報告容許最大集合長度且尾端全零的 padding。
2. USB callback 只解析與更新緩衝區，跨 task 狀態由短臨界區保護；RMT 顯示在 loop 執行。
3. 等 Windows 關閉自主模式且收到有效 UpdateComplete 才提供 host frame，多包更新不分段顯示。
4. RMT 以 GPIO4 傳送 GRB，約 800kbit/s，附 300µs latch。Windows 協定與記憶體緩衝區維持 RGB。
5. 直連模式送黑後停止重複傳黑；USB 恢復時可顯示上一個仍有效的 Windows 畫面。斷線重設或回到自主模式會清掉舊畫面。
6. 每圈檢查 hasCommittedHostFrame，處理快速自主模式 true → false 切換，避免 loop 沒看到中間狀態而沿用舊畫面。
7. RMT 傳送失敗會停止正常輸出並嘗試送黑；直連不能硬體斷電，不能記為失敗必定熄燈。

## 2026-10-21 檔案索引

| 位置 | 用途 |
| --- | --- |
| `README.md` | 目前方案、建置與 Windows 使用步驟。 |
| `firmware/WindowsDynamicLighting/Config.h` | 顆數、幾何、GPIO、直連／備選電源開關與亮度估算。 |
| `firmware/WindowsDynamicLighting/WindowsDynamicLighting.ino` | USB 初始化、顯示與停燈、斷線／暫停／模式切換。 |
| `firmware/WindowsDynamicLighting/LampArrayProtocol.h` | 可在 PC 測試的純 C++ LampArray 狀態機。 |
| `firmware/WindowsDynamicLighting/LampArrayHid.h`、`.cpp` | Arduino USB HID wrapper 與 task 同步。 |
| `firmware/WindowsDynamicLighting/FrameLimiter.h`、`Ws2812Rmt.h` | 電流估算縮放與 RMT 資料輸出。 |
| `LampArrayReportDescriptor.h`、`LICENSE-Microsoft.txt` | 位於韌體目錄，Microsoft descriptor 與授權。 |
| `scripts/build.ps1` | 只建置，不燒錄。 |
| `tests/protocol_tests.cpp`、`tests/run-tests.ps1` | PC 原生 C++ 協定與估算測試，不連接硬體。 |
| `docs/驗證紀錄.md` | 日期化驗證結果與未完成事項。 |
| `docs/電路與供電.md`、`docs/circuit.svg`、`docs/circuit.png` | 先前含 TPS2553、74AHCT125 的備選設計，非目前直連接法。 |
| `build/firmware-build.log`、`build/host-tests.log`、`build/verification.json` | 最新建置／測試紀錄及檔案 SHA-256 清單。 |

## 2026-10-21 建置方式

既有環境紀錄為 Windows 11 專業版 10.0.26100、Arduino CLI nightly-20260918 commit 189327e、ESP32 core 3.1.1。建置沿用既有環境，未升級全機套件。

在專案根目錄執行：

```powershell
.\scripts\build.ps1
.\tests\run-tests.ps1
```

建置 FQBN 為：

```text
esp32:esp32:esp32s3:USBMode=default,CDCOnBoot=default,MSCOnBoot=default,DFUOnBoot=default,FlashMode=opi,FlashSize=32M,PSRAM=opi,PartitionScheme=app5M_fat24M_32MB,CPUFreq=80
```

USB Mode 必須是 USB-OTG TinyUSB，USB CDC On Boot 關閉。CH343 UART 作燒錄與 115200 日誌；須重新辨識實際 COM 埠，不能套用其他專案埠號。32MB OPI Flash／OPI PSRAM 來自系列資料，燒錄前仍須核對實板。原生 HID 執行時不期待同時保留共用 PHY 的 USB Serial/JTAG。

產物在 `build/`，其中 `.ino.bin` 是應用程式映像，`.merged.bin` 含整體配置，不能混用燒錄位址。專案未提供自動燒錄腳本，本輪沒有執行 upload。保留 Arduino 開發用預設 VID/PID；尚無正式產品識別碼配置或產品認證結論。

## 2026-10-21 先前含開關版本的驗證歷史

先前使用 TPS2553 與 74AHCT125 的版本，已實際編譯及通過 23/23 PC 測試。當時 Sketch 占用 376060 bytes、全域變數 41920 bytes；`.ino.bin` 實際檔案為 376176 bytes，兩個數字代表不同量測。

切換直連前，已逐一核對原 `build/verification.json`：18/18 檔案存在、Bytes 與 SHA-256 全相符。原清單、原始碼、測試、腳本與清單列出的產物已複製保存在：

`build/archive/20261021-switched-baseline/`

該資料夾的 `verification.json` 路徑以該資料夾為根，保留原始相對路徑結構。此歷史驗證沒有實板結果；不能把它套作目前直連版本的電氣或 Windows 相容性結論。`build/` 排除於 Git，後續清理或同步前須保留必要證據。

## 2026-10-21 待完成與驗收順序

1. 確認開發板實物型號、記憶體、USB 拓撲與 CH343 埠，再進行燒錄。
2. 先不接外部燈條，檢查 Windows 11 動態光效頁是否出現 `ESP32-S3 USB Light Strip`。只有 COM 埠或一般 HID 不能視為通過。
3. 斷電接線後，以低亮度測全黑、紅、綠、藍，確認 DIN 方向、色序與 8 顆可控制；量測並更新實際幾何。
4. 測試 Windows 內建效果、亮度與控制優先順序。原生設定頁成功加上實際顏色可控制，才算基本功能通過。
5. 量測 GPIO4／DIN 訊號、燈條端電壓、USB 整板與 LED 支路電流，包含全白、開機與熱插拔。3.3V 相容性與軟體電流模型各自驗證。
6. 測睡眠／喚醒、USB 重插、停用動態光效、快速切換控制模式與 RMT 失敗後行為。直連的全黑與電源斷電必須分開記錄。
7. 根據量測決定可用亮度與燈數。使用者目前先不加兩顆 IC；如需改回備選電路，先確認實際接線再改 UsePowerSwitch。

目前不宣稱 USB 電氣合規、已校準電流限制、已完成 Windows 相容性或燈效測試。尚未完成的主要工作是實板連線、燒錄、接線與量測。

## 2026-10-21 直連版本最終驗證追加

本次交接期間已完成直連版本的重新建置、PC 測試與檔案核對：

| 檢查 | 結果與證據 |
| --- | --- |
| 直連韌體 | 建置成功，Sketch 376008 bytes；全域變數 41920 bytes，剩餘 285760 bytes。見 `build/firmware-build.log`。 |
| PC 測試 | 24/24 通過，含快速自主模式切換後必須等待新畫面。見 `build/host-tests.log`。 |
| 目前 SHA-256 | 最新直連原始碼、測試、腳本與產物列於 `build/verification.json`。 |
| 歷史封存 | `build/archive/20261021-switched-baseline/verification.json` 列出 18 檔，封存後逐一核對 SHA-256 全部相符。 |
| 接線圖 | `docs/circuit-direct.svg` 已轉 PNG 並檢視，三線接點與限制可讀。 |
| 實板操作 | 未燒錄、未自動連接 COM 埠、未驗證實際 Windows 動態光效辨識、未量測電流。 |

最新方案以直連文件與 `UsePowerSwitch = false` 為準。下一步依前節驗收順序處理實板；保留軟體估算 50mA 與 24/255 上限，不據此宣稱整板實際電流已受限制。

## 2026-10-21 USB 偵測與實板燒錄追加

使用者已授權「偵測 ESP32-S3 的 USB 連線與 COM 埠，確認板型後燒錄直連版本程式」，指定 Arduino CLI 路徑 `C:\Program Files\Arduino CLI`。以下為本次硬體實際讀回，更新先前「尚未燒錄」的歷史狀態。

- **實際埠號**：CH343 為 COM3（VID 1A86 / PID 55D3）；燒錄前 ESP32-S3 原生 USB 為 COM9（VID 303A / PID 1001）。USB 裝置路徑確認兩者在同一板上 Hub 分支。COM4、COM6 是藍牙序列埠，未操作。
- **晶片與記憶體**：從 COM3 使用 esptool 的 flash_id 指令讀回 ESP32-S3 QFN56 revision v0.2、40MHz 晶振、Embedded PSRAM 16MB AP_1v8、32MB Flash、Octal 8 線 eFuse 設定。配置符合照片中的 N32R16；不是僅憑 Arduino 自動板型標籤判斷。
- **燒錄來源**：原有直連映像，`UsePowerSwitch=false`、GPIO4、8 顆、50mA 模型與 24/255 上限。燒錄前清單 18/18 檔案大小與 SHA-256 全部相符；本次未修改韌體或重編譯。
- **執行結果**：使用指定 `arduino-cli.exe upload`、COM3、115200 baud、原 FQBN、`--input-dir build --verify`；退出碼 0。bootloader、partition、boot_app0 與 app 四段均通過寫入雜湊驗證，隨後 RTS 重啟。
- **啟動讀回**：COM3 有 Octal Flash 開機日誌及 `LampArray USB started; waiting for Windows.`，本次取樣未見初始化失敗或重啟循環。
- **Windows 辨識**：產品名 `ESP32-S3 USB Light Strip`，HID 硬體 ID 含 `HID_DEVICE_UP:0059_U:0001`，PnP 問題碼 0。WinRT LampArray 唯讀枚舉找到 1 個同名裝置，IsEnabled=true。沒有開啟 LampArray 控制物件或更改 Windows 燈效。
- **目前埠狀態**：COM3 保留作下載與日誌；COM9 已轉為原生 HID 而不再列為 COM，這是目前配置的預期行為。

燒錄命令與映像：

```powershell
& 'C:\Program Files\Arduino CLI\arduino-cli.exe' upload `
  --fqbn 'esp32:esp32:esp32s3:USBMode=default,CDCOnBoot=default,MSCOnBoot=default,DFUOnBoot=default,FlashMode=opi,FlashSize=32M,PSRAM=opi,PartitionScheme=app5M_fat24M_32MB,CPUFreq=80' `
  --port COM3 --protocol serial --upload-property 'upload.speed=115200' `
  --input-dir 'D:\00_Codex_project\esp32-s3-windows動態光效\build' --verify `
  'D:\00_Codex_project\esp32-s3-windows動態光效\firmware\WindowsDynamicLighting'
```

COM3 是本次偵測值，後續仍須重新確認埠。應用程式位址 0x10000，映像 376112 bytes，SHA-256 `07E982D874ACB52631F5478038E648253A70F6FBA272789390EF45B8C9398157`。

完整證據位於 `build/device-verification/20261021-upload/`，獨立 `result.json` 記錄本次硬體狀態。`scripts/inspect-lamparray.ps1` 是新增的唯讀檢查工具，用 Windows PowerShell 5.1 執行；不開 COM、不取得燈效控制權。原 `build/verification.json` 是燒錄前軟體基準，保留當時 HardwareTested=false，不將歷史檔案改寫成整體硬體驗收通過。

**接續工作**：使用者可到 Windows 11「設定 → 個人化 → 動態光效」檢查裝置卡片並測試實際燈條。OS API 辨識已通過；設定頁尚未視覺檢查，LED 變色／動畫、3.3V DIN 相容性、實際電壓／電流與 suspend 耗電仍未驗證。直連仍只有軟體亮度估算，沒有硬體限流或斷電。

## 2026-10-21 本機 Git 與私人 GitHub 儲存庫

使用者授權建立本機 repo，並 commit／push 至 `github.com/chiangyih`。已確認既有 Git Credential Manager 授權登入為 chiangyih，建立私人儲存庫：

`https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting`

本機在專案根目錄初始化 `main`，`origin` 為上述 HTTPS 儲存庫。沿用既有 Git 作者身分，僅在本 repo 設定 credential.username=chiangyih、credential.interactive=false 與 core.autocrlf=false；沒有在檔案或 Git 遠端 URL 寫入認證資料。

Git 範圍包含韌體、PC 測試、工具腳本、README、交接、硬體照片及接線圖。`build/`、測試執行檔與本機機密／編輯器設定由 `.gitignore` 排除，本機完整歷史封存未刪除。

為使遠端保有可讀證據，已將 13 份精簡紀錄複製至 `docs/verification/2026-10-21/`，並逐檔確認與本機原件 SHA-256 相符：直連版建置／24 項測試／驗證清單 3 份、USB 與燒錄紀錄 7 份，以及原含開關方案的建置／23 項測試／清單 3 份。證據目錄說明為 `docs/verification/README.md`。這些複本沿用歷史清單路徑，不代表大型編譯映像均已上傳。

本輪沒有修改韌體、重編譯或再次燒錄；Git 發布不改變既有實板驗證範圍。

## 2026-10-21 第一個程式集中至 01-8_ws2812

使用者追加要求將第一個程式的各項檔案存放在 `01-8_ws2812`。目前有效的韌體目錄為專案根目錄的 **`01-8_ws2812/`**，主程式為 **`01-8_ws2812/01-8_ws2812.ino`**；本檔前段的 `firmware/WindowsDynamicLighting/` 位置與舊燒錄命令保留作當時的歷史紀錄，不是現行路徑。

共搬移 9 檔：`01-8_ws2812.ino`、`Config.h`、`FrameLimiter.h`、`LampArrayHid.h`、`LampArrayHid.cpp`、`LampArrayProtocol.h`、`LampArrayReportDescriptor.h`、`Ws2812Rmt.h`、`LICENSE-Microsoft.txt`。原 `WindowsDynamicLighting.ino` 僅改檔名，符合 Arduino 主檔與資料夾同名的要求；9/9 檔案大小與 SHA-256 均與搬移前相符，韌體內容未變。

共用建置腳本、PC 測試與診斷工具維持根目錄 `scripts/`、`tests/`。`scripts/build.ps1` 的 sketch 位置、PC 測試的兩個 include 與 README 的現行路徑均已更新。移位後 PC 測試再次 24/24 通過；沒有再次操作硬體或燒錄。

歷史驗證清單及日誌不改路徑、不覆寫。移位後的新建置／測試證據另放 `docs/verification/2026-10-21/01-8_ws2812/`。未來燒錄須使用新 sketch 目錄與對應的 `01-8_ws2812.ino.*` 產物，不能將舊檔名誤當成本次重新燒錄結果。

移位後已重新建置成功：Sketch 376008 bytes、全域變數 41920 bytes，PC 測試 24/24 通過。`build/01-8_ws2812.ino.bin` 為 376112 bytes、SHA-256 `2AFF8C9688BFE43697EDB5BF0CBE7ECA02CD082E694E1B4FC53B2033824C4A32`；本次沒有重刷。裝置仍對應前節實際燒錄的 `07E982D874ACB52631F5478038E648253A70F6FBA272789390EF45B8C9398157` 映像，雖然 9 個來源檔內容相同，也分開保存兩次建置紀錄。

最新 19 項軟體檔案清單為本機 `build/verification.json`；Git 內的新清單與新建置／測試日誌在 `docs/verification/2026-10-21/01-8_ws2812/`。先前各版本證據繼續保留。

## 2026-10-21 首次 commit 與 push 核對

已完成本機 `main` 首次提交及推送，私人儲存庫為 [chiangyih/esp32-s3-windows-dynamic-lighting](https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting)。首次提交為 `f0c1817dfc92a0ea4097631be65a201b8679af0b`，包含 45 檔，第一個程式完整存放於 `01-8_ws2812/`；沒有把舊 `firmware/` 路徑或本機 `build/` 產物納入 Git。

首次 push 後，以 `git rev-parse HEAD`、`git rev-parse origin/main` 與 `git ls-remote origin refs/heads/main` 核對三者 SHA 完全相同，`git status --porcelain` 為空。讀回快照保存於 `docs/verification/2026-10-21/git-initial-push.json`。後續文件提交的最新 HEAD，應重新用同樣方式與遠端比較，不將首次提交 SHA 視為永久最新值。

`.gitattributes` 保留原始 CRLF／LF 位元組，使來源與證據的 SHA-256 不因 Git 換行轉換而改變；圖片以二進位處理。`.gitignore` 排除本機編譯產物與機密設定。沒有 force push、全機 Git 設定變更或任何認證資料寫入儲存庫。
