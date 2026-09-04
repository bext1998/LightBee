using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Wcalss.AmbientBrightness;

/// <summary>
/// 設定視窗：調整感測與亮度對應設定、看即時讀數，並檢視每一筆判斷回溯到哪個 Spike Test 的驗證紀錄。
/// 全部用程式碼建立控制項，不用 Designer，方便之後改動。
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppConfig config;
    private readonly DisplayBrightnessController brightnessController;
    private readonly ValidationLog validationLog;
    private readonly BrightnessMapper mapperForPreview;
    private readonly Action<bool> onSaved;

    private TextBox deviceNameBox = null!;
    private ComboBox sharingModeCombo = null!;
    private NumericUpDown intervalUpDown = null!;
    private NumericUpDown hysteresisUpDown = null!;
    private CheckBox autoAdjustCheck = null!;
    private DataGridView bandsGrid = null!;
    private Label currentReadingLabel = null!;
    private Label brightnessAvailabilityLabel = null!;
    private DataGridView logGrid = null!;
    private Label logPathLabel = null!;

    public SettingsForm(
        AppConfig config,
        DisplayBrightnessController brightnessController,
        ValidationLog validationLog,
        BrightnessMapper mapperForPreview,
        Action<bool> onSaved)
    {
        this.config = config;
        this.brightnessController = brightnessController;
        this.validationLog = validationLog;
        this.mapperForPreview = mapperForPreview;
        this.onSaved = onSaved;

        Text = "WCALSS 環境光自動亮度 — 設定";
        Width = 780;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = true;
        FormClosing += (_, e) =>
        {
            // 關閉視窗只是隱藏，背景取樣繼續跑；真正結束要用系統匣選單的「結束」。
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSettingsTab());
        tabs.TabPages.Add(BuildStatusTab());
        tabs.TabPages.Add(BuildValidationLogTab());
        Controls.Add(tabs);

        RefreshStatusTab();
        RefreshLogGrid();
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("設定");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        deviceNameBox = new TextBox { Text = config.DeviceName, Width = 260 };
        sharingModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        sharingModeCombo.Items.AddRange(new object[] { "shared", "exclusive" });
        sharingModeCombo.SelectedItem = config.SharingMode;

        intervalUpDown = new NumericUpDown { Minimum = 1000, Maximum = 300000, Increment = 500, Width = 260, Value = config.SampleIntervalMs };
        hysteresisUpDown = new NumericUpDown { Minimum = 0, Maximum = 1, Increment = 0.005m, DecimalPlaces = 3, Width = 260, Value = (decimal)config.HysteresisMargin };
        autoAdjustCheck = new CheckBox { Text = "啟用自動調整螢幕亮度", Checked = config.AutoAdjustEnabled, AutoSize = true };

        AddRow(layout, "相機裝置名稱", deviceNameBox);
        AddRow(layout, "Sharing Mode", sharingModeCombo, "Test 10 實測：shared（SharedReadOnly）起始讀數比 exclusive 穩定，建議維持 shared。");
        AddRow(layout, "取樣間隔 (ms)", intervalUpDown, "週期性 Lazy 取樣，對應 Test 08 驗證過的長時間穩定模式，不常駐佔用相機。");
        AddRow(layout, "遲滯區間", hysteresisUpDown, "讀數在分級邊界附近時避免反覆切換亮度的緩衝量。");
        AddRow(layout, "", autoAdjustCheck);

        var bandsLabel = new Label { Text = "亮度分級（依 Gate B 實測結論，固定三段，不做連續調光）：", AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
        layout.SetColumnSpan(bandsLabel, 2);
        layout.Controls.Add(bandsLabel);
        layout.SetColumnSpan(bandsLabel, 2);

        bandsGrid = new DataGridView
        {
            Width = 680,
            Height = 160,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        bandsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分級名稱", DataPropertyName = "Label", Width = 140 });
        bandsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "上界（-1=以上）", DataPropertyName = "UpperBound", Width = 110 });
        bandsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "套用亮度 %", DataPropertyName = "TargetBrightnessPercent", Width = 90 });
        bandsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "對應驗證來源", DataPropertyName = "ValidatedBy", Width = 320, ReadOnly = true });
        bandsGrid.Columns[3].DefaultCellStyle.ForeColor = SystemColors.GrayText;
        bandsGrid.DataSource = new BindingList<LuminanceBandConfig>(config.Bands.Select(CloneBand).ToList());
        layout.SetColumnSpan(bandsGrid, 2);
        layout.Controls.Add(bandsGrid);

        var saveButton = new Button { Text = "儲存並套用", Width = 140, Margin = new Padding(0, 12, 0, 0) };
        saveButton.Click += OnSave;
        layout.SetColumnSpan(saveButton, 2);
        layout.Controls.Add(saveButton);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildStatusTab()
    {
        var page = new TabPage("即時狀態");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), AutoScroll = true };

        currentReadingLabel = new Label { AutoSize = true, Font = new Font(Font!.FontFamily, 12) };
        brightnessAvailabilityLabel = new Label { AutoSize = true, MaximumSize = new Size(680, 0) };
        var sampleNowButton = new Button { Text = "立即取樣一次", Width = 140, Margin = new Padding(0, 12, 0, 0) };
        sampleNowButton.Click += (_, _) => onSaved(false); // 觸發呼叫端重新讀狀態；實際取樣由 tray context timer 負責

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Margin = new Padding(0, 16, 0, 0),
            Text = "程式會先嘗試 WMI／ACPI 亮度控制，再改用外接螢幕的 DDC/CI。若下方顯示不支援，取樣與分級判定仍會正常記錄在「驗證紀錄」分頁，但無法調整螢幕亮度。"
        };

        panel.Controls.Add(currentReadingLabel);
        panel.Controls.Add(brightnessAvailabilityLabel);
        panel.Controls.Add(sampleNowButton);
        panel.Controls.Add(note);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildValidationLogTab()
    {
        var page = new TabPage("驗證紀錄");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = "每一列對應一次取樣循環：原始亮度讀數、判定的分級、是否套用了新亮度，以及這個判斷回溯到 docs/spike-report.md 的哪個 Gate/Test。"
        };
        layout.Controls.Add(header, 0, 0);

        logGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            RowHeadersVisible = false
        };
        logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "時間 (UTC)", DataPropertyName = "TimestampUtc", Width = 150 });
        logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "成功", DataPropertyName = "SampleSucceeded", Width = 50 });
        logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "平均亮度", DataPropertyName = "MeanLuminance", Width = 80 });
        logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分級", DataPropertyName = "BandLabel", Width = 110 });
        logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "套用亮度%", DataPropertyName = "AppliedBrightnessPercent", Width = 80 });
        logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "驗證來源", DataPropertyName = "ValidatedBy", Width = 260 });
        logGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "備註", DataPropertyName = "Note", Width = 180 });
        layout.Controls.Add(logGrid, 0, 1);

        logPathLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        layout.Controls.Add(logPathLabel, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private static void AddRow(TableLayoutPanel layout, string label, Control control, string? helpText = null)
    {
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });

        if (helpText is null)
        {
            layout.Controls.Add(control);
            return;
        }

        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        stack.Controls.Add(control);
        stack.Controls.Add(new Label { Text = helpText, AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText });
        layout.Controls.Add(stack);
    }

    private static LuminanceBandConfig CloneBand(LuminanceBandConfig source) => new()
    {
        Label = source.Label,
        UpperBound = source.UpperBound,
        TargetBrightnessPercent = source.TargetBrightnessPercent,
        ValidatedBy = source.ValidatedBy
    };

    private void OnSave(object? sender, EventArgs e)
    {
        var sensorSettingsChanged =
            !string.Equals(deviceNameBox.Text.Trim(), config.DeviceName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((string)sharingModeCombo.SelectedItem!, config.SharingMode, StringComparison.OrdinalIgnoreCase);

        config.DeviceName = deviceNameBox.Text.Trim();
        config.SharingMode = (string)sharingModeCombo.SelectedItem!;
        config.SampleIntervalMs = (int)intervalUpDown.Value;
        config.HysteresisMargin = (double)hysteresisUpDown.Value;
        config.AutoAdjustEnabled = autoAdjustCheck.Checked;
        config.Bands = ((BindingList<LuminanceBandConfig>)bandsGrid.DataSource).ToList();
        config.Save();

        onSaved(sensorSettingsChanged);
        MessageBox.Show(this, "設定已儲存並套用。", "WCALSS 環境光自動亮度", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void OnLogUpdated()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnLogUpdated);
            return;
        }

        RefreshStatusTab();
        RefreshLogGrid();
    }

    private void RefreshStatusTab()
    {
        brightnessController.Probe();
        var current = brightnessController.CurrentBrightnessPercent;
        currentReadingLabel.Text = validationLog.RecentEntries.LastOrDefault(e => e.SampleSucceeded) is { } last
            ? $"最近一次讀數：{last.MeanLuminance:F4}（{last.BandLabel}），時間 {last.TimestampUtc.ToLocalTime():T}"
            : "尚無取樣紀錄。";

        brightnessAvailabilityLabel.Text = brightnessController.IsAvailable
            ? $"螢幕亮度控制：可用（{brightnessController.ControlMethodDescription}，目前 {current?.ToString() ?? "未知"}%）"
            : $"螢幕亮度控制：不可用 — {brightnessController.UnavailableReason}";
    }

    private void RefreshLogGrid()
    {
        logGrid.DataSource = new BindingList<ValidationLogEntry>(validationLog.RecentEntries.Reverse().ToList());
        logPathLabel.Text = $"完整紀錄檔：{validationLog.Path_}";
    }
}
