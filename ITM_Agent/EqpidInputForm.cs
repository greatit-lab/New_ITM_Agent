using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ITM_Agent
{
    /// <summary>
    /// 신규 EQPID 등록을 위한 입력 폼입니다.
    /// </summary>
    public class EqpidInputForm : Form
    {
        public string Eqpid { get; private set; }
        public string Type { get; private set; }

        private TextBox textBox;
        private Button submitButton;
        private Button cancelButton;
        private Label instructionLabel;
        private Label warningLabel;
        private PictureBox pictureBox;
        private RadioButton rdo_Onto;
        private RadioButton rdo_Nova;

        public EqpidInputForm()
        {
            this.Text = "New EQPID Registry";
            this.Size = new Size(300, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.ControlBox = false;

            instructionLabel = new Label()
            {
                Text = "신규로 등록 필요한 장비명을 입력하세요.",
                Top = 20,
                Left = 25,
                Width = 300,
                BackColor = Color.Transparent
            };

            textBox = new TextBox()
            {
                Top = 70,
                Left = 125,
                Width = 110
            };

            warningLabel = new Label()
            {
                Text = "장비명을 입력해주세요.",
                Top = 100,
                Left = 115,
                ForeColor = Color.Red,
                AutoSize = true,
                Visible = false
            };

            submitButton = new Button()
            {
                Text = "Submit",
                Top = 120,
                Left = 50,
                Width = 90
            };

            cancelButton = new Button()
            {
                Text = "Cancel",
                Top = 120,
                Left = 150,
                Width = 90
            };

            pictureBox = new PictureBox()
            {
                Image = CreateTransparentImage("Resources\\Icons\\icon.png", 128),
                Location = new Point(22, 36),
                Size = new Size(75, 75),
                SizeMode = PictureBoxSizeMode.StretchImage
            };

            textBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    submitButton.PerformClick();
                }
            };

            rdo_Onto = new RadioButton()
            {
                Text = "ONTO",
                Top = 45,
                Left = 115,
                AutoSize = true,
                Checked = true
            };

            rdo_Nova = new RadioButton()
            {
                Text = "NOVA",
                Top = 45,
                Left = rdo_Onto.Left + 75,
                AutoSize = true
            };

            submitButton.Click += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    warningLabel.Visible = true;
                    return;
                }
                Eqpid = textBox.Text.Trim();
                Type = rdo_Onto.Checked ? "ONTO" : "NOVA";
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            cancelButton.Click += (sender, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.Add(instructionLabel);
            this.Controls.Add(textBox);
            this.Controls.Add(warningLabel);
            this.Controls.Add(submitButton);
            this.Controls.Add(cancelButton);
            this.Controls.Add(rdo_Onto);
            this.Controls.Add(rdo_Nova);
            this.Controls.Add(pictureBox);
            this.Controls.SetChildIndex(pictureBox, 0);
        }

        private Image CreateTransparentImage(string filePath, int alpha)
        {
            try
            {
                if (!File.Exists(filePath)) return SystemIcons.Application.ToBitmap();
                
                using (Bitmap original = new Bitmap(filePath))
                {
                    Bitmap transparentImage = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(transparentImage))
                    {
                        ColorMatrix colorMatrix = new ColorMatrix { Matrix33 = alpha / 255f };
                        ImageAttributes attributes = new ImageAttributes();
                        attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                        g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                    }
                    return transparentImage;
                }
            }
            catch
            {
                return SystemIcons.Application.ToBitmap();
            }
        }
    }
}
