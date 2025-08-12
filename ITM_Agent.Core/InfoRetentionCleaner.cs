using ITM_Agent.Common;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace ITM_Agent.Core
{
    /// <summary>
    /// 설정된 보존 기간에 따라 오래된 .info 파일 및 날짜 패턴이 포함된 파일을 주기적으로 정리합니다.
    /// </summary>
    public sealed class InfoRetentionCleaner : IDisposable
    {
        private readonly SettingsManager _settings;
        private readonly ILogger _logger;
        private readonly Timer _timer;
        private const int SCAN_INTERVAL_MS = 60 * 60 * 1000; // 1 시간

        public InfoRetentionCleaner(SettingsManager settingsManager, ILogger logger)
        {
            _settings = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 프로그램 시작 후 10초 뒤에 첫 스캔을 시작하고, 이후 1시간마다 반복합니다.
            _timer = new Timer(_ => ExecuteScan(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(SCAN_INTERVAL_MS));
        }

        private void ExecuteScan()
        {
            try
            {
                bool isDeletionEnabled = _settings.GetValue("Option", "EnableInfoAutoDel") == "1";
                if (!isDeletionEnabled)
                {
                    _logger.Debug("[InfoCleaner] Auto-deletion is disabled. Skipping scan.");
                    return;
                }

                if (!int.TryParse(_settings.GetValue("Option", "InfoRetentionDays"), out int retentionDays) || retentionDays <= 0)
                {
                    _logger.Debug($"[InfoCleaner] Invalid retention days setting. Skipping scan.");
                    return;
                }

                string baseFolder = _settings.GetValuesFromSection("BaseFolder").FirstOrDefault();
                if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
                {
                     _logger.Debug($"[InfoCleaner] BaseFolder is not set or does not exist. Skipping scan.");
                    return;
                }

                _logger.Event($"[InfoCleaner] Starting scan with retention period of {retentionDays} days in folder '{baseFolder}'.");
                CleanFolderRecursively(baseFolder, retentionDays);
            }
            catch(Exception ex)
            {
                _logger.Error($"[InfoCleaner] An unexpected error occurred during scan execution: {ex.Message}");
            }
        }

        private void CleanFolderRecursively(string rootDir, int retentionDays)
        {
            DateTime today = DateTime.Today;
            try
            {
                // Directory.EnumerateFiles를 사용하여 메모리 효율적으로 파일을 순회합니다.
                var filesToDelete = Directory.EnumerateFiles(rootDir, "*.*", SearchOption.AllDirectories)
                    .Select(file => new { Path = file, Date = TryExtractDateFromFileName(file) })
                    .Where(f => f.Date.HasValue && (today - f.Date.Value.Date).TotalDays >= retentionDays)
                    .ToList();

                if (filesToDelete.Any())
                {
                     _logger.Event($"[InfoCleaner] Found {filesToDelete.Count} files to delete.");
                }

                foreach (var file in filesToDelete)
                {
                    TryDelete(file.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[InfoCleaner] Error while scanning directory {rootDir}: {ex.Message}");
            }
        }
        
        // 정규식 패턴을 미리 컴파일하여 성능을 향상시킵니다.
        private static readonly Regex[] DatePatterns = new[]
        {
            new Regex(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_\d{6}", RegexOptions.Compiled), // yyyyMMdd_HHmmss
            new Regex(@"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})", RegexOptions.Compiled),   // yyyy-MM-dd
            new Regex(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", RegexOptions.Compiled)      // yyyyMMdd
        };

        private static DateTime? TryExtractDateFromFileName(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            foreach (var pattern in DatePatterns)
            {
                var match = pattern.Match(fileName);
                if (match.Success)
                {
                    try
                    {
                        // 정규식 그룹 이름을 사용하여 더 명확하게 파싱합니다.
                        int year = int.Parse(match.Groups["year"].Value);
                        int month = int.Parse(match.Groups["month"].Value);
                        int day = int.Parse(match.Groups["day"].Value);
                        return new DateTime(year, month, day);
                    }
                    catch { continue; } // 파싱 실패 시 다음 패턴으로 넘어갑니다.
                }
            }
            return null; // 일치하는 패턴이 없으면 null 반환
        }

        private void TryDelete(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                File.SetAttributes(filePath, FileAttributes.Normal); // 읽기 전용 속성 제거
                File.Delete(filePath);
                _logger.Event($"[InfoCleaner] Deleted old file: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[InfoCleaner] Failed to delete file {filePath}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
