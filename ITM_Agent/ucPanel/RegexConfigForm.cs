using ITM_Agent.Properties;
using System;
using System.Windows.Forms;

namespace ITM_Agent.ucPanel
{
    public partial class RegexConfigForm : Form
    {
        private readonly string _baseFolderPath;

        public string RegexPattern
        {
            get => tb_RegInput.Text;
            set => tb_RegInput.Text = value;
        }

        public string TargetFolder
        {
            get => tb_RegFolder.Text;
            set => tb_RegFolder.Text = value;
        }

        public RegexConfigForm(string baseFolderPath)
        {
            _baseFolderPath = string.IsNullOrEmpty(baseFolderPath) || !System.IO.Directory.Exists(baseFolderPath)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseFolderPath;

            InitializeComponent();
            InitializeForm();
        }

        private void InitializeForm()
        {
            this.Text = "Regex Configuration";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            tb_RegFolder.ReadOnly = true;

            btn_RegSelectFolder.Click += Btn_RegSelectFolder_Click;
            btn_RegApply.Click += Btn_RegApply_Click;
            btn_RegCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
        }

        private void Btn_RegSelectFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "정규식과 일치하는 파일을 복사할 대상 폴더를 선택하세요.";
                dialog.SelectedPath = _baseFolderPath;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    TargetFolder = dialog.SelectedPath;
                }
            }
        }

        private void Btn_RegApply_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RegexPattern))
            {
                MessageBox.Show(Resources.MSG_REGEX_REQUIRED, 
                                Resources.CAPTION_WARNING,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TargetFolder))
            {
                MessageBox.Show(Resources.MSG_FOLDER_REQUIRED,
                                Resources.CAPTION_WARNING,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
