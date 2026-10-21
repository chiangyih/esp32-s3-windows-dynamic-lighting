# Store 上架資料索引

準備日期：2026-10-21；候選版本 0.3.1.0。正式身分候選已建置並本機註冊，PFN與使用者提供值相符；Partner Center後台仍未讀回。此目錄不代表已上傳、送審、認證或發布；0.3.0歷史證據維持原版本標記。

| 文件 | 用途 |
| --- | --- |
| [listing.zh-TW.md](listing.zh-TW.md) | 繁體中文商品頁文案與待填欄位 |
| [certification-notes.md](certification-notes.md) | runFullTrust 理由、審查測試步驟、WACK 操作方式 |
| [assets.json](assets.json) | 現有圖示與截圖尺寸／雜湊、可用性及來源 |
| [partner-center-identity.template.json](partner-center-identity.template.json) | 正式產品身分及聯絡／政策資料模板；null 不是可提交設定 |
| [partner-center-identity.json](partner-center-identity.json) | 使用者提供的正式產品身分；尚未後台驗證，未記錄 SID |
| [內建隱私政策](../privacy-policy.zh-TW.md) | 發行者 Tseng、實際資料處理與本機診斷；與 App 共用同一嵌入來源，離線閱讀 |

Partner Center 政策網址：[公開隱私政策](https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting/blob/main/02-windows-music-sync/Packaging/privacy-policy.zh-TW.md)。使用者已授權以本專案提供政策，並自行將儲存庫改為 public；本輪匿名存取確認 HTTP 200，原始文字與內建政策來源一致。網址已備妥，尚未寫入後台。

## 現有素材

300×300商品頁圖示已備，1024×1024為設計原稿。0.3.1.0已透過正式身分套件AUMID擷取三張 **1366×900** 真實App畫面，目視確認通過，符合目前官方Desktop至少1366×768的尺寸；不是放大舊圖。0.3.0的820×786畫面仍保留為歷史，不作本次Store截圖。[Microsoft 截圖與圖示要求](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/screenshots-and-images)

新圖位於 [Screenshots](Screenshots/)：主頁、關於與硬體、內建隱私政策。三次測試PID均消失且清理回報無錯誤，未回讀到退出碼；不把截圖或清理結果當成實體燈條驗收。診斷JSON可含本機端點描述，僅作本機驗證證據，不是Store素材。

## 本輪候選狀態

- 0.3.1.0正式身分候選建置、本機註冊及148項純程式檢查通過；本機PFN相符，BackendVerified=false。
- 候選MSIX 58,198,227 bytes；SHA-256 `412629BE9587F16D6CB4D5D52742D715A99D60C13497FDC111189E35F0977CB9`。
- WACK未執行：目前非管理員；三頁清理通過不等於WACK通過。
- 瀏覽器與node_repl工具因kernel assets初始化路徑錯誤，無法操作Partner Center，因此未上傳、送審、認證或發布。
- 使用者最新授權取代先前不建立外部網址的偏好；公開 GitHub 政策已驗證可匿名閱讀，無需另建 Pages 或重建 MSIX。歷史拒絕與首次檢查仍保留於 handoff 及舊證據，不代表目前偏好。

## 仍需 Partner Center 或使用者提供

1. 使用者已提供正式產品名稱、Package/Identity/Name、完整 Publisher（CN=...）、PublisherDisplayName、PFN及Store ID，本機註冊PFN相符；仍需後台讀回與Store套件驗證。
2. 將上方已驗證公開的政策網址填入 Partner Center，保留 App 內建離線政策；發行者為 Tseng。[GitHub Issues 支援頁](https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting/issues) 已啟用並經匿名 HTTP 200 檢查，可作支援網址，未捏造 email。網址可公開閱讀不等於已在後台儲存或已通過審查。[Store 政策 10.5.1](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#105-personal-information)
3. 發行市場、可見性、價格與實際產品分類；文案中的免費為候選，沒有替使用者作帳戶設定。
4. 實際 IARC 問卷內容與必要的本人聲明／同意；沒有捏造年齡、同意或分級結果。
5. 正式候選套件WACK、必要的硬體與乾淨環境驗證；符合尺寸的三張截圖已備。

封裝腳本須讀取正式 Partner Center 身分，不能直接上傳 `LocalDev.Esp32MusicSync` 開發套件。參數範例見 [MSIX 與 Store 準備](../README.md)。
