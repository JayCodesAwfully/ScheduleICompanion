using System.Diagnostics;

namespace ScheduleICompanion.Installer;

internal sealed class InstallerForm : Form
{
    private static readonly Color Background = Color.FromArgb(18, 25, 20);
    private static readonly Color Panel = Color.FromArgb(28, 39, 31);
    private static readonly Color Accent = Color.FromArgb(91, 183, 117);
    private static readonly Color TextPrimary = Color.FromArgb(232, 240, 234);
    private static readonly Color TextMuted = Color.FromArgb(163, 181, 168);
    private readonly InstallationService _service = new();
    private readonly TextBox _gamePath = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = false, Height = 46, Dock = DockStyle.Fill };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly CheckBox _installMelon = new() { Text = $"Install official MelonLoader v{InstallationService.MelonLoaderVersion} when missing", AutoSize = true, Checked = true };
    private readonly CheckBox _desktopShortcut = new() { Text = "Create desktop shortcut", AutoSize = true, Checked = true };
    private readonly CheckBox _launchAfter = new() { Text = "Start Companion after installation", AutoSize = true, Checked = true };
    private readonly CheckBox _installBackpack = new() { Text = "Enable Personal Backpack mod", AutoSize = true, Checked = true };
    private readonly Button _install = new() { Text = "Install / Repair", Width = 132, Height = 36 };
    private readonly Button _uninstall = new() { Text = "Uninstall Companion", Width = 145, Height = 36 };
    private readonly Button _launch = new() { Text = "Launch Companion", Width = 132, Height = 36 };
    private readonly Button _browse = new() { Text = "Browse...", Width = 88, Height = 28 };
    private readonly Button _melonHelp = new() { Text = "Official MelonLoader page", Width = 174, Height = 28 };
    private readonly List<Control> _actionControls = new();

    public InstallerForm()
    {
        Text = "Schedule I Companion Setup";
        ClientSize = new Size(780, 610);
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f);

        Controls.Add(BuildLayout());
        ConfigureTheme(this);
        AcceptButton = _install;

        _browse.Click += (_, _) => BrowseForGame();
        _gamePath.TextChanged += (_, _) => RefreshStatus();
        _install.Click += async (_, _) => await InstallAsync();
        _uninstall.Click += (_, _) => Uninstall();
        _launch.Click += (_, _) => RunGuarded(() => InstallationService.LaunchCompanion(_gamePath.Text));
        _melonHelp.Click += (_, _) => Process.Start(new ProcessStartInfo(InstallationService.MelonLoaderReleasesUrl) { UseShellExecute = true });
        _actionControls.AddRange(new Control[] { _gamePath, _browse, _install, _uninstall, _launch, _installMelon, _desktopShortcut, _launchAfter, _installBackpack });

        _gamePath.Text = SteamLocator.FindScheduleI() ?? "";
        RefreshStatus();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 8
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var title = new Label
        {
            Text = "SCHEDULE I  •  COMPANION SETUP",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 20f),
            ForeColor = Accent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(new Label { Text = "Schedule I installation folder", Dock = DockStyle.Fill, ForeColor = TextMuted }, 0, 1);

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        pathRow.Controls.Add(_gamePath, 0, 0);
        pathRow.Controls.Add(_browse, 1, 0);
        root.Controls.Add(pathRow, 0, 2);
        root.Controls.Add(_status, 0, 3);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Panel, Padding = new Padding(10, 7, 10, 5) };
        options.Controls.Add(_installMelon);
        options.Controls.Add(_desktopShortcut);
        options.Controls.Add(_launchAfter);
        options.Controls.Add(_installBackpack);
        root.Controls.Add(options, 0, 4);

        _log.BackColor = Color.FromArgb(12, 18, 14);
        _log.ForeColor = TextMuted;
        _log.Font = new Font("Consolas", 9f);
        root.Controls.Add(_log, 0, 5);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
        buttons.Controls.Add(_install);
        buttons.Controls.Add(_launch);
        buttons.Controls.Add(_uninstall);
        root.Controls.Add(buttons, 0, 6);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        footer.Controls.Add(_melonHelp);
        footer.Controls.Add(new Label
        {
            Text = "MelonLoader is downloaded only from the official LavaGang release.",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(10, 6, 0, 0)
        });
        root.Controls.Add(footer, 0, 7);
        return root;
    }

    private async Task InstallAsync()
    {
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(AppendLog);
            await _service.InstallAsync(
                _gamePath.Text.Trim(), _installMelon.Checked, _desktopShortcut.Checked,
                progress, CancellationToken.None, installBackpack: _installBackpack.Checked);
            RefreshStatus();
            if (_launchAfter.Checked) InstallationService.LaunchCompanion(_gamePath.Text.Trim());
            MessageBox.Show(this, "Schedule I Companion is ready. Launch the game normally through Steam.",
                "Installation complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Installation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void Uninstall()
    {
        if (MessageBox.Show(this,
                "Remove Schedule I Companion? MelonLoader and your LocalAppData settings will be preserved.",
                "Confirm uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        RunGuarded(() =>
        {
            _service.UninstallCompanion(_gamePath.Text.Trim(), new Progress<string>(AppendLog));
            RefreshStatus();
        });
    }

    private void BrowseForGame()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder containing Schedule I.exe",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_gamePath.Text) ? _gamePath.Text : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _gamePath.Text = dialog.SelectedPath;
    }

    private void RefreshStatus()
    {
        var path = _gamePath.Text.Trim();
        if (!SteamLocator.IsGameDirectory(path))
        {
            _status.Text = "Schedule I was not detected at this location.";
            _status.ForeColor = Color.FromArgb(230, 148, 95);
            return;
        }
        var melon = InstallationService.IsMelonLoaderInstalled(path) ? "MelonLoader installed" : "MelonLoader missing";
        var companion = InstallationService.IsCompanionInstalled(path) ? "Companion installed" : "Companion not installed";
        _status.Text = $"Game detected  •  {melon}  •  {companion}" +
                       (_service.IsPayloadReady ? "" : "  •  Setup payload missing");
        _status.ForeColor = Accent;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.ScrollToCaret();
    }

    private void SetBusy(bool busy)
    {
        foreach (var control in _actionControls) control.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void RunGuarded(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Schedule I Companion Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ConfigureTheme(Control root)
    {
        foreach (Control control in root.Controls)
        {
            control.ForeColor = TextPrimary;
            if (control is Button button)
            {
                button.BackColor = Color.FromArgb(45, 67, 51);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Accent;
            }
            else if (control is TextBox textBox)
            {
                textBox.BackColor = Panel;
                textBox.ForeColor = TextPrimary;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            ConfigureTheme(control);
        }
    }
}
