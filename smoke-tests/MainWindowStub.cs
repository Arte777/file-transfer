using System;

namespace FileTransfer
{
    public static class MainWindow
    {
        public static void Log(string msg)
        {
            Console.WriteLine($"[APP_LOG] {msg}");
        }
    }
}
