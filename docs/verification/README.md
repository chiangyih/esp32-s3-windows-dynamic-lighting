# 版本控制內的驗證證據

這個目錄保存 2026-10-21 的建置、PC 測試、燒錄與 Windows LampArray 辨識紀錄。檔案從本機 `build/` 複製後逐一比對 SHA-256，原件保留在原位置。

| 目錄 | 證據 |
| --- | --- |
| `2026-10-21/firmware-build.log`、`host-tests.log` | 目前直連版本的建置與 24/24 PC 邏輯測試結果。 |
| `2026-10-21/software-verification.json` | 燒錄前 18 個原始碼、腳本、測試與產物的大小及 SHA-256。 |
| `2026-10-21/device-upload/` | 晶片讀回、四段燒錄雜湊驗證、啟動日誌、USB／COM 與 Windows LampArray 枚舉結果。 |
| `2026-10-21/switched-baseline/` | 原先含 TPS2553、74AHCT125 方案的建置、23/23 測試與原驗證清單。 |
| `2026-10-21/git-initial-push.json` | 首次推送後的本機 HEAD、origin/main、遠端 main 一致及乾淨工作目錄快照；屬該次提交的歷史讀回。 |

第一個程式的現行位置為專案根目錄的 `01-8_ws2812/`，主程式為 `01-8_ws2812/01-8_ws2812.ino`。歷史 manifest 中的原始 `firmware/WindowsDynamicLighting/` 路徑保留不改，以維持當時建置證據；移位後的最新建置與測試紀錄另存於 `2026-10-21/01-8_ws2812/`，不覆寫原有清單或日誌。

JSON 清單中的路徑沿用當時專案根目錄與 `build/` 相對路徑，用來辨識歷史建置；不表示清單中的編譯產物均已上傳 GitHub。`.bin`、`.elf`、`.map`、測試執行檔及工具鏈快取仍保存在本機 `build/`，由 `.gitignore` 排除。重新建置後的產物須用新的紀錄核對，不能覆寫歷史證據後沿用原結論。

裝置紀錄已省略 MAC、USB instance 與序號。本次已確認燒錄及 OS LampArray 辨識；實際 LED 變色、亮度、DIN 訊號與耗電仍待實板觀察及量測。
