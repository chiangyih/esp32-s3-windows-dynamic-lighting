using System.Text;

namespace MusicSync.UI;

internal sealed partial class MainForm
{
    private const string PrivacyResourceName = "MusicSync.PrivacyPolicy.zh-TW.md";
    private readonly string _privacyPolicyText = LoadPrivacyPolicy();

    private static string LoadPrivacyPolicy()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(PrivacyResourceName)
            ?? throw new InvalidOperationException("內建隱私政策資源不存在。");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        // The embedded Markdown is the single source for both the app and repository.
        // Display its paragraphs as plain text without a Markdown rendering dependency.
        var paragraphs = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < paragraphs.Length; i++)
            if (paragraphs[i].StartsWith('#')) paragraphs[i] = paragraphs[i].TrimStart('#', ' ');
        return string.Join(Environment.NewLine, paragraphs);
    }

    private Control BuildPrivacyPage()
    {
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(22), Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = UiTheme.Surface, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(Label("隱私政策", _statusFont, UiTheme.Accent), 0, 0);
        layout.Controls.Add(Label("內建離線閱讀 · 發行者 Tseng", _smallFont, UiTheme.Muted), 0, 1);
        var policy = new RichTextBox
        {
            Name = "PrivacyPolicyText", AccessibleName = "隱私政策全文，可捲動及複製",
            Multiline = true, ReadOnly = true, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface, ForeColor = UiTheme.Text, Font = _bodyFont,
            Text = _privacyPolicyText, Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(policy, 0, 2);
        card.Controls.Add(layout);
        return card;
    }
}
