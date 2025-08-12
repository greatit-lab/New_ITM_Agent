using ITM_Agent.Properties;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ITM_Agent
{
    public partial class AboutInfoForm : Form
    {
        public AboutInfoForm(string versionInfo)
        {
            InitializeComponent();
            LoadIconSafe();
            
            lb_Version.Text = versionInfo; 

            this.label1.Text = Resources.AboutInfo_Desc1;
            this.label2.Text = Resources.AboutInfo_Desc2;
            this.label3.Text = Resources.AboutInfo_Desc3;
            this.label4.Text = Resources.AboutInfo_Desc4;
        }

        private void LoadIconSafe()
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "Resources", "Icons", "icon.png");
                Image baseImg = File.Exists(path) ? Image.FromFile(path) : SystemIcons.Application.ToBitmap();
                picIcon.Image = ApplyOpacity(baseImg, 0.5f);
            }
            catch
            {
                picIcon.Image = SystemIcons.Application.ToBitmap();
            }
        }

        private static Bitmap ApplyOpacity(Image src, float opacity)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                var matrix = new ColorMatrix { Matrix33 = opacity };
                var attr = new ImageAttributes();
                attr.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
            }
            return bmp;
        }
    }
}
