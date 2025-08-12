using ITM_Agent.Common;
using ITM_Agent.Core;
using ITM_Agent.Core.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;

namespace ITM_Agent.ucPanel
{
    public partial class ucUploadPanel : UserControl
    {
        // --- 주입받는 서비스 및 의존성 ---
        private readonly SettingsManager _settingsManager;
        private readonly ILogger _logger;
        private readonly FileProcessingService _fileProcessingService;
        private readonly ucPluginPanel _pluginPanel;
        private readonly ucOverrideNamesPanel _overridePanel;

        // --- 상수 ---
        private const string SECTION = "UploadSettings";

        // Watcher 이름을 상수로 관리
        private const string WATCHER_WAFER_FLAT = "Upload_WaferFlat";
        private const string WATCHER_PRE_ALIGN = "Upload_PreAlign";
        private const string WATCHER_ERROR = "Upload_Error";
        private const string WATCHER_IMAGE = "Upload_Image";
        private const string WATCHER_EVENT = "Upload_Event";
        private const string WATCHER_WAVE = "Upload_Wave";

        // 설정을 스레드로부터 안전하게 저장할 멤버 변수 추가
        private readonly Dictionary<string, (string folder, string plugin)> _watcherSettings = new Dictionary<string, (string folder, string plugin)>();

        public ucUploadPanel(
            SettingsManager settingsManager,
            ILogger logger,
            FileProcessingService fileProcessingService,
            ucPluginPanel pluginPanel,
            ucOverrideNamesPanel overridePanel)
        {
            _settingsManager = settingsManager;
            _logger = logger;
            _fileProcessingService = fileProcessingService;
            _pluginPanel = pluginPanel;
            _overridePanel = overridePanel;

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
            if (this.InvokeRequired) this.Invoke(new Action(LoadAllSettings));
            else LoadAllSettings();
        }

        private void RegisterEvents()
        {
            _fileProcessingService.FileReadyForProcessing += OnFileReadyForProcessing;

            SetupButtonGroupEvents(cb_WaferFlat_Path, cb_FlatPlugin, btn_FlatSet, btn_FlatClear, "WaferFlat");
            SetupButtonGroupEvents(cb_PreAlign_Path, cb_PreAlignPlugin, btn_PreAlignSet, btn_PreAlignClear, "PreAlign");
            SetupButtonGroupEvents(cb_ErrPath, cb_ErrPlugin, btn_ErrSet, btn_ErrClear, "Error");
            SetupButtonGroupEvents(cb_ImgPath, cb_ImagePlugin, btn_ImgSet, btn_ImgClear, "Image");
            SetupButtonGroupEvents(cb_EvPath, cb_EvPlugin, btn_EvSet, btn_EvClear, "Event");
            SetupButtonGroupEvents(cb_WavePath, cb_WavePlugin, btn_WaveSet, btn_WaveClear, "Wave");
        }

