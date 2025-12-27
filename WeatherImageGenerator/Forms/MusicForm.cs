using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Form for managing music tracks for video generation
    /// </summary>
    public class MusicForm : Form
    {
        private ListBox lstMusic;
        private TextBox txtMusicName;
        private TextBox txtMusicPath;
        private Button btnBrowse;
        private Button btnAdd;
        private Button btnEdit;
        private Button btnRemove;
        private Button btnMoveUp;
        private Button btnMoveDown;
        private Button btnSave;
        private Button btnCancel;
        private RadioButton rbRandom;
        private RadioButton rbSpecific;
        private Label lblCount;
        private Label lblSelection;
        private GroupBox grpSelection;
        private CheckBox chkEnableMusic;
        private readonly List<MusicEntry> _musicTracks;

        public MusicForm()
        {
            InitializeComponents();
            _musicTracks = new List<MusicEntry>();
            LoadMusicTracks();
        }

        private void InitializeComponents()
        {
            this.Text = "Manage Music";
            this.Width = 650;
            this.Height = 600;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // Info label
            var lblInfo = new Label
            {
                Text = "Manage music tracks for video generation. Add demo tracks or your own music files.",
                Left = 10,
                Top = 10,
                Width = 610,
                Height = 30,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Music list
            lstMusic = new ListBox
            {
                Left = 10,
                Top = 50,
                Width = 400,
                Height = 250,
                SelectionMode = SelectionMode.One,
                Font = new Font("Segoe UI", 9.5F)
            };
            lstMusic.SelectedIndexChanged += LstMusic_SelectedIndexChanged;
            lstMusic.DoubleClick += (s, e) => EditMusic();

            // Count label
            lblCount = new Label
            {
                Left = 10,
                Top = 305,
                Width = 400,
                Text = "0 tracks",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic)
            };

            // === Input Controls ===
            var lblName = new Label
            {
                Text = "Track Name:",
                Left = 420,
                Top = 50,
                Width = 200,
                AutoSize = false
            };

            txtMusicName = new TextBox
            {
                Left = 420,
                Top = 75,
                Width = 200,
                PlaceholderText = "e.g., Calm Ambient"
            };

            var lblPath = new Label
            {
                Text = "File Path:",
                Left = 420,
                Top = 110,
                Width = 200,
                AutoSize = false
            };

            txtMusicPath = new TextBox
            {
                Left = 420,
                Top = 135,
                Width = 200,
                PlaceholderText = "Select a music file...",
                ReadOnly = true
            };

            btnBrowse = new Button
            {
                Text = "Browse...",
                Left = 420,
                Top = 165,
                Width = 200,
                Height = 25
            };
            btnBrowse.Click += BtnBrowse_Click;

            // Action buttons
            btnAdd = new Button
            {
                Text = "Add",
                Left = 420,
                Top = 200,
                Width = 95,
                Height = 30
            };
            btnAdd.Click += BtnAdd_Click;

            btnEdit = new Button
            {
                Text = "Edit",
                Left = 525,
                Top = 200,
                Width = 95,
                Height = 30,
                Enabled = false
            };
            btnEdit.Click += (s, e) => EditMusic();

            btnRemove = new Button
            {
                Text = "Remove",
                Left = 420,
                Top = 240,
                Width = 200,
                Height = 30,
                Enabled = false
            };
            btnRemove.Click += BtnRemove_Click;

            // Separator
            var separator = new Label
            {
                Left = 420,
                Top = 280,
                Width = 200,
                Height = 2,
                BorderStyle = BorderStyle.Fixed3D
            };

            btnMoveUp = new Button
            {
                Text = "Move Up ↑",
                Left = 420,
                Top = 290,
                Width = 95,
                Height = 30,
                Enabled = false
            };
            btnMoveUp.Click += BtnMoveUp_Click;

            btnMoveDown = new Button
            {
                Text = "Move Down ↓",
                Left = 525,
                Top = 290,
                Width = 95,
                Height = 30,
                Enabled = false
            };
            btnMoveDown.Click += BtnMoveDown_Click;

            // === Music Selection Mode ===
            grpSelection = new GroupBox
            {
                Text = "Music Selection",
                Left = 10,
                Top = 335,
                Width = 610,
                Height = 120,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            lblSelection = new Label
            {
                Text = "Choose how music is selected for video generation:",
                Left = 10,
                Top = 25,
                Width = 580,
                Height = 20,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            rbRandom = new RadioButton
            {
                Text = "🎲 Random - Pick a random track each time",
                Left = 20,
                Top = 50,
                Width = 400,
                Height = 25,
                Checked = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };
            rbRandom.CheckedChanged += (s, e) => UpdateSelectionMode();

            rbSpecific = new RadioButton
            {
                Text = "🎯 Specific - Use the selected track from the list above",
                Left = 20,
                Top = 80,
                Width = 400,
                Height = 25,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };
            rbSpecific.CheckedChanged += (s, e) => UpdateSelectionMode();

            grpSelection.Controls.Add(lblSelection);
            grpSelection.Controls.Add(rbRandom);
            grpSelection.Controls.Add(rbSpecific);

            // Enable/Disable music checkbox
            chkEnableMusic = new CheckBox
            {
                Text = "✓ Enable music in video generation",
                Left = 10,
                Top = 465,
                Width = 350,
                Height = 25,
                Checked = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.DarkGreen
            };
            chkEnableMusic.CheckedChanged += (s, e) => 
            {
                chkEnableMusic.ForeColor = chkEnableMusic.Checked ? Color.DarkGreen : Color.Gray;
            };

            // Bottom buttons
            btnSave = new Button
            {
                Text = "Save",
                Left = 380,
                Top = 470,
                Width = 110,
                Height = 40,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += BtnSave_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                Left = 500,
                Top = 470,
                Width = 110,
                Height = 40,
                Font = new Font("Segoe UI", 10F),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[]
            {
                lblInfo, lstMusic, lblCount, lblName, txtMusicName,
                lblPath, txtMusicPath, btnBrowse, btnAdd, btnEdit, btnRemove,
                separator, btnMoveUp, btnMoveDown, grpSelection, chkEnableMusic, btnSave, btnCancel
            });

            this.AcceptButton = btnAdd;
            this.CancelButton = btnCancel;
        }

        private void LoadMusicTracks()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var musicSettings = config.Music;

                _musicTracks.Clear();

                if (musicSettings == null)
                {
                    // Initialize with defaults if not present
                    var basePath = Directory.GetCurrentDirectory();
                    musicSettings = MusicSettings.CreateDefault(basePath);
                    config.Music = musicSettings;
                }

                if (musicSettings.MusicTracks != null)
                {
                    _musicTracks.AddRange(musicSettings.MusicTracks);
                }

                // Set selection mode
                rbRandom.Checked = musicSettings.UseRandomMusic;
                rbSpecific.Checked = !musicSettings.UseRandomMusic;
                chkEnableMusic.Checked = musicSettings.EnableMusicInVideo;

                RefreshMusicList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading music tracks: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshMusicList()
        {
            lstMusic.Items.Clear();
            for (int i = 0; i < _musicTracks.Count; i++)
            {
                var track = _musicTracks[i];
                string status = track.FileExists() ? "✓" : "✗";
                string demo = track.IsDemo ? "[Demo]" : "[Custom]";
                lstMusic.Items.Add($"{status} {i + 1}. {track.Name} {demo}");
            }

            lblCount.Text = $"{_musicTracks.Count} track(s)";
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            int selectedIndex = lstMusic.SelectedIndex;
            bool hasSelection = selectedIndex >= 0;

            btnEdit.Enabled = hasSelection;
            btnRemove.Enabled = hasSelection;
            btnMoveUp.Enabled = hasSelection && selectedIndex > 0;
            btnMoveDown.Enabled = hasSelection && selectedIndex < _musicTracks.Count - 1;
        }

        private void UpdateSelectionMode()
        {
            // Visual feedback when selection mode changes
            if (rbSpecific.Checked && lstMusic.SelectedIndex < 0 && _musicTracks.Count > 0)
            {
                lstMusic.SelectedIndex = 0;
            }
        }

        private void LstMusic_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateButtonStates();
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.m4a;*.aac;*.flac;*.wma|All Files|*.*";
                openFileDialog.Title = "Select Music File";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    txtMusicPath.Text = openFileDialog.FileName;

                    // Auto-fill name if empty
                    if (string.IsNullOrWhiteSpace(txtMusicName.Text))
                    {
                        txtMusicName.Text = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                    }
                }
            }
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            string musicName = txtMusicName.Text.Trim();
            string musicPath = txtMusicPath.Text.Trim();

            if (string.IsNullOrWhiteSpace(musicName))
            {
                MessageBox.Show("Please enter a track name.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtMusicName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(musicPath))
            {
                MessageBox.Show("Please select a music file.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnBrowse.Focus();
                return;
            }

            if (!File.Exists(musicPath))
            {
                var result = MessageBox.Show(
                    $"The file '{musicPath}' does not exist. Add it anyway?",
                    "File Not Found",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.No)
                    return;
            }

            if (_musicTracks.Any(m => m.Name.Equals(musicName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A track with this name already exists.", "Duplicate Track",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtMusicName.Focus();
                return;
            }

            _musicTracks.Add(new MusicEntry(musicName, musicPath, false));
            RefreshMusicList();
            txtMusicName.Clear();
            txtMusicPath.Clear();
            txtMusicName.Focus();

            // Auto-select the newly added item
            lstMusic.SelectedIndex = lstMusic.Items.Count - 1;
        }

        private void EditMusic()
        {
            int selectedIndex = lstMusic.SelectedIndex;
            if (selectedIndex < 0) return;

            var currentTrack = _musicTracks[selectedIndex];

            using (var editForm = new Form())
            {
                editForm.Text = "Edit Music Track";
                editForm.Width = 450;
                editForm.Height = 220;
                editForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                editForm.StartPosition = FormStartPosition.CenterParent;
                editForm.MaximizeBox = false;
                editForm.MinimizeBox = false;

                var lblName = new Label { Text = "Track Name:", Left = 10, Top = 20, Width = 100 };
                var txtName = new TextBox { Left = 120, Top = 17, Width = 300, Text = currentTrack.Name };

                var lblPath = new Label { Text = "File Path:", Left = 10, Top = 55, Width = 100 };
                var txtPath = new TextBox { Left = 120, Top = 52, Width = 250, Text = currentTrack.FilePath };

                var btnBrowseEdit = new Button { Text = "...", Left = 375, Top = 50, Width = 45, Height = 25 };
                btnBrowseEdit.Click += (s, e) =>
                {
                    using (var openFileDialog = new OpenFileDialog())
                    {
                        openFileDialog.Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.m4a;*.aac;*.flac;*.wma|All Files|*.*";
                        openFileDialog.FileName = txtPath.Text;
                        if (openFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            txtPath.Text = openFileDialog.FileName;
                        }
                    }
                };

                var chkDemo = new CheckBox
                {
                    Text = "This is a demo track",
                    Left = 120,
                    Top = 90,
                    Width = 200,
                    Checked = currentTrack.IsDemo
                };

                var btnOk = new Button
                {
                    Text = "OK",
                    Left = 210,
                    Top = 130,
                    Width = 100,
                    DialogResult = DialogResult.OK
                };

                var btnCancelEdit = new Button
                {
                    Text = "Cancel",
                    Left = 320,
                    Top = 130,
                    Width = 100,
                    DialogResult = DialogResult.Cancel
                };

                editForm.Controls.AddRange(new Control[]
                {
                    lblName, txtName, lblPath, txtPath, btnBrowseEdit,
                    chkDemo, btnOk, btnCancelEdit
                });

                editForm.AcceptButton = btnOk;
                editForm.CancelButton = btnCancelEdit;

                if (editForm.ShowDialog() == DialogResult.OK)
                {
                    string newName = txtName.Text.Trim();
                    string newPath = txtPath.Text.Trim();

                    if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(newPath))
                    {
                        MessageBox.Show("Name and path cannot be empty.", "Invalid Input",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Check for duplicate names (excluding current item)
                    if (_musicTracks.Where((m, i) => i != selectedIndex)
                        .Any(m => m.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("A track with this name already exists.", "Duplicate Track",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    _musicTracks[selectedIndex] = new MusicEntry(newName, newPath, chkDemo.Checked);
                    RefreshMusicList();
                    lstMusic.SelectedIndex = selectedIndex;
                }
            }
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            int selectedIndex = lstMusic.SelectedIndex;
            if (selectedIndex < 0) return;

            var track = _musicTracks[selectedIndex];
            var result = MessageBox.Show(
                $"Remove '{track.Name}' from the list?",
                "Confirm Remove",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _musicTracks.RemoveAt(selectedIndex);
                RefreshMusicList();

                // Select next item or previous if at end
                if (lstMusic.Items.Count > 0)
                {
                    lstMusic.SelectedIndex = Math.Min(selectedIndex, lstMusic.Items.Count - 1);
                }
            }
        }

        private void BtnMoveUp_Click(object? sender, EventArgs e)
        {
            int selectedIndex = lstMusic.SelectedIndex;
            if (selectedIndex <= 0) return;

            // Swap with previous
            var temp = _musicTracks[selectedIndex];
            _musicTracks[selectedIndex] = _musicTracks[selectedIndex - 1];
            _musicTracks[selectedIndex - 1] = temp;

            RefreshMusicList();
            lstMusic.SelectedIndex = selectedIndex - 1;
        }

        private void BtnMoveDown_Click(object? sender, EventArgs e)
        {
            int selectedIndex = lstMusic.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _musicTracks.Count - 1) return;

            // Swap with next
            var temp = _musicTracks[selectedIndex];
            _musicTracks[selectedIndex] = _musicTracks[selectedIndex + 1];
            _musicTracks[selectedIndex + 1] = temp;

            RefreshMusicList();
            lstMusic.SelectedIndex = selectedIndex + 1;
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            try
            {
                var config = ConfigManager.LoadConfig();

                // Initialize Music if null
                if (config.Music == null)
                {
                    config.Music = new MusicSettings();
                }

                config.Music.MusicTracks = new List<MusicEntry>(_musicTracks);
                config.Music.UseRandomMusic = rbRandom.Checked;
                config.Music.SelectedMusicIndex = rbSpecific.Checked && lstMusic.SelectedIndex >= 0
                    ? lstMusic.SelectedIndex
                    : -1;
                config.Music.EnableMusicInVideo = chkEnableMusic.Checked;

                ConfigManager.SaveConfig(config);

                MessageBox.Show("Music settings saved successfully!", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                Logger.Log($"✓ Saved {_musicTracks.Count} music track(s) to configuration", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving music settings: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None; // Prevent form from closing
            }
        }
    }
}
