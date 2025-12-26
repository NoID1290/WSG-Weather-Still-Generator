using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WeatherImageGenerator.Forms
{
    [Obsolete("Replaced by AboutDialog in MainForm.cs")]
    public class AboutForm_Old : Form
    {
        public AboutForm_Old()
        {
            this.Text = "About Weather Image Generator";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Width = 600;
            this.Height = 500;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                          ?? asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                          ?? "Weather Image Generator";
            var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? asm.GetName().Version?.ToString() ?? "Unknown";
            var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

            // --- Tab Control Setup ---
            var tabControl = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 400
            };

            // --- Tab 1: General Info ---
            var tabGeneral = new TabPage("General");
            
            var lblProduct = new Label { Text = product, Font = new Font("Segoe UI", 14F, FontStyle.Bold), Left = 20, Top = 20, Width = 520, Height = 30 };
            var lblVersion = new Label { Text = $"Version: {version}", Left = 20, Top = 60, Width = 520, Font = new Font("Segoe UI", 10F) };
            var lblCopyright = new Label { Text = copyright, Left = 20, Top = 85, Width = 520, Font = new Font("Segoe UI", 10F) };

            var githubUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator";
            var linkGithub = new LinkLabel { Text = "GitHub Repository", Left = 20, Top = 120, Width = 520, LinkColor = Color.Blue, Font = new Font("Segoe UI", 10F) };
            linkGithub.LinkClicked += (s, e) => OpenUrl(githubUrl);

            var lblDesc = new Label 
            { 
                Text = "A tool to generate weather forecast images and videos using data from Open-Meteo and alerts from Environment Canada.",
                Left = 20, Top = 160, Width = 520, Height = 60,
                Font = new Font("Segoe UI", 10F)
            };

            tabGeneral.Controls.AddRange(new Control[] { lblProduct, lblVersion, lblCopyright, linkGithub, lblDesc });

            // --- Tab 2: Credits & Attribution ---
            var tabCredits = new TabPage("Credits");
            var flowCredits = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White };

            // Local helpers for UI construction
            GroupBox CreateGroup(string title, params Control[] ctrls)
            {
                var gb = new GroupBox { Text = title, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Width = 540, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 15) };
                var pnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 25, 10, 10) };
                foreach (var c in ctrls) { c.Margin = new Padding(0, 0, 0, 5); pnl.Controls.Add(c); }
                gb.Controls.Add(pnl);
                return gb;
            }
            
            LinkLabel MkLink(string txt, string url) {
                var l = new LinkLabel { Text = txt, AutoSize = true, Font = new Font("Segoe UI", 9.5F), LinkColor = Color.FromArgb(0, 102, 204) };
                l.LinkClicked += (s, e) => OpenUrl(url);
                return l;
            }
            
            Label MkLbl(string txt, bool bold = false, bool italic = false) {
                var style = FontStyle.Regular;
                if (bold) style |= FontStyle.Bold;
                if (italic) style |= FontStyle.Italic;
                return new Label { Text = txt, AutoSize = true, Font = new Font("Segoe UI", 9F, style), ForeColor = italic ? Color.DimGray : Color.Black };
            }

            // 1. Development
            var grpDev = CreateGroup("Development",
                MkLbl("Author: NoID Softwork", true),
                MkLink("GitHub Repository", "https://github.com/NoID1290/WSG-Weather-Still-Generator"),
                MkLbl("License: MIT License", false, true),
                MkLbl("Built with .NET 8.0")
            );

            // 2. Weather Data
            var grpData = CreateGroup("Weather Data Sources",
                MkLbl("Open-Meteo.com", true),
                MkLink("https://open-meteo.com/", "https://open-meteo.com/"),
                MkLbl("License: Creative Commons Attribution 4.0 (CC-BY 4.0)", false, true),
                new Label { Height = 10 }, // spacer
                MkLbl("Environment and Climate Change Canada", true),
                MkLink("https://weather.gc.ca/", "https://weather.gc.ca/"),
                MkLbl("License: Open Government Licence - Canada", false, true)
            );

            // 3. Multimedia
            var grpMedia = CreateGroup("Multimedia",
                MkLbl("FFmpeg Project", true),
                MkLink("https://ffmpeg.org/", "https://ffmpeg.org/"),
                MkLbl("License: LGPL v2.1", false, true),
                MkLbl("FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.", false, true)
            );

            flowCredits.Controls.AddRange(new Control[] { grpDev, grpData, grpMedia });
            tabCredits.Controls.Add(flowCredits);

            // --- Tab 3: License ---
            var tabLicense = new TabPage("License");
            var txtLicense = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F) };
            txtLicense.Text = @"MIT License

Copyright (c) 2020-2026 NoID Softwork 

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";
            tabLicense.Controls.Add(txtLicense);

            // --- Tab 4: Disclaimer ---
            var tabDisclaimer = new TabPage("Disclaimer");
            var txtDisclaimer = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10F) };
            txtDisclaimer.Text = @"IMPORTANT DISCLAIMER:

1. Not for Safety-Critical Use:
This application is for informational and educational purposes only. It should NOT be used for safety-critical decisions, navigation, or protection of life and property.

2. Data Accuracy:
Weather data is retrieved from third-party sources (Open-Meteo, ECCC) and may contain errors, delays, or inaccuracies. The generated images and videos may not reflect the most current conditions.

3. Official Sources:
Always consult official sources (e.g., Environment Canada, National Weather Service, local authorities) for severe weather alerts and emergency information.

4. No Warranty:
The authors and contributors of this software provide it ""AS IS"" without any warranty of any kind. We are not responsible for any damages or losses resulting from the use of this software.";
            tabDisclaimer.Controls.Add(txtDisclaimer);

            tabControl.TabPages.Add(tabGeneral);
            tabControl.TabPages.Add(tabCredits);
            tabControl.TabPages.Add(tabLicense);
            tabControl.TabPages.Add(tabDisclaimer);

            var ok = new Button { Text = "OK", Left = 490, Top = 420, Width = 80, Height = 30 };
            ok.Click += (s, e) => this.Close();

            this.Controls.Add(tabControl);
            this.Controls.Add(ok);

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
