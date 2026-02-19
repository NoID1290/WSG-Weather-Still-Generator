#nullable enable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// First-boot disclaimer dialog shown once when no configuration exists.
    /// Requires the user to acknowledge the disclaimer before continuing.
    /// </summary>
    public class DisclaimerDialog : Form
    {
        private static readonly Color BgDark = Color.FromArgb(25, 32, 45);
        private static readonly Color BgPanel = Color.FromArgb(35, 45, 60);
        private static readonly Color AccentBlue = Color.FromArgb(41, 128, 185);
        private static readonly Color AccentRed = Color.FromArgb(231, 76, 60);
        private static readonly Color TextPrimary = Color.FromArgb(220, 225, 235);
        private static readonly Color TextDim = Color.FromArgb(160, 170, 185);
        private static readonly Color TextWarning = Color.FromArgb(241, 196, 15);

        public DisclaimerDialog()
        {
            Text = "WSG — Important Disclaimer";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(600, 520);
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = BgDark;
            TopMost = true;
            ShowInTaskbar = true;

            // ── Warning icon + Title ──
            var lblIcon = new Label
            {
                Text = "⚠️",
                Font = new Font("Segoe UI", 28F),
                ForeColor = TextWarning,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(580, 50),
                Location = new Point(10, 15)
            };

            var lblTitle = new Label
            {
                Text = "Important Disclaimer",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(580, 35),
                Location = new Point(10, 60)
            };

            var lblSubtitle = new Label
            {
                Text = "Please read carefully before continuing",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Italic),
                ForeColor = TextDim,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(580, 22),
                Location = new Point(10, 95)
            };

            // ── Disclaimer content panel ──
            var contentPanel = new Panel
            {
                Location = new Point(20, 125),
                Size = new Size(545, 260),
                BackColor = BgPanel,
                AutoScroll = true,
                Padding = new Padding(15)
            };

            var lblDisclaimer1 = new Label
            {
                Text = "While this application provides access to Alert Ready emergency alerts, " +
                       "users should always verify critical information through official government " +
                       "channels and local emergency management authorities.",
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = TextWarning,
                AutoSize = true,
                MaximumSize = new Size(505, 0),
                Location = new Point(15, 15)
            };

            var lblDisclaimer2 = new Label
            {
                Text = "This application is for informational and educational purposes only. " +
                       "It should NOT be used for safety-critical decisions, navigation, emergency " +
                       "response, or protection of life and property.",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = AccentRed,
                AutoSize = true,
                MaximumSize = new Size(505, 0),
                Location = new Point(15, 90)
            };

            var lblOfficialSources = new Label
            {
                Text = "Always consult official sources for severe weather warnings:\n" +
                       "  • Environment and Climate Change Canada (weather.gc.ca)\n" +
                       "  • National Weather Service (weather.gov)\n" +
                       "  • Local emergency management authorities",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextPrimary,
                AutoSize = true,
                MaximumSize = new Size(505, 0),
                Location = new Point(15, 170)
            };

            contentPanel.Controls.Add(lblDisclaimer1);
            contentPanel.Controls.Add(lblDisclaimer2);
            contentPanel.Controls.Add(lblOfficialSources);

            // ── Acknowledgment checkbox ──
            var chkAccept = new CheckBox
            {
                Text = "I have read and understand this disclaimer",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextPrimary,
                AutoSize = true,
                Location = new Point(20, 400),
                Checked = false
            };

            // ── Accept button (disabled until checkbox is checked) ──
            var btnAccept = new Button
            {
                Text = "I Understand — Continue",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(80, 80, 90),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 40),
                Location = new Point(190, 435),
                Enabled = false,
                Cursor = Cursors.Hand
            };
            btnAccept.FlatAppearance.BorderSize = 0;

            chkAccept.CheckedChanged += (s, e) =>
            {
                btnAccept.Enabled = chkAccept.Checked;
                btnAccept.BackColor = chkAccept.Checked ? AccentBlue : Color.FromArgb(80, 80, 90);
            };

            btnAccept.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(lblIcon);
            Controls.Add(lblTitle);
            Controls.Add(lblSubtitle);
            Controls.Add(contentPanel);
            Controls.Add(chkAccept);
            Controls.Add(btnAccept);

            // Escape closes (cancel)
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }
    }
}
