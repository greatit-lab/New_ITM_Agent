using ITM_Agent.Common;
using ITM_Agent.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ITM_Agent.ucPanel
{
    public partial class ucConfigurationPanel : UserControl
    {
        private readonly SettingsManager _settingsManager;
        private readonly ILogger _logger;

        private const string TARGET_FOLDERS_SECTION = "TargetFolders";
        private const string EXCLUDE_FOLDERS_SECTION = "ExcludeFolders";
        private const string BASE_FOLDER_SECTION = "BaseFolder";
        private const string REGEX_SECTION = "Regex";

        public ucConfigurationPanel(SettingsManager settingsManager, ILogger logger)
        {
            _settingsManager = settingsManager;
            _logger = logger;
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
            btn_TargetFolder.Click += (s, e) => AddFolder(TARGET_FOLDERS_SECTION, lb_TargetList);
            btn_TargetRemove.Click += (s, e) => RemoveSelectedItems(TARGET_FOLDERS_SECTION, lb_TargetList);
            btn_ExcludeFolder.Click += (s, e) => AddFolder(EXCLUDE_FOLDERS_SECTION, lb_ExcludeList);
            btn_ExcludeRemove.Click += (s, e) => RemoveSelectedItems(EXCLUDE_FOLDERS_SECTION, lb_ExcludeList);
            btn_BaseFolder.Click += SelectBaseFolder_Click;
            btn_RegAdd.Click += AddRegex_Click;
            btn_RegEdit.Click += EditRegex_Click;
            btn_RegRemove.Click += RemoveSelectedRegex_Click;
        }

        private void LoadAllSettings()
        {
            if (!IsHandleCreated) return;
            LoadFoldersToListBox(TARGET_FOLDERS_SECTION, lb_TargetList);
            LoadFoldersToListBox(EXCLUDE_FOLDERS_SECTION, lb_ExcludeList);
            LoadBaseFolder();
            LoadRegexToListBox();
        }

        private void LoadFoldersToListBox(string section, ListBox listBox)
        {
            listBox.Items.Clear();
            var folders = _settingsManager.GetValuesFromSection(section);
            foreach (var folder in folders)
            {
                listBox.Items.Add(folder);
            }
        }

        private void LoadBaseFolder()
        {
            var baseFolder = _settingsManager.GetValuesFromSection(BASE_FOLDER_SECTION).FirstOrDefault();
            if (!string.IsNullOrEmpty(baseFolder))
            {
                lb_BaseFolder.Text = baseFolder;
                lb_BaseFolder.ForeColor = Color.Black;
            }
            else
            {
                lb_BaseFolder.Text = "기준 폴더가 선택되지 않았습니다.";
                lb_BaseFolder.ForeColor = Color.Red;
            }
        }

        private void LoadRegexToListBox()
        {
            lb_RegexList.Items.Clear();
            var regexDict = _settingsManager.GetRegexList();
            foreach (var kvp in regexDict)
            {
                lb_RegexList.Items.Add($"{kvp.Key} -> {kvp.Value}");
            }
        }

        private void AddFolder(string section, ListBox listBox)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string newFolder = dialog.SelectedPath;
                    var currentFolders = _settingsManager.GetValuesFromSection(section);
                    if (currentFolders.Any(f => f.Equals(newFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("이미 추가된 폴더입니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    currentFolders.Add(newFolder);
                    _settingsManager.SetValues(section, currentFolders);
                    _logger.Event($"New folder added to [{section}]: {newFolder}");
                }
            }
        }

        private void RemoveSelectedItems(string section, ListBox listBox)
        {
            if (listBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("삭제할 항목을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var currentFolders = _settingsManager.GetValuesFromSection(section);
            var itemsToRemove = listBox.SelectedItems.Cast<string>().ToList();
            var updatedFolders = currentFolders.Except(itemsToRemove, StringComparer.OrdinalIgnoreCase).ToList();
            _settingsManager.SetValues(section, updatedFolders);
            _logger.Event($"{itemsToRemove.Count} item(s) removed from [{section}].");
        }

        private void SelectBaseFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _settingsManager.SetValues(BASE_FOLDER_SECTION, new List<string> { dialog.SelectedPath });
                    _logger.Event($"Base folder set to: {dialog.SelectedPath}");
                }
            }
        }

        private void AddRegex_Click(object sender, EventArgs e)
        {
            using (var form = new RegexConfigForm(GetBaseFolder()))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var regexDict = _settingsManager.GetRegexList();
                    regexDict[form.RegexPattern] = form.TargetFolder;
                    _settingsManager.SetRegexList(regexDict);
                    _logger.Event($"New regex added or updated: {form.RegexPattern}");
                }
            }
        }

        private void EditRegex_Click(object sender, EventArgs e)
        {
            if (lb_RegexList.SelectedItem == null)
            {
                MessageBox.Show("수정할 정규식을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var parts = lb_RegexList.SelectedItem.ToString().Split(new[] { "->" }, 2, StringSplitOptions.None);
            string oldPattern = parts[0].Trim();
            string oldFolder = parts[1].Trim();
            using (var form = new RegexConfigForm(GetBaseFolder()))
            {
                form.RegexPattern = oldPattern;
                form.TargetFolder = oldFolder;
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var regexDict = _settingsManager.GetRegexList();
                    if (regexDict.ContainsKey(oldPattern))
                    {
                        regexDict.Remove(oldPattern);
                    }
                    regexDict[form.RegexPattern] = form.TargetFolder;
                    _settingsManager.SetRegexList(regexDict);
                    _logger.Event($"Regex edited. Old: {oldPattern}, New: {form.RegexPattern}");
                }
            }
        }

        private void RemoveSelectedRegex_Click(object sender, EventArgs e)
        {
            if (lb_RegexList.SelectedItem == null)
            {
                MessageBox.Show("삭제할 정규식을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var parts = lb_RegexList.SelectedItem.ToString().Split(new[] { "->" }, 2, StringSplitOptions.None);
            string patternToRemove = parts[0].Trim();
            var regexDict = _settingsManager.GetRegexList();
            if (regexDict.ContainsKey(patternToRemove))
            {
                regexDict.Remove(patternToRemove);
                _settingsManager.SetRegexList(regexDict);
                _logger.Event($"Regex removed: {patternToRemove}");
            }
        }

        public void SetControlsEnabled(bool isEnabled)
        {
            this.tabControl1.Enabled = isEnabled;
        }
        
        private string GetBaseFolder()
        {
            return lb_BaseFolder.Text.Contains("선택되지 않았습니다") ? null : lb_BaseFolder.Text;
        }
    }
}
