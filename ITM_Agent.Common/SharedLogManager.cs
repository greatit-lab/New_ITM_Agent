using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ITM_Agent.Common
{
    /// <summary>
    /// ILogger 인터페이스를 구현한 중앙 집중식 싱글턴 로그 관리자입니다.
    /// 기존 LogManager와 플러그인의 SimpleLogger 기능을 통합하고 최적화합니다.
    /// </summary>
    public sealed class SharedLogManager : ILogger
    {
        private static readonly Lazy<SharedLogManager> _instance = new Lazy<SharedLogManager>(() => new SharedLogManager());
        public static SharedLogManager Instance => _instance.Value;

        private readonly string _logDirectory;
        private readonly object _fileLock = new object();
        private volatile bool _debugMode = false;
        private const long MAX_LOG_SIZE = 5 * 1024 * 1024; // 5MB

        private SharedLogManager()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(_logDirectory);
        }

        public void SetDebugMode(bool isEnabled)
        {
            _debugMode = isEnabled;
        }

        public void Event(string message)
        {
            WriteLog("event", $"[Event] {message}");
        }

        public void Error(string message)
        {
            WriteLog("error", $"[Error] {message}");
        }

        public void Debug(string message)
        {
            if (_debugMode)
            {
                WriteLog("debug", $"[Debug] {message}");
            }
        }

        private void WriteLog(string logType, string formattedMessage)
        {
            // 로그 작성을 백그라운드 스레드에 위임하여 UI 스레드나 주요 로직의 지연을 방지합니다.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string filePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyyMMdd}_{logType}.log");
                string logContent = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {formattedMessage}{Environment.NewLine}";

                // 파일 접근 충돌을 최소화하기 위해 lock 사용
                lock (_fileLock)
                {
                    try
                    {
                        RotateLogFileIfNeeded(filePath);
                        File.AppendAllText(filePath, logContent, Encoding.UTF8);
                    }
                    catch
                    {
                        // 로깅 실패는 다른 기능에 영향을 주지 않도록 무시합니다.
                    }
                }
            });
        }

        private void RotateLogFileIfNeeded(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < MAX_LOG_SIZE) return;

                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string extension = Path.GetExtension(filePath);
                int index = 1;
                string newPath;
                do
                {
                    newPath = Path.Combine(_logDirectory, $"{baseName}_{index++}{extension}");
                } while (File.Exists(newPath));

                File.Move(filePath, newPath);
            }
            catch (Exception ex)
            {
                 // 회전 실패 시에도 에러 로그를 남겨 추적할 수 있도록 합니다.
                WriteLog("error", $"[Error] Log file rotation failed for {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }
    }
}
