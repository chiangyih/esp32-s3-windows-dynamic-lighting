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

## 2026-10-21 Windows 原生純色與 8 顆變色驗證

本輪約 12:18–12:27（Asia/Taipei）。使用者確認已接好 GPIO4、5V、GND，授權測試 Windows 原生動態光效。未修改韌體、重編譯、燒錄或開啟 COM；實板仍對應先前已上傳的 SHA-256 `07E982D874ACB52631F5478038E648253A70F6FBA272789390EF45B8C9398157` 映像。

| 檢查 | 結果與證據來源 |
| --- | --- |
| 本機 Windows | 實際讀回 Windows 11 Pro 24H2，build 26100.9711。 |
| Windows 原生設定頁 | 已目視確認「個人化 → 動態光效 → ESP32-S3 USB Light Strip」；裝置控制與前景相容應用程式控制均開啟。 |
| 裝置屬性 | WinRT 實際讀回唯一符合的 VID 303A / PID 1001、LampCount=8、IsConnected=true、IsEnabled=true；最小更新間隔 16.667ms。 |
| 原生純色點亮 | 將裝置效果由「音效」改為「純色」後，使用者回報「8 顆都有亮起」。 |
| 原生紅／綠／藍切換 | 以設定頁色票依序選紅色、草皮綠、藍色，每次均目視確認色票勾選更新；使用者回報「8 顆都跟著變成紅、綠、藍」。基本純色控制通過。 |
| 最終狀態 | 設定頁停在純色／藍色；搭配 Windows 輔色為關閉，亮度滑桿維持本輪觀察值，未調整。韌體既有低亮度限制保留。 |
| 逐顆索引 0–7 | 尚未完成。WinRT 測試工具等候前景及 IsAvailable=true 的條件未滿足，於送色前停止，Steps 為空；沒有執行逐顆序列或送出最後黑幀。不能從這個失敗推定硬體不支援逐顆控制。 |

實際 LED 顏色結果來自使用者實物目視回報；不是攝影、色度計或示波器量測。這次已確認 GPIO4 直連在目前接線下可完成基本變色；不代表已量測 DIN 裕量、整板／LED 電流、USB 暫停耗電或長期可靠性。動畫效果、睡眠／喚醒、USB 重插及逐顆實體索引仍待驗證。

新增 `scripts/test-lamparray.ps1`：預設 InspectOnly 開啟 WinRT 物件讀取屬性，不送色；明確加 `-RunSequence` 才會等待測試視窗取得前景及 Windows 控制權，再嘗試低亮度序列。首次初始化的 .NET Color 型別問題已修正，最後版本語法檢查與 InspectOnly 實測成功。RunSequence 本輪未通過控制權條件；後續加入 ControlWait 診斷欄位，尚未重跑送色序列。工具以 JSON 的 Status／Error 判讀結果，不能只看程序退出碼。

完整本機證據位於 `build/device-verification/20261021-windows-control-20261021-041855/`，精簡複本與本輪結論位於 `docs/verification/2026-10-21/windows-control/`。保留初始化失敗、修正後讀回、未送色序列與使用者確認的不同證據，不覆寫先前的驗證清單。沒有保存含 Windows 帳號的設定頁截圖。本輪文件與工具僅在本機更新，尚未新增 commit／push。

## 2026-10-21 「音效」與 YouTube 音樂的差異

使用者提供 Windows 11 動態光效截圖，顯示全域效果選為「音效」，並回報燈條似乎沒有隨播放中的音樂變化。其後在本機設定頁實際讀回 `ESP32-S3 USB Light Strip` 的**個別**效果也已選「音效」，因此先前測試留下的個別「純色／藍色」已不是目前阻擋全域效果的原因。沒有更改效果、亮度、韌體或裝置接線。

