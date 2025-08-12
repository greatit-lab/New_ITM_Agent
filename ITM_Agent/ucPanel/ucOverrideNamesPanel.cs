using ITM_Agent.Common;
using ITM_Agent.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITM_Agent.ucPanel
{
    public partial class ucOverrideNamesPanel : UserControl
    {
        private readonly SettingsManager _settingsManager;
        private readonly ILogger _logger;
        private readonly FileProcessingService _fileProcessingService;

        private const string BASE_DATE_PATH_SECTION = "OverrideBaseDatePath";
        private const string TARGET_COMPARE_PATH_SECTION = "OverrideTargetComparePath";
        private const string WATCHER_BASE_DATE = "OverrideBaseDateWatcher";
        private const string WATCHER_BASELINE = "OverrideBaselineWatcher";

        public ucOverrideNamesPanel(
            SettingsManager settingsManager,
            ILogger logger,
            FileProcessingService fileProcessingService)
        {
            _settingsManager = settingsManager;
            _logger = logger;
            _fileProcessingService = fileProcessingService;

            InitializeComponent();
            RegisterEvents();
        }

        public void InitializePanelData()
        {
            RefreshData();
        }

        public void RefreshData()
        {
            if (!IsHandleCreated) return;
            if (this.InvokeRequired) this.Invoke(new Action(LoadSettings));
            else LoadSettings();
        }

        private void RegisterEvents()
        {
            _fileProcessingService.FileReadyForProcessing += OnFileReadyForProcessing;
            cb_BaseDatePath.SelectedIndexChanged += Cb_BaseDatePath_SelectedIndexChanged;
            btn_BaseClear.Click += (s, e) => {
                cb_BaseDatePath.SelectedIndex = -1;
                cb_BaseDatePath.Text = "";
                _settingsManager.SetValue(BASE_DATE_PATH_SECTION, "Path", null);
                StopWatching();
            };
            btn_SelectFolder.Click += AddTargetComparePath_Click;
            btn_Remove.Click += RemoveTargetComparePath_Click;
        }

        private void LoadSettings()
        {
            if (!IsHandleCreated) return;

            cb_BaseDatePath.SelectedIndexChanged -= Cb_BaseDatePath_SelectedIndexChanged;
            try
            {
                var regexFolders = _settingsManager.GetRegexList()
                                    .Values
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToArray();
                
                string currentSelection = cb_BaseDatePath.Text;
                cb_BaseDatePath.Items.Clear();
                cb_BaseDatePath.Items.AddRange(regexFolders);
                
                string savedBasePath = _settingsManager.GetValue(BASE_DATE_PATH_SECTION, "Path");
                if (!string.IsNullOrEmpty(savedBasePath) && cb_BaseDatePath.Items.Contains(savedBasePath))
                {
                    cb_BaseDatePath.SelectedItem = savedBasePath;
                }
                else if (cb_BaseDatePath.Items.Contains(currentSelection))
                {
                    cb_BaseDatePath.SelectedItem = currentSelection;
                }

                lb_TargetComparePath.Items.Clear();
                var comparePaths = _settingsManager.GetValuesFromSection(TARGET_COMPARE_PATH_SECTION);
                lb_TargetComparePath.Items.AddRange(comparePaths.ToArray());
            }
            finally
            {
                cb_BaseDatePath.SelectedIndexChanged += Cb_BaseDatePath_SelectedIndexChanged;
            }
        }

        private void Cb_BaseDatePath_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cb_BaseDatePath.SelectedItem != null)
            {
                _settingsManager.SetValue(BASE_DATE_PATH_SECTION, "Path", cb_BaseDatePath.SelectedItem.ToString());
                StartWatching();
            }
        }

        private void AddTargetComparePath_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (!lb_TargetComparePath.Items.Contains(dialog.SelectedPath))
                    {
                        lb_TargetComparePath.Items.Add(dialog.SelectedPath);
                        UpdateTargetComparePathsInSettings();
                    }
                }
            }
        }

        private void RemoveTargetComparePath_Click(object sender, EventArgs e)
        {
            if (lb_TargetComparePath.SelectedItems.Count > 0)
            {
                var itemsToRemove = lb_TargetComparePath.SelectedItems.Cast<string>().ToList();
                foreach (var item in itemsToRemove)
                {
                    lb_TargetComparePath.Items.Remove(item);
                }
                UpdateTargetComparePathsInSettings();
            }
        }

        private void UpdateTargetComparePathsInSettings()
        {
            var paths = lb_TargetComparePath.Items.Cast<string>().ToList();
            _settingsManager.SetValues(TARGET_COMPARE_PATH_SECTION, paths);
            _logger.Event("[OverrideNames] Target Compare Path list updated.");
        }

        public void StartWatching()
        {
            string baseDatePath = _settingsManager.GetValue(BASE_DATE_PATH_SECTION, "Path");
            _fileProcessingService.RegisterWatcher(WATCHER_BASE_DATE, baseDatePath, "*.*", false);

            string baseFolder = _settingsManager.GetValuesFromSection("BaseFolder").FirstOrDefault();
            if (!string.IsNullOrEmpty(baseFolder))
            {
                string baselinePath = Path.Combine(baseFolder, "Baseline");
                Directory.CreateDirectory(baselinePath);
                _fileProcessingService.RegisterWatcher(WATCHER_BASELINE, baselinePath, "*.info", false);
            }
        }

        public void StopWatching()
        {
            _fileProcessingService.StopWatcher(WATCHER_BASE_DATE);
            _fileProcessingService.StopWatcher(WATCHER_BASELINE);
        }

        private void OnFileReadyForProcessing(string filePath, string watcherName)
        {
            Task.Run(() =>
            {
                if (watcherName == WATCHER_BASE_DATE)
                {
                    ProcessBaseDateFile(filePath);
                }
                else if (watcherName == WATCHER_BASELINE)
                {
                    ProcessBaselineFile(filePath);
                }
            });
        }

        private void ProcessBaseDateFile(string filePath)
        {
            _logger.Debug($"[OverrideNames] Processing base date file: {filePath}");
            DateTime? dateTime = ExtractDateTimeFromFileContent(filePath);
            if (!dateTime.HasValue)
            {
                _logger.Error($"[OverrideNames] Could not extract date and time from {filePath}");
                return;
            }

            string baseFolder = _settingsManager.GetValuesFromSection("BaseFolder").FirstOrDefault();
            if (string.IsNullOrEmpty(baseFolder))
            {
                _logger.Error("[OverrideNames] BaseFolder is not configured.");
                return;
            }

            string baselineFolder = Path.Combine(baseFolder, "Baseline");
            Directory.CreateDirectory(baselineFolder);

            string originalName = Path.GetFileNameWithoutExtension(filePath);
            string infoFileName = $"{dateTime.Value:yyyyMMdd_HHmmss}_{originalName}.info";
            string infoFilePath = Path.Combine(baselineFolder, infoFileName);

            try
            {
                File.Create(infoFilePath).Close();
                _logger.Event($"[OverrideNames] Baseline .info file created: {infoFileName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[OverrideNames] Failed to create .info file at {infoFilePath}: {ex.Message}");
            }
        }

        private void ProcessBaselineFile(string infoFilePath)
        {
            _logger.Debug($"[OverrideNames] Processing baseline file: {infoFilePath}");
            var baselineData = ExtractDataFromInfoFileName(infoFilePath);
            if (baselineData == null) return;

            var targetFolders = _settingsManager.GetValuesFromSection(TARGET_COMPARE_PATH_SECTION);
            foreach (string folder in targetFolders)
            {
                if (!Directory.Exists(folder)) continue;
                
                var targetFiles = Directory.EnumerateFiles(folder)
                    .Where(f => Path.GetFileName(f).Contains(baselineData.Value.TimeInfo) && 
                                Path.GetFileName(f).Contains(baselineData.Value.Prefix));

                foreach (string targetFile in targetFiles)
                {
                    RenameTargetFile(targetFile, baselineData.Value.CInfo);
                }
            }
        }
        
        private DateTime? ExtractDateTimeFromFileContent(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                Match match = Regex.Match(content, @"Date and Time:\s*(.+)");
                if (match.Success && DateTime.TryParse(match.Groups[1].Value.Trim(), out DateTime result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[OverrideNames] Error reading file content from {filePath}: {ex.Message}");
            }
            return null;
        }

        private (string TimeInfo, string Prefix, string CInfo)? ExtractDataFromInfoFileName(string infoFilePath)
        {
            var match = Regex.Match(Path.GetFileNameWithoutExtension(infoFilePath), @"(\d{8}_\d{6})_([^_]+?)_(C\dW\d+)");
            if (match.Success)
            {
                return (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
            }
            _logger.Debug($"[OverrideNames] Info file name '{Path.GetFileName(infoFilePath)}' does not match pattern.");
            return null;
        }

        private void RenameTargetFile(string targetFilePath, string cInfo)
        {
            try
            {
                string originalName = Path.GetFileName(targetFilePath);
                string newName = originalName.Replace("_#1_", $"_{cInfo}_");
                if (originalName.Equals(newName)) return;

                string directory = Path.GetDirectoryName(targetFilePath);
                string newFilePath = Path.Combine(directory, newName);

                File.Move(targetFilePath, newFilePath);
                _logger.Event($"[OverrideNames] File renamed: {originalName} -> {newName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"[OverrideNames] Failed to rename file {targetFilePath}: {ex.Message}");
            }
        }
        
        public string EnsureOverrideAndReturnPath(string originalPath, int timeoutMs = 30000)
        {
            string baseFolder = _settingsManager.GetValuesFromSection("BaseFolder").FirstOrDefault();
            if (string.IsNullOrEmpty(baseFolder)) return originalPath;

            string baselineFolder = Path.Combine(baseFolder, "Baseline");
            string originalFileName = Path.GetFileNameWithoutExtension(originalPath);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var infoFile = Directory.EnumerateFiles(baselineFolder, $"*_{originalFileName}.info").FirstOrDefault();
                if (infoFile != null)
                {
                    var baselineData = ExtractDataFromInfoFileName(infoFile);
                    if (baselineData.HasValue)
                    {
                        string originalNameWithExt = Path.GetFileName(originalPath);
                        string expectedNewName = originalNameWithExt.Replace("_#1_", $"_{baselineData.Value.CInfo}_");
                        string expectedNewPath = Path.Combine(Path.GetDirectoryName(originalPath), expectedNewName);

                        if (File.Exists(expectedNewPath))
                        {
                            _logger.Debug($"[OverrideNames] Override confirmed. New path: {expectedNewPath}.");
                            return expectedNewPath;
                        }
                    }
                }
                Task.Delay(500).Wait();
            }

            _logger.Event($"[OverrideNames] Timeout waiting for override of {originalPath}. Proceeding with original path.");
            return originalPath;
        }

        public void SetControlsEnabled(bool isEnabled)
        {
            this.groupBox1.Enabled = isEnabled;
            this.groupBox2.Enabled = isEnabled;
        }
    }
}
