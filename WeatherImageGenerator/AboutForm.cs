using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WSG_WeatherImageGenerator.Forms
{
    [Obsolete("Replaced by AboutDialog in MainForm.cs")]
    public class AboutForm_Old : Form
    {
        public AboutForm()
        {
            this.Text = "About";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Width = 560;
            this.Height = 360;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                          ?? asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                          ?? "Weather Image Generator";
            var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? asm.GetName().Version?.ToString() ?? "Unknown";
            var company = asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
            var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

            var lblProduct = new Label { Text = product, Font = new Font("Segoe UI", 12F, FontStyle.Bold), Left = 16, Top = 14, Width = 520, Height = 26 };
            var lblVersion = new Label { Text = $"Version: {version}", Left = 16, Top = 44, Width = 520 };
            var lblCompany = new Label { Text = company, Left = 16, Top = 64, Width = 520 };
            var lblCopyright = new Label { Text = copyright, Left = 16, Top = 84, Width = 520 };

            // Replace with your repository URL if available
            var githubUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator";
            var linkGithub = new LinkLabel { Text = githubUrl, Left = 16, Top = 110, Width = 520, LinkColor = Color.Blue };
            linkGithub.LinkClicked += (s, e) => OpenUrl(githubUrl);

            var licenseUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator/blob/main/LICENSE";
            var linkLicense = new LinkLabel { Text = "MIT License", Left = 16, Top = 136, Width = 520, LinkColor = Color.Blue };
            linkLicense.LinkClicked += (s, e) => OpenUrl(licenseUrl);

            var txtCredits = new TextBox { Left = 16, Top = 160, Width = 510, Height = 120, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            txtCredits.Text = "Credits:\r\n - NoID Softwork (Author)\r\n - OpenMeteo API (data)\r\n - Environment Canada (ECCC) alerts\r\n\r\nBuilt with .NET 8.0\r\n";

            var ok = new Button { Text = "OK", Left = 450, Top = 290, Width = 80, Height = 28 };
            ok.Click += (s, e) => this.Close();

            this.Controls.AddRange(new Control[] { lblProduct, lblVersion, lblCompany, lblCopyright, linkGithub, linkLicense, txtCredits, ok });

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) this.Close(); };
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* best-effort only */ }
        }
    }
}
