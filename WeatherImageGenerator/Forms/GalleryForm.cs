using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    public class GalleryForm : Form
    {
        private FlowLayoutPanel? _galleryPanel;
        private Button? _refreshBtn;
        private Button? _openOutputBtn;
        private Label? _statusLabel;
        private Panel? _topPanel;
        
        // Theme colors (will be set from config)
        private Color _themeTextColor = Color.Black;
        private Color _themeAccentColor = Color.Blue;

        public GalleryForm()
        {
            this.Text = "🖼 Gallery - Weather Images & Videos";
            this.Width = 1000;
            this.Height = 700;
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Top panel with controls
            _topPanel = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.WhiteSmoke };
            
            _refreshBtn = CreateStyledButton("🔄 Refresh", 10, 10, 100, 30, Color.Gray, Color.White);
            _refreshBtn.Click += (s, e) => RefreshGallery();
            
            _openOutputBtn = CreateStyledButton("📁 Open Folder", 120, 10, 120, 30, Color.Gray, Color.White);
            _openOutputBtn.Click += (s, e) => OpenOutputDirectory();
            
            _statusLabel = new Label 
            { 
                Left = 250, 
                Top = 15, 
                Width = 500, 
                Height = 25,
                Text = "Ready",
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _topPanel.Controls.Add(_refreshBtn);
            _topPanel.Controls.Add(_openOutputBtn);
            _topPanel.Controls.Add(_statusLabel);

            // Gallery panel
            _galleryPanel = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Fill, 
                AutoScroll = true, 
                BackColor = Color.White,
                Padding = new Padding(10)
            };

            this.Controls.Add(_galleryPanel);
            this.Controls.Add(_topPanel);

            // Apply theme
            var config = ConfigManager.LoadConfig();
            ApplyTheme(config.Theme);

            // Load gallery on form load
            this.Load += (s, e) => RefreshGallery();
        }

        private Button CreateStyledButton(string text, int x, int y, int width, int height, Color bgColor, Color fgColor)
        {
            var btn = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                BackColor = bgColor,
                ForeColor = fgColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(Math.Min(bgColor.R + 20, 255), Math.Min(bgColor.G + 20, 255), Math.Min(bgColor.B + 20, 255));
            return btn;
        }

        private void RefreshGallery()
        {
            if (_galleryPanel == null) return;
            
            if (_statusLabel != null) _statusLabel.Text = "Loading gallery...";
            this.Cursor = Cursors.WaitCursor;
            
            // Suspend layout to avoid flickering
            _galleryPanel.SuspendLayout();
            
            // Dispose old images to free memory
            foreach (Control c in _galleryPanel.Controls)
            {
                if (c is Panel p)
                {
                    foreach (Control pc in p.Controls)
                    {
                        if (pc is PictureBox pb && pb.Image != null)
                        {
                            pb.Image.Dispose();
                        }
                    }
                }
                c.Dispose();
            }
            _galleryPanel.Controls.Clear();

            try
            {
                var config = ConfigManager.LoadConfig();
                string path = config.ImageGeneration?.OutputDirectory ?? "WeatherImages";
                if (!System.IO.Path.IsPathRooted(path))
                {
                    path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path);
                }

                if (!System.IO.Directory.Exists(path)) 
                {
                    var lbl = new Label 
                    { 
                        Text = "Output directory does not exist yet.", 
                        AutoSize = true, 
                        Margin = new Padding(20),
                        Font = new Font("Segoe UI", 10F),
                        ForeColor = _themeTextColor
                    };
                    _galleryPanel.Controls.Add(lbl);
                    _galleryPanel.ResumeLayout();
                    if (_statusLabel != null) _statusLabel.Text = "No output directory found";
                    this.Cursor = Cursors.Default;
                    return;
                }

                var files = System.IO.Directory.GetFiles(path)
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => System.IO.File.GetCreationTime(f))
                    .Take(50); // Show up to 50 items

                if (!files.Any())
                {
                    var lbl = new Label 
                    { 
                        Text = "No images or videos found.", 
                        AutoSize = true, 
                        Margin = new Padding(20),
                        Font = new Font("Segoe UI", 10F),
                        ForeColor = _themeTextColor
                    };
                    _galleryPanel.Controls.Add(lbl);
                    if (_statusLabel != null) _statusLabel.Text = "No media files found";
                }
                else
                {
                    int count = 0;
                    foreach (var file in files)
                    {
                        var container = new Panel 
                        { 
                            Width = 180, 
                            Height = 160, 
                            Margin = new Padding(5), 
                            BackColor = _themeTextColor == Color.White ? Color.FromArgb(60, 60, 60) : Color.WhiteSmoke 
                        };
                        
                        var pb = new PictureBox 
                        { 
                            Width = 170, 
                            Height = 120, 
                            SizeMode = PictureBoxSizeMode.Zoom, 
                            Top = 5, 
                            Left = 5,
                            BackColor = Color.Black,
                            Cursor = Cursors.Hand
                        };

                        var lbl = new Label 
                        { 
                            Text = System.IO.Path.GetFileName(file), 
                            Top = 130, 
                            Left = 5, 
                            Width = 170, 
                            Height = 25,
                            AutoEllipsis = true,
                            TextAlign = ContentAlignment.MiddleCenter,
                            Font = new Font("Segoe UI", 8F),
                            ForeColor = _themeTextColor
                        };

                        if (file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        {
                            // Placeholder for video
                            var bmp = new Bitmap(170, 120);
                            using (var g = Graphics.FromImage(bmp))
                            {
                                g.Clear(Color.DarkBlue);
                                g.DrawString("VIDEO", new Font("Segoe UI", 14, FontStyle.Bold), Brushes.White, new PointF(55, 50));
                                g.DrawString("▶", new Font("Segoe UI", 20, FontStyle.Bold), Brushes.White, new PointF(75, 70));
                            }
                            pb.Image = bmp;
                        }
                        else
                        {
                            try 
                            {
                                // Load image without locking file
                                using (var bmpTemp = new Bitmap(file))
                                {
                                    pb.Image = new Bitmap(bmpTemp);
                                }
                            }
                            catch 
                            {
                                // Fallback if image is corrupted or locked
                                var bmp = new Bitmap(170, 120);
                                using (var g = Graphics.FromImage(bmp))
                                {
                                    g.Clear(Color.Red);
                                    g.DrawString("ERROR", new Font("Segoe UI", 10, FontStyle.Bold), Brushes.White, new PointF(60, 50));
                                }
                                pb.Image = bmp;
                            }
                        }

                        pb.Click += (s, e) => {
                            try {
                                // Get all media files for navigation
                                var allFiles = System.IO.Directory.GetFiles(path)
                                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                                                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                                    .OrderByDescending(f => System.IO.File.GetCreationTime(f))
                                    .Take(50)
                                    .ToArray();
                                
                                var viewer = new MediaViewerForm(file, allFiles);
                                viewer.ShowDialog(this);
                            } 
                            catch (Exception ex) 
                            {
                                MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        };

                        // Add tooltip
                        var tt = new ToolTip();
                        tt.SetToolTip(pb, file);

                        container.Controls.Add(pb);
                        container.Controls.Add(lbl);
                        _galleryPanel.Controls.Add(container);
                        count++;
                    }
                    
                    if (_statusLabel != null) _statusLabel.Text = $"Showing {count} media file(s)";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error refreshing gallery: {ex.Message}", Logger.LogLevel.Error);
                if (_statusLabel != null) _statusLabel.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading gallery: {ex.Message}", "Gallery Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _galleryPanel.ResumeLayout();
                this.Cursor = Cursors.Default;
            }
        }

        private void OpenOutputDirectory()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                string path = config.ImageGeneration?.OutputDirectory ?? "WeatherImages";
                if (!System.IO.Path.IsPathRooted(path))
                {
                    path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path);
                }

                if (System.IO.Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    Logger.Log($"Opened output directory: {path}");
                }
                else
                {
                    MessageBox.Show("Output directory does not exist yet.", "Directory Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening output directory: {ex.Message}", Logger.LogLevel.Error);
                MessageBox.Show($"Could not open directory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyTheme(string theme)
        {
            Color primaryColor, secondaryColor, buttonColor, textColor, accentColor;
            
            if (theme == "Dark")
            {
                primaryColor = Color.FromArgb(30, 30, 30);
                secondaryColor = Color.FromArgb(45, 45, 45);
                buttonColor = Color.FromArgb(60, 60, 60);
                textColor = Color.White;
                accentColor = Color.FromArgb(0, 122, 204);
            }
            else
            {
                primaryColor = Color.White;
                secondaryColor = Color.WhiteSmoke;
                buttonColor = Color.Gainsboro;
                textColor = Color.Black;
                accentColor = Color.Blue;
            }
            
            _themeTextColor = textColor;
            _themeAccentColor = accentColor;
            
            this.BackColor = primaryColor;
            
            if (_topPanel != null)
            {
                _topPanel.BackColor = primaryColor;
            }
            
            if (_galleryPanel != null)
            {
                _galleryPanel.BackColor = secondaryColor;
            }
            
            if (_statusLabel != null)
            {
                _statusLabel.ForeColor = textColor;
            }
            
            // Update button colors
            void SetBtn(Button? b, Color bg, Color fg)
            {
                if (b != null)
                {
                    b.BackColor = bg;
                    b.ForeColor = fg;
                    b.FlatAppearance.BorderColor = ControlPaint.Light(bg, 0.2f);
                    b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
                    b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.15f);
                }
            }
            
            SetBtn(_refreshBtn, buttonColor, textColor);
            SetBtn(_openOutputBtn, buttonColor, textColor);
        }
    }
}