展開個別「音效」選項後，實際看到效果速度滑桿、搜尋方向（目前為右）、主色與第二種色彩。使用者另外確認：暫停 YouTube 時，8 顆仍持續自行流動或變色。這支持目前看到的是按設定持續運作的燈光動畫，**不構成 YouTube 音訊同步的證據**；使用者原本也回報播放音樂時沒有觀察到隨節奏變化。Microsoft [內建效果表](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-devices-lights-winrt-windldefaulteffecttype)記載以顏色、速度及方向運作的 Wave 等視覺效果，未列音訊分析效果；本輪未取得足夠在地化資源證明本機「音效」字串確切對應哪個英文名稱，不據名稱推定音訊功能。

目前 `01-8_ws2812/` 韌體是 USB HID LampArray 收色並驅動 WS2812，專案內沒有 Windows 播放音訊擷取與節奏分析程式。要實現 YouTube 音樂同步，需要新增 Windows 端音訊擷取／分析與 LampArray 控制，並再做實板驗證；Microsoft [WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)可擷取電腦播放的音訊。本輪只完成設定與使用者目視的診斷，沒有宣稱已實作音樂同步。使用者截圖含 Windows 帳號資訊，未複製進專案或 Git。

## 2026-10-21 USB HID Usage Tables 1.4 參考核對

