# 認證測試說明草稿

本次候選版本為0.3.1.0，加入內建隱私政策，已建置並本機註冊；PFN與使用者提供值相符。產品身分記錄於 [partner-center-identity.json](partner-center-identity.json)，Source=UserProvided、BackendVerified=false，尚未讀回Partner Center後台。此文件不是WACK或Store認證通過報告。

## runFullTrust 用途（可供能力審查欄位）

This is a C# / .NET WinForms desktop lighting utility. It requires the desktop process model to capture the default multimedia render endpoint using WASAPI loopback, analyse playback volume and broad frequency-band energy in memory, and control a matching external USB LampArray device through the Windows Dynamic Lighting API. It does not open a microphone capture endpoint, record audio files, upload audio or analysis results, or use cloud accounts, advertising, tracking or telemetry. User-selected lamp count, colours, effects, sensitivity and brightness are saved only in a local preference file. The app respects Windows lighting arbitration and does not bypass another application's control priority.

## 無硬體時可驗證

1. 啟動「音樂動態燈光設置」，確認燈光設置、關於／版本與隱私政策分頁可開啟。隱私政策為唯讀、可捲動與複製的內建文字，不開啟外部網頁；發行者為 Tseng。
2. 確認停止狀態可修改 1～64 顆燈數；開始同步時數字欄位停用。
3. 使用系統預設多媒體輸出播放測試音樂，按「開始同步」；預覽應隨音訊變化。硬體狀態會顯示找不到裝置或等待控制權，這不會把預覽變化當成硬體成功。
4. 切換音量／聲頻／高低音、配色、呼吸／流動／單顆依序與速度選項。暫停音訊後預覽漸暗。
5. 按「停止」可恢復燈數編輯；正常關閉後重新開啟，偏好應還原。
6. 以關閉按鈕、視窗 X 或通知區關閉正常退出，程式不再留背景分析工作。無帳號或測試登入資料。

開發診斷可用 `--privacy` 直接選取隱私政策頁；這個選項本身不會開始同步。只有另指定自動開始／診斷參數才啟動分析，截圖與診斷報告仍需明確指定。

依使用者偏好，政策與 App 一起封裝，不建立公開政策外部連結。Microsoft Store 政策另要求提供隱私政策網址，Win32 產品也適用；內建頁可補充提供，但不能取代 Partner Center 所需網址。目前該欄位尚未完成，沒有填入假網址或以不傳輸音訊主張豁免。[Microsoft Store 政策 10.5.1](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#105-personal-information)

## 實體燈條驗證前提

需要本專案 `ESP32-S3 USB Light Strip`、VID 303A／PID 1001 的相容韌體與正確接線的 WS2812；只支援唯一可確認的此類裝置。測試者須自備硬體；不提供遠端連線或雲端裝置。

1. 目前經使用者確認的原型為 8 顆、GPIO4／5V／GND；App 設為 8 顆。增加燈數需重新建置相同 LampCount 韌體及核對供電。64 顆 USB 負載尚未實測，不作認證測試的已通過前提。
2. Windows「設定 → 個人化 → 動態光效」啟用裝置；將本程式在背景光源控制項排至適當優先順序。
3. 播放預設多媒體端點的音訊，開始同步並觀察實體燈條；暫停後應漸暗。Windows 內建效果無需設成「音效」。
4. 設定值與韌體燈數不符時應顯示兩個數值、不送色；不要把預覽當成已控制實體燈條。

## 已有證據與限制

- 0.3.1.0：本輪148項純程式檢查通過；正式身分候選本機註冊PFN相符。
- 0.3.1.0：三頁真實截圖1366×900已擷取並目視通過；隱私政策內容可見、非空白。三個PID消失且清理錯誤為空；退出碼未取回，不宣稱退出碼0，亦無新增實體燈條驗收。
- 0.3.0.0：148 項純程式、6 項預覽、26 項韌體主機與 8 項設定邊界檢查通過。
- 0.3.0.0：四種退出路徑與設定保存／還原、停止／重啟通過。
- 0.3.0.0 封裝 API 讀回 8 顆；設定 1／64 顆拒絕送色。設定 8 顆當次仍等待 Windows 仲裁、0 幀，未新增此版本實體音樂驗收。
- 舊版 8 顆三種效果及暫停漸暗由使用者目視確認；0.2.1 另有實際送色與正常退出證據。不能直接改寫為 0.3.0 新驗收。
- WACK因目前非管理員尚未執行；MakeAppx正常驗證、本機註冊、Partner Center套件驗證、提交、認證及發布各自分開記錄。
- 瀏覽器及node_repl工具在kernel assets初始化時發生路徑錯誤，未操作後台，未上傳、送審、認證或發布。使用者再次確認不建立外部政策網址，Store要求的該資料仍未完成；未建立網站／公開儲存庫或假網址。

精簡證據：[0.3.0 驗證摘要](../../../docs/verification/2026-10-21/lamp-counts/0.3.0/verification-summary.json)；完整歷史：[handoff.md](../../../handoff.md)。

## WACK 本機工具與命令

2026-10-21 唯讀檢查找到 `C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe`，FileVersion `10.0.26100.8249`。只呼叫了 help，沒有執行 reset、test 或關閉現有 App；help 沒有標準輸出，未保留 appcert 程序。

官方說明要求在有效使用者工作階段、具管理員權限的命令環境執行。準備正式對應套件與測試環境後，可使用下列命令；目前只提供操作方式，尚未執行：

```powershell
$appcert = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe'
& $appcert reset
& $appcert test -appxpackagepath '<正式Store候選MSIX完整路徑>' `
    -reportoutputpath '<新的測試報告完整路徑.xml>'
```

對已部署的對應套件，可改用 `test -packagefullname '<完整PackageFullName>' -reportoutputpath '<新報告.xml>'`。測試可能啟動／部署 App，應使用對應候選套件並保存完整報告，不能用工具存在或 help 成功當作測試通過。[Microsoft WACK 官方說明](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit)
