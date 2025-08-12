using ITM_Agent.Common;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace ITM_Agent.Core
{
    /// <summary>
    /// 파일 시스템 감시 및 파일 안정성 검사를 중앙에서 처리하는 서비스입니다.
    /// </summary>
    public class FileProcessingService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
        private readonly ConcurrentDictionary<string, DateTime> _recentEvents = new ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// 파일이 안정화(쓰기 완료)되었을 때 발생하는 이벤트입니다.
        /// string: 파일의 전체 경로
        /// string: 이벤트를 발생시킨 Watcher의 이름
        /// </summary>
        public event Action<string, string> FileReadyForProcessing;

        public FileProcessingService(ILogger logger)
        {
            _logger = logger;
        }

        public void RegisterWatcher(string watcherName, string path, string filter = "*.*", bool includeSubdirectories = true)
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.Debug($"Watcher registration skipped for '{watcherName}'. Path is null or empty.");
                return;
            }
            if (!Directory.Exists(path))
            {
                _logger.Error($"Watcher registration failed for '{watcherName}'. Path does not exist: {path}");
                return;
            }

            // 이미 동일한 이름의 Watcher가 있으면 중지하고 새로 등록
            StopWatcher(watcherName);

            var watcher = new FileSystemWatcher(path, filter)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            watcher.Created += (s, e) => OnFileEvent(e, watcherName);
            watcher.Changed += (s, e) => OnFileEvent(e, watcherName);
            watcher.Renamed += (s, e) => OnFileEvent(e, watcherName);

            if (_watchers.TryAdd(watcherName, watcher))
            {
                _logger.Event($"Watcher '{watcherName}' started for path: {path}");
            }
        }

        public void StopWatcher(string watcherName)
        {
            if (_watchers.TryRemove(watcherName, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _logger.Event($"Watcher '{watcherName}' stopped.");
            }
        }

        public void StopAllWatchers()
        {
            foreach (var name in _watchers.Keys)
            {
                StopWatcher(name);
            }
        }

        private void OnFileEvent(FileSystemEventArgs e, string watcherName)
        {
            // 짧은 시간 내에 발생하는 중복 이벤트를 무시 (Debouncing)
            if (_recentEvents.TryGetValue(e.FullPath, out var lastEventTime) && (DateTime.Now - lastEventTime).TotalSeconds < 2)
            {
                return;
            }
            _recentEvents[e.FullPath] = DateTime.Now;

            // 디렉토리에 대한 이벤트는 무시
            if (Directory.Exists(e.FullPath)) return;

            Task.Run(async () =>
            {
                if (await IsFileStable(e.FullPath))
                {
                    _logger.Debug($"File is stable: {e.FullPath}. Raising event for watcher '{watcherName}'.");
                    FileReadyForProcessing?.Invoke(e.FullPath, watcherName);
                }
            });
        }

        private async Task<bool> IsFileStable(string filePath, int maxRetries = 10, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        return true; // 파일 접근 성공
                    }
                }
                catch (FileNotFoundException)
                {
                     // 파일이 생성 중이거나 삭제된 경우
                    _logger.Debug($"File not found during stability check, it might have been deleted: {filePath}");
                    return false;
                }
                catch (IOException)
                {
                    await Task.Delay(delayMs); // 파일이 잠겨 있으면 대기
                }
                catch (Exception ex)
                {
                    _logger.Error($"An unexpected error occurred while checking file stability for {filePath}: {ex.Message}");
                    return false;
                }
            }
            _logger.Error($"File '{filePath}' remained locked after {maxRetries} retries.");
            return false;
        }

        public void Dispose()
        {
            StopAllWatchers();
        }
    }
}
