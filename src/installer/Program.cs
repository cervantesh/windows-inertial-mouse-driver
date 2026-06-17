using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;
using System.Windows.Forms;

namespace InertialMouseInstaller;

internal static class Program
{
    private const string UninstallRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\InertialMouseFilter";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0)
            {
                var commandRoot = FindWorkspaceRoot() ?? ExtractEmbeddedPayload();
                if (commandRoot is null)
                {
                    Console.Error.WriteLine("Could not find or extract the embedded installer payload.");
                    PauseIfInteractive(args);
                    return 1;
                }

                return RunCommand(commandRoot, args);
            }

            if (!EnsureAdministrator(args))
            {
                return 0;
            }

            ApplicationConfiguration.Initialize();

            string? workspaceRoot;
            using (var startup = new StartupForm())
            {
                startup.Show();
                startup.Refresh();
                Application.DoEvents();

                var workspaceTask = Task.Run(() => FindWorkspaceRoot() ?? ExtractEmbeddedPayload());
                while (!workspaceTask.Wait(50))
                {
                    Application.DoEvents();
                }

                workspaceRoot = workspaceTask.GetAwaiter().GetResult();
                startup.Close();
            }

            if (workspaceRoot is null)
            {
                MessageBox.Show(
                    "Could not find or extract the embedded installer payload.",
                    "Inertial Mouse Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }

            Application.Run(new InstallerForm(workspaceRoot));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Error:");
            Console.Error.WriteLine(ex.Message);
            PauseIfInteractive(args);
            return 1;
        }
    }

    private static int RunCommand(string repoRoot, string[] args)
    {
        var command = args[0].Trim().ToLowerInvariant();

        if (command is "help" or "--help" or "-h" or "/?")
        {
            PrintHelp();
            return 0;
        }

        if (command == "list")
        {
            return RunScript(repoRoot, "install-inertialmouse.ps1", "-ListMice");
        }

        if (!EnsureAdministrator(args))
        {
            return 0;
        }

        return command switch
        {
            "install" => RunInstall(repoRoot),
            "build" => RunScript(repoRoot, "build-inertialmouse.ps1", "-Configuration", "Release"),
            "toolchain" => RunScript(repoRoot, "install-modern-toolchain.ps1", "-IncludeVsCommunity"),
            "uninstall" => RunUninstall(repoRoot, disableTestSigning: false),
            "uninstall-full" => RunUninstall(repoRoot, disableTestSigning: true),
            _ => UnknownCommand(command)
        };
    }

    private static int RunMenu(string repoRoot)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("Inertial Mouse Installer");
            Console.WriteLine("========================");
            Console.WriteLine();
            Console.WriteLine("Working payload:");
            Console.WriteLine(repoRoot);
            Console.WriteLine();
            Console.WriteLine("1. List HID mice");
            Console.WriteLine("2. Install driver");
            Console.WriteLine("3. Build Release package");
            Console.WriteLine("4. Uninstall driver");
            Console.WriteLine("5. Uninstall driver and disable test-signing");
            Console.WriteLine("6. Install or repair toolchain");
            Console.WriteLine("7. Exit");
            Console.WriteLine();
            Console.Write("Select an option: ");

            var option = Console.ReadLine()?.Trim();
            Console.WriteLine();

            var exitCode = option switch
            {
                "1" => RunScript(repoRoot, "install-inertialmouse.ps1", "-ListMice"),
                "2" => RunInstall(repoRoot),
                "3" => RunScript(repoRoot, "build-inertialmouse.ps1", "-Configuration", "Release"),
                "4" => RunUninstall(repoRoot, disableTestSigning: false),
                "5" => RunUninstall(repoRoot, disableTestSigning: true),
                "6" => RunScript(repoRoot, "install-modern-toolchain.ps1", "-IncludeVsCommunity"),
                "7" => 0,
                _ => InvalidMenuOption()
            };

