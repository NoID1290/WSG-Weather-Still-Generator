using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.InteropServices;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    public class MediaViewerForm : Form
    {
        // Windows API imports for embedding external windows
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const int GWL_STYLE = -16;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_CAPTION = 0x00C00000;

        private string _filePath;
        private Panel? _mediaPanel;
        private PictureBox? _pictureBox;
        private Panel? _videoPanel;
        private Label? _filenameLabel;
        private Button? _closeBtn;
        private Button? _openExternalBtn;
        private Button? _prevBtn;
        private Button? _nextBtn;
        private string[] _mediaFiles;
        private int _currentIndex;
        private Button? _playPauseBtn;
        private Process? _ffplayProcess;
        private bool _isVideoPlaying = false;

        public MediaViewerForm(string filePath, string[] allMediaFiles)
        {
            _filePath = filePath;
            _mediaFiles = allMediaFiles;
            _currentIndex = Array.IndexOf(_mediaFiles, filePath);

            InitializeComponents();
            LoadMedia();
        }

        private void InitializeComponents()
        {
            this.Text = "Media Viewer";
            this.Width = 1200;
            this.Height = 800;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.Black;
            this.KeyPreview = true;
            this.KeyDown += MediaViewerForm_KeyDown;

            // Top panel with controls
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.FromArgb(45, 45, 45)
            };

            _filenameLabel = new Label
            {
                Left = 240,
                Top = 15,
                Width = 500,
                AutoSize = false,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _prevBtn = CreateButton("◀ Previous", 10, 10, 100, 30);
            _prevBtn.Click += (s, e) => NavigateMedia(-1);
            _prevBtn.Enabled = _currentIndex > 0;

            _nextBtn = CreateButton("Next ▶", 120, 10, 100, 30);
            _nextBtn.Click += (s, e) => NavigateMedia(1);
            _nextBtn.Enabled = _currentIndex < _mediaFiles.Length - 1;

            _openExternalBtn = CreateButton("📂 Open External", this.Width - 240, 10, 120, 30);
            _openExternalBtn.Click += (s, e) => OpenExternal();
            _openExternalBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _closeBtn = CreateButton("✖ Close", this.Width - 110, 10, 100, 30);
            _closeBtn.Click += (s, e) => this.Close();
            _closeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            topPanel.Controls.Add(_prevBtn);
            topPanel.Controls.Add(_nextBtn);
            topPanel.Controls.Add(_filenameLabel);
            topPanel.Controls.Add(_openExternalBtn);
            topPanel.Controls.Add(_closeBtn);

            // Media panel
            _mediaPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };

            // Picture box for images
            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            _mediaPanel.Controls.Add(_pictureBox);

            // Video panel (will be created when needed)
            _videoPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Visible = false
            };
            _mediaPanel.Controls.Add(_videoPanel);

            this.Controls.Add(_mediaPanel);
            this.Controls.Add(topPanel);
        }

        private Button CreateButton(string text, int x, int y, int width, int height)
        {
            var btn = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void MediaViewerForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
            else if (e.KeyCode == Keys.Left && _currentIndex > 0)
            {
                NavigateMedia(-1);
            }
            else if (e.KeyCode == Keys.Right && _currentIndex < _mediaFiles.Length - 1)
            {
                NavigateMedia(1);
            }
        }

        private void NavigateMedia(int direction)
        {
            _currentIndex += direction;
            if (_currentIndex < 0) _currentIndex = 0;
            if (_currentIndex >= _mediaFiles.Length) _currentIndex = _mediaFiles.Length - 1;

            _filePath = _mediaFiles[_currentIndex];
            LoadMedia();

            if (_prevBtn != null) _prevBtn.Enabled = _currentIndex > 0;
            if (_nextBtn != null) _nextBtn.Enabled = _currentIndex < _mediaFiles.Length - 1;
        }

        private void LoadMedia()
        {
            if (_filenameLabel != null)
                _filenameLabel.Text = Path.GetFileName(_filePath);

            string extension = Path.GetExtension(_filePath).ToLowerInvariant();

            if (extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".wmv")
            {
                LoadVideo();
            }
            else if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif")
            {
                LoadImage();
            }
        }

        private void LoadImage()
        {
            if (_pictureBox == null || _videoPanel == null) return;

            // Hide video panel
            _videoPanel.Visible = false;

            // Show image
            _pictureBox.Visible = true;

            try
            {
                // Dispose old image
                if (_pictureBox.Image != null)
                {
                    _pictureBox.Image.Dispose();
                    _pictureBox.Image = null;
                }

                // Load image without locking the file
                using (var bmpTemp = new Bitmap(_filePath))
                {
                    _pictureBox.Image = new Bitmap(bmpTemp);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadVideo()
        {
            if (_pictureBox == null || _videoPanel == null) return;

            // Stop any playing video
            StopVideo();

            // Show video panel
            _videoPanel.Visible = true;
            _videoPanel.Controls.Clear();
            
            // Add PictureBox back to video panel
            _videoPanel.Controls.Add(_pictureBox);
            _pictureBox.Dock = DockStyle.Fill;
            _pictureBox.Visible = true;

            // Create video preview using FFmpeg
            try
            {
                // Extract a thumbnail from the video using FFmpeg
                string tempThumb = Path.Combine(Path.GetTempPath(), $"thumb_{Guid.NewGuid()}.jpg");
                
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{_filePath}\" -vframes 1 -q:v 2 \"{tempThumb}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                string errorOutput = "";
                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        errorOutput = process.StandardError.ReadToEnd();
                        process.WaitForExit(5000); // 5 second timeout
                    }
                }

                if (File.Exists(tempThumb) && new FileInfo(tempThumb).Length > 0)
                {
                    // Show thumbnail
                    using (var bmpTemp = new Bitmap(tempThumb))
                    {
                        _pictureBox.Image?.Dispose();
                        _pictureBox.Image = new Bitmap(bmpTemp);
                    }
                    File.Delete(tempThumb);

                    // Add playback controls
                    AddVideoControls();
                }
                else
                {
                    // Clean up empty thumbnail file
                    if (File.Exists(tempThumb))
                        File.Delete(tempThumb);
                    
                    string errorMsg = "Could not extract video preview.";
                    if (!string.IsNullOrEmpty(errorOutput))
                    {
                        // Extract relevant error info
                        var lines = errorOutput.Split('\n');
                        var relevantError = lines.FirstOrDefault(l => l.Contains("Invalid") || l.Contains("Error") || l.Contains("not found"));
                        if (!string.IsNullOrEmpty(relevantError))
                        {
                            errorMsg += $"\n\n{relevantError.Trim()}";
                        }
                    }
                    errorMsg += "\n\nMake sure FFmpeg is installed and in your PATH.\n\nClick 'Open External' to view in default player.";
                    
                    Logger.Log($"FFmpeg thumbnail extraction failed: {errorOutput}", Logger.LogLevel.Warning);
                    ShowVideoPlaceholder(errorMsg);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading video preview: {ex.Message}", Logger.LogLevel.Error);
                ShowVideoPlaceholder($"Video preview unavailable.\n\nError: {ex.Message}\n\nMake sure FFmpeg is installed.\n\nClick 'Open External' to view in default player.");
            }
        }

        private void AddVideoControls()
        {
            if (_videoPanel == null || _pictureBox == null) return;

            // Create control panel at bottom
            var controlPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 80,
                BackColor = Color.FromArgb(45, 45, 45)
            };

            // Info label at top
            var infoLabel = new Label
            {
                Left = 20,
                Top = 10,
                Width = controlPanel.Width - 40,
                ForeColor = Color.White,
                Text = "Click Play to watch the video inline",
                Font = new Font("Segoe UI", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Play button - centered
            _playPauseBtn = new Button
            {
                Text = "▶ Play Video",
                Width = 150,
                Height = 40,
                Top = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _playPauseBtn.Left = (controlPanel.Width - _playPauseBtn.Width) / 2;
            _playPauseBtn.Anchor = AnchorStyles.Top;
            _playPauseBtn.FlatAppearance.BorderSize = 0;
            _playPauseBtn.Click += PlayPauseBtn_Click;

            controlPanel.Controls.Add(infoLabel);
            controlPanel.Controls.Add(_playPauseBtn);

            _videoPanel.Controls.Add(controlPanel);
            _pictureBox.BringToFront();
        }

        private async void PlayPauseBtn_Click(object? sender, EventArgs e)
        {
            if (_playPauseBtn == null || _videoPanel == null) return;

            if (_isVideoPlaying)
            {
                // Stop the video
                StopVideo();
                _playPauseBtn.Text = "▶ Play Video";
                _isVideoPlaying = false;
            }
            else
            {
                // Play the video inline
                try
                {
                    // Hide the thumbnail
                    if (_pictureBox != null)
                        _pictureBox.Visible = false;

                    // Start ffplay process
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ffplay",
                        Arguments = $"-autoexit -noborder -loop 0 \"{_filePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };

                    _ffplayProcess = Process.Start(psi);

                    if (_ffplayProcess != null)
                    {
                        // Poll for the window handle
                        IntPtr ffplayHandle = await GetFFPlayWindowHandleAsync(_ffplayProcess);

                        if (ffplayHandle != IntPtr.Zero)
                        {
                            // Embed the ffplay window into our video panel
                            SetParent(ffplayHandle, _videoPanel.Handle);

                            // Remove window border and title bar
                            int style = GetWindowLong(ffplayHandle, GWL_STYLE);
                            style &= ~(WS_CAPTION | WS_BORDER);
                            style |= WS_CHILD;
                            SetWindowLong(ffplayHandle, GWL_STYLE, style);

                            // Resize to fit the panel
                            MoveWindow(ffplayHandle, 0, 0, _videoPanel.Width, _videoPanel.Height - 80, true);

                            // Set up resize handler
                            _videoPanel.Resize += VideoPanel_Resize;

                            // Monitor process exit
                            _ffplayProcess.EnableRaisingEvents = true;
                            _ffplayProcess.Exited += (s, ev) => 
                            {
                                if (this.InvokeRequired)
                                {
                                    this.Invoke(new Action(() => OnVideoEnded()));
                                }
                                else
                                {
                                    OnVideoEnded();
                                }
                            };

                            _playPauseBtn.Text = "⏸ Stop Video";
                            _isVideoPlaying = true;
                        }
                        else
                        {
                            MessageBox.Show("Could not get video player window handle. The video player may not have started correctly.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            StopVideo();
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = "Could not launch video player.\n\n";
                    
                    if (ex.Message.Contains("cannot find"))
                    {
                        errorMsg += "FFplay is not installed or not in your PATH.\n\n";
                        errorMsg += "FFplay comes with FFmpeg. Please install FFmpeg and add it to your system PATH.";
                    }
                    else
                    {
                        errorMsg += $"Error: {ex.Message}";
                    }
                    
                    MessageBox.Show(errorMsg, "Video Playback Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Logger.Log($"Failed to launch ffplay: {ex.Message}", Logger.LogLevel.Error);
                }
            }
        }

        private async Task<IntPtr> GetFFPlayWindowHandleAsync(Process process)
        {
            // Poll for the window handle for up to 5 seconds
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                
                if (process.HasExited)
                    return IntPtr.Zero;

                process.Refresh();
                IntPtr handle = process.MainWindowHandle;
                
                if (handle != IntPtr.Zero)
                    return handle;
            }
            
            return IntPtr.Zero;
        }

        private void VideoPanel_Resize(object? sender, EventArgs e)
        {
            if (_ffplayProcess != null && !_ffplayProcess.HasExited && _videoPanel != null)
            {
                IntPtr ffplayHandle = _ffplayProcess.MainWindowHandle;
                if (ffplayHandle != IntPtr.Zero)
                {
                    MoveWindow(ffplayHandle, 0, 0, _videoPanel.Width, _videoPanel.Height - 80, true);
                }
            }
        }

        private void OnVideoEnded()
        {
            _isVideoPlaying = false;
            if (_playPauseBtn != null)
            {
                _playPauseBtn.Text = "▶ Play Video";
            }
            if (_pictureBox != null)
            {
                _pictureBox.Visible = true;
            }
        }

        private void StopVideo()
        {
            if (_ffplayProcess != null && !_ffplayProcess.HasExited)
            {
                try
                {
                    _ffplayProcess.Kill();
                    _ffplayProcess.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to stop ffplay process: {ex}");
                }
                finally
                {
                    _ffplayProcess?.Dispose();
                    _ffplayProcess = null;
                }
            }

            if (_pictureBox != null)
            {
                _pictureBox.Visible = true;
            }
        }

        private void ShowVideoPlaceholder(string message)
        {
            if (_videoPanel == null) return;

            _videoPanel.Controls.Clear();
            
            // Create a container panel for better layout
            var containerPanel = new Panel
            {
                Width = 500,
                Height = 250,
                BackColor = Color.FromArgb(40, 40, 40)
            };
            containerPanel.Location = new Point(
                (_videoPanel.Width - containerPanel.Width) / 2,
                (_videoPanel.Height - containerPanel.Height) / 2
            );
            containerPanel.Anchor = AnchorStyles.None;

            var lblInfo = new Label
            {
                Text = message,
                Width = 480,
                Top = 20,
                Left = 10,
                Height = 150,
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 10F),
                AutoSize = false
            };

            var playBtn = new Button
            {
                Text = "▶ Play Video Externally",
                Width = 200,
                Height = 50,
                Top = 180,
                Left = (containerPanel.Width - 200) / 2,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            playBtn.FlatAppearance.BorderSize = 0;
            playBtn.Click += (s, e) => OpenExternal();

            containerPanel.Controls.Add(lblInfo);
            containerPanel.Controls.Add(playBtn);
            _videoPanel.Controls.Add(containerPanel);
        }

        private void OpenExternal()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopVideo();
                
                if (_pictureBox?.Image != null)
                {
                    _pictureBox.Image.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
