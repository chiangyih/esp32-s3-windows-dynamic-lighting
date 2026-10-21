# 版本控制內的驗證證據

這個目錄保存 2026-10-21 的建置、PC 測試、燒錄與 Windows LampArray 辨識紀錄。檔案從本機 `build/` 複製後逐一比對 SHA-256，原件保留在原位置。

| 目錄 | 證據 |
| --- | --- |
| `2026-10-21/firmware-build.log`、`host-tests.log` | 目前直連版本的建置與 24/24 PC 邏輯測試結果。 |
| `2026-10-21/software-verification.json` | 燒錄前 18 個原始碼、腳本、測試與產物的大小及 SHA-256。 |
| `2026-10-21/device-upload/` | 晶片讀回、四段燒錄雜湊驗證、啟動日誌、USB／COM 與 Windows LampArray 枚舉結果。 |
| `2026-10-21/switched-baseline/` | 原先含 TPS2553、74AHCT125 方案的建置、23/23 測試與原驗證清單。 |
| `2026-10-21/git-initial-push.json` | 首次推送後的本機 HEAD、origin/main、遠端 main 一致及乾淨工作目錄快照；屬該次提交的歷史讀回。 |
| `2026-10-21/windows-control/` | Windows 原生設定頁辨識、8 顆紅／綠／藍變色的使用者確認，以及 WinRT 工具的初始化、屬性讀回與未取得控制權的序列紀錄。 |
| `2026-10-21/music-sync/` | C# 10／.NET 10 App 的43項檢查、音訊與資源診斷、0.1.2.0開發註冊；後續使用者已確認YouTube播放會變化、暫停漸暗，見user-confirmation.json。 |
| `2026-10-21/icons/` | 0.1.3.0 Windows／Store圖示的70項素材核對、9種ICO原生載入、單一PRI與MakeAppx驗證。尚未安裝此圖示版。 |
| `2026-10-21/store-github-policy/0.3.1/` | 前一輪政策公開存取：使用者改為 public，政策／支援頁匿名 HTTP 200、文字及候選雜湊相符；當時工具仍初始化失敗。此歷史狀態已由下列提交證據接續。 |
| `2026-10-21/store-submission/0.3.1/` | 正式身分／套件 Validated、政策與商品頁三圖／圖示讀回、既存 IARC／免費全球設定、已提交並進入預先處理，以及通過認證後自動發布的概觀截圖。尚未認證通過或公開發布。 |

第一個程式的現行位置為專案根目錄的 `01-8_ws2812/`，主程式為 `01-8_ws2812/01-8_ws2812.ino`。歷史 manifest 中的原始 `firmware/WindowsDynamicLighting/` 路徑保留不改，以維持當時建置證據；移位後的最新建置與測試紀錄另存於 `2026-10-21/01-8_ws2812/`，不覆寫原有清單或日誌。

JSON 清單中的路徑沿用當時專案根目錄與 `build/` 相對路徑，用來辨識歷史建置；不表示清單中的編譯產物均已上傳 GitHub。`.bin`、`.elf`、`.map`、測試執行檔及工具鏈快取仍保存在本機 `build/`，由 `.gitignore` 排除。重新建置後的產物須用新的紀錄核對，不能覆寫歷史證據後沿用原結論。

裝置紀錄已省略 MAC、USB instance 與序號。已確認燒錄、OS LampArray 與原生設定頁辨識；使用者已目視確認 8 顆都能隨原生設定切換紅／綠／藍。逐顆索引、亮度量測、DIN 訊號與耗電仍待驗證。`windows-control/sequence.json` 是送色前停止的紀錄，不能當成逐顆測試通過。該次序列之後，工具才加入 ControlWait 診斷；`lamp-properties-final.json` 對應最後工具的 InspectOnly，並未重跑送色。
