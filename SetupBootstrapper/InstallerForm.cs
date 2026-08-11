using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace SetupBootstrapper;

public class InstallerForm : Form
{
    private const string APP_NAME = "DR Bahig Books Portal";

    private static readonly Color ClrPrimary = Color.FromArgb(16, 185, 129);
    private static readonly Color ClrDark = Color.FromArgb(22, 23, 28);
    private static readonly Color ClrSide = Color.FromArgb(26, 27, 33);
    private static readonly Color ClrHeader = Color.FromArgb(30, 32, 38);
    private static readonly Color ClrBottom = Color.FromArgb(26, 27, 33);
    private static readonly Color ClrText = Color.FromArgb(220, 222, 228);
    private static readonly Color ClrMuted = Color.FromArgb(140, 142, 150);
    private static readonly Color ClrDim = Color.FromArgb(90, 92, 100);

    private Panel sidePanel = null!, contentPanel = null!, bottomPanel = null!;
    private Label step1Label = null!, step2Label = null!, step3Label = null!;
    private Label? statusLabel;
    private ProgressBar? progressBar;
    private Button installBtn = null!, cancelBtn = null!;
    private CheckBox? shortcutCb, launchCb;
    private int currentStep;

    private const int STEP_WELCOME = 0;
    private const int STEP_INSTALL = 1;
    private const int STEP_DONE = 2;

    private const int FORM_W = 680;
    private const int FORM_H = 500;
    private const int SIDE_W = 180;

    public InstallerForm()
    {
        var appIcon = LoadAppIcon();

        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(FORM_W, FORM_H);
        Text = APP_NAME + " Setup";
        BackColor = ClrDark;
        Font = new Font("Segoe UI", 9);
        Icon = appIcon;
        Padding = new Padding(0);

        BuildSidePanel(appIcon);
        BuildBottomBar();

        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ClrDark,
            Padding = new Padding(36, 20, 36, 0),
            ForeColor = ClrText
        };

