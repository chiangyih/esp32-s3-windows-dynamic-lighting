# Store 上架資料索引

**0.3.1.0 已正式上架 Microsoft Store**，台灣[公開商品頁已提供下載](https://apps.microsoft.com/detail/9nkrn1kcr738?hl=zh-TW&gl=TW)。本輪於 **2026-10-21 晚間追蹤讀回確認**，確認時間不代表實際發布時刻。Partner Center 顯示「在 Microsoft Store 中」，同一 Submission 1 的產品詳細資料讀回 0.3.1.0 X64 桌面套件。先前提交與 0.3.0 驗證證據維持歷史標記。

| 文件 | 用途 |
| --- | --- |
| [listing.zh-TW.md](listing.zh-TW.md) | 繁體中文商品頁文案及欄位紀錄 |
| [certification-notes.md](certification-notes.md) | runFullTrust 理由、審查測試步驟、WACK 操作方式 |
| [assets.json](assets.json) | 現有圖示與截圖尺寸／雜湊、可用性及來源 |
| [partner-center-identity.template.json](partner-center-identity.template.json) | 正式產品身分及聯絡／政策資料模板；null 不是可提交設定 |
| [partner-center-identity.json](partner-center-identity.json) | 使用者提供並與後台核對的正式產品身分及提交狀態，未記錄 SID |
| [內建隱私政策](../privacy-policy.zh-TW.md) | 發行者 Tseng、實際資料處理與本機診斷；與 App 共用同一嵌入來源，離線閱讀 |

Partner Center 已保存[公開隱私政策](https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting/blob/main/02-windows-music-sync/Packaging/privacy-policy.zh-TW.md)與 [GitHub Issues 支援頁](https://github.com/chiangyih/esp32-s3-windows-dynamic-lighting/issues)。使用者授權以本專案提供政策並自行將儲存庫改為 public；匿名存取確認 HTTP 200，政策原始文字與 App 內建來源一致。App 仍保留離線閱讀，不需另外部署 Pages。

## 現有素材

300×300商品頁圖示已備，1024×1024為設計原稿。0.3.1.0已透過正式身分套件AUMID擷取三張 **1366×900** 真實App畫面，目視確認通過，符合目前官方Desktop至少1366×768的尺寸；不是放大舊圖。0.3.0的820×786畫面仍保留為歷史，不作本次Store截圖。[Microsoft 截圖與圖示要求](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/screenshots-and-images)

新圖位於 [Screenshots](Screenshots/)：主頁、關於與硬體、內建隱私政策。三次測試PID均消失且清理回報無錯誤，未回讀到退出碼；不把截圖或清理結果當成實體燈條驗收。診斷JSON可含本機端點描述，僅作本機驗證證據，不是Store素材。

## 提交歷史與正式上架讀回

- 產品 `9NKRN1KCR738`，Submission 1：`1152921505702190045`；正式產品名稱、Package Identity 與 Publisher 已與後台核對相符。
- 自動上傳曾兩次回報 Error；使用者手動上傳後，後台顯示 `Validated`、0.3.1.0、X64、Windows.Desktop、最低 10.0.22631.0。已取消未來裝置系列，只發行桌面版；錯誤歷史保留於交接文件。
- 送審 MSIX 58,198,227 bytes；SHA-256 `412629BE9587F16D6CB4D5D52742D715A99D60C13497FDC111189E35F0977CB9`。本機註冊、148 項純程式檢查及三頁截圖驗證均已完成，不當作認證通過證據。
- 屬性已保存：公用程式與工具、次類空白；個人資訊問題選「是」，因 loopback 混音可能包含個人資訊，即使本程式只在本機記憶體處理。政策、網站與支援網址均已填入。
- 繁中商品頁 zh-TW（languageId 88）的說明、7 項功能、7 組搜尋字詞、三張 1366×900 截圖與 captions、300×300 圖示均已保存，概觀顯示 Store 清單完成。
- 認證測試備註含英文 `runFullTrust` 理由、無硬體預覽測試、實物 8 顆與 64 顆未測限制、退出方式；保存後重新載入文字一致。
- 沿用後台既存 IARC 10.3 分級：IARC／PEGI 3+、ESRB Everyone；本輪沒有重新答題或新增同意。價格與發行讀回為 240 個市場、TWD 0、公開可搜尋、盡快發行、從不停止。
- 2026-10-21 提交後，當時概觀顯示認證中，Publishing Status 步驟 2/4、預先處理進行中，並設定通過認證後立即發布；該狀態保留為提交時的歷史證據。
- 同日晚間追蹤確認後台顯示「在 Microsoft Store 中」，目前上架套件為 `Esp32MusicSync_0.3.1.0_x64_store.msix`、0.3.1.0、X64、Desktop 最低 10.0.22631.0，其他裝置系列及未來系列未勾選。台灣公開未登入商品頁顯示正確標題、發行者 Tseng、下載及「在 Microsoft Store 中檢視」按鈕，確認正式上架；未據此宣稱完成下載或安裝測試。
- WACK未執行：目前非管理員；三頁清理通過不等於WACK通過。

最新上架證據：[publication-status.json](../../../docs/verification/2026-10-21/store-publication/0.3.1/publication-status.json)、[公開商品頁畫面](../../../docs/verification/2026-10-21/store-publication/0.3.1/store-public-live.jpg)。確認時間不作為實際發布時刻。較早的 [submission-status.json](../../../docs/verification/2026-10-21/store-submission/0.3.1/submission-status.json)及[提交後畫面](../../../docs/verification/2026-10-21/store-submission/0.3.1/submission-in-certification.jpg)保留當時步驟 2/4 的歷史。先前工具初始化故障、政策偏好變更與上傳錯誤保留於 [handoff.md](../../../handoff.md)，不是目前阻擋。

## 後續確認

1. 本輪已由後台與台灣公開商品頁交叉確認正式上架；Store 簽署套件下載後的安裝及執行驗證仍未完成，不能用商品頁按鈕代替安裝測試。
2. WACK、乾淨環境安裝及必要的硬體測試仍未完成。現有實物為 8 顆，64 顆 USB 耗電與實體功能仍未驗證；正式上架不改變這些限制。
3. 若後台後續要求補件或必要本人聲明，依實際問題處理，不捏造資料或同意。現有公開政策及 App 內建政策應保持一致。[Store 政策 10.5.1](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#105-personal-information)

封裝腳本須讀取正式 Partner Center 身分，不能直接上傳 `LocalDev.Esp32MusicSync` 開發套件。參數範例見 [MSIX 與 Store 準備](../README.md)。
