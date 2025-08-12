using ITM_Agent.Common;
using ITM_Agent.Core;
using System;
using System.Windows.Forms;

namespace ITM_Agent.ucPanel
{
    public partial class ucOptionPanel : UserControl
    {
        private readonly SettingsManager _settingsManager;
        private readonly ILogger _logger;

        private const string SECTION = "Option";

        public ucOptionPanel(SettingsManager settingsManager, ILogger logger)
        {
            _settingsManager = settingsManager;
            _logger = logger;
            InitializeComponent();
            InitializeOptions();
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

        private void InitializeOptions()
        {
            cb_info_Retention.Items.Clear();
            cb_info_Retention.Items.AddRange(new object[] { "1", "3", "5", "7", "14", "30" });
            cb_info_Retention.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        private void LoadSettings()
        {
            if (!IsHandleCreated) return;

            chk_DebugMode.CheckedChanged -= chk_DebugMode_CheckedChanged;
            chk_PerfoMode.CheckedChanged -= chk_PerfoMode_CheckedChanged;
            chk_infoDel.CheckedChanged -= chk_infoDel_CheckedChanged;
            cb_info_Retention.SelectedIndexChanged -= cb_info_Retention_SelectedIndexChanged;

            try
            {
                chk_DebugMode.Checked = _settingsManager.GetValue(SECTION, "EnableDebugLog") == "1";
                chk_PerfoMode.Checked = _settingsManager.GetValue(SECTION, "EnablePerfoLog") == "1";
                bool isDeletionEnabled = _settingsManager.GetValue(SECTION, "EnableInfoAutoDel") == "1";
                chk_infoDel.Checked = isDeletionEnabled;

                string retentionDays = _settingsManager.GetValue(SECTION, "InfoRetentionDays");
                if (!string.IsNullOrEmpty(retentionDays) && cb_info_Retention.Items.Contains(retentionDays))
                {
                    cb_info_Retention.SelectedItem = retentionDays;
                }
                else
                {
                    cb_info_Retention.SelectedItem = "3";
                }
                
                UpdateRetentionControlsState(isDeletionEnabled);
            }
            finally
            {
                chk_DebugMode.CheckedChanged += chk_DebugMode_CheckedChanged;
                chk_PerfoMode.CheckedChanged += chk_PerfoMode_CheckedChanged;
                chk_infoDel.CheckedChanged += chk_infoDel_CheckedChanged;
                cb_info_Retention.SelectedIndexChanged += cb_info_Retention_SelectedIndexChanged;
            }
        }

        private void UpdateRetentionControlsState(bool isEnabled)
        {
            label3.Enabled = isEnabled;
            label4.Enabled = isEnabled;
            cb_info_Retention.Enabled = isEnabled;
        }

        private void chk_DebugMode_CheckedChanged(object sender, EventArgs e)
        {
            bool isEnabled = chk_DebugMode.Checked;
            _settingsManager.SetValue(SECTION, "EnableDebugLog", isEnabled ? "1" : "0");
            _logger.SetDebugMode(isEnabled);
        }

        private void chk_PerfoMode_CheckedChanged(object sender, EventArgs e)
        {
            bool isEnabled = chk_PerfoMode.Checked;
            _settingsManager.SetValue(SECTION, "EnablePerfoLog", isEnabled ? "1" : "0");
            PerformanceMonitor.Instance.SetFileLogging(isEnabled);
        }

        private void chk_infoDel_CheckedChanged(object sender, EventArgs e)
        {
            bool isEnabled = chk_infoDel.Checked;
            _settingsManager.SetValue(SECTION, "EnableInfoAutoDel", isEnabled ? "1" : "0");

            if (isEnabled)
            {
                if (cb_info_Retention.SelectedItem == null)
                {
                    cb_info_Retention.SelectedItem = "3";
                }
                _settingsManager.SetValue(SECTION, "InfoRetentionDays", cb_info_Retention.SelectedItem.ToString());
            }
            else
            {
                _settingsManager.SetValue(SECTION, "InfoRetentionDays", null);
                cb_info_Retention.SelectedIndex = -1;
            }

            UpdateRetentionControlsState(isEnabled);
        }

        private void cb_info_Retention_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cb_info_Retention.SelectedItem != null)
            {
                _settingsManager.SetValue(SECTION, "InfoRetentionDays", cb_info_Retention.SelectedItem.ToString());
            }
        }

        public void SetControlsEnabled(bool isEnabled)
        {
            groupBox1.Enabled = isEnabled;
            groupBox2.Enabled = isEnabled;
            if (isEnabled)
            {
                 UpdateRetentionControlsState(chk_infoDel.Checked);
            }
        }
    }
}
