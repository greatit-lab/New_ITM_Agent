using ITM_Agent.Common;
using ITM_Agent.Core;
using ITM_Agent.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ITM_Agent.ucPanel
{
    public partial class ucPluginPanel : UserControl
    {
        private readonly SettingsManager _settingsManager;
        private readonly ILogger _logger;
        private List<PluginListItem> _loadedPlugins = new List<PluginListItem>();

        private const string PLUGIN_SECTION = "RegPlugins";

        public ucPluginPanel(SettingsManager settingsManager, ILogger logger)
        {
            _settingsManager = settingsManager;
            _logger = logger;
            InitializeComponent();
        }

        public void InitializePanelData()
        {
            RefreshData();
        }

        public void RefreshData()
        {
            if (!IsHandleCreated) return;
            if (this.InvokeRequired) this.Invoke(new Action(LoadPluginsFromSettings));
            else LoadPluginsFromSettings();
        }

        private void LoadPluginsFromSettings()
        {
            if (!IsHandleCreated) return;

            _loadedPlugins.Clear();
            var pluginEntries = _settingsManager.GetLinesFromSection(PLUGIN_SECTION);

            foreach (string entry in pluginEntries)
            {
                var parts = entry.Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;

                string relativePath = parts[1].Trim();
                string absolutePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

                if (!File.Exists(absolutePath))
                {
                    _logger.Error($"Plugin DLL not found: {absolutePath}");
                    continue;
                }
                
                try
                {
                    byte[] dllBytes = File.ReadAllBytes(absolutePath);
                    Assembly asm = Assembly.Load(dllBytes);

                    var item = new PluginListItem
                    {
                        PluginName = asm.GetName().Name,
                        PluginVersion = asm.GetName().Version.ToString(),
                        AssemblyPath = absolutePath
                    };
                    _loadedPlugins.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to load plugin assembly {absolutePath}: {ex.Message}");
                }
            }
            UpdatePluginListDisplay();
        }

        private void UpdatePluginListDisplay()
        {
            if (!IsHandleCreated) return;
            lb_PluginList.Items.Clear();
            for (int i = 0; i < _loadedPlugins.Count; i++)
            {
                lb_PluginList.Items.Add($"{i + 1}. {_loadedPlugins[i]}");
            }
        }

        private void btn_PlugAdd_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory,
                Title = "Select a Plugin DLL"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                string selectedDllPath = dialog.FileName;

                try
                {
                    byte[] dllBytes = File.ReadAllBytes(selectedDllPath);
                    Assembly asm = Assembly.Load(dllBytes);
                    string pluginName = asm.GetName().Name;

                    if (_loadedPlugins.Any(p => p.PluginName.Equals(pluginName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("이미 등록된 플러그인입니다.", "중복", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    string libraryFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library");
                    Directory.CreateDirectory(libraryFolder);
                    string destDllPath = Path.Combine(libraryFolder, Path.GetFileName(selectedDllPath));
                    File.Copy(selectedDllPath, destDllPath, true);

                    var newItem = new PluginListItem
                    {
                        PluginName = pluginName,
                        PluginVersion = asm.GetName().Version.ToString(),
                        AssemblyPath = destDllPath
                    };

                    _loadedPlugins.Add(newItem);
                    string relativePath = Path.Combine("Library", Path.GetFileName(destDllPath));
                    _settingsManager.SetValue(PLUGIN_SECTION, newItem.PluginName, relativePath);
                    _logger.Event($"Plugin registered: {newItem}");
                    
                    (this.FindForm() as MainForm)?.NotifyPluginsChanged();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to load plugin from {selectedDllPath}: {ex.Message}");
                    MessageBox.Show("플러그인을 로드하는 중 오류가 발생했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btn_PlugRemove_Click(object sender, EventArgs e)
        {
            if (lb_PluginList.SelectedItem == null)
            {
                MessageBox.Show("삭제할 플러그인을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectedText = lb_PluginList.SelectedItem.ToString();
            string pluginName = Regex.Match(selectedText, @"^\d+\.\s*([^(\s]+)").Groups[1].Value;
            var itemToRemove = _loadedPlugins.FirstOrDefault(p => p.PluginName.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
            if (itemToRemove == null) return;
            
            if (MessageBox.Show($"'{itemToRemove.PluginName}' 플러그인을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                if (File.Exists(itemToRemove.AssemblyPath))
                {
                    File.Delete(itemToRemove.AssemblyPath);
                }
                _settingsManager.SetValue(PLUGIN_SECTION, itemToRemove.PluginName, null);
                _logger.Event($"Plugin removed: {itemToRemove.PluginName}");

                (this.FindForm() as MainForm)?.NotifyPluginsChanged();
            }
            catch (Exception ex)
            {
                 _logger.Error($"Failed to remove plugin {itemToRemove.PluginName}: {ex.Message}");
                 MessageBox.Show("플러그인을 삭제하는 중 오류가 발생했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        public List<PluginListItem> GetLoadedPlugins()
        {
            return new List<PluginListItem>(_loadedPlugins);
        }

        public void SetControlsEnabled(bool isEnabled)
        {
            btn_PlugAdd.Enabled = isEnabled;
            btn_PlugRemove.Enabled = isEnabled;
            lb_PluginList.Enabled = isEnabled;
        }
    }
}
