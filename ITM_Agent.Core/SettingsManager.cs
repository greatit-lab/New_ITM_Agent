using ITM_Agent.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ITM_Agent.Core
{
    public sealed class SettingsManager : IDisposable
    {
        private readonly string _settingsFilePath;
        private readonly object _fileWriteLock = new object();
        private readonly ILogger _logger;
        private FileSystemWatcher _watcher;
        private ConcurrentDictionary<string, List<string>> _settingsCache = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public event Action SettingsChanged;

        public SettingsManager(string settingsFilePath, ILogger logger)
        {
            _settingsFilePath = settingsFilePath;
            _logger = logger;
            EnsureSettingsFileExists();
            LoadSettingsToCache();
            SetupFileWatcher();
        }

        #region --- 설정 읽기 (GET) ---

        public string GetValue(string section, string key)
        {
            if (_settingsCache.TryGetValue($"[{section}]", out var lines))
            {
                string keyPrefix = $"{key.Trim()}=";
                var line = lines.FirstOrDefault(l => l.Trim().StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
                if (line != null)
                {
                    return line.Substring(line.IndexOf('=') + 1).Trim();
                }
            }
            return null;
        }

        public List<string> GetValuesFromSection(string section)
        {
            var values = new List<string>();
            if (_settingsCache.TryGetValue($"[{section}]", out var lines))
            {
                values.AddRange(lines.Where(line => !line.Contains("=")).Select(line => line.Trim()));
            }
            return values;
        }
        
        public List<string> GetLinesFromSection(string section)
        {
             if (_settingsCache.TryGetValue($"[{section}]", out var lines))
             {
                 return new List<string>(lines);
             }
             return new List<string>();
        }

        public Dictionary<string, string> GetRegexList()
        {
            var regexDict = new Dictionary<string, string>();
            if (_settingsCache.TryGetValue("[Regex]", out var lines))
            {
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { "->" }, 2, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        regexDict[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            return regexDict;
        }

        #endregion

        #region --- 설정 쓰기 (SET) ---

        public void SetValue(string section, string key, string value)
        {
            WriteToFile(lines =>
            {
                string sectionHeader = $"[{section}]";
                string keyPrefix = $"{key.Trim()} =";
                int sectionIndex = FindSectionIndex(lines, sectionHeader);

                if (sectionIndex == -1)
                {
                    if (value == null) return;
                    lines.Add(sectionHeader);
                    lines.Add($"{key.Trim()} = {value.Trim()}");
                }
                else
                {
                    int keyIndex = FindKeyIndex(lines, sectionIndex, keyPrefix);
                    if (keyIndex != -1)
                    {
                        if (value != null)
                            lines[keyIndex] = $"{key.Trim()} = {value.Trim()}";
                        else
                            lines.RemoveAt(keyIndex);
                    }
                    else if (value != null)
                    {
                        int insertionPoint = FindInsertionPointForSection(lines, sectionIndex);
                        lines.Insert(insertionPoint, $"{key.Trim()} = {value.Trim()}");
                    }
                }
            });
        }
        
        public void SetValues(string section, List<string> values)
        {
             WriteToFile(lines =>
            {
                string sectionHeader = $"[{section}]";
                int sectionIndex = FindSectionIndex(lines, sectionHeader);
                
                if (sectionIndex != -1)
                {
                     int endSectionIndex = FindSectionEndIndex(lines, sectionIndex);
                     lines.RemoveRange(sectionIndex + 1, endSectionIndex - (sectionIndex + 1));
                     lines.InsertRange(sectionIndex + 1, values);
                }
                else
                {
                    lines.Add(sectionHeader);
                    lines.AddRange(values);
                }
            });
        }

        public void SetRegexList(Dictionary<string, string> regexDict)
        {
            var regexLines = regexDict.Select(kvp => $"{kvp.Key} -> {kvp.Value}").ToList();
            
            WriteToFile(lines => {
                string sectionHeader = "[Regex]";
                int sectionIndex = FindSectionIndex(lines, sectionHeader);

                if (sectionIndex != -1)
                {
                    int endSectionIndex = FindSectionEndIndex(lines, sectionIndex);
                    lines.RemoveRange(sectionIndex, endSectionIndex - sectionIndex);
                }
                
                if (regexLines.Any())
                {
                    lines.Add(sectionHeader);
                    lines.AddRange(regexLines);
                }
            });
        }
        
        public void ResetSettingsExceptEqpid()
        {
            WriteToFile(lines =>
            {
                int sectionIndex = FindSectionIndex(lines, "[Eqpid]");
                var eqpidLines = new List<string>();
                if (sectionIndex != -1)
                {
                    int endSectionIndex = FindSectionEndIndex(lines, sectionIndex);
                    eqpidLines.AddRange(lines.Skip(sectionIndex).Take(endSectionIndex - sectionIndex));
                }
                lines.Clear();
                if (eqpidLines.Any())
                {
                    lines.AddRange(eqpidLines);
                }
            });
            _logger.Event("Settings have been reset, keeping [Eqpid] section.");
        }

        #endregion

        #region --- 내부 헬퍼 메서드 ---
        
        private void EnsureSettingsFileExists()
        {
            if (!File.Exists(_settingsFilePath))
            {
                File.Create(_settingsFilePath).Close();
                _logger.Event($"Settings.ini file created at: {_settingsFilePath}");
            }
        }

        private void LoadSettingsToCache()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    lock (_fileWriteLock)
                    {
                        var tempCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        if (File.Exists(_settingsFilePath))
                        {
                            string currentSection = null;
                            foreach (var line in File.ReadAllLines(_settingsFilePath))
                            {
                                var trimmedLine = line.Trim();
                                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                                {
                                    currentSection = trimmedLine;
                                    if (!tempCache.ContainsKey(currentSection)) tempCache[currentSection] = new List<string>();
                                }
                                else if (!string.IsNullOrWhiteSpace(trimmedLine) && currentSection != null)
                                {
                                    tempCache[currentSection].Add(line);
                                }
                            }
                        }
                        _settingsCache = new ConcurrentDictionary<string, List<string>>(tempCache);
                        _logger.Debug("Settings loaded into cache.");
                    }
                    return; 
                }
                catch (IOException) { Thread.Sleep(100); }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to load settings: {ex.Message}");
                    return; 
                }
            }
            _logger.Error($"Failed to load settings file after multiple retries: {_settingsFilePath}");
        }
        
        private void SetupFileWatcher()
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (Directory.Exists(directory))
            {
                _watcher = new FileSystemWatcher(directory, Path.GetFileName(_settingsFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += (s, e) => {
                    _logger.Event("Settings.ini has changed. Reloading cache.");
                    LoadSettingsToCache();
                    SettingsChanged?.Invoke();
                };
            }
        }

        private void WriteToFile(Action<List<string>> manipulateLines)
        {
            lock (_fileWriteLock)
            {
                if (_watcher != null) _watcher.EnableRaisingEvents = false;
                var lines = File.Exists(_settingsFilePath) ? File.ReadAllLines(_settingsFilePath).ToList() : new List<string>();
                manipulateLines(lines);
                FormatSectionSpacing(lines);
                File.WriteAllLines(_settingsFilePath, lines.ToArray(), Encoding.UTF8);
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
            }
            LoadSettingsToCache();
            SettingsChanged?.Invoke();
        }

        private void FormatSectionSpacing(List<string> lines)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].Trim().StartsWith("[") && i > 0)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i - 1]))
                    {
                        lines.Insert(i, string.Empty);
                    }
                }
            }
            while (lines.Any() && string.IsNullOrWhiteSpace(lines.First()))
            {
                lines.RemoveAt(0);
            }
        }

        private int FindSectionIndex(List<string> lines, string section) => lines.FindIndex(l => l.Trim().Equals(section, StringComparison.OrdinalIgnoreCase));
        private int FindKeyIndex(List<string> lines, int sectionIndex, string keyPrefix)
        {
            for (int i = sectionIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Trim().StartsWith("[")) return -1;
                if (lines[i].Trim().StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
        private int FindSectionEndIndex(List<string> lines, int sectionIndex)
        {
            for (int i = sectionIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Trim().StartsWith("[")) return i;
            }
            return lines.Count;
        }
        
        private int FindInsertionPointForSection(List<string> lines, int sectionIndex)
        {
            int insertionPoint = lines.Count;
            for (int i = sectionIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Trim().StartsWith("["))
                {
                    insertionPoint = i;
                    break;
                }
            }
            while (insertionPoint > sectionIndex + 1 && string.IsNullOrWhiteSpace(lines[insertionPoint - 1]))
            {
                insertionPoint--;
            }
            return insertionPoint;
        }

        public void Dispose() => _watcher?.Dispose();
        
        #endregion

        #region --- 편의 메서드 ---
        public bool IsReadyToRun()
        {
            bool hasBase = GetValuesFromSection("BaseFolder").Any();
            bool hasTarget = GetValuesFromSection("TargetFolders").Any();
            bool hasRegex = GetRegexList().Any();
            return hasBase && hasTarget && hasRegex;
        }
        public string GetEqpid() => GetValue("Eqpid", "Eqpid");
        public void SetEqpid(string eqpid) => SetValue("Eqpid", "Eqpid", eqpid);
        public string GetEqpidType() => GetValue("Eqpid", "Type");
        public void SetEqpidType(string type) => SetValue("Eqpid", "Type", type);
        #endregion
    }
}