        private void SetupButtonGroupEvents(ComboBox cbPath, ComboBox cbPlugin, Button btnSet, Button btnClear, string key)
        {
            btnSet.Click += (s, e) =>
            {
                string folder = cbPath.Text.Trim();
                string plugin = cbPlugin.Text.Trim();

                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(plugin))
                {
                    MessageBox.Show("폴더와 플러그인을 모두 선택해야 합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!Directory.Exists(folder))
                {
                    MessageBox.Show("선택한 폴더가 존재하지 않습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string value = $"Folder={folder}, Plugin={plugin}";
                _settingsManager.SetValue(SECTION, key, value);

                string watcherName = GetWatcherName(key);
                _watcherSettings[watcherName] = (folder, plugin);

                _logger.Event($"[{key}] setting saved: {value}");
                MessageBox.Show($"[{key}] 설정이 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                StartWatchingByKey(key);
            };

            btnClear.Click += (s, e) =>
            {
                cbPath.SelectedIndex = -1;
                cbPath.Text = "";
                cbPlugin.SelectedIndex = -1;
                cbPlugin.Text = "";
                _settingsManager.SetValue(SECTION, key, null);

                string watcherName = GetWatcherName(key);
                if (_watcherSettings.ContainsKey(watcherName))
                {
                    _watcherSettings.Remove(watcherName);
                }

                _logger.Event($"[{key}] setting cleared.");
                _fileProcessingService.StopWatcher(watcherName);
            };
        }

        private void LoadAllSettings()
        {
            if (!IsHandleCreated) return;
            LoadTargetFolderItems();
            LoadPluginItems();
            LoadSettingByKey("WaferFlat", cb_WaferFlat_Path, cb_FlatPlugin);
            LoadSettingByKey("PreAlign", cb_PreAlign_Path, cb_PreAlignPlugin);
            LoadSettingByKey("Error", cb_ErrPath, cb_ErrPlugin);
            LoadSettingByKey("Image", cb_ImgPath, cb_ImagePlugin);
            LoadSettingByKey("Event", cb_EvPath, cb_EvPlugin);
            LoadSettingByKey("Wave", cb_WavePath, cb_WavePlugin);
        }

        private void LoadSettingByKey(string key, ComboBox cbPath, ComboBox cbPlugin)
        {
            string settingValue = _settingsManager.GetValue(SECTION, key);
            if (string.IsNullOrEmpty(settingValue)) return;

            var settings = ParseSettingValue(settingValue);
            cbPath.SelectedItem = settings.folder;
            cbPlugin.SelectedItem = settings.plugin;
        }

        private void LoadTargetFolderItems()
        {
            var regexFolders = _settingsManager.GetRegexList()
                                .Values
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray();
            var pathCombos = new[] { cb_WaferFlat_Path, cb_PreAlign_Path, cb_ErrPath, cb_ImgPath, cb_EvPath, cb_WavePath };
            foreach (var cb in pathCombos)
            {
                string current = cb.Text;
                cb.Items.Clear();
                cb.Items.AddRange(regexFolders);
                if (cb.Items.Contains(current))
                {
                    cb.SelectedItem = current;
                }
            }
        }

        private void LoadPluginItems()
        {
            var plugins = _pluginPanel.GetLoadedPlugins().Select(p => p.PluginName).ToArray();
            var pluginCombos = new[] { cb_FlatPlugin, cb_PreAlignPlugin, cb_ErrPlugin, cb_ImagePlugin, cb_EvPlugin, cb_WavePlugin };
            foreach (var cb in pluginCombos)
            {
                string current = cb.Text;
                cb.Items.Clear();
                cb.Items.AddRange(plugins);
                if (cb.Items.Contains(current))
                {
                    cb.SelectedItem = current;
                }
            }
        }

        public void StartWatching()
        {
            StartWatchingByKey("WaferFlat");
            StartWatchingByKey("PreAlign");
            StartWatchingByKey("Error");
            StartWatchingByKey("Image");
            StartWatchingByKey("Event");
            StartWatchingByKey("Wave");
        }

        public void StopWatching()
        {
            _fileProcessingService.StopWatcher(WATCHER_WAFER_FLAT);
            _fileProcessingService.StopWatcher(WATCHER_PRE_ALIGN);
            _fileProcessingService.StopWatcher(WATCHER_ERROR);
            _fileProcessingService.StopWatcher(WATCHER_IMAGE);
            _fileProcessingService.StopWatcher(WATCHER_EVENT);
            _fileProcessingService.StopWatcher(WATCHER_WAVE);
        }

        private void StartWatchingByKey(string key)
        {
            string settingValue = _settingsManager.GetValue(SECTION, key);
            if (!string.IsNullOrEmpty(settingValue))
            {
                var settings = ParseSettingValue(settingValue);
                _fileProcessingService.RegisterWatcher(GetWatcherName(key), settings.folder, "*.*", false);
            }
        }

        private void OnFileReadyForProcessing(string filePath, string watcherName)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => ProcessFile(filePath, watcherName)));
                return;
            }
            ProcessFile(filePath, watcherName);
        }

        private void ProcessFile(string filePath, string watcherName)
        {
            string pluginName = GetPluginNameByWatcher(watcherName);
            if (string.IsNullOrEmpty(pluginName)) return;

            _logger.Event($"[UploadPanel] Stable file detected by '{watcherName}': {filePath}");
            string finalPath = _overridePanel.EnsureOverrideAndReturnPath(filePath);
            var pluginInfo = _pluginPanel.GetLoadedPlugins().FirstOrDefault(p => p.PluginName.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
            if (pluginInfo == null)
            {
                _logger.Error($"[UploadPanel] Plugin '{pluginName}' is not loaded or not found.");
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    _logger.Event($"[UploadPanel] Executing plugin '{pluginInfo.PluginName}' for file: {finalPath}");
                    byte[] dllBytes = File.ReadAllBytes(pluginInfo.AssemblyPath);
                    Assembly asm = Assembly.Load(dllBytes);
                    Type targetType = asm.GetTypes().FirstOrDefault(t => t.IsClass && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t));
                    if (targetType == null)
                    {
                        _logger.Error($"[UploadPanel] Plugin '{pluginInfo.PluginName}' does not implement IPlugin interface.");
                        return;
                    }

                    IPlugin instance = (IPlugin)Activator.CreateInstance(targetType);
                    var dbRepository = new DatabaseRepository(_logger);
                    instance.Initialize(_logger, dbRepository);

                    string eqpid = _settingsManager.GetEqpid();
                    instance.ProcessAndUpload(finalPath, null, eqpid);
                    _logger.Event($"[UploadPanel] Plugin '{pluginInfo.PluginName}' execution completed for file: {finalPath}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[UploadPanel] Error executing plugin '{pluginInfo.PluginName}' for file '{finalPath}': {ex.ToString()}");
                }
            });
        }

        public void SetControlsEnabled(bool isEnabled)
        {
            groupBox1.Enabled = isEnabled;
        }

        private (string folder, string plugin) ParseSettingValue(string value)
        {
            string folder = null, plugin = null;
            var parts = value.Split(',');
            foreach (var part in parts)
            {
                var kv = part.Split(new[]{'='}, 2);
                if (kv.Length == 2)
                {
                    string key = kv[0].Trim();
                    string val = kv[1].Trim();
                    if (key.Equals("Folder", StringComparison.OrdinalIgnoreCase)) folder = val;
                    else if (key.Equals("Plugin", StringComparison.OrdinalIgnoreCase)) plugin = val;
                }
            }
            return (folder, plugin);
        }

        private string GetWatcherName(string key) => $"Upload_{key}";

        private string GetPluginNameByWatcher(string watcherName)
        {
            if (_watcherSettings.TryGetValue(watcherName, out var settings))
            {
                return settings.plugin;
            }
            return null;
        }
    }
}
