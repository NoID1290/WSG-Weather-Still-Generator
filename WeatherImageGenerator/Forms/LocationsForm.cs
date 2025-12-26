using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Form for managing weather fetch locations
    /// </summary>
    public class LocationsForm : Form
    {
        private ListBox lstLocations;
        private TextBox txtLocationName;
        private Button btnAdd;
        private Button btnEdit;
        private Button btnRemove;
        private Button btnMoveUp;
        private Button btnMoveDown;
        private Button btnSave;
        private Button btnCancel;
        private Label lblCount;
        private readonly List<string> _locations;
        private const int MaxLocations = 9; // Location0 through Location8

        public LocationsForm()
        {
            InitializeComponents();
            _locations = new List<string>();
            LoadLocations();
        }

        private void InitializeComponents()
        {
            this.Text = "Manage Locations";
            this.Width = 500;
            this.Height = 500;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // Info label
            var lblInfo = new Label
            {
                Text = "Manage weather locations to fetch. Maximum 9 locations.",
                Left = 10,
                Top = 10,
                Width = 460,
                Height = 30,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Location list
            lstLocations = new ListBox
            {
                Left = 10,
                Top = 50,
                Width = 300,
                Height = 300,
                SelectionMode = SelectionMode.One,
                Font = new Font("Segoe UI", 10F)
            };
            lstLocations.SelectedIndexChanged += LstLocations_SelectedIndexChanged;
            lstLocations.DoubleClick += (s, e) => EditLocation();

            // Count label
            lblCount = new Label
            {
                Left = 10,
                Top = 355,
                Width = 300,
                Text = "0 locations",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic)
            };

            // Input textbox
            var lblNew = new Label
            {
                Text = "Location Name:",
                Left = 320,
                Top = 50,
                Width = 150,
                AutoSize = false
            };

            txtLocationName = new TextBox
            {
                Left = 320,
                Top = 75,
                Width = 150,
                PlaceholderText = "e.g., Montréal"
            };
            txtLocationName.KeyDown += TxtLocationName_KeyDown;

            // Action buttons
            btnAdd = new Button
            {
                Text = "Add",
                Left = 320,
                Top = 110,
                Width = 150,
                Height = 30
            };
            btnAdd.Click += BtnAdd_Click;

            btnEdit = new Button
            {
                Text = "Edit",
                Left = 320,
                Top = 150,
                Width = 150,
                Height = 30,
                Enabled = false
            };
            btnEdit.Click += (s, e) => EditLocation();

            btnRemove = new Button
            {
                Text = "Remove",
                Left = 320,
                Top = 190,
                Width = 150,
                Height = 30,
                Enabled = false
            };
            btnRemove.Click += BtnRemove_Click;

            // Separator
            var separator = new Label
            {
                Left = 320,
                Top = 230,
                Width = 150,
                Height = 2,
                BorderStyle = BorderStyle.Fixed3D
            };

            btnMoveUp = new Button
            {
                Text = "Move Up ↑",
                Left = 320,
                Top = 240,
                Width = 150,
                Height = 30,
                Enabled = false
            };
            btnMoveUp.Click += BtnMoveUp_Click;

            btnMoveDown = new Button
            {
                Text = "Move Down ↓",
                Left = 320,
                Top = 280,
                Width = 150,
                Height = 30,
                Enabled = false
            };
            btnMoveDown.Click += BtnMoveDown_Click;

            // Bottom buttons
            btnSave = new Button
            {
                Text = "Save",
                Left = 250,
                Top = 400,
                Width = 100,
                Height = 35,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += BtnSave_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                Left = 360,
                Top = 400,
                Width = 100,
                Height = 35,
                Font = new Font("Segoe UI", 10F),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[]
            {
                lblInfo, lstLocations, lblCount, lblNew, txtLocationName,
                btnAdd, btnEdit, btnRemove, separator, btnMoveUp, btnMoveDown,
                btnSave, btnCancel
            });

            this.AcceptButton = btnAdd;
            this.CancelButton = btnCancel;
        }

        private void LoadLocations()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var locationSettings = config.Locations;

                _locations.Clear();

                if (locationSettings != null)
                {
                    var locationArray = locationSettings.GetLocationsArray();
                    foreach (var loc in locationArray)
                    {
                        if (!string.IsNullOrWhiteSpace(loc))
                        {
                            _locations.Add(loc);
                        }
                    }
                }

                RefreshLocationList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading locations: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshLocationList()
        {
            lstLocations.Items.Clear();
            for (int i = 0; i < _locations.Count; i++)
            {
                lstLocations.Items.Add($"{i + 1}. {_locations[i]}");
            }
            
            lblCount.Text = $"{_locations.Count} / {MaxLocations} locations";
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            int selectedIndex = lstLocations.SelectedIndex;
            bool hasSelection = selectedIndex >= 0;

            btnEdit.Enabled = hasSelection;
            btnRemove.Enabled = hasSelection;
            btnMoveUp.Enabled = hasSelection && selectedIndex > 0;
            btnMoveDown.Enabled = hasSelection && selectedIndex < _locations.Count - 1;
            btnAdd.Enabled = _locations.Count < MaxLocations;
        }

        private void LstLocations_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateButtonStates();
        }

        private void TxtLocationName_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && btnAdd.Enabled)
            {
                BtnAdd_Click(sender, e);
                e.SuppressKeyPress = true;
            }
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            string locationName = txtLocationName.Text.Trim();

            if (string.IsNullOrWhiteSpace(locationName))
            {
                MessageBox.Show("Please enter a location name.", "Invalid Input", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtLocationName.Focus();
                return;
            }

            if (_locations.Count >= MaxLocations)
            {
                MessageBox.Show($"Maximum of {MaxLocations} locations reached.", "Limit Reached", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_locations.Any(l => l.Equals(locationName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This location already exists in the list.", "Duplicate Location", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtLocationName.Focus();
                return;
            }

            _locations.Add(locationName);
            RefreshLocationList();
            txtLocationName.Clear();
            txtLocationName.Focus();

            // Auto-select the newly added item
            lstLocations.SelectedIndex = lstLocations.Items.Count - 1;
        }

        private void EditLocation()
        {
            int selectedIndex = lstLocations.SelectedIndex;
            if (selectedIndex < 0) return;

            string currentName = _locations[selectedIndex];
            string? newName = PromptForLocationName("Edit Location", currentName);

            if (newName == null) return; // User cancelled

            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Location name cannot be empty.", "Invalid Input", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check for duplicates (excluding current item)
            if (_locations.Where((l, i) => i != selectedIndex)
                .Any(l => l.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This location already exists in the list.", "Duplicate Location", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _locations[selectedIndex] = newName;
            RefreshLocationList();
            lstLocations.SelectedIndex = selectedIndex;
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            int selectedIndex = lstLocations.SelectedIndex;
            if (selectedIndex < 0) return;

            string locationName = _locations[selectedIndex];
            var result = MessageBox.Show(
                $"Remove '{locationName}' from the list?", 
                "Confirm Remove", 
                MessageBoxButtons.YesNo, 
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _locations.RemoveAt(selectedIndex);
                RefreshLocationList();

                // Select next item or previous if at end
                if (lstLocations.Items.Count > 0)
                {
                    lstLocations.SelectedIndex = Math.Min(selectedIndex, lstLocations.Items.Count - 1);
                }
            }
        }

        private void BtnMoveUp_Click(object? sender, EventArgs e)
        {
            int selectedIndex = lstLocations.SelectedIndex;
            if (selectedIndex <= 0) return;

            // Swap with previous
            var temp = _locations[selectedIndex];
            _locations[selectedIndex] = _locations[selectedIndex - 1];
            _locations[selectedIndex - 1] = temp;

            RefreshLocationList();
            lstLocations.SelectedIndex = selectedIndex - 1;
        }

        private void BtnMoveDown_Click(object? sender, EventArgs e)
        {
            int selectedIndex = lstLocations.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _locations.Count - 1) return;

            // Swap with next
            var temp = _locations[selectedIndex];
            _locations[selectedIndex] = _locations[selectedIndex + 1];
            _locations[selectedIndex + 1] = temp;

            RefreshLocationList();
            lstLocations.SelectedIndex = selectedIndex + 1;
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                
                // Initialize Locations if null
                if (config.Locations == null)
                {
                    config.Locations = new LocationSettings();
                }

                // Set all locations from the list
                config.Locations.Location0 = _locations.Count > 0 ? _locations[0] : null;
                config.Locations.Location1 = _locations.Count > 1 ? _locations[1] : null;
                config.Locations.Location2 = _locations.Count > 2 ? _locations[2] : null;
                config.Locations.Location3 = _locations.Count > 3 ? _locations[3] : null;
                config.Locations.Location4 = _locations.Count > 4 ? _locations[4] : null;
                config.Locations.Location5 = _locations.Count > 5 ? _locations[5] : null;
                config.Locations.Location6 = _locations.Count > 6 ? _locations[6] : null;
                config.Locations.Location7 = _locations.Count > 7 ? _locations[7] : null;
                config.Locations.Location8 = _locations.Count > 8 ? _locations[8] : null;

                ConfigManager.SaveConfig(config);

                MessageBox.Show("Locations saved successfully!", "Success", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                Logger.Log($"✓ Saved {_locations.Count} location(s) to configuration", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving locations: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None; // Prevent form from closing
            }
        }

        private string? PromptForLocationName(string title, string defaultValue)
        {
            using (var promptForm = new Form())
            {
                promptForm.Text = title;
                promptForm.Width = 400;
                promptForm.Height = 150;
                promptForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                promptForm.StartPosition = FormStartPosition.CenterParent;
                promptForm.MaximizeBox = false;
                promptForm.MinimizeBox = false;

                var lblPrompt = new Label
                {
                    Text = "Location Name:",
                    Left = 10,
                    Top = 20,
                    Width = 360
                };

                var txtInput = new TextBox
                {
                    Left = 10,
                    Top = 45,
                    Width = 360,
                    Text = defaultValue
                };
                txtInput.SelectAll();

                var btnOk = new Button
                {
                    Text = "OK",
                    Left = 200,
                    Top = 75,
                    Width = 80,
                    DialogResult = DialogResult.OK
                };

                var btnCancelPrompt = new Button
                {
                    Text = "Cancel",
                    Left = 290,
                    Top = 75,
                    Width = 80,
                    DialogResult = DialogResult.Cancel
                };

                promptForm.Controls.AddRange(new Control[] { lblPrompt, txtInput, btnOk, btnCancelPrompt });
                promptForm.AcceptButton = btnOk;
                promptForm.CancelButton = btnCancelPrompt;

                return promptForm.ShowDialog() == DialogResult.OK ? txtInput.Text.Trim() : null;
            }
        }
    }
}
