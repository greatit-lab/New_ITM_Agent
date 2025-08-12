using ITM_Agent.Common;
using ITM_Agent.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITM_Agent.ucPanel
{
    public partial class ucImageTransPanel : UserControl
    {
        private readonly SettingsManager _settingsManager;
        private readonly ILogger _logger;
        private readonly FileProcessingService _fileProcessingService;
        private readonly PdfMergeManager _pdfMergeManager;

        private const string SECTION = "ImageTrans";
        private const string WATCHER_NAME = "ImageTransWatcher";
        private readonly ConcurrentDictionary<string, bool> _processedBaseNames = new ConcurrentDictionary<string, bool>();

        public ucImageTransPanel(
            SettingsManager settingsManager,
            ILogger logger,
            FileProcessingService fileProcessingService,
            PdfMergeManager pdfMergeManager)
        {
            _settingsManager = settingsManager;
            _logger = logger;
            _fileProcessingService = fileProcessingService;
            _pdfMergeManager = pdfMergeManager;

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
            btn_SetFolder.Click += btn_SetFolder_Click;
            btn_FolderClear.Click += (s, e) => ClearSetting("Target", cb_TargetImageFolder);
            btn_SetTime.Click += (s, e) => SaveSetting("Wait", cb_WaitTime.Text);
            btn_TimeClear.Click += (s, e) => ClearSetting("Wait", cb_WaitTime);
            btn_SelectOutputFolder.Click += SelectOutputFolder_Click;
        }

        private void LoadSettings()
        {
            if (!IsHandleCreated) return;
            
            btn_SetFolder.Click -= btn_SetFolder_Click;
            try
            {
                cb_WaitTime.Items.Clear();
                cb_WaitTime.Items.AddRange(new object[] { "30", "60", "120", "180", "300" });

                var regexFolders = _settingsManager.GetRegexList()
                                    .Values
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToArray();
                
                string currentSelection = cb_TargetImageFolder.Text;
                cb_TargetImageFolder.Items.Clear();
                cb_TargetImageFolder.Items.AddRange(regexFolders);

                string savedTarget = _settingsManager.GetValue(SECTION, "Target");
                if (!string.IsNullOrEmpty(savedTarget) && cb_TargetImageFolder.Items.Contains(savedTarget))
                {
                    cb_TargetImageFolder.SelectedItem = savedTarget;
                }
                else if (cb_TargetImageFolder.Items.Contains(currentSelection))
                {
                    cb_TargetImageFolder.SelectedItem = currentSelection;
                }

                cb_WaitTime.SelectedItem = _settingsManager.GetValue(SECTION, "Wait") ?? "30";
                lb_ImageSaveFolder.Text = _settingsManager.GetValue(SECTION, "SaveFolder") ?? "출력 폴더가 설정되지 않았습니다.";
            }
            finally
            {
                btn_SetFolder.Click += btn_SetFolder_Click;
            }
        }

        private void btn_SetFolder_Click(object sender, EventArgs e)
        {
            SaveSetting("Target", cb_TargetImageFolder.Text);
            StartWatching();
        }

        private void SaveSetting(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("값을 선택하거나 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _settingsManager.SetValue(SECTION, key, value);
            _logger.Event($"[ImageTrans] Setting '{key}' saved: {value}");
            MessageBox.Show("설정이 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearSetting(string key, ComboBox comboBox)
        {
            comboBox.SelectedIndex = -1;
            comboBox.Text = "";
            _settingsManager.SetValue(SECTION, key, null);
            _logger.Event($"[ImageTrans] Setting '{key}' cleared.");
        }

        private void SelectOutputFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "PDF 파일이 저장될 폴더를 선택하세요.";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    lb_ImageSaveFolder.Text = dialog.SelectedPath;
                    SaveSetting("SaveFolder", dialog.SelectedPath);
                }
            }
        }

        public void StartWatching()
        {
            _processedBaseNames.Clear();
            string targetFolder = _settingsManager.GetValue(SECTION, "Target");
            _fileProcessingService.RegisterWatcher(WATCHER_NAME, targetFolder, "*.*", false);
        }

        public void StopWatching()
        {
            _fileProcessingService.StopWatcher(WATCHER_NAME);
        }

        private void OnFileReadyForProcessing(string filePath, string watcherName)
        {
            if (watcherName != WATCHER_NAME) return;

            Task.Run(async () =>
            {
                try
                {
                    if (!int.TryParse(_settingsManager.GetValue(SECTION, "Wait"), out int waitSeconds))
                    {
                        waitSeconds = 30;
                    }
                    
                    _logger.Debug($"[ImageTrans] File detected: {Path.GetFileName(filePath)}. Waiting for {waitSeconds} seconds...");
                    await Task.Delay(waitSeconds * 1000);

                    MergeImagesForFile(filePath);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[ImageTrans] Error processing file {filePath}: {ex.Message}");
                }
            });
        }

        private void MergeImagesForFile(string triggerFilePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(triggerFilePath);
            var match = Regex.Match(fileName, @"^(?<basename>.+)_(?<page>\d+)$");
            if (!match.Success)
            {
                _logger.Debug($"[ImageTrans] File '{fileName}' does not match pattern. Skipping.");
                return;
            }
            if (fileName.Contains("_#1_"))
            {
                _logger.Debug($"[ImageTrans] File '{fileName}' is handled by another process. Skipping.");
                return;
            }

            string baseName = match.Groups["basename"].Value;
            if (_processedBaseNames.ContainsKey(baseName))
            {
                _logger.Debug($"[ImageTrans] Merge for '{baseName}' is already complete or in progress. Skipping.");
                return;
            }
            _processedBaseNames.TryAdd(baseName, true);

            string directory = Path.GetDirectoryName(triggerFilePath);
            string[] supportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
            var imageFiles = Directory.EnumerateFiles(directory)
                .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                .Select(file => new {
                    Path = file,
                    Match = Regex.Match(Path.GetFileNameWithoutExtension(file), $"^{Regex.Escape(baseName)}_(?<page>\\d+)$")
                })
                .Where(x => x.Match.Success)
                .OrderBy(x => int.Parse(x.Match.Groups["page"].Value))
                .Select(x => x.Path)
                .ToList();

            if (imageFiles.Count == 0)
            {
                _processedBaseNames.TryRemove(baseName, out _);
                return;
            }

            string outputFolder = _settingsManager.GetValue(SECTION, "SaveFolder");
            if (string.IsNullOrEmpty(outputFolder) || !Directory.Exists(outputFolder))
            {
                outputFolder = directory;
            }
            
            string safeBaseName = baseName.Replace('.', '_');
            string outputPdfPath = Path.Combine(outputFolder, $"{safeBaseName}.pdf");
            
            _logger.Event($"[ImageTrans] Starting PDF merge for '{baseName}' with {imageFiles.Count} images.");
            _pdfMergeManager.MergeImagesToPdf(imageFiles, outputPdfPath);
        }

        public void SetControlsEnabled(bool isEnabled)
        {
            this.groupBox1.Enabled = isEnabled;
            this.groupBox2.Enabled = isEnabled;
        }
    }
}
