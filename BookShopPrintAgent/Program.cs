using BookShopPrintAgent.Controllers;
using BookShopPrintAgent.Services;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

// Set up file logging (use process path for single-file publish)
var agentDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
var logDir = Path.Combine(agentDir, "logs");
Directory.CreateDirectory(logDir);
var logFile = Path.Combine(logDir, $"agent_{DateTime.Now:yyyyMMdd}.log");
void Log(string msg)
{
    var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
    Console.WriteLine(line);
    try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
}

// Single-instance guard
using var mutex = new Mutex(true, "BookShopPrintAgent", out var isFirst);
if (!isFirst)
{
    Console.WriteLine("[BookShopPrintAgent] Already running. Exiting.");
    return;
}

// Force-kill any process holding port 8080 (even SYSTEM-level from scheduled task)
try
{
    var pid = Environment.ProcessId;
    var psi = new ProcessStartInfo
    {
        FileName = "powershell",
        Arguments = $"-NoProfile -Command \"$p=Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; if ($p -and $p -ne {pid}) {{ Stop-Process -Id $p -Force; Write-Host ('Freed port 8080 from PID '+$p) }}\"",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var p = Process.Start(psi);
    var outText = p?.StandardOutput.ReadToEnd();
    p?.WaitForExit(5000);
    if (!string.IsNullOrWhiteSpace(outText))
        Console.WriteLine("[BookShopPrintAgent] " + outText.Trim());
}
catch (Exception ex)
{
    Console.WriteLine($"[BookShopPrintAgent] Port cleanup: {ex.Message}");
}
Thread.Sleep(1000);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(8080);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<PdfPrintService>();
builder.Services.AddControllers();

var app = builder.Build();

// Chrome/Edge Private Network Access: a public site calling http://localhost
// requires the preflight to opt in via Access-Control-Allow-Private-Network.
// Must run BEFORE UseCors (CORS short-circuits preflights without invoking the next middleware).
app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method) &&
        context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        context.Response.Headers.Append("Access-Control-Allow-Private-Network", "true");
    }
    await next();
});

app.UseCors();
app.MapControllers();

var baseUrl = app.Configuration.GetValue<string>("ServerSettings:BaseUrl") ?? "https://drbaheegbook.runasp.net";
var apiKey = app.Configuration.GetValue<string>("ServerSettings:ApiKey") ?? "";
var defaultPrinter = app.Configuration.GetValue<string>("PrinterSettings:DefaultPrinterName") ?? "";
Console.WriteLine($"[BookShopPrintAgent] Listening on http://localhost:8080");
Console.WriteLine($"[BookShopPrintAgent] Server: {baseUrl}");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine($"[BookShopPrintAgent] NO API KEY CONFIGURED — printers will NOT be sent and jobs will NOT be received.");
    Console.WriteLine($"[BookShopPrintAgent] Ask your teacher for your SHOP's API key, then open the tray icon → 'Setup Configuration' and enter it.");
}
else if (!apiKey.StartsWith("bpk_", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"[BookShopPrintAgent] WARNING: API key does not look valid (must start with 'bpk_'). Check it in Setup Configuration.");
    Console.WriteLine($"[BookShopPrintAgent] API Key: {apiKey[..Math.Min(12, apiKey.Length)]}...");
}
else
{
    Console.WriteLine($"[BookShopPrintAgent] API Key: {apiKey[..12]}...");
}
Console.WriteLine($"[BookShopPrintAgent] Agent dir: {agentDir}");
Console.WriteLine($"[BookShopPrintAgent] Polling for jobs every 3 seconds...");

