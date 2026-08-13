using System.Text.Json;

namespace BookShopAgentUI;

public class SetupForm : Form
{
    private TextBox apiKeyTextBox;
    private TextBox serverUrlTextBox;
    private Button saveButton;
    private Button testButton;
    private Label statusLabel;
    private string configPath;

    public SetupForm()
    {
        InitializeComponent();
        configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
                                  "BookShopPrintAgent", "appsettings.json");
        LoadCurrentConfig();
    }

    private void InitializeComponent()
    {
        Text = "BookShop Print Agent - Setup";
        ClientSize = new Size(560, 340);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 34, 50);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var titleLabel = new Label
        {
            Text = "BookShop Print Agent Setup",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Location = new Point(20, 20),
            AutoSize = true,
            ForeColor = Color.White
        };

        var descLabel = new Label
        {
            Text = "Configure your shop's connection to the DR Bahig Books Portal",
            Location = new Point(20, 50),
            Size = new Size(520, 20),
            ForeColor = Color.FromArgb(180, 180, 200)
        };

        var serverLabel = new Label
        {
            Text = "Portal Server URL:",
            Location = new Point(20, 90),
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 200)
        };

        serverUrlTextBox = new TextBox
        {
            Location = new Point(20, 115),
            Size = new Size(520, 25),
            BackColor = Color.FromArgb(45, 50, 65),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "https://books-portal.thenurdz.online"
        };

        var apiKeyLabel = new Label
        {
            Text = "Shop API Key (provided by your teacher):",
            Location = new Point(20, 150),
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 200)
        };

        apiKeyTextBox = new TextBox
        {
            Location = new Point(20, 175),
            Size = new Size(520, 25),
            BackColor = Color.FromArgb(45, 50, 65),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Enter YOUR shop's API key (e.g., bpk_...)"
        };

        var keyHintLabel = new Label
        {
            Text = "Each bookshop has its own key. With it, the portal shows only the printers detected on THIS computer.",
            Location = new Point(20, 202),
            Size = new Size(520, 16),
            ForeColor = Color.FromArgb(140, 142, 155),
            Font = new Font("Segoe UI", 8)
        };

        testButton = new Button
        {
            Text = "Test Connection",
            Location = new Point(20, 228),
            Size = new Size(120, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = Color.FromArgb(59, 130, 246) }
        };
        testButton.Click += TestConnection_Click;

        saveButton = new Button
        {
            Text = "Save Configuration",
            Location = new Point(150, 228),
            Size = new Size(140, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = Color.FromArgb(16, 185, 129) }
        };
        saveButton.Click += SaveButton_Click;

        statusLabel = new Label
        {
            Text = "",
            Location = new Point(20, 268),
            Size = new Size(520, 20),
            ForeColor = Color.FromArgb(180, 180, 200)
        };

        Controls.AddRange(new Control[] { 
            titleLabel, descLabel, serverLabel, serverUrlTextBox, 
            apiKeyLabel, apiKeyTextBox, keyHintLabel, testButton, saveButton, statusLabel 
        });
    }

    private void LoadCurrentConfig()
    {
        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (config.TryGetProperty("ServerSettings", out var serverSettings))
                {
                    if (serverSettings.TryGetProperty("BaseUrl", out var baseUrl))
                        serverUrlTextBox.Text = baseUrl.GetString() ?? "";
                    
                    if (serverSettings.TryGetProperty("ApiKey", out var apiKey))
                        apiKeyTextBox.Text = apiKey.GetString() ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Could not load current config: {ex.Message}";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
        }
    }

    private async void TestConnection_Click(object sender, EventArgs e)
    {
        var trimmedKey = apiKeyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            statusLabel.Text = "Please enter an API key first.";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            return;
        }

        if (!trimmedKey.StartsWith("bpk_", StringComparison.OrdinalIgnoreCase))
        {
            statusLabel.Text = "This key does not look valid — shop API keys start with 'bpk_'.";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            return;
        }

        testButton.Enabled = false;
        statusLabel.Text = "Testing connection...";
        statusLabel.ForeColor = Color.FromArgb(180, 180, 200);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("X-Api-Key", trimmedKey);
            
            var response = await client.PostAsync($"{serverUrlTextBox.Text.TrimEnd('/')}/api/pdf/print-agent/test", null);
            
            if (response.IsSuccessStatusCode)
            {
                statusLabel.Text = "✓ Connection successful! Your shop's API key is valid.";
                statusLabel.ForeColor = Color.FromArgb(16, 185, 129);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                statusLabel.Text = "✗ Invalid API key. Please check with your teacher.";
                statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            }
            else
            {
                statusLabel.Text = $"✗ Server error: {response.StatusCode}";
                statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"✗ Connection failed: {ex.Message}";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
        }
        finally
        {
            testButton.Enabled = true;
        }
    }

    private void SaveButton_Click(object sender, EventArgs e)
    {
        var trimmedKey = apiKeyTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            statusLabel.Text = "Please enter an API key.";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            return;
        }

        if (!trimmedKey.StartsWith("bpk_", StringComparison.OrdinalIgnoreCase))
        {
            statusLabel.Text = "This key does not look valid — shop API keys start with 'bpk_'.";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            return;
        }

        if (string.IsNullOrWhiteSpace(serverUrlTextBox.Text))
        {
            statusLabel.Text = "Please enter a server URL.";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
            return;
        }

        try
        {
            var config = new
            {
                ServerSettings = new
                {
                    BaseUrl = serverUrlTextBox.Text.Trim(),
                    ApiKey = trimmedKey,
                    OwnerPassword = "P8mKx9#jL2vR$5nWq7cY",
                    UseSignalR = true
                },
                PrinterSettings = new
                {
                    DefaultPrinterName = "",
                    Copies = 1
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);

            statusLabel.Text = "✓ Configuration saved successfully!";
            statusLabel.ForeColor = Color.FromArgb(16, 185, 129);

            // Apply immediately: restart the agent so it starts sending this shop's printers.
            Program.RestartAgentAfterConfigChange();

            // Close the form after a brief delay
            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, args) => { timer.Stop(); Close(); };
            timer.Start();
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Error saving configuration: {ex.Message}";
            statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
        }
    }
}