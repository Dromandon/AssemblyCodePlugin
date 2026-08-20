using System;
using System.IO;
using System.Linq;
using System.Text;

namespace AssemblyCodePlugin.Services
{
    public static class PluginLogger
    {
        private static string _logFilePath;
        private static readonly object _lock = new object();

        public static string LogFilePath => _logFilePath;

        public static void Initialize()
        {
            try
            {
                // 1. Очищаем старый лог с рабочего стола, если он остался от прошлых версий
                try
                {
                    string oldDesktopLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AssemblyCode_Log.txt");
                    if (File.Exists(oldDesktopLog))
                        File.Delete(oldDesktopLog);
                }
                catch { }

                // 2. Создаём папку логов
                string logsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AssemblyCodePlugin", "Logs");

                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                // 3. Создаём новый файл лога с временной меткой
                string fileName = $"AssemblyCode_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
                _logFilePath = Path.Combine(logsDir, fileName);

                lock (_lock)
                {
                    File.WriteAllText(_logFilePath,
                        $"=== ЛОГ ЗАПУСКА ASSEMBLYCODE [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ===\r\n",
                        Encoding.UTF8);
                }

                // 4. Очищаем старые логи, оставляя ровно 10 последних
                CleanupOldLogs(logsDir, maxCount: 10);
            }
            catch { }
        }

        private static void CleanupOldLogs(string folder, int maxCount)
        {
            try
            {
                var files = new DirectoryInfo(folder).GetFiles("AssemblyCode_*.log")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();

                for (int i = maxCount; i < files.Count; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_logFilePath)) return;

                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n";
                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, line, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