        Controls.AddRange([sidePanel, contentPanel, bottomPanel]);
        ShowWelcome();
    }

    // ──────────────────────────────────────────────
    //  SIDE PANEL
    // ──────────────────────────────────────────────
    private void BuildSidePanel(Icon? appIcon)
    {
        sidePanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = SIDE_W,
            BackColor = ClrSide
        };

        var iconBox = new PictureBox
        {
            Size = new Size(56, 56),
            Location = new Point((SIDE_W - 56) / 2, 28),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = appIcon?.ToBitmap() ?? SystemIcons.Application.ToBitmap()
        };

        var appLabel = new Label
        {
            Text = APP_NAME,
            Location = new Point(8, 92),
            Width = SIDE_W - 16,
            Height = 20,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ClrText,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        var subLabel = new Label
        {
            Text = "Print Agent Setup",
            Location = new Point(8, 112),
            Width = SIDE_W - 16,
            Height = 18,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ClrMuted,
            Font = new Font("Segoe UI", 8)
        };

        // Separator
        var sep = new Panel
        {
            Location = new Point(20, 148),
            Size = new Size(SIDE_W - 40, 1),
            BackColor = Color.FromArgb(50, 52, 58)
        };

        // Step indicators using numbered circles
        step1Label = CreateStepLabel("Welcome", 1, 170);
        step2Label = CreateStepLabel("Install", 2, 205);
        step3Label = CreateStepLabel("Complete", 3, 240);

        sidePanel.Controls.AddRange([iconBox, appLabel, subLabel, sep, step1Label, step2Label, step3Label]);
    }

    private static Label CreateStepLabel(string text, int number, int y)
    {
        return new Label
        {
            Text = $"  {number}   {text}",
            Location = new Point(12, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            ForeColor = ClrDim
        };
    }

    // ──────────────────────────────────────────────
    //  BOTTOM BAR
    // ──────────────────────────────────────────────
    private void BuildBottomBar()
    {
        bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = ClrBottom
        };

        // Top border line
        var topLine = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(48, 50, 56)
        };

        cancelBtn = new Button
        {
            Text = "Cancel",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 47, 53),
            ForeColor = ClrMuted,
            FlatAppearance = { BorderColor = Color.FromArgb(65, 67, 73) },
            Cursor = Cursors.Hand,
            Size = new Size(90, 28),
            Font = new Font("Segoe UI", 9)
        };
        cancelBtn.Click += (_, _) => Close();

        installBtn = new Button
        {
            Text = "Install",
            FlatStyle = FlatStyle.Flat,
            BackColor = ClrPrimary,
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = ClrPrimary },
            Cursor = Cursors.Hand,
            Size = new Size(150, 28),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        installBtn.Click += OnInstallClick;

        bottomPanel.Controls.AddRange([topLine, cancelBtn, installBtn]);
        bottomPanel.Resize += (_, _) => RepositionBottomButtons();
    }

    private void RepositionBottomButtons()
    {
        var bw = bottomPanel.ClientSize.Width;
        var gap = 8;
        installBtn.Location = new Point(bw - 16 - installBtn.Width, (bottomPanel.ClientSize.Height - installBtn.Height) / 2);
        cancelBtn.Location = new Point(installBtn.Left - gap - cancelBtn.Width, (bottomPanel.ClientSize.Height - cancelBtn.Height) / 2);
    }

    // ──────────────────────────────────────────────
    //  STEP TRACKING
    // ──────────────────────────────────────────────
    private void SetStep(int step)
    {
        currentStep = step;
        var labels = new[] { step1Label, step2Label, step3Label };
        for (int i = 0; i < labels.Length; i++)
        {
            bool active = i == step;
            labels[i].ForeColor = active ? ClrPrimary : ClrDim;
            labels[i].Font = new Font("Segoe UI", 10, active ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private void ClearContent()
    {
        contentPanel.Controls.Clear();
        progressBar = null!;
        statusLabel = null!;
        shortcutCb = null!;
        launchCb = null!;
    }

    // ──────────────────────────────────────────────
    //  WELCOME PAGE
    // ──────────────────────────────────────────────
    private void ShowWelcome()
    {
        SetStep(STEP_WELCOME);
        ClearContent();
        installBtn.Text = "Install";
        installBtn.Enabled = true;
        cancelBtn.Text = "Cancel";
        cancelBtn.Enabled = true;

        var cp = contentPanel;
        var cw = cp.ClientSize.Width - cp.Padding.Horizontal;

        // Title
        var welcomeTitle = new Label
        {
            Text = "Welcome to the " + APP_NAME + " Setup Wizard",
            Location = new Point(0, 4),
            Size = new Size(cw, 28),
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = ClrText
        };

        var welcomeDesc = new Label
        {
            Text = "This wizard will install the print agent on your computer.\nThe agent runs in the background to enable printing from the portal.",
            Location = new Point(0, 38),
            Size = new Size(cw, 40),
            ForeColor = ClrMuted
        };

        // Section: What's included
        var sectionTitle = new Label
        {
            Text = "What\u2019s included",
            Location = new Point(0, 96),
            Size = new Size(cw, 20),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = ClrText
        };

        var items = new[]
        {
            ("Print Agent Service",    "Runs at startup; listens for print jobs from the portal"),
            ("Agent Dashboard",        "Desktop UI to monitor status and queued jobs"),
            ("Printer Detection",      "Detects USB, WiFi, and network printers automatically"),
            ("System Tray App",        "Quick access from the notification area"),
        };

        var y = 126;
        foreach (var (item, desc) in items)
        {
            // Colored bullet
            var bullet = new Label
            {
                Text = "\u25CF",
                Location = new Point(2, y),
                AutoSize = true,
                ForeColor = ClrPrimary,
                Font = new Font("Segoe UI", 10)
            };
            var itemLabel = new Label
            {
                Text = item,
                Location = new Point(20, y),
                AutoSize = true,
                ForeColor = ClrText,
                Font = new Font("Segoe UI", 10)
            };
            var descLabel = new Label
            {
                Text = desc,
                Location = new Point(20, y + 16),
                AutoSize = true,
                ForeColor = ClrMuted,
                Font = new Font("Segoe UI", 9)
            };
            cp.Controls.AddRange([bullet, itemLabel, descLabel]);
            y += 44;
        }

        // Note about existing installation
        var note = new Label
        {
            Text = "Tip: Running this installer again updates the agent and keeps your\ncurrent server settings (the API key / server address are preserved).",
            Location = new Point(0, y + 8),
            Size = new Size(cw, 32),
            ForeColor = ClrDim,
            Font = new Font("Segoe UI", 8)
        };
        cp.Controls.AddRange([welcomeTitle, welcomeDesc, sectionTitle, note]);
    }

    // ──────────────────────────────────────────────
    //  INSTALL PAGE
    // ──────────────────────────────────────────────
    private void ShowInstalling()
    {
        SetStep(STEP_INSTALL);
        ClearContent();
        installBtn.Enabled = false;
        installBtn.Text = "Installing\u2026";
        cancelBtn.Enabled = false;

        var cp = contentPanel;
        var cw = cp.ClientSize.Width - cp.Padding.Horizontal;

        var instTitle = new Label
        {
            Text = "Installing " + APP_NAME + " Print Agent",
            Location = new Point(0, 12),
            Size = new Size(cw, 24),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = ClrText
        };

        var instDesc = new Label
        {
            Text = "Please wait while the installation completes.",
            Location = new Point(0, 40),
            Size = new Size(cw, 18),
            ForeColor = ClrMuted,
            Font = new Font("Segoe UI", 9)
        };

        progressBar = new ProgressBar
        {
            Location = new Point(0, 72),
            Width = cw,
            Height = 24,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            ForeColor = ClrPrimary,
            BackColor = Color.FromArgb(40, 42, 48)
        };

        statusLabel = new Label
        {
            Text = "Preparing...",
            Location = new Point(0, 104),
            AutoSize = true,
            ForeColor = ClrText,
            Font = new Font("Segoe UI", 10)
        };

        cp.Controls.AddRange([instTitle, instDesc, progressBar, statusLabel]);
    }

    // ──────────────────────────────────────────────
    //  COMPLETE PAGE
    // ──────────────────────────────────────────────
    private void ShowCompleted()
    {
        SetStep(STEP_DONE);
        ClearContent();
        installBtn.Text = "Finish";
        installBtn.Enabled = true;
        cancelBtn.Text = "Close";
        cancelBtn.Enabled = true;

        var cp = contentPanel;
        var cw = cp.ClientSize.Width - cp.Padding.Horizontal;

        // Large checkmark
        var checkIcon = new Label
        {
            Text = "\u2714",
            Font = new Font("Segoe UI", 40, FontStyle.Bold),
            Location = new Point(0, 16),
            AutoSize = true,
            ForeColor = ClrPrimary
        };

        var doneTitle = new Label
        {
            Text = APP_NAME + " Print Agent",
            Location = new Point(4, 68),
            Size = new Size(cw - 8, 22),
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = ClrText
        };

        var doneDesc = new Label
        {
            Text = "has been installed successfully.\nThe agent is running and ready to receive print jobs.",
            Location = new Point(4, 94),
            Size = new Size(cw - 8, 36),
            ForeColor = ClrMuted
        };

        shortcutCb = new CheckBox
        {
            Text = "Create a desktop shortcut",
            Location = new Point(2, 148),
            AutoSize = true,
            Checked = true,
            ForeColor = ClrText,
            Font = new Font("Segoe UI", 10)
        };

        launchCb = new CheckBox
        {
            Text = "Open the agent dashboard",
            Location = new Point(2, 174),
            AutoSize = true,
            Checked = true,
            ForeColor = ClrText,
            Font = new Font("Segoe UI", 10)
        };

        var trayNote = new Label
        {
            Text = "You can also manage the agent from the system tray.",
            Location = new Point(4, 206),
            AutoSize = true,
            ForeColor = ClrDim,
            Font = new Font("Segoe UI", 9)
        };

        cp.Controls.AddRange([checkIcon, doneTitle, doneDesc, shortcutCb, launchCb, trayNote]);

        CreateDesktopShortcut();
    }

    // ──────────────────────────────────────────────
    //  INSTALL LOGIC
    // ──────────────────────────────────────────────
    private async void OnInstallClick(object? sender, EventArgs e)
    {
        if (currentStep == STEP_WELCOME)
        {
            ShowInstalling();
            await RunInstallation();
            ShowCompleted();
        }
        else if (currentStep == STEP_DONE)
        {
            if (launchCb?.Checked == true)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BookShopPrintAgent");
                try { Process.Start(Path.Combine(dir, "BookShopAgentUI.exe")); } catch { }
            }
            Close();
        }
    }

    private async Task RunInstallation()
    {
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "BkSetup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);

            await SetProgress(5, "Extracting files...");
            ExtractResource("BookShopPrintAgent.exe", tmpDir);
            ExtractResource("BookShopAgentUI.exe", tmpDir);
            ExtractResource("BookShopAgentUI.dll", tmpDir);
            ExtractResource("BookShopAgentUI.deps.json", tmpDir);
            ExtractResource("BookShopAgentUI.runtimeconfig.json", tmpDir);
            ExtractResource("SumatraPDF-3.6.1-64.exe", tmpDir);
            ExtractResource("appsettings.json", tmpDir);
            ExtractResource("book.ico", tmpDir);

            await SetProgress(15, "Preparing destination...");
            var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BookShopPrintAgent");
            Directory.CreateDirectory(installDir);

            await SetProgress(25, "Stopping running agents...");
            KillProc("BookShopPrintAgent");
            KillProc("BookShopAgentUI");
            await Task.Delay(500);

            await SetProgress(35, "Cleaning previous installation...");
            foreach (var f in Directory.GetFiles(installDir, "*.dll")) SafeDelete(f);
            foreach (var f in Directory.GetFiles(installDir, "*.pdb")) SafeDelete(f);
            SafeDelete(Path.Combine(installDir, "BookShopPrintAgent.deps.json"));
            SafeDelete(Path.Combine(installDir, "BookShopPrintAgent.runtimeconfig.json"));
            SafeDelete(Path.Combine(installDir, "BookShopAgentUI.deps.json"));
            SafeDelete(Path.Combine(installDir, "BookShopAgentUI.runtimeconfig.json"));

            await SetProgress(40, "Copying print agent...");
            File.Copy(Path.Combine(tmpDir, "BookShopPrintAgent.exe"), Path.Combine(installDir, "BookShopPrintAgent.exe"), true);

            await SetProgress(52, "Copying dashboard...");
            File.Copy(Path.Combine(tmpDir, "BookShopAgentUI.exe"), Path.Combine(installDir, "BookShopAgentUI.exe"), true);
            File.Copy(Path.Combine(tmpDir, "BookShopAgentUI.dll"), Path.Combine(installDir, "BookShopAgentUI.dll"), true);
            File.Copy(Path.Combine(tmpDir, "BookShopAgentUI.deps.json"), Path.Combine(installDir, "BookShopAgentUI.deps.json"), true);
            File.Copy(Path.Combine(tmpDir, "BookShopAgentUI.runtimeconfig.json"), Path.Combine(installDir, "BookShopAgentUI.runtimeconfig.json"), true);

            await SetProgress(62, "Copying printer engine...");
            File.Copy(Path.Combine(tmpDir, "SumatraPDF-3.6.1-64.exe"), Path.Combine(installDir, "SumatraPDF-3.6.1-64.exe"), true);

            await SetProgress(70, "Writing configuration...");
            // Preserve the machine's existing settings (server URL / API key) when updating.
            var existingConfig = Path.Combine(installDir, "appsettings.json");
            var configBackup = Path.Combine(installDir, "appsettings.json.setupbak");
            bool hadConfig = File.Exists(existingConfig);
            if (hadConfig) File.Copy(existingConfig, configBackup, true);

            File.Copy(Path.Combine(tmpDir, "appsettings.json"), existingConfig, true);
            if (hadConfig)
            {
                File.Copy(configBackup, existingConfig, true);
                SafeDelete(configBackup);
            }
            File.Copy(Path.Combine(tmpDir, "book.ico"), Path.Combine(installDir, "book.ico"), true);

            await SetProgress(78, "Creating scheduled task...");
            RunCmd("schtasks", "/create /tn \"BookShopPrintAgent\" /tr \"'" + Path.Combine(installDir, "BookShopPrintAgent.exe") + "'\" /sc onstart /ru SYSTEM /rl highest /f");

            await SetProgress(86, "Registering uninstaller...");
            WriteUninstallScript(installDir);
            RegisterUninstall(installDir);

            await SetProgress(94, "Starting dashboard...");
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installDir, "BookShopAgentUI.exe"),
                WorkingDirectory = installDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            await SetProgress(100, "Done!");
            await Task.Delay(300);

            SafeDeleteDir(tmpDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Installation failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task SetProgress(int value, string text)
    {
        if (progressBar is { IsDisposed: false }) progressBar.Value = Math.Clamp(value, 0, 100);
        if (statusLabel is { IsDisposed: false }) statusLabel.Text = text;
        await Task.Delay(60);
    }

    // ──────────────────────────────────────────────
    //  HELPERS
    // ──────────────────────────────────────────────
    private static Icon? LoadAppIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("book.ico", StringComparison.OrdinalIgnoreCase));
            if (name != null) using (var s = asm.GetManifestResourceStream(name)!) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }

    private static void KillProc(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
            try { p.Kill(); p.WaitForExit(3000); } catch { }
    }

    private static void RunCmd(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, Verb = "runas" });
            p?.WaitForExit(10000);
        }
        catch { }
    }

    private static string FindResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var r in asm.GetManifestResourceNames())
            if (r.EndsWith(name, StringComparison.OrdinalIgnoreCase)) return r;
        throw new InvalidOperationException("Resource not found: " + name);
    }

    private static void ExtractResource(string name, string dir)
    {
        var asm = Assembly.GetExecutingAssembly();
        var full = FindResource(name);
        using var stream = asm.GetManifestResourceStream(full)!;
        using var file = File.Create(Path.Combine(dir, name));
        stream.CopyTo(file);
    }

    private static void SafeDelete(string path) { try { File.Delete(path); } catch { } }
    private static void SafeDeleteDir(string path) { try { Directory.Delete(path, true); } catch { } }

    private void CreateDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BookShopPrintAgent");
            var target = Path.Combine(installDir, "BookShopAgentUI.exe");
            var iconPath = Path.Combine(installDir, "book.ico");
            if (!File.Exists(target)) return;

            var shortcut = Path.Combine(desktop, "DR Bahig Books Portal.lnk");
            var ps = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{shortcut.Replace("'", "''")}');" +
                     $"$s.TargetPath='{target.Replace("'", "''")}';" +
                     $"$s.WorkingDirectory='{installDir.Replace("'", "''")}';" +
                     $"$s.Description='Print Agent for DR Bahig Books Portal';" +
                     $"$s.IconLocation='{iconPath.Replace("'", "''")}';$s.Save()";
            Process.Start(new ProcessStartInfo("powershell", "-NoProfile -Command \"" + ps + "\"") { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
        }
        catch { }
    }

    private static void WriteUninstallScript(string installDir)
    {
        var path = Path.Combine(installDir, "uninstall.ps1");
        var content = @"# DR Bahig Books Portal Print Agent - Uninstaller
$dir = '" + installDir.Replace("'", "''") + @"'
Stop-Process -Name 'BookShopPrintAgent' -Force -ErrorAction SilentlyContinue
Stop-Process -Name 'BookShopAgentUI' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
& schtasks /delete /tn 'BookShopPrintAgent' /f 2>$null
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DR Bahig Books Portal.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
reg delete 'HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\DR Bahig Books Portal' /f 2>$null
reg delete 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\DR Bahig Books Portal' /f 2>$null
";
        File.WriteAllText(path, content);
    }

    private static void RegisterUninstall(string installDir)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DR Bahig Books Portal");
            key.SetValue("DisplayName", APP_NAME + " Print Agent");
            key.SetValue("DisplayVersion", "2.0.0");
            key.SetValue("Publisher", "DR Bahig Books");
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("DisplayIcon", Path.Combine(installDir, "book.ico"));
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString", "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "uninstall.ps1") + "\"");
            key.SetValue("QuietUninstallString", "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(installDir, "uninstall.ps1") + "\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 255000, RegistryValueKind.DWord);
        }
        catch { }
    }
}
