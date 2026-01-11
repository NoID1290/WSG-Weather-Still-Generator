using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WeatherImageGenerator.Models;
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
        private ComboBox cmbWeatherApi;
        private Button btnAdd;
        private Button btnEdit;
        private Button btnRemove;
        private Button btnMoveUp;
        private Button btnMoveDown;
        private Button btnSave;
        private Button btnCancel;
        private Label lblCount;
        private Label lblSelectedApi;
        private readonly List<LocationEntry> _locations;
        private const int MaxLocations = 9; // Location0 through Location8

        public LocationsForm()
        {
            InitializeComponents();
            _locations = new List<LocationEntry>();
            LoadLocations();
        }

        private void InitializeComponents()
        {
            this.Text = "Manage Locations";
            this.Width = 550;
            this.Height = 550;
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
                Width = 510,
                Height = 30,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Location list
            lstLocations = new ListBox
            {
                Left = 10,
                Top = 50,
                Width = 350,
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
                Width = 350,
                Text = "0 locations",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic)
            };

            // Selected location API label
            lblSelectedApi = new Label
            {
                Left = 10,
                Top = 375,
                Width = 350,
                Text = "",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.DarkBlue
            };

            // Input textbox
            var lblNew = new Label
            {
                Text = "Location Name:",
                Left = 370,
                Top = 50,
                Width = 160,
                AutoSize = false
            };

            txtLocationName = new TextBox
            {
                Left = 370,
                Top = 75,
                Width = 160,
                PlaceholderText = "e.g., Montréal"
            };
            txtLocationName.KeyDown += TxtLocationName_KeyDown;

            // Weather API dropdown
            var lblApi = new Label
            {
                Text = "Weather API:",
                Left = 370,
                Top = 105,
                Width = 160,
                AutoSize = false
            };

            cmbWeatherApi = new ComboBox
            {
                Left = 370,
                Top = 125,
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F)
            };
            cmbWeatherApi.Items.AddRange(new object[] { "OpenMeteo", "ECCC" });
            cmbWeatherApi.SelectedIndex = 0; // Default to OpenMeteo

            // Action buttons
            btnAdd = new Button
            {
                Text = "Add",
                Left = 370,
                Top = 160,
                Width = 160,
                Height = 30
            };
            btnAdd.Click += BtnAdd_Click;

            btnEdit = new Button
            {
                Text = "Edit",
                Left = 370,
                Top = 200,
                Width = 160,
                Height = 30,
                Enabled = false
            };
            btnEdit.Click += (s, e) => EditLocation();

            btnRemove = new Button
            {
                Text = "Remove",
                Left = 370,
                Top = 240,
                Width = 160,
                Height = 30,
                Enabled = false
            };
            btnRemove.Click += BtnRemove_Click;

            // Separator
            var separator = new Label
            {
                Left = 370,
                Top = 280,
                Width = 160,
                Height = 2,
                BorderStyle = BorderStyle.Fixed3D
            };

            btnMoveUp = new Button
            {
                Text = "Move Up ↑",
                Left = 370,
                Top = 290,
                Width = 160,
                Height = 30,
                Enabled = false
            };
            btnMoveUp.Click += BtnMoveUp_Click;

            btnMoveDown = new Button
            {
                Text = "Move Down ↓",
                Left = 370,
                Top = 330,
                Width = 160,
                Height = 30,
                Enabled = false
            };
            btnMoveDown.Click += BtnMoveDown_Click;

            // Bottom buttons
            btnSave = new Button
            {
                Text = "Save",
                Left = 300,
                Top = 450,
                Width = 100,
                Height = 35,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += BtnSave_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                Left = 410,
                Top = 450,
                Width = 100,
                Height = 35,
                Font = new Font("Segoe UI", 10F),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[]
            {
                lblInfo, lstLocations, lblCount, lblSelectedApi, lblNew, txtLocationName,
                lblApi, cmbWeatherApi, btnAdd, btnEdit, btnRemove, separator, btnMoveUp, btnMoveDown,
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
                    var entries = locationSettings.GetLocationEntries();
                    foreach (var entry in entries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Name))
                        {
                            _locations.Add(entry);
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
                var entry = _locations[i];
                string apiLabel = entry.Api == WeatherApiType.ECCC ? "[ECCC]" : "[OpenMeteo]";
                lstLocations.Items.Add($"{i + 1}. {entry.Name} {apiLabel}");
            }
            
            lblCount.Text = $"{_locations.Count} / {MaxLocations} locations";
            UpdateButtonStates();
            UpdateSelectedApiLabel();
        }

        private void UpdateSelectedApiLabel()
        {
            int selectedIndex = lstLocations.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _locations.Count)
            {
                var entry = _locations[selectedIndex];
                lblSelectedApi.Text = $"API: {entry.Api}";
            }
            else
            {
                lblSelectedApi.Text = "";
            }
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
            UpdateSelectedApiLabel();
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

            if (_locations.Any(l => l.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This location already exists in the list.", "Duplicate Location", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtLocationName.Focus();
                return;
            }

            // Get selected API from combo box
            WeatherApiType selectedApi = cmbWeatherApi.SelectedIndex == 1 
                ? WeatherApiType.ECCC 
                : WeatherApiType.OpenMeteo;

            _locations.Add(new LocationEntry(locationName, selectedApi));
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

            var currentEntry = _locations[selectedIndex];
            var result = PromptForLocationEntry("Edit Location", currentEntry.Name, currentEntry.Api);

            if (result == null) return; // User cancelled

            if (string.IsNullOrWhiteSpace(result.Value.Name))
            {
                MessageBox.Show("Location name cannot be empty.", "Invalid Input", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check for duplicates (excluding current item)
            if (_locations.Where((l, i) => i != selectedIndex)
                .Any(l => l.Name.Equals(result.Value.Name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This location already exists in the list.", "Duplicate Location", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _locations[selectedIndex] = new LocationEntry(result.Value.Name, result.Value.Api);
            RefreshLocationList();
            lstLocations.SelectedIndex = selectedIndex;
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            int selectedIndex = lstLocations.SelectedIndex;
            if (selectedIndex < 0) return;

            string locationName = _locations[selectedIndex].Name;
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

                // Set all locations and API preferences from the list
                config.Locations.Location0 = _locations.Count > 0 ? _locations[0].Name : null;
                config.Locations.Location1 = _locations.Count > 1 ? _locations[1].Name : null;
                config.Locations.Location2 = _locations.Count > 2 ? _locations[2].Name : null;
                config.Locations.Location3 = _locations.Count > 3 ? _locations[3].Name : null;
                config.Locations.Location4 = _locations.Count > 4 ? _locations[4].Name : null;
                config.Locations.Location5 = _locations.Count > 5 ? _locations[5].Name : null;
                config.Locations.Location6 = _locations.Count > 6 ? _locations[6].Name : null;
                config.Locations.Location7 = _locations.Count > 7 ? _locations[7].Name : null;
                config.Locations.Location8 = _locations.Count > 8 ? _locations[8].Name : null;

                // Set API preferences
                config.Locations.Location0Api = _locations.Count > 0 ? _locations[0].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location1Api = _locations.Count > 1 ? _locations[1].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location2Api = _locations.Count > 2 ? _locations[2].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location3Api = _locations.Count > 3 ? _locations[3].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location4Api = _locations.Count > 4 ? _locations[4].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location5Api = _locations.Count > 5 ? _locations[5].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location6Api = _locations.Count > 6 ? _locations[6].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location7Api = _locations.Count > 7 ? _locations[7].Api : WeatherApiType.OpenMeteo;
                config.Locations.Location8Api = _locations.Count > 8 ? _locations[8].Api : WeatherApiType.OpenMeteo;

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

        private (string Name, WeatherApiType Api)? PromptForLocationEntry(string title, string defaultName, WeatherApiType defaultApi)
        {
            using (var promptForm = new Form())
            {
                promptForm.Text = title;
                promptForm.Width = 400;
                promptForm.Height = 200;
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
                    Text = defaultName
                };
                txtInput.SelectAll();

                var lblApi = new Label
                {
                    Text = "Weather API:",
                    Left = 10,
                    Top = 75,
                    Width = 100
                };

                var cmbApi = new ComboBox
                {
                    Left = 110,
                    Top = 72,
                    Width = 150,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                cmbApi.Items.AddRange(new object[] { "OpenMeteo", "ECCC" });
                cmbApi.SelectedIndex = defaultApi == WeatherApiType.ECCC ? 1 : 0;

                var btnOk = new Button
                {
                    Text = "OK",
                    Left = 200,
                    Top = 115,
                    Width = 80,
                    DialogResult = DialogResult.OK
                };

                var btnCancelPrompt = new Button
                {
                    Text = "Cancel",
                    Left = 290,
                    Top = 115,
                    Width = 80,
                    DialogResult = DialogResult.Cancel
                };

                promptForm.Controls.AddRange(new Control[] { lblPrompt, txtInput, lblApi, cmbApi, btnOk, btnCancelPrompt });
                promptForm.AcceptButton = btnOk;
                promptForm.CancelButton = btnCancelPrompt;

                if (promptForm.ShowDialog() == DialogResult.OK)
                {
                    WeatherApiType selectedApi = cmbApi.SelectedIndex == 1 
                        ? WeatherApiType.ECCC 
                        : WeatherApiType.OpenMeteo;
                    return (txtInput.Text.Trim(), selectedApi);
                }
                return null;
            }
        }
    }
}
