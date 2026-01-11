#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
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
        private Button btnSearch;
        private Label lblCount;
        private Label lblSelectedApi;
        private readonly List<LocationEntry> _locations;
        private Dictionary<string, string> _newEcccFeeds = new Dictionary<string, string>();
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

            // Search button
            btnSearch = new Button
            {
                Text = "🔍 Search Cities",
                Left = 370,
                Top = 155,
                Width = 160,
                Height = 30
            };
            btnSearch.Click += BtnSearch_Click;

            // Action buttons
            btnAdd = new Button
            {
                Text = "Add",
                Left = 370,
                Top = 195,
                Width = 160,
                Height = 30
            };
            btnAdd.Click += BtnAdd_Click;

            btnEdit = new Button
            {
                Text = "Edit",
                Left = 370,
                Top = 235,
                Width = 160,
                Height = 30,
                Enabled = false
            };
            btnEdit.Click += (s, e) => EditLocation();

            btnRemove = new Button
            {
                Text = "Remove",
                Left = 370,
                Top = 275,
                Width = 160,
                Height = 30,
                Enabled = false
            };
            btnRemove.Click += BtnRemove_Click;

            // Separator
            var separator = new Label
            {
                Left = 370,
                Top = 315,
                Width = 160,
                Height = 2,
                BorderStyle = BorderStyle.Fixed3D
            };

            btnMoveUp = new Button
            {
                Text = "Move Up ↑",
                Left = 370,
                Top = 325,
                Width = 160,
                Height = 30,
                Enabled = false
            };
            btnMoveUp.Click += BtnMoveUp_Click;

            btnMoveDown = new Button
            {
                Text = "Move Down ↓",
                Left = 370,
                Top = 365,
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
                lblApi, cmbWeatherApi, btnSearch, btnAdd, btnEdit, btnRemove, separator, btnMoveUp, btnMoveDown,
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

            // Check if input is an ECCC URL
            if (locationName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                locationName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // Attempt to parse the URL
                    var request = WeatherImageGenerator.Services.ECCC.ParseFeedUrl(locationName);
                    
                    // It's a valid ECCC URL. Prompt for a friendly name.
                    string defaultName = !string.IsNullOrEmpty(request.Province) && !string.IsNullOrEmpty(request.CityCode)
                        ? $"{request.Province.ToUpper()}-{request.CityCode}"
                        : "New Location";
                        
                    string? friendlyName = PromptForName(
                        "ECCC Feed Detected", 
                        "Enter a name for this location:", 
                        defaultName);

                    if (string.IsNullOrWhiteSpace(friendlyName)) return; // User cancelled or empty

                    // Use the friendly name from now on
                    locationName = friendlyName!;
                    
                    // Add to pending feeds to be saved later
                    _newEcccFeeds[locationName] = txtLocationName.Text.Trim();
                    
                    // Force ECCC API selection
                    cmbWeatherApi.SelectedIndex = 1; // ECCC
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"The URL provided does not appear to be a valid ECCC feed.\nError: {ex.Message}", 
                        "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
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

                // Save pending ECCC feeds if any
                if (_newEcccFeeds.Count > 0)
                {
                    if (config.ECCC == null) config.ECCC = new ECCCSettings();
                    if (config.ECCC.CityFeeds == null) config.ECCC.CityFeeds = new Dictionary<string, string>();
                    
                    foreach (var kv in _newEcccFeeds)
                    {
                        config.ECCC.CityFeeds[kv.Key] = kv.Value;
                    }
                    
                    Logger.Log($"✓ Added {_newEcccFeeds.Count} new ECCC feed(s) to configuration");
                }

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

        private void BtnSearch_Click(object? sender, EventArgs e)
        {
            string query = txtLocationName.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Please enter a city name to search for.", "Search Required", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtLocationName.Focus();
                return;
            }

            // Show progress
            btnSearch.Enabled = false;
            btnSearch.Text = "Searching...";
            this.Cursor = Cursors.WaitCursor;

            // Perform async search
            Task.Run(async () =>
            {
                try
                {
                    // Search online using OpenMeteo geocoding
                    var client = new OpenMeteo.OpenMeteoClient();
                    var results = await WeatherImageGenerator.Services.ECCC.SearchCitiesOnlineAsync(client, query, 30);
                    
                    // If no online results, try local database as fallback
                    if (results.Count == 0)
                    {
                        results = WeatherImageGenerator.Services.ECCC.SearchCities(query, 20);
                    }
                    
                    // Return to UI thread
                    this.Invoke((Action)(() =>
                    {
                        btnSearch.Enabled = true;
                        btnSearch.Text = "🔍 Search Cities";
                        this.Cursor = Cursors.Default;
                        
                        if (results.Count == 0)
                        {
                            MessageBox.Show($"No cities found matching '{query}'.\n\nTry:\n• Different spelling\n• English or local name\n• Major city names", 
                                "No Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }

                        // Show search results dialog
                        var selectedCity = ShowCitySearchResults(query, results);
                        
                        if (selectedCity != null)
                        {
                            // Auto-fill the location name (remove province/country suffix for cleaner name)
                            var cleanName = selectedCity.Name;
                            var commaIndex = cleanName.IndexOf(',');
                            if (commaIndex > 0)
                            {
                                cleanName = cleanName.Substring(0, commaIndex).Trim();
                            }
                            txtLocationName.Text = cleanName;
                            
                            // Set API to ECCC
                            cmbWeatherApi.SelectedIndex = 1;
                            
                            // Store the feed URL for later
                            _newEcccFeeds[cleanName] = selectedCity.GetCityFeedUrl();
                            
                            // Add it automatically
                            BtnAdd_Click(sender, e);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke((Action)(() =>
                    {
                        btnSearch.Enabled = true;
                        btnSearch.Text = "🔍 Search Cities";
                        this.Cursor = Cursors.Default;
                        MessageBox.Show($"Search error: {ex.Message}", "Error", 
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private WeatherImageGenerator.Services.ECCC.CityInfo? ShowCitySearchResults(string query, List<WeatherImageGenerator.Services.ECCC.CityInfo> results)
        {
            using (var searchForm = new Form())
            {
                searchForm.Text = $"Search Results for '{query}'";
                searchForm.Width = 500;
                searchForm.Height = 450;
                searchForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                searchForm.StartPosition = FormStartPosition.CenterParent;
                searchForm.MaximizeBox = false;
                searchForm.MinimizeBox = false;

                var lblInfo = new Label
                {
                    Text = $"Found {results.Count} city/cities. Select one to add:",
                    Left = 10,
                    Top = 10,
                    Width = 460,
                    Height = 30,
                    Font = new Font("Segoe UI", 9F)
                };

                var lstResults = new ListBox
                {
                    Left = 10,
                    Top = 45,
                    Width = 460,
                    Height = 280,
                    Font = new Font("Segoe UI", 10F)
                };

                foreach (var city in results)
                {
                    lstResults.Items.Add(city);
                }
                lstResults.DisplayMember = "ToString";
                if (lstResults.Items.Count > 0) lstResults.SelectedIndex = 0;
                lstResults.DoubleClick += (s, e) =>
                {
                    if (lstResults.SelectedItem != null)
                    {
                        searchForm.DialogResult = DialogResult.OK;
                        searchForm.Close();
                    }
                };

                var lblPreview = new Label
                {
                    Text = "",
                    Left = 10,
                    Top = 335,
                    Width = 460,
                    Height = 40,
                    Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                    ForeColor = Color.DarkBlue
                };

                lstResults.SelectedIndexChanged += (s, e) =>
                {
                    if (lstResults.SelectedItem is WeatherImageGenerator.Services.ECCC.CityInfo city)
                    {
                        var feedType = city.IsCoordinateBased ? "Alerts Feed" : "City Weather Feed";
                        lblPreview.Text = $"Type: {feedType}\nFeed: {city.GetCityFeedUrl()}";
                    }
                };
                if (lstResults.Items.Count > 0 && lstResults.SelectedItem is WeatherImageGenerator.Services.ECCC.CityInfo firstCity)
                {
                    var feedType = firstCity.IsCoordinateBased ? "Alerts Feed" : "City Weather Feed";
                    lblPreview.Text = $"Type: {feedType}\nFeed: {firstCity.GetCityFeedUrl()}";
                }

                var btnSelect = new Button
                {
                    Text = "Select",
                    Left = 280,
                    Top = 365,
                    Width = 100,
                    Height = 35,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    DialogResult = DialogResult.OK
                };

                var btnCancelSearch = new Button
                {
                    Text = "Cancel",
                    Left = 390,
                    Top = 365,
                    Width = 80,
                    Height = 35,
                    Font = new Font("Segoe UI", 10F),
                    DialogResult = DialogResult.Cancel
                };

                searchForm.Controls.AddRange(new Control[] { lblInfo, lstResults, lblPreview, btnSelect, btnCancelSearch });
                searchForm.AcceptButton = btnSelect;
                searchForm.CancelButton = btnCancelSearch;

                if (searchForm.ShowDialog() == DialogResult.OK && lstResults.SelectedItem is WeatherImageGenerator.Services.ECCC.CityInfo selected)
                {
                    return selected;
                }
                return null;
            }
        }

        private string? PromptForName(string title, string prompt, string defaultValue = "")
        {
            using (var promptForm = new Form())
            {
                promptForm.Text = title;
                promptForm.Width = 400;
                promptForm.Height = 180;
                promptForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                promptForm.StartPosition = FormStartPosition.CenterParent;
                promptForm.MaximizeBox = false;
                promptForm.MinimizeBox = false;

                var lblPrompt = new Label
                {
                    Text = prompt,
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
                    Top = 90,
                    Width = 80,
                    DialogResult = DialogResult.OK
                };

                var btnCancelPrompt = new Button
                {
                    Text = "Cancel",
                    Left = 290,
                    Top = 90,
                    Width = 80,
                    DialogResult = DialogResult.Cancel
                };

                promptForm.Controls.AddRange(new Control[] { lblPrompt, txtInput, btnOk, btnCancelPrompt });
                promptForm.AcceptButton = btnOk;
                promptForm.CancelButton = btnCancelPrompt;

                if (promptForm.ShowDialog() == DialogResult.OK)
                {
                    return txtInput.Text.Trim();
                }
                return null;
            }
        }
    }
}
