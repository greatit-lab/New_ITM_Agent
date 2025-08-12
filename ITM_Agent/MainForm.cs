using ITM_Agent.Common;
using ITM_Agent.Core;
using ITM_Agent.ucPanel;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;

namespace ITM_Agent
{
    public partial class MainForm : Form
    {
        // --- 주입받는 서비스 및 관리자 클래스 ---
        private readonly ILogger _logger;
        private readonly SettingsManager _settingsManager;
        private readonly DatabaseRepository _dbRepository;
        private readonly FileProcessingService _fileProcessingService;
        private readonly EqpidManager _eqpidManager;
        private readonly InfoRetentionCleaner _infoCleaner;
        private readonly PdfMergeManager _pdfMergeManager;

        // --- UI 패널 ---
        private ucConfigurationPanel _ucConfigPanel;
        private ucOverrideNamesPanel _ucOverrideNamesPanel;
        private ucImageTransPanel _ucImageTransPanel;
        private ucUploadPanel _ucUploadPanel;
        private ucPluginPanel _ucPluginPanel;
        private ucOptionPanel _ucOptionPanel;

        // --- 상태 변수 ---
        private bool _isRunning = false;
        private bool _pluginListDirty = false; // 플러그인 목록 변경 여부 추적
        private const string AppVersion = "v2.1.0 Final";

        public MainForm(
            ILogger logger,
            SettingsManager settingsManager,
            DatabaseRepository dbRepository,
            FileProcessingService fileProcessingService)
        {
            _logger = logger;
            _settingsManager = settingsManager;
            _dbRepository = dbRepository;
            _fileProcessingService = fileProcessingService;

            InitializeComponent();

            _eqpidManager = new EqpidManager(_settingsManager, _logger, _dbRepository, AppVersion);
            _infoCleaner = new InfoRetentionCleaner(_settingsManager, _logger);
            bool isDebug = _settingsManager.GetValue("Option", "EnableDebugLog") == "1";
            _pdfMergeManager = new PdfMergeManager(_logger);

            InitializeUserControls();
            InitializeTrayIcon();
            RegisterMenuEvents();

            this.Text = $"ITM Agent - {AppVersion}";
            this.Shown += MainForm_Shown; // 폼이 완전히 표시된 후 이벤트 핸들러 등록
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            this.Icon = new Icon(@"Resources\Icons\icon.ico");

            string eqpid = _eqpidManager.CheckAndGetEqpid();
            if (string.IsNullOrEmpty(eqpid))
            {
                using (var form = new EqpidInputForm())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        _eqpidManager.RegisterNewEqpid(form.Eqpid, form.Type);
                        eqpid = form.Eqpid.ToUpper();
                    }
                    else
                    {
                        MessageBox.Show("EQPID 입력이 취소되었습니다. 프로그램을 종료합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        this.Load -= MainForm_Load;
                        Application.Exit();
                        return;
                    }
                }
            }
            lb_eqpid.Text = $"Eqpid: {eqpid}";

            ShowUserControl(_ucConfigPanel);
            UpdateUIBasedOnState();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            InitializeAllPanels();
            _settingsManager.SettingsChanged += OnSettingsChanged;
            _logger.Event("[MainForm] UI is fully loaded and ready.");
        }

        private void RegisterMenuEvents()
        {
            tsm_Categorize.Click += (s, e) => ShowUserControl(_ucConfigPanel);
            tsm_OverrideNames.Click += (s, e) => ShowUserControl(_ucOverrideNamesPanel);
            tsm_ImageTrans.Click += (s, e) => ShowUserControl(_ucImageTransPanel);
            tsm_UploadData.Click += (s, e) => ShowUserControl(_ucUploadPanel);
            tsm_PluginList.Click += (s, e) => ShowUserControl(_ucPluginPanel);
            tsm_Option.Click += (s, e) => ShowUserControl(_ucOptionPanel);
            tsm_AboutInfo.Click += (s, e) => new AboutInfoForm(AppVersion).ShowDialog(this);
        }

        private void InitializeUserControls()
        {
            _ucConfigPanel = new ucConfigurationPanel(_settingsManager, _logger);
            _ucPluginPanel = new ucPluginPanel(_settingsManager, _logger);
            _ucPluginPanel.PluginsChanged += (sender, e) => NotifyPluginsChanged();
            _ucImageTransPanel = new ucImageTransPanel(_settingsManager, _logger, _fileProcessingService, _pdfMergeManager);
            _ucOverrideNamesPanel = new ucOverrideNamesPanel(_settingsManager, _logger, _fileProcessingService);
            _ucUploadPanel = new ucUploadPanel(_settingsManager, _logger, _fileProcessingService, _ucPluginPanel, _ucOverrideNamesPanel);
            _ucOptionPanel = new ucOptionPanel(_settingsManager, _logger);
        }

        private void InitializeAllPanels()
        {
            _ucConfigPanel.InitializePanelData();
            _ucPluginPanel.InitializePanelData();
            _ucUploadPanel.InitializePanelData();
            _ucImageTransPanel.InitializePanelData();
            _ucOverrideNamesPanel.InitializePanelData();
            _ucOptionPanel.InitializePanelData();
            UpdateUIBasedOnState();
        }

