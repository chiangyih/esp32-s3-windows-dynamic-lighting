namespace MusicSync.UI;

internal sealed partial class MainForm
{
    private static string AboutVersion => typeof(Program).Assembly.GetName().Version?.ToString() ?? Application.ProductVersion;
    private const string HardwareRequirement = "必須搭配支援 Windows「動態光效」的硬體，才能控制實體燈光。";

    private Control BuildAboutPage()
    {
        var card = new SurfacePanel { Dock = DockStyle.Top, Height = 440, Padding = new Padding(22), Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = UiTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 30, 48, 34, 64, 64, 30, 72 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Label("版本資訊", _statusFont, UiTheme.Accent), 0, 0);
        var version = Label($"版本 {AboutVersion}", _headingFont, UiTheme.Text);
        version.Name = "AboutVersionLabel";
        layout.Controls.Add(version, 0, 1);
        layout.Controls.Add(Label("硬體需求", _statusFont, UiTheme.Accent), 0, 2);
        var requirement = Label(HardwareRequirement, _bodyFont, UiTheme.Text);
        requirement.Name = "HardwareRequirementLabel";
        layout.Controls.Add(requirement, 0, 3);
        layout.Controls.Add(Label("目前支援本專案 ESP32-S3 USB Light Strip（1～64 顆 WS2812）。\n燈數須與韌體回報一致；燈條需搭配相容控制板與韌體。\n增加燈數前，請確認 USB 供電能力。", _bodyFont, UiTheme.Muted), 0, 4);
        layout.Controls.Add(Label("使用前請確認", _statusFont, UiTheme.Accent), 0, 5);
        layout.Controls.Add(Label("在 Windows 設定 → 個人化 → 動態光效確認能看到燈具，並啟用裝置控制。\n背景使用時，請將本程式的控制優先順序排到前面。", _bodyFont, UiTheme.Text), 0, 6);
        layout.Controls.Add(Label("未連接相容硬體時，畫面預覽仍可能變化，實體燈光不會受控制。", _smallFont, UiTheme.Muted), 0, 7);
        card.Controls.Add(layout);
        return card;
    }
}
