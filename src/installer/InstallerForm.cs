using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace InertialMouseInstaller;

internal sealed class InstallerForm : Form
{
    private readonly string workspaceRoot;
    private readonly Panel pageHost;
    private readonly Label pageTitle;
    private readonly Label pageSubtitle;
    private readonly Button backButton;
    private readonly Button nextButton;
    private readonly Button cancelButton;
    private readonly LinkLabel logLink;
    private readonly StringBuilder fullLog = new();

    private ComboBox? mouseSelector;
    private RadioButton? removeOnlyOption;
    private RadioButton? removeAndDisableTestModeOption;
    private Label? progressStatusLabel;
    private ProgressBar? progressBar;
    private Label? finishTitleLabel;
    private Label? finishDetailsLabel;

    private readonly List<MouseDevice> mice = [];
    private InstallerState installerState = new();
    private WizardPage currentPage = WizardPage.Checking;
    private bool isBusy;
    private bool operationSucceeded;
    private string finalMessage = "";

    public InstallerForm(string workspaceRoot)
    {
        this.workspaceRoot = workspaceRoot;

        Text = "Inertial Mouse Filter Driver Setup";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(780, 560);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9F);
        MaximizeBox = false;

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 98,
            BackColor = Color.White
        };

        pageTitle = new Label
        {
            Text = "Welcome",
            Left = 28,
            Top = 18,
            Width = 620,
            Height = 30,
            ForeColor = Color.FromArgb(20, 29, 43),
            Font = new Font("Segoe UI Semibold", 15.5F)
        };

        pageSubtitle = new Label
        {
            Text = "This wizard will install the mouse filter step by step.",
            Left = 30,
            Top = 52,
            Width = 640,
            Height = 24,
            ForeColor = Color.FromArgb(86, 98, 115)
        };

        topPanel.Controls.Add(pageTitle);
        topPanel.Controls.Add(pageSubtitle);

        pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(30, 18, 30, 18)
        };

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            BackColor = Color.FromArgb(244, 246, 249)
        };

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(218, 224, 232)
        };
        bottomPanel.Controls.Add(separator);

        backButton = CreateWizardButton("< Back");
        nextButton = CreateWizardButton("Next >");
        cancelButton = CreateWizardButton("Cancel");
        logLink = new LinkLabel
        {
            Text = "View log",
            AutoSize = true,
            Left = 28,
            Top = 29,
            LinkColor = Color.FromArgb(34, 103, 207),
            Visible = false
        };

        backButton.Click += (_, _) => GoBack();
        nextButton.Click += async (_, _) => await GoNextAsync();
        cancelButton.Click += (_, _) => Close();
        logLink.Click += (_, _) => OpenFullLog();

        bottomPanel.Controls.Add(logLink);
        bottomPanel.Controls.Add(backButton);
        bottomPanel.Controls.Add(nextButton);
        bottomPanel.Controls.Add(cancelButton);
        bottomPanel.Resize += (_, _) => LayoutBottomButtons(bottomPanel);

        Controls.Add(pageHost);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);
        ShowPage(WizardPage.Checking);

        Shown += async (_, _) =>
        {
            AppendLog("Starting setup wizard.");
            ShowPage(WizardPage.Checking);
            await RefreshStateAsync();
            ShowPage(WizardPage.Welcome);
        };
    }

    private static Button CreateWizardButton(string text)
    {
        return new Button
        {
            Text = text,
            Width = 104,
            Height = 32,
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true
        };
    }

    private void LayoutBottomButtons(Control parent)
    {
        const int gap = 8;
        cancelButton.Left = parent.ClientSize.Width - cancelButton.Width - 24;
        nextButton.Left = cancelButton.Left - nextButton.Width - gap;
        backButton.Left = nextButton.Left - backButton.Width - gap;

        backButton.Top = nextButton.Top = cancelButton.Top = 22;
    }

    private async Task RefreshStateAsync()
    {
        await SetBusyAsync("Checking the system...", async () =>
        {
            installerState = await QueryInstallerStateAsync();
            mice.Clear();
            mice.AddRange(await QueryMiceAsync());
        });
    }

    private void ShowPage(WizardPage page)
    {
        currentPage = page;
        pageHost.Controls.Clear();

        switch (page)
        {
            case WizardPage.Checking:
                BuildCheckingPage();
                break;
            case WizardPage.Welcome:
                BuildWelcomePage();
                break;
            case WizardPage.SelectMouse:
                BuildSelectMousePage();
                break;
            case WizardPage.Ready:
                BuildReadyPage();
                break;
            case WizardPage.Progress:
                BuildProgressPage();
                break;
            case WizardPage.Finish:
                BuildFinishPage();
                break;
        }

        ApplyPageMargins();
        UpdateNavigation();
    }

    private void ApplyPageMargins()
    {
        foreach (Control control in pageHost.Controls)
        {
            control.Left += pageHost.Padding.Left;
            control.Top += pageHost.Padding.Top;

            if ((control.Anchor & AnchorStyles.Right) == AnchorStyles.Right)
            {
                control.Width = Math.Max(1, pageHost.ClientSize.Width - control.Left - pageHost.Padding.Right);
            }
        }
    }

    private void BuildCheckingPage()
    {
        pageTitle.Text = "Checking setup";
        pageSubtitle.Text = "Detecting the driver state and connected HID mice.";

        progressStatusLabel = new Label
        {
            Text = "Checking the system...",
            Left = 0,
            Top = 12,
            Width = 660,
            Height = 24,
            ForeColor = Color.FromArgb(42, 54, 72)
        };

        progressBar = new ProgressBar
        {
            Left = 0,
            Top = 48,
            Width = 660,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        pageHost.Controls.Add(progressStatusLabel);
        pageHost.Controls.Add(progressBar);
        pageHost.Controls.Add(CreateMutedText("This can take a few seconds while Windows reports installed drivers and connected mouse devices.", 0, 92, 660));
    }

    private void BuildWelcomePage()
    {
        pageTitle.Text = installerState.IsInstalled
            ? "Modify installation"
            : "Welcome to the setup wizard";
        pageSubtitle.Text = installerState.IsInstalled
            ? "The filter is already installed on this computer."
            : "This wizard will install the inertial filter for a HID mouse.";

        var body = CreateBodyText(
            installerState.IsInstalled
                ? "You can remove the installed filter. If you also want to disable Windows test-signing, choose that option in the next step."
                : "The wizard will detect connected mice, prepare the driver for the selected device, and install the filter. You will need to restart Windows when it finishes.");

        var status = CreateStatusBox();
        status.Top = 122;

        pageHost.Controls.Add(body);
        pageHost.Controls.Add(status);
    }

    private void BuildSelectMousePage()
    {
        pageTitle.Text = "Select mouse";
        pageSubtitle.Text = "Choose the device that will receive the filter.";

        var label = new Label
        {
            Text = "Available mouse:",
            Left = 0,
            Top = 8,
            Width = 500,
            Height = 22
        };

        mouseSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Left = 0,
            Top = 36,
            Width = 660,
            Height = 28,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        mouseSelector.SelectedIndexChanged += (_, _) => UpdateNavigation();

        foreach (var mouse in mice)
        {
            mouseSelector.Items.Add(mouse);
        }

        if (mouseSelector.Items.Count > 0)
        {
            mouseSelector.SelectedIndex = 0;
        }

        var recommendation = CreateRecommendationBox(GetMouseRecommendationText(), 0, 82, 660);

        var refresh = new Button
        {
            Text = "Refresh list",
            Left = 0,
            Top = 154,
            Width = 130,
            Height = 32,
            FlatStyle = FlatStyle.System
        };
        refresh.Click += async (_, _) =>
        {
            await RefreshStateAsync();
            ShowPage(WizardPage.SelectMouse);
        };

        var hint = CreateMutedText(
            "If several devices look the same, leave the preselected option unless you know the exact hardware ID. Keep a keyboard or second mouse available when testing kernel drivers.",
            0,
            208,
            660);

        pageHost.Controls.Add(label);
        pageHost.Controls.Add(mouseSelector);
        pageHost.Controls.Add(recommendation);
        pageHost.Controls.Add(refresh);
        pageHost.Controls.Add(hint);
    }

    private string GetMouseRecommendationText()
    {
        var recommended = mice.FirstOrDefault(mouse => mouse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
            ?? mice.FirstOrDefault();

        if (recommended is null)
        {
            return "Recommendation: connect a HID mouse, then click Refresh list.";
        }

        var name = string.IsNullOrWhiteSpace(recommended.FriendlyName) ? "HID mouse" : recommended.FriendlyName;
        return $"Recommendation: keep the preselected Active mouse unless you are targeting a different device.\nSelected target: {name}";
    }

    private void BuildReadyPage()
    {
        var installed = installerState.IsInstalled;
        pageTitle.Text = installed ? "Ready to uninstall" : "Ready to install";
        pageSubtitle.Text = installed
            ? "Confirm how you want to remove the filter."
            : "Confirm the selected mouse before continuing.";

        if (installed)
        {
            removeOnlyOption = new RadioButton
            {
                Text = "Uninstall the driver",
                Left = 0,
                Top = 8,
                Width = 520,
                Checked = true
            };
            removeAndDisableTestModeOption = new RadioButton
            {
                Text = "Uninstall the driver and disable test-signing",
                Left = 0,
                Top = 38,
                Width = 520
            };
            pageHost.Controls.Add(removeOnlyOption);
            pageHost.Controls.Add(removeAndDisableTestModeOption);
            pageHost.Controls.Add(CreateMutedText("Disabling test-signing requires restarting Windows afterward.", 0, 82, 660));
            return;
        }

        var selectedMouse = mouseSelector?.SelectedItem as MouseDevice;
        var summary = new Label
        {
            Text = $"The filter will be installed for:\n\n{selectedMouse?.FriendlyName ?? "HID mouse"}\n{selectedMouse?.HardwareId ?? ""}",
            Left = 0,
            Top = 6,
            Width = 680,
            Height = 108,
            ForeColor = Color.FromArgb(32, 43, 59)
        };

        pageHost.Controls.Add(summary);
        pageHost.Controls.Add(CreateMutedText("The wizard will enable test-signing if needed, install the driver, and add a normal Windows uninstall entry.", 0, 132, 660));
    }

    private void BuildProgressPage()
    {
        pageTitle.Text = installerState.IsInstalled ? "Uninstalling" : "Installing";
        pageSubtitle.Text = "Please wait while the wizard completes the operation.";

        progressStatusLabel = new Label
        {
            Text = "Preparing...",
            Left = 0,
            Top = 12,
            Width = 660,
            Height = 24,
            ForeColor = Color.FromArgb(42, 54, 72)
        };

        progressBar = new ProgressBar
        {
            Left = 0,
            Top = 48,
            Width = 660,
            Height = 18,
            Style = ProgressBarStyle.Marquee,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        pageHost.Controls.Add(progressStatusLabel);
        pageHost.Controls.Add(progressBar);
        pageHost.Controls.Add(CreateMutedText("Do not close this window while Windows installs or removes the driver.", 0, 92, 660));
    }

    private void BuildFinishPage()
    {
        pageTitle.Text = operationSucceeded ? "Setup completed" : "Setup could not complete";
        pageSubtitle.Text = operationSucceeded
            ? "The operation completed successfully."
            : "The operation completed with errors.";

        finishTitleLabel = new Label
        {
            Text = operationSucceeded ? "Restart Windows to apply the changes." : "Open the log to see the details.",
            Left = 0,
            Top = 10,
            Width = 660,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = operationSucceeded ? Color.FromArgb(30, 112, 78) : Color.FromArgb(174, 57, 47)
        };

        finishDetailsLabel = CreateMutedText(finalMessage, 0, 52, 660);

        pageHost.Controls.Add(finishTitleLabel);
        pageHost.Controls.Add(finishDetailsLabel);
    }

    private Label CreateStatusBox()
    {
        var installed = installerState.IsInstalled ? "Installed" : "Not installed";
        var testMode = installerState.TestSigning ? "Test-signing enabled" : "Test-signing disabled";

        return new Label
        {
            Text = $"Current status: {installed}\n{testMode}",
            Left = 0,
            Width = 660,
            Height = 66,
            Padding = new Padding(14, 11, 14, 11),
            BackColor = Color.FromArgb(244, 246, 249),
            ForeColor = Color.FromArgb(38, 49, 64)
        };
    }

    private static Label CreateBodyText(string text)
    {
        return CreateMutedText(text, 0, 10, 660, 96);
    }

    private static Label CreateMutedText(string text, int left, int top, int width, int height = 60)
    {
        return new Label
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ForeColor = Color.FromArgb(78, 90, 106)
        };
    }

    private static Label CreateRecommendationBox(string text, int left, int top, int width)
    {
        return new Label
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = 58,
            Padding = new Padding(12, 9, 12, 9),
            BackColor = Color.FromArgb(236, 244, 255),
            ForeColor = Color.FromArgb(30, 70, 128)
        };
    }

    private async Task GoNextAsync()
    {
        if (isBusy)
        {
            return;
        }

        switch (currentPage)
        {
            case WizardPage.Welcome:
                ShowPage(installerState.IsInstalled ? WizardPage.Ready : WizardPage.SelectMouse);
                break;
            case WizardPage.SelectMouse:
                ShowPage(WizardPage.Ready);
                break;
            case WizardPage.Ready:
                ShowPage(WizardPage.Progress);
                await ExecuteOperationAsync();
                break;
            case WizardPage.Finish:
                Close();
                break;
        }
    }

    private void GoBack()
    {
        if (isBusy)
        {
            return;
        }

        switch (currentPage)
        {
            case WizardPage.SelectMouse:
                ShowPage(WizardPage.Welcome);
                break;
            case WizardPage.Ready:
                ShowPage(installerState.IsInstalled ? WizardPage.Welcome : WizardPage.SelectMouse);
                break;
        }
    }

    private async Task ExecuteOperationAsync()
    {
        SetBusy(true, installerState.IsInstalled ? "Uninstalling..." : "Installing...");
        logLink.Visible = true;
        operationSucceeded = false;

        try
        {
            int exitCode;
            if (installerState.IsInstalled)
            {
                var disableTestMode = removeAndDisableTestModeOption?.Checked == true;
                exitCode = disableTestMode
                    ? await RunScriptAsync("uninstall-inertialmouse.ps1", "-DisableTestSigning")
                    : await RunScriptAsync("uninstall-inertialmouse.ps1");

                if (exitCode == 0)
                {
                    Program.UnregisterWindowsUninstaller();
                }
            }
            else
            {
                var selectedMouse = mouseSelector?.SelectedItem as MouseDevice;
                if (selectedMouse is null)
                {
                    throw new InvalidOperationException("Select a mouse before installing.");
                }

                exitCode = await RunScriptAsync(
                    "install-inertialmouse.ps1",
                    "-HardwareId",
                    selectedMouse.HardwareId,
                    "-EnableTestSigning");

                if (exitCode == 0)
                {
                    Program.RegisterWindowsUninstaller();
                }
            }

            operationSucceeded = exitCode == 0;
            finalMessage = operationSucceeded
                ? "The operation completed. Restart Windows before testing the mouse."
                : $"The operation failed with exit code {exitCode}. Use View log to inspect the details.";

            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            finalMessage = ex.Message;
        }
        finally
        {
            SetBusy(false, "Ready");
            ShowPage(WizardPage.Finish);
        }
    }

    private void UpdateNavigation()
    {
        backButton.Enabled = !isBusy && currentPage is WizardPage.SelectMouse or WizardPage.Ready;
        cancelButton.Enabled = !isBusy && currentPage != WizardPage.Finish;

        nextButton.Enabled = !isBusy;
        nextButton.Text = currentPage switch
        {
            WizardPage.Ready => installerState.IsInstalled ? "Uninstall" : "Install",
            WizardPage.Progress => "Please wait...",
            WizardPage.Finish => "Finish",
            _ => "Next >"
        };

        if (currentPage == WizardPage.SelectMouse)
        {
            nextButton.Enabled = !isBusy && mouseSelector?.SelectedItem is MouseDevice;
        }

        if (currentPage is WizardPage.Checking or WizardPage.Progress)
        {
            backButton.Enabled = false;
            nextButton.Enabled = false;
            cancelButton.Enabled = false;
        }
    }

    private void SetBusy(bool busy, string status)
    {
        isBusy = busy;
        if (progressStatusLabel is not null)
        {
            progressStatusLabel.Text = status;
        }
        UpdateNavigation();
    }

    private async Task SetBusyAsync(string status, Func<Task> action)
    {
        SetBusy(true, status);
        try
        {
            await action();
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private async Task<List<MouseDevice>> QueryMiceAsync()
    {
        var script = """
            $items = Get-PnpDevice -Class Mouse | ForEach-Object {
                $device = $_
                $property = Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_HardwareIds' -ErrorAction SilentlyContinue
                foreach ($id in @($property.Data)) {
                    if ($id -like 'HID\VID_*&PID_*') {
                        [PSCustomObject]@{
                            Status = [string]$device.Status
                            FriendlyName = [string]$device.FriendlyName
                            HardwareId = [string]$id
                            InstanceId = [string]$device.InstanceId
                        }
                    }
                }
            }
            @($items) | ConvertTo-Json -Depth 3
            """;

        var result = await RunPowerShellCommandAsync(script);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            AppendLog("No HID mice were found.");
            return [];
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var detected = JsonSerializer.Deserialize<List<MouseDevice>>(result.Output, options) ?? [];

        return detected
            .Where(mouse => !string.IsNullOrWhiteSpace(mouse.HardwareId))
            .GroupBy(mouse => mouse.HardwareId + "|" + mouse.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(mouse => mouse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
            .ThenBy(mouse => mouse.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mouse => mouse.HardwareId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<InstallerState> QueryInstallerStateAsync()
    {
        var script = """
            $drivers = pnputil /enum-drivers | Out-String
            $service = Get-Service inertialmouse -ErrorAction SilentlyContinue
            $uninstall = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\InertialMouseFilter'
            $boot = bcdedit /enum "{current}" 2>$null | Out-String
            [PSCustomObject]@{
                DriverPackage = ($drivers -match 'inertialmouse\.inf')
                Service = ($null -ne $service)
                UninstallEntry = $uninstall
                TestSigning = ($boot -match '(?im)^\s*testsigning\s+Yes\s*$')
            } | ConvertTo-Json -Compress
            """;

        var result = await RunPowerShellCommandAsync(script);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            AppendLog("Could not determine the installation state.");
            return new InstallerState();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<InstallerState>(result.Output, options) ?? new InstallerState();
    }

    private async Task<int> RunScriptAsync(string scriptName, params string[] scriptArgs)
    {
        var scriptPath = Path.Combine(workspaceRoot, "scripts", scriptName);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Script was not found: {scriptPath}");
        }

        var args = new List<string>
        {
            "-NoProfile",
            "-WindowStyle",
            "Hidden",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        };
        args.AddRange(scriptArgs);

        var result = await RunProcessAsync(GetPowerShellPath(), workspaceRoot, args);
        AppendLog(result.Output);
        AppendLog(result.Error);
        return result.ExitCode;
    }

    private async Task<ProcessResult> RunPowerShellCommandAsync(string command)
    {
        var args = new[] { "-NoProfile", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-Command", command };
        return await RunProcessAsync(GetPowerShellPath(), workspaceRoot, args);
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string workingDirectory, IEnumerable<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        foreach (var line in message.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            fullLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line.Trim()}");
        }
    }

    private void OpenFullLog()
    {
        var path = Path.Combine(Path.GetTempPath(), "InertialMouseInstaller.log");
        File.WriteAllText(path, fullLog.ToString());
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static string GetPowerShellPath()
    {
        var powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return File.Exists(powerShell) ? powerShell : "powershell.exe";
    }

    private enum WizardPage
    {
        Checking,
        Welcome,
        SelectMouse,
        Ready,
        Progress,
        Finish
    }

    private sealed class MouseDevice
    {
        public string Status { get; init; } = "";
        public string FriendlyName { get; init; } = "";
        public string HardwareId { get; init; } = "";
        public string InstanceId { get; init; } = "";

        public override string ToString()
        {
            var status = Status.Equals("OK", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
            var name = string.IsNullOrWhiteSpace(FriendlyName) ? "HID mouse" : FriendlyName;
            return $"{status} - {name} - {HardwareId}";
        }
    }

    private sealed class InstallerState
    {
        public bool DriverPackage { get; init; }
        public bool Service { get; init; }
        public bool UninstallEntry { get; init; }
        public bool TestSigning { get; init; }
        public bool IsInstalled => DriverPackage || Service || UninstallEntry;
    }

    private readonly record struct ProcessResult(int ExitCode, string Output, string Error);
}