        private void OnSettingsChanged()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(RefreshAllPanelsUI));
            }
            else
            {
                RefreshAllPanelsUI();
            }
        }

        private void RefreshAllPanelsUI()
        {
            _ucConfigPanel.RefreshData();
            _ucPluginPanel.RefreshData();
            _ucUploadPanel.RefreshData();
            _ucImageTransPanel.RefreshData();
            _ucOverrideNamesPanel.RefreshData();
            _ucOptionPanel.RefreshData();
            UpdateUIBasedOnState();
        }

        public void NotifyPluginsChanged()
        {
            _pluginListDirty = true;
            _logger.Event("[MainForm] Plugin list has changed. Flag set to dirty.");
            if (pMain.Controls.Count > 0 && pMain.Controls[0] == _ucUploadPanel)
            {
                _ucUploadPanel.RefreshData();
                _pluginListDirty = false;
            }
        }

        private void ShowUserControl(UserControl control)
        {
            if (control == null) return;
            if (control == _ucUploadPanel && _pluginListDirty)
            {
                _ucUploadPanel.RefreshData();
                _pluginListDirty = false;
                _logger.Event("[MainForm] Dirty plugin list reflected on ucUploadPanel.");
            }
            pMain.Controls.Clear();
            pMain.Controls.Add(control);
            control.Dock = DockStyle.Fill;
        }

        private void UpdateUIBasedOnState()
        {
            bool isReady = _settingsManager.IsReadyToRun();
            string statusText = "Stopped";
            Color statusColor = Color.Red;

            if (_isRunning)
            {
                statusText = "Running...";
                statusColor = Color.Blue;
            }
            else if (isReady)
            {
                statusText = "Ready to Run";
                statusColor = Color.Green;
            }

            UpdateMainStatus(statusText, statusColor);
            btn_Run.Enabled = !_isRunning && isReady;
            btn_Stop.Enabled = _isRunning;
            btn_Quit.Enabled = !_isRunning;
            menuStrip1.Enabled = !_isRunning;
            UpdateAllPanelsState();
        }

        private void UpdateAllPanelsState()
        {
            _ucConfigPanel.SetControlsEnabled(!_isRunning);
            _ucPluginPanel.SetControlsEnabled(!_isRunning);
            _ucImageTransPanel.SetControlsEnabled(!_isRunning);
            _ucOverrideNamesPanel.SetControlsEnabled(!_isRunning);
            _ucUploadPanel.SetControlsEnabled(!_isRunning);
            _ucOptionPanel.SetControlsEnabled(!_isRunning);
        }

        private void UpdateMainStatus(string status, Color color)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => {
                    ts_Status.Text = status;
                    ts_Status.ForeColor = color;
                }));
            }
            else
            {
                ts_Status.Text = status;
                ts_Status.ForeColor = color;
            }
        }

        private void Btn_Run_Click(object sender, EventArgs e)
        {
            _logger.Event("Run button clicked. Starting all services.");
            _isRunning = true;
            PerformanceDbWriter.Start(lb_eqpid.Text, _logger, _dbRepository);
            _ucImageTransPanel.StartWatching();
            _ucOverrideNamesPanel.StartWatching();
            _ucUploadPanel.StartWatching();
            UpdateUIBasedOnState();
        }

        private void Btn_Stop_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("프로그램을 중지하시겠습니까?", "작업 중지 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _logger.Event("Stop button clicked. Stopping all services.");
            _isRunning = false;
            PerformanceDbWriter.Stop();
            _ucImageTransPanel.StopWatching();
            _ucOverrideNamesPanel.StopWatching();
            _ucUploadPanel.StopWatching();
            UpdateUIBasedOnState();
        }

        private void btn_Quit_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("프로그램을 완전히 종료하시겠습니까?", "종료 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                PerformQuit();
            }
        }

        private void PerformQuit()
        {
            _logger.Event("Quit requested. Cleaning up resources...");
            trayIcon.Visible = false;
            trayIcon.Dispose();
            _infoCleaner.Dispose();
            _fileProcessingService.Dispose();
            _settingsManager.Dispose();
            Application.Exit();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                trayIcon.ShowBalloonTip(1000, "ITM Agent", "프로그램이 백그라운드에서 계속 실행됩니다.", ToolTipIcon.Info);
            }
        }

        private void NewMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("현재 설정을 초기화하시겠습니까? (EQPID 정보는 유지됩니다)", "새 설정", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _settingsManager.ResetSettingsExceptEqpid();
                MessageBox.Show("설정이 초기화되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OpenMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string currentIniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.ini");
                        File.Copy(dialog.FileName, currentIniPath, true);
                        _logger.Event($"Settings loaded from {dialog.FileName}");
                        MessageBox.Show("설정 파일을 성공적으로 불러왔습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to load settings from file: {ex.Message}");
                        MessageBox.Show($"파일을 불러오는 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void SaveAsMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog { Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*", FileName = "Settings.ini" })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string currentIniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.ini");
                        File.Copy(currentIniPath, dialog.FileName, true);
                        _logger.Event($"Settings saved to {dialog.FileName}");
                        MessageBox.Show("현재 설정을 성공적으로 저장했습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to save settings to file: {ex.Message}");
                        MessageBox.Show($"파일을 저장하는 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void QuitMenuItem_Click(object sender, EventArgs e)
        {
            btn_Quit_Click(sender, e);
        }

        #region Tray Icon
        private NotifyIcon trayIcon;

        private void InitializeTrayIcon()
        {
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Quit", null, (s, e) => btn_Quit_Click(s, e));

            trayIcon = new NotifyIcon
            {
                Icon = new Icon(@"Resources\Icons\icon.ico"),
                ContextMenuStrip = trayMenu,
                Visible = true,
                Text = "ITM Agent"
            };

            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
        }
        #endregion
    }
}
