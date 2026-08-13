using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookShopAgentUI;

static class Program
{
    private static NotifyIcon? trayIcon;
    private static ContextMenuStrip? trayMenu;
    private static string? agentDir;
    private static string? agentExe;
    private static bool agentRunning;
    private static DashboardForm? dashboard;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        agentDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BookShopPrintAgent");
        agentExe = Path.Combine(agentDir, "BookShopPrintAgent.exe");

        CreateTrayIcon();

        // "Setup Configuration" first, right after installation (--setup from the installer):
        // the bookshop enters its own API key before the agent starts heartbeating.
        bool runSetup = args.Any(a => string.Equals(a, "--setup", StringComparison.OrdinalIgnoreCase));

        // Show dashboard immediately on launch
        ShowDashboard();

        // Start the agent if install dir exists
        if (Directory.Exists(agentDir))
            _ = StartAgentAsync();

        if (runSetup)
            ShowSetup();

        Application.Run();
    }

    private static void CreateTrayIcon()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Dashboard", null, (_, _) => ShowDashboard());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Setup Configuration", null, (_, _) => ShowSetup());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Start Agent", null, async (_, _) => await StartAgentAsync());
        trayMenu.Items.Add("Stop Agent", null, (_, _) => StopAgent());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApp());

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DR Bahig Books Portal",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => ShowDashboard();
    }

    private static void ShowDashboard()
    {
        if (dashboard == null || dashboard.IsDisposed)
        {
            dashboard = new DashboardForm();
            dashboard.FormClosed += (_, _) => dashboard = null;
        }
        dashboard.Show();
        dashboard.Activate();
        _ = dashboard.RefreshStatusAsync();
    }

    private static void ShowSetup()
    {
        var setupForm = new SetupForm();
        setupForm.ShowDialog();
    }

    /// <summary>Restarts the agent so a freshly saved API key takes effect immediately.</summary>
    internal static void RestartAgentAfterConfigChange()
    {
        if (agentRunning)
        {
            StopAgent();
            Thread.Sleep(800);
            _ = StartAgentAsync();
        }
    }

    internal static async Task StartAgentAsync()
    {
        if (agentRunning) return;
        if (!File.Exists(agentExe)) return;

        try
        {
            // Kill stale process holding port 8080 via PowerShell (can kill SYSTEM-level)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -Command \"$p=Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; if ($p) { Stop-Process -Id $p -Force }\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }
            await Task.Delay(1000);

            Process.Start(new ProcessStartInfo
            {
                FileName = agentExe,
                WorkingDirectory = agentDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            // Wait for it to come online
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                try
                {
                    var r = await client.GetAsync("http://127.0.0.1:8080/api/print-job/health");
                    if (r.IsSuccessStatusCode) { agentRunning = true; break; }
                }
                catch { }
            }

            UpdateMenu();
            if (dashboard != null && !dashboard.IsDisposed)
                await dashboard.RefreshStatusAsync();

            // Show balloon tip with result
            trayIcon?.ShowBalloonTip(3000, "DR Bahig Books Portal",
                agentRunning ? "Agent started successfully." : "Agent failed to start. Check that port 8080 is available.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            trayIcon?.ShowBalloonTip(3000, "DR Bahig Books Portal",
                "Error starting agent: " + ex.Message, ToolTipIcon.Error);
        }
    }

    internal static void StopAgent()
    {
        foreach (var p in Process.GetProcessesByName("BookShopPrintAgent"))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
        agentRunning = false;
        UpdateMenu();
    }

    private static void UpdateMenu()
    {
        if (trayMenu == null) return;
        trayMenu.Items[2].Enabled = !agentRunning; // Start
        trayMenu.Items[3].Enabled = agentRunning;  // Stop
        trayIcon!.Text = agentRunning ? "DR Bahig Books Portal — Agent Running" : "DR Bahig Books Portal — Agent Stopped";
    }

    private static void ExitApp()
    {
        trayIcon?.Visible = false;
        trayIcon?.Dispose();
        Application.Exit();
    }

    public static bool IsAgentRunning() => agentRunning;
    public static string? AgentExePath => agentExe;
}

public class DashboardForm : Form
{
    private Label statusLabel;
    private Button startBtn, stopBtn;
    private ListBox printerList;
    private System.Windows.Forms.Timer refreshTimer;

    public DashboardForm()
    {
        Text = "DR Bahig Books Print Agent";
        ClientSize = new Size(420, 380);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 34, 50);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var title = new Label
        {
            Text = "DR Bahig Books Print Agent",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(16, 12),
            AutoSize = true,
            ForeColor = Color.White
        };

        statusLabel = new Label
        {
            Text = "Status: Checking...",
            Location = new Point(16, 44),
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 180, 200)
        };

        startBtn = new Button
        {
            Text = "Start Agent",
            Location = new Point(16, 72),
            Size = new Size(110, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = Color.FromArgb(16, 185, 129) }
        };
        startBtn.Click += async (_, _) => await Program.StartAgentAsync();

        stopBtn = new Button
        {
            Text = "Stop Agent",
            Location = new Point(136, 72),
            Size = new Size(110, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 38, 38),
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = Color.FromArgb(220, 38, 38) }
        };
        stopBtn.Click += (_, _) => Program.StopAgent();

        var printerTitle = new Label
        {
            Text = "Detected Printers",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Location = new Point(16, 114),
            AutoSize = true,
            ForeColor = Color.FromArgb(160, 160, 180)
        };

        printerList = new ListBox
        {
            Location = new Point(16, 136),
            Size = new Size(388, 200),
            BackColor = Color.FromArgb(22, 25, 38),
            ForeColor = Color.FromArgb(200, 200, 215),
            BorderStyle = BorderStyle.None
        };

        Controls.AddRange([title, statusLabel, startBtn, stopBtn, printerTitle, printerList]);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = RefreshStatusAsync();
        refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        refreshTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        refreshTimer?.Stop();
    }

    public async Task RefreshStatusAsync()
    {
        var running = Program.IsAgentRunning();
        statusLabel.Text = running ? "Status: Running" : "Status: Stopped";
        statusLabel.ForeColor = running ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
        startBtn.Enabled = !running;
        stopBtn.Enabled = running;

        if (running)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = await client.GetStringAsync("http://127.0.0.1:8080/api/print-job/printers");
                var data = JsonSerializer.Deserialize<PrinterResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                printerList.Items.Clear();
                if (data?.Printers != null)
                {
                    foreach (var p in data.Printers)
                        printerList.Items.Add(p.Name + "  (" + p.ConnectionType + ")");
                }
            }
            catch { printerList.Items.Clear(); printerList.Items.Add("Could not fetch printer list."); }
        }
        else
        {
            printerList.Items.Clear();
            printerList.Items.Add("Agent is not running.");
        }
    }
}

public class PrinterResponse
{
    [JsonPropertyName("printers")]
    public List<PrinterItem>? Printers { get; set; }
}

public class PrinterItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("connectionType")]
    public string ConnectionType { get; set; } = "";
}