使用者提供 USB-IF 官方 [HID Usage Tables 1.4](https://www.usb.org/sites/default/files/hut1_4.pdf)。本輪唯讀核對第 26 章（PDF 第 329–345 頁附近，印刷頁 328–344）與目前 `01-8_ws2812/` 原始碼，未修改或重新燒錄裝置。

目前描述元宣告 Lighting and Illumination Usage Page `0x59`、LampArray Usage `0x01`。6 個 Feature Report 的 Usage 依次為 LampArray Attributes `0x02`、Lamp Attributes Request `0x20`、Response `0x22`、Multi Update `0x50`、Range Update `0x60`、Control `0x70`；RGBI `0x51`–`0x54` 與 AutonomousMode `0x71` 亦對應第 26 章。協定程式以 RGB 乘 Intensity/255 更新燈色，處理 8 槽 Multi、含首尾的 Range、UpdateComplete 批次套用，以及預設開啟自主模式。這是來源碼對照，不是 USB 認證或每個報告的實板交叉測試。

查出一項能力宣告不一致：`LampArrayProtocol.h` 的每燈屬性將紅、綠、藍與整體亮度 `*LevelCount` 都回報為 255；但目前 `MaxChannelValue=24`，`FrameLimiter.h` 會將每個輸出通道縮至最高 24，並可能因 50 mA 模型進一步降低。同一顆燈的實際輸出因此沒有 255 個非零亮度階數，不符合第 26.9 節對 `*LevelCount` 作為支援階數的描述。這是靜態程式檢查發現，非實測 PWM 精度。須在保留 USB 供電保護前提下，連同主機可送的有效 RGBI 範圍（第 26.11 節）評估宣告、映射及驗證；不能只把屬性值從 255 改成 24。此項不會解釋燈條未隨 YouTube 音樂改變，且本輪未修改或重新燒錄韌體。

燈距、邊界尺寸與 UpdateLatency 仍使用原型估值，需實物與時序量測。第 26 章定義主機與 LampArray 之間的燈色／控制報告，沒有定義 YouTube 或系統音訊分析；音樂同步須另由 Windows 端程式完成。本輪在 README、交接與驗證紀錄中加入官方 PDF 連結及核對結果，未複製或重新發布 PDF 內容。

## 2026-10-21 WASAPI loopback 與背景音樂同步設計

使用者提供 [Microsoft WASAPI loopback 文件](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)，並明確選擇「**先評估並更新設計文件**」。本輪新增 [Windows 音樂同步設計](docs/Windows音樂同步設計.md)，只規劃 Windows 端應用，不新增程式、不安裝 MSIX、不修改系統設定或韌體，也沒有音樂同步實板測試。

官方文件確認 loopback 能以共用模式擷取選定 Windows 播放端點的混音，可作 YouTube 播放音訊分析，但不會自動控制 HID 燈具，且混音並非 YouTube 專屬。建議第一版以 RMS 音量驅動 8 格燈條，暫停或無音訊時漸暗；以既有 LampArray 介面送出 8 顆色彩畫面，並保留韌體亮度上限。若要在瀏覽器位於前景時持續控制，Microsoft [Dynamic lighting 文件](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/lighting-dynamic-lamparray)要求背景 ambient 應用具 package identity，並在 manifest 宣告 `com.microsoft.windows.lighting` AppExtension；使用者須於背景光源控制項安排優先順序。現有前景診斷腳本不能代替此封裝應用。

設計文件分列音訊、控制權、實物與電氣四階段驗證。音訊擷取、MSIX 封裝／安裝、背景 `IsAvailable` 與播放／暫停實際燈效仍全數待實作／驗證；不把既有「音效」自行動畫或原生純色成功當作音樂同步通過。

## 2026-10-21 Microsoft ArduinoHidForWindows 整套函式庫評估

使用者詢問是否可採用 [Microsoft ArduinoHidForWindows](https://github.com/microsoft/ArduinoHidForWindows) 全套函式庫。本輪對照其 `deb7e5e20d96bab5571ee2629d634375002f5513` 來源、現行 `01-8_ws2812/` 與本機 Arduino-ESP32 core 3.1.1；未修改或重新燒錄韌體。上游 `library.properties` 是 v1.3.0、`architectures=samd`，README 記載 Arduino Zero／SAMD core 1.8.13 的驗證；MIT 授權允許在保留授權文字的條件下修改及移植。

本專案目前只複製 Microsoft 的 `LampArrayReportDescriptor.h`（移除 `PROGMEM`）及授權文字，固定來源為上述 commit。`LampArrayProtocol.h` 的 RGBI／批次報告處理、`LampArrayHid.{h,cpp}` 的 Arduino-ESP32 TinyUSB／`USBHIDDevice` 介面、FreeRTOS 同步、`Ws2812Rmt.h` 與 `FrameLimiter.h` 都是本專案實作。上游 `Microsoft_HidDevice` 依賴 SAMD 的 `USBCON`、`PluggableUSBModule`、`USBDevice.sendControl/recvControl`；目前 ESP32 core 提供另一套 `USBHIDDevice` 回呼，故**完整函式庫不是 ESP32-S3 即裝即用方案**。若要移植，需要改寫 USB 傳輸及 Report ID／Feature Report 銜接，保留現有睡眠／自主模式、RMT、USB 供電估算與低亮度行為，之後重建並重新驗證 Windows 枚舉與實體 8 燈。

上游 `Microsoft_HidLampArray.cpp` 在 `UpdateComplete` 時將讀指標直接設成寫指標，靜態程式檢查可見後續寫入會與已提交畫面共用緩衝，且解構時兩指標可能重複釋放；Report handler 也未先依傳入長度檢查再複製固定尺寸。這些是來源碼風險推論，不是此板實測故障，移植不能原樣照搬。上游 40 顆 NeoPixel 範例使用 Arduino Zero 與 74AHCT125，其輸出範例僅取 RGB，未處理 Intensity；也沒有目前 8 顆直連方案的 50 mA 模型。上游完整函式庫還包含與本燈條無關的 Consumer/System/Telephony/Wireless Radio HID 類別；**它沒有 Windows WASAPI 音訊擷取，因此不能單靠替換韌體實現 YouTube 音樂同步**。現行已通過原生紅／綠／藍實物目視的韌體先維持不變，音樂同步依前節 Windows 端設計處理。

來源：[上游套件設定](https://github.com/microsoft/ArduinoHidForWindows/blob/deb7e5e20d96bab5571ee2629d634375002f5513/library.properties)、[USB 基底類別](https://github.com/microsoft/ArduinoHidForWindows/blob/deb7e5e20d96bab5571ee2629d634375002f5513/src/Microsoft_HidDevice.cpp)、[LampArray 實作](https://github.com/microsoft/ArduinoHidForWindows/blob/deb7e5e20d96bab5571ee2629d634375002f5513/src/Microsoft_HidLampArray.cpp)、[上游燈條範例](https://github.com/microsoft/ArduinoHidForWindows/blob/deb7e5e20d96bab5571ee2629d634375002f5513/examples/LampArray/LampArray.ino)。

## 2026-10-21 Windows 音樂同步 App 實作（C# 10／.NET 10）

使用者已明確授權開始實作，解除前節「先評估」限制。新增獨立目錄 `02-windows-music-sync/`，不與 `01-8_ws2812/` 混放。本輪沒有修改、重編譯或燒錄 ESP32 韌體，沒有開啟 COM；原接線 GPIO4／5V／GND 與既有軟體限亮維持。

### 程式與資源設計

目前版本 **0.1.2.0**，x64、C# LangVersion=10.0、.NET 10，SDK 10.0.402。WinForms 介面，不採 WebView、Windows App SDK、NAudio 或外部音訊／FFT 函式庫。Windows SDK 參考為 26100，套件最低 22631，並以 ApiInformation 檢查 LampArray 控制權 API；缺少時明確拒絕送色。

音訊使用自寫 WASAPI COM shared／event-driven loopback，追蹤預設多媒體播放端點。支援浮點與 PCM8／16／24／32、WAVEFORMATEXTENSIBLE；直接在原生 buffer 計算各聲道 RMS／Peak，避免左右反相抵銷，不保存或上傳原始音訊。預設輸出變更／失效可重新開啟。第一版為**音量同步**，沒有宣稱節拍偵測或八頻帶頻譜。

8 格由綠向黃、紅延伸，提供靈敏度與亮度調整；快速上升、較慢下降，資料逾 200ms 視為失效並漸暗。約 30fps，靜音後降至 8Hz，重複畫面不送色。只有唯一符合名稱／VID 303A／PID 1001／LampCount=8 才選取；遵守 MinUpdateInterval、IsAvailable、AvailabilityChanged 與 DeviceWatcher，不使用 raw HID 或修改系統控制權。介面預覽每 250ms 更新，隱藏不重繪；可縮至通知區，關閉即退出。

### 本輪驗證與確切界線

- Release 建置成功，0 warnings／0 errors；音訊與映射檢查共 **43 項**（26 audio＋17 core）通過。暖機後一萬次畫面映射測得 managed allocation=0 bytes。五份 PowerShell 腳本語法檢查皆零錯誤。
- 0.1.1.0 已封裝啟動，讀回 package identity=true，找到唯一 8 燈裝置。6 秒、三段振幅的自製 440Hz 測試音只在記憶體產生，不修改系統音量。WASAPI 量測 RMS 約 0.197／0.545／0.696；停止播放後 RMS 歸零，預覽 Level 於第 21 秒歸零。音訊數值是實際 Windows 混音，與產生訊號振幅不同，未量測音效增強／音量處理鏈。
- 該 60 秒診斷的穩定區間為 55.06 秒，CPU 約 **1.65% 單一邏輯核心**（本機四個邏輯核心，全機約 0.41%）；全期間含啟動的 CPU 約全機 1.03%。峰值工作集約 88.4 MB、私用約 26.6 MB。另一次 42.55 秒常駐取樣為工作集約 77.8 MB、私用約 17.4 MB。這些均為目前未取得燈光控制權的情境，不能稱為完整同步負載。早期 resource-initial-steady-invalid-time.json 時區換算錯誤，僅在本機保留，已排除效能證據。
- **尚未取得控制權**：0.1.1.0 診斷 EverHadLightingControl=false、SentFrames=0，沒有以 App 送出燈色，不能稱為實體音樂同步通過。已請使用者在 Windows 動態光效「背景光源控制項」把「ESP32 音樂同步」排至最前，再播放／暫停 YouTube；回報尚待取得。未改登錄值、停用其他控制者或繞過 Windows 仲裁。
- 0.1.2.0 僅追加靜音時預覽重繪修正與版本一致化，已重新封裝並開發註冊；最新啟動結果見 `docs/verification/2026-10-21/music-sync/0.1.2/`。前一個 0.1.1.0 的測試結果另保留在父目錄，不覆寫成新版本實測。

### 封裝與 Store 狀態

目前 unsigned 開發 MSIX：`02-windows-music-sync/artifacts/package-development-20261021-133216-749/Esp32MusicSync_0.1.2.0_x64_development.msix`，**57,610,921 bytes**，SHA-256 `25B31343EC53727CD1E8EFDC7A830FDA55E525DF9FB8ABA4A6FC19682BB70808`。self-contained layout 為 149,914,094 bytes。MakeAppx 正常驗證通過，沒有使用 /nv；MSIX 版本與 EXE 檔案版本一致。

manifest 宣告 `com.microsoft.windows.lighting` AppExtension 與桌面 `runFullTrust`。程式不開啟麥克風、不建立網路連線；runFullTrust 本身不代表被網路沙箱限制。Store 建置模式要求實際 Partner Center 的 Identity／Publisher／DisplayName／PublisherDisplayName；預設 `LocalDev.Esp32MusicSync` 是開發佔位身分，不能冒用為正式 Store 身分。

本機原已有 Developer Mode=1，本次使用 Add-AppxPackage -Register 標準開發註冊，未改模式、憑證信任、執行原則或管理員權限，沒有建立或匯出私鑰。已註冊 `LocalDev.Esp32MusicSync_0.1.2.0_x64__3p158v40tjaa6`，依賴上述 layout 目錄，暫勿將其視為可刪除中間產物。這不是從已簽署 MSIX 安裝的驗證，更不是 Store 安裝驗證。

已提供包裝圖示、建置／開發安裝／帶參數啟動腳本、隱私政策草稿與 Store 待辦。WACK 工具存在，但未執行；目前使用者權杖非管理員。**Partner Center 產品保留、正式身分套件、WACK、乾淨機／Store 簽署安裝、認證與發布均未完成**，本輪未建立產品、上傳或送審。功能與操作見 `02-windows-music-sync/README.md`，上架事項見 `02-windows-music-sync/Packaging/README.md`。

### 接續與版本控制

先完成 Windows 背景控制權，讀回 IsAvailable=true 且實際有 SentFrames，再請使用者確認 8 顆隨 YouTube 播放／暫停反應。其後測試 USB 重插、音訊輸出切換、App 停止／退出與競爭控制者。API 成功不取代實體 LED 觀察，亦不代表電流／訊號電氣合規。若 YouTube 指定到非預設播放端點，需改用預設端點；混音包含其他應用程式，不是 YouTube 專屬。

新程式、文件與精簡驗證證據目前僅在本機，未新增 commit／push；保留原先尚未提交的文件與測試腳本變更。本輪新增 .gitignore 排除 Windows bin／obj／artifacts，但已註冊的 layout 仍需保留以供目前本機開發 App 執行。

## 2026-10-21 Windows App 與累積文件 commit／push

使用者授權「請自動commit/push」。已在 main 提交並正常推送至私人 `chiangyih/esp32-s3-windows-dynamic-lighting`，實作提交為 `60e756e9abd3a2699beccd581f1f79d47ac0290c`，共60檔，包含 C# App、MSIX腳本／資產、設計、交接、原生Windows控制證據及test-lamparray。bin／obj／artifacts與機密副檔名未納入；保留目前已註冊App使用的本機layout。

推送後已核對 local HEAD、origin/main與git ls-remote的main完全相同，工作目錄乾淨；證據在 `docs/verification/2026-10-21/git-music-sync-push.json`。本節與該讀回紀錄另以文件提交同步，故未來最新HEAD請重新查Git，不把實作提交SHA視為永久最新值。未force push、未修改遠端歷史、未保存認證資料。此Git同步不改變實體燈條尚待控制權／YouTube觀察，以及WACK／Store送審未完成的狀態。

## 2026-10-21 使用者確認 YouTube 音樂同步與暫停漸暗

使用者在背景光源控制優先順序／播放暫停問題中回覆：「**已設定，燈會隨音樂變化，暫停後漸暗**」。問題對象為目前8顆WS2812與本App，因此基本YouTube播放／暫停的實體音量燈效驗收，更新為**使用者目視確認通過**。本機讀回安裝版本仍為0.1.2.0；本次沒有為確認而重刷ESP32、播放測試音、重新啟動或升級App。

此結果來源為使用者實物觀察，不是攝影或儀器量測；設定變更後本App的IsAvailable／SentFrames未另行讀回。前面0.1.1／0.1.2短時診斷的IsAvailable=false、SentFrames=0是調整控制權前的歷史，保留不改寫，也不再當成目前實體同步仍失敗的證據。使用者確認紀錄見 `docs/verification/2026-10-21/music-sync/user-confirmation.json`。

USB重插、預設音訊輸出切換、競爭控制者、長時間資源占用、電氣量測、WACK與Store認證仍須分別驗證。本次確認沒有把這些項目一併列為通過。

## 2026-10-21 Windows App／Microsoft Store 圖示設計

使用者要求設計Windows App與上架Store用圖示。完成原創SVG「Pulse Notes」：白色雙音符、青至紫的音符橫樑、下方8格RGB LED、深藍圓角底；另有深／淺背景透明標誌。使用可編輯向量幾何，不含第三方圖庫、品牌商標或字型輪廓，不依賴AI點陣服務。

原稿／預覽／素材雜湊在 `02-windows-music-sync/Packaging/Branding/`；Windows `Packaging/Assets/AppIcon.ico`含16／20／24／32／40／48／64／128／256共9個32-bit alpha尺寸。Assets共有66份PNG加1份ICO，涵蓋4種基礎資產、5種scale與14種targetsize的三種背景版本。Store商品頁交付 `Packaging/StoreListing/StoreListingIcon-300.png`（官方建議的300×300）與1024×1024設計原稿；商品頁圖示與manifest的StoreLogo用途分開。

設計階段 `scripts/render-brand-assets.cjs`以sharp直接從SVG各尺寸渲染。一般建置由generate-assets.ps1驗證並複製已簽入圖檔，不需要Node／sharp，不增加App執行時相依套件。70項交付素材逐檔SHA均核對相符，Windows原生LoadImage實測9種ICO尺寸均可載入。System.Drawing.Icon建構子對多尺寸ICO的256px曾選到128px，已用原生LoadImage確認256px實際存在並正確載入，未將該選圖限制誤認成ICO缺少尺寸。

圖示版為 **0.1.3.0**，已重新編譯／封裝，音訊與燈效程式碼未改。MakePri需移除createconfig預設的autoResourcePackage拆包設定，將所有scale／targetsize索引至單一resources.pri；初次產出的多PRI迭代僅留本機，最終已修正。dump確認Scale400、TargetSize16、unplated與lightunplated全部在單一索引；MakeAppx正常驗證通過。新圖示也已嵌入EXE並抽出目視確認。

目前有效新圖示套件：`02-windows-music-sync/artifacts/package-development-20261021-134721-590/Esp32MusicSync_0.1.3.0_x64_development.msix`，58,164,335 bytes，SHA-256 `2111B6F25BEFB8112D6F4B1A3AAB773D7947F5985359F90F369461381F104DAF`；自包含layout 150,475,474 bytes，單一resources.pri SHA-256 `8F39541C4E86417AC1338E9FDE5B11B07D74B6040ECF7D3BB6AB91FC77F2B58F`。精簡證據見 `docs/verification/2026-10-21/icons/`。

本輪**沒有安裝0.1.3.0或為圖示重新啟動App**，已安裝版本仍為0.1.2.0，保留使用者已確認正常的音樂同步環境。0.1.3.0尚未做安裝後圖示快取／深淺主題Shell實測；WACK、正式Store身分與上傳認證狀態不變。本輪同時追加使用者確認YouTube音樂同步通過的證據，舊0幀報告仍保留歷史。