_ = Task.Run(async () =>
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    var printService = app.Services.GetRequiredService<PdfPrintService>();
    // JobIds rejected because the requested printer is not installed on this machine —
    // skipped for 6 minutes to avoid claiming + releasing them every 3 seconds.
    var printerGuard = new Dictionary<string, DateTime>();

    while (true)
    {
        try
        {
            var pendingResponse = await client.GetAsync($"{baseUrl}/api/pdf/print-agent/pending");
            if (pendingResponse.IsSuccessStatusCode)
            {
                var json = await pendingResponse.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PendingResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (result?.Jobs != null)
                {
                    foreach (var jobId in result.Jobs)
                {
                    if (printerGuard.TryGetValue(jobId, out var skipUntil) && DateTime.UtcNow < skipUntil)
                        continue;

                    Log($"Found pending job: {jobId}");

                        var claimResponse = await client.PostAsync($"{baseUrl}/api/pdf/print-agent/claim/{jobId}", null);
                        if (!claimResponse.IsSuccessStatusCode)
                        {
                            Log($"Claim failed for {jobId}, skipping");
                            continue;
                        }

                        var claimJson = await claimResponse.Content.ReadAsStringAsync();
                        var claimResult = JsonSerializer.Deserialize<ClaimResponse>(claimJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        var copies = claimResult?.Copies ?? 1;
                        var jobPrinter = claimResult?.PrinterName;
                        var effectivePrinter = !string.IsNullOrWhiteSpace(jobPrinter) ? jobPrinter : defaultPrinter;

                        // Accuracy guard: never print to a different printer than requested.
                        // If the claimed printer is not installed on this machine, leave the
                        // job in the queue (bypass it for a while) so it is not silently
                        // printed on the wrong printer or retried every 3 seconds.
                        if (!string.IsNullOrWhiteSpace(effectivePrinter) && !PrinterGuard.IsInstalled(effectivePrinter))
                        {
                            Log($"Job {jobId}: requested printer '{effectivePrinter}' is not installed on this machine — leaving job in queue.");
                            printerGuard[jobId] = DateTime.UtcNow.AddMinutes(6);
                            try { await client.PostAsync($"{baseUrl}/api/pdf/print-agent/release/{jobId}", null); } catch { }
                            continue;
                        }

                        var settings = new PrintSettings
                        {
                            PrinterName = effectivePrinter,
                            Copies = copies,
                            ScalingMode = claimResult?.ScalingMode ?? "actual",
                            CustomScale = claimResult?.CustomScale ?? 100,
                            Duplex = claimResult?.Duplex ?? "off",
                            PaperSize = claimResult?.PaperSize ?? "A4",
                            MarginUnit = claimResult?.MarginUnit ?? "mm",
                            MarginTop = claimResult?.MarginTop ?? 0,
                            MarginBottom = claimResult?.MarginBottom ?? 0,
                            MarginLeft = claimResult?.MarginLeft ?? 0,
                            MarginRight = claimResult?.MarginRight ?? 0
                        };

                        Log($"Printing job {jobId}, {copies} copy(ies), printer: {settings.PrinterName}, scaling: {settings.ScalingMode}");

                        try
                        {
                            await printService.DownloadAndPrintAsync(jobId, settings);
                            Log($"Job {jobId} completed successfully");
                        }
                        catch (Exception ex)
                        {
                            Log($"Job {jobId} FAILED: {ex.Message}");
                            // Release job back to pending queue so it can be retried
                            try
                            {
                                var release = await client.PostAsync($"{baseUrl}/api/pdf/print-agent/release/{jobId}", null);
                                Log($"Released job {jobId} back to queue");
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Polling error: {ex.Message}");
        }

    // Heartbeat: send printer list to server so the website can show agent status.
    // Skipped when no API key is configured — the server would reject it anyway.
    try
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            using var localClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var printersResponse = await localClient.GetAsync("http://127.0.0.1:8080/api/print-job/printers");
            if (printersResponse.IsSuccessStatusCode)
            {
                var printersJson = await printersResponse.Content.ReadAsStringAsync();
                using var content = new StringContent(printersJson, System.Text.Encoding.UTF8, "application/json");

                // Send heartbeat (for backward compatibility)
                await client.PostAsync($"{baseUrl}/api/pdf/print-agent/heartbeat", content);

                // Register printers in database
                await client.PostAsync($"{baseUrl}/api/pdf/print-agent/register-printers", content);
            }
        }
    }
    catch (Exception ex)
    {
        Log($"Heartbeat failed: {ex.Message}");
    }

        await Task.Delay(3000);
    }
});

app.Run();

public class PendingResponse
{
    public List<string> Jobs { get; set; } = new();
}

public class ClaimResponse
{
    public bool Success { get; set; }
    public string JobId { get; set; } = "";
    public int Copies { get; set; } = 1;
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Duplex { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}

public static class PrinterGuard
{
    public static bool IsInstalled(string printerName)
    {
        try
        {
            foreach (string installed in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                if (string.Equals(installed, printerName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // If enumeration fails, allow the attempt — SumatraPDF will surface the error.
            return true;
        }
        return false;
    }
}