            if (option == "7")
            {
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine($"Command exit code: {exitCode}");
            Console.WriteLine("Press Enter to continue.");
            Console.ReadLine();
        }
    }

    internal static int RunInstall(string repoRoot)
    {
        var exitCode = RunScript(repoRoot, "install-inertialmouse.ps1", "-EnableTestSigning");
        if (exitCode == 0)
        {
            RegisterWindowsUninstaller();
        }

        return exitCode;
    }

    internal static int RunUninstall(string repoRoot, bool disableTestSigning)
    {
        var exitCode = disableTestSigning
            ? RunScript(repoRoot, "uninstall-inertialmouse.ps1", "-DisableTestSigning")
            : RunScript(repoRoot, "uninstall-inertialmouse.ps1");

        if (exitCode == 0)
        {
            UnregisterWindowsUninstaller();
        }

        return exitCode;
    }

    internal static int RunScript(string repoRoot, string scriptName, params string[] scriptArgs)
    {
        var scriptPath = Path.Combine(repoRoot, "scripts", scriptName);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Missing script: {scriptPath}");
        }

        var powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(powerShell))
        {
            powerShell = "powershell.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = powerShell,
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var arg in scriptArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Could not start {scriptName}.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);

        var output = outputTask.Result;
        var error = errorTask.Result;
        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.Write(output);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.Write(error);
        }

        return process.ExitCode;
    }

    internal static void RegisterWindowsUninstaller()
    {
        var currentExe = Environment.GetEnvironmentVariable("IM_BOOTSTRAPPER_PATH");
        if (string.IsNullOrWhiteSpace(currentExe)) {
            currentExe = Environment.ProcessPath;
        }

        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            Console.WriteLine("Skipping Windows uninstaller registration: current executable path is unavailable.");
            return;
        }

        if (Path.GetFileName(currentExe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Skipping Windows uninstaller registration while running from dotnet.");
            return;
        }

        var installDirectory = GetInstalledAppDirectory();
        Directory.CreateDirectory(installDirectory);

        var installedExe = Path.Combine(installDirectory, "InertialMouseInstaller.exe");
        if (!Path.GetFullPath(currentExe).Equals(Path.GetFullPath(installedExe), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(currentExe, installedExe, overwrite: true);
        }

        using var key = Registry.LocalMachine.CreateSubKey(UninstallRegistryPath);
        if (key is null)
        {
            throw new InvalidOperationException("Could not create the Windows uninstall registry key.");
        }

        var quotedExe = QuoteForCommand(installedExe);
        var estimatedSizeKb = new FileInfo(installedExe).Length / 1024;

        key.SetValue("DisplayName", "Inertial Mouse Filter Driver", RegistryValueKind.String);
        key.SetValue("DisplayVersion", "1.0.0", RegistryValueKind.String);
        key.SetValue("Publisher", "CervantesH", RegistryValueKind.String);
        key.SetValue("DisplayIcon", installedExe, RegistryValueKind.String);
        key.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
        key.SetValue("UninstallString", $"{quotedExe} uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"{quotedExe} uninstall", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(estimatedSizeKb, int.MaxValue), RegistryValueKind.DWord);

        Console.WriteLine("Windows uninstaller registered:");
        Console.WriteLine(installedExe);
    }

    private static string GetInstalledAppDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Inertial Mouse Filter");
    }

    private static string QuoteForCommand(string path)
    {
        return "\"" + path.Replace("\"", "\\\"") + "\"";
    }

    internal static void UnregisterWindowsUninstaller()
    {
        Registry.LocalMachine.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
        RemoveInstalledAppCopy();
        Console.WriteLine("Windows uninstaller entry removed.");
    }

    private static void RemoveInstalledAppCopy()
    {
        var installDirectory = GetInstalledAppDirectory();
        var installDirectoryFull = Path.GetFullPath(installDirectory);
        var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        if (!installDirectoryFull.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Skipping install directory cleanup outside Program Files: {installDirectoryFull}");
            return;
        }

        if (!Directory.Exists(installDirectoryFull))
        {
            return;
        }

        var currentExe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentExe)
            && Path.GetFullPath(currentExe).StartsWith(installDirectoryFull, StringComparison.OrdinalIgnoreCase))
        {
            ScheduleDirectoryRemoval(installDirectoryFull);
            return;
        }

        Directory.Delete(installDirectoryFull, recursive: true);
    }

    private static void ScheduleDirectoryRemoval(string directory)
    {
        var powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        var escapedDirectory = directory.Replace("'", "''");
        var command = $"Start-Sleep -Seconds 3; Remove-Item -LiteralPath '{escapedDirectory}' -Recurse -Force -ErrorAction SilentlyContinue";

        var startInfo = new ProcessStartInfo
        {
            FileName = File.Exists(powerShell) ? powerShell : "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        Process.Start(startInfo);
    }

    private static string? FindWorkspaceRoot()
    {
        foreach (var start in GetSearchStarts())
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (HasDriverLayout(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string? ExtractEmbeddedPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith("Payload/", StringComparison.Ordinal))
            .ToArray();

        if (resources.Length == 0)
        {
            return null;
        }

        var baseDirectory = IsAdministrator()
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var root = Path.GetFullPath(Path.Combine(baseDirectory, "InertialMouseInstaller", "payload"));
        var allowedRoot = Path.GetFullPath(Path.Combine(baseDirectory, "InertialMouseInstaller"));

        if (!root.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to extract outside {allowedRoot}.");
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);

        foreach (var resourceName in resources)
        {
            var relative = resourceName["Payload/".Length..]
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to extract suspicious path: {relative}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using var input = assembly.GetManifestResourceStream(resourceName);
            if (input is null)
            {
                throw new InvalidOperationException($"Could not read embedded resource: {resourceName}");
            }

            using var output = File.Create(destination);
            input.CopyTo(output);
        }

        return HasDriverLayout(root) ? root : null;
    }

    private static IEnumerable<string> GetSearchStarts()
    {
        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
    }

    private static bool HasDriverLayout(string directory)
    {
        return File.Exists(Path.Combine(directory, "scripts", "install-inertialmouse.ps1"))
            && File.Exists(Path.Combine(directory, "scripts", "build-inertialmouse.ps1"))
            && File.Exists(Path.Combine(directory, "src", "inertialmouse", "inertialmouse.sln"));
    }

    private static bool EnsureAdministrator(IReadOnlyList<string> args)
    {
        if (IsAdministrator())
        {
            return true;
        }

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            throw new InvalidOperationException("This action requires Administrator privileges.");
        }

        Console.WriteLine("Administrator privileges are required. Opening UAC prompt...");

        var startInfo = new ProcessStartInfo
        {
            FileName = currentExe,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory,
            Arguments = QuoteArguments(args)
        };

        Process.Start(startInfo);
        return false;
    }

    private static string QuoteArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteArgument));
    }

    private static string QuoteArgument(string arg)
    {
        if (arg.Length == 0)
        {
            return "\"\"";
        }

        return arg.Any(char.IsWhiteSpace) || arg.Contains('"')
            ? "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : arg;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("InertialMouseInstaller commands:");
        Console.WriteLine("  list            List HID mice");
        Console.WriteLine("  install         Build, enable test-signing, and install");
        Console.WriteLine("  build           Build Release package");
        Console.WriteLine("  toolchain       Install or repair VS/WDK toolchain");
        Console.WriteLine("  uninstall       Remove installed driver package");
        Console.WriteLine("  uninstall-full  Remove driver and disable test-signing");
        Console.WriteLine();
        Console.WriteLine("After a successful install, this executable registers a Windows uninstall entry.");
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static int InvalidMenuOption()
    {
        Console.WriteLine("Invalid option.");
        return 1;
    }

    private static void PauseIfInteractive(string[] args)
    {
        if (args.Length > 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Press Enter to close.");
        Console.ReadLine();
    }

    private sealed class StartupForm : Form
    {
        public StartupForm()
        {
            Text = "Inertial Mouse Filter Driver Setup";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Size = new Size(460, 170);
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9F);

            var title = new Label
            {
                Text = "Preparing setup",
                Left = 26,
                Top = 22,
                Width = 380,
                Height = 28,
                Font = new Font("Segoe UI Semibold", 13F),
                ForeColor = Color.FromArgb(20, 29, 43)
            };

            var detail = new Label
            {
                Text = "Extracting and checking installer files...",
                Left = 28,
                Top = 58,
                Width = 390,
                Height = 22,
                ForeColor = Color.FromArgb(78, 90, 106)
            };

            var progress = new ProgressBar
            {
                Left = 28,
                Top = 96,
                Width = 388,
                Height = 18,
                Style = ProgressBarStyle.Marquee
            };

            Controls.Add(title);
            Controls.Add(detail);
            Controls.Add(progress);
        }
    }
}
