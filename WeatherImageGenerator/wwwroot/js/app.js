// API Base URL
const API_BASE_URL = '/api';

// DOM Elements
let navLinks, tabContents;

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    console.log('DOM Content Loaded - Web UI initializing...');
    
    navLinks = document.querySelectorAll('.nav-link');
    tabContents = document.querySelectorAll('.tab-content');
    
    console.log('Found', navLinks.length, 'navigation links');
    console.log('Found', tabContents.length, 'tab contents');
    
    setupNavigation();
    setupForms();
    loadStatus();
    loadAllSettings();
    loadLocations();
    refreshData();
    
    // Set up periodic refresh
    setInterval(loadStatus, 30000); // Refresh every 30 seconds
    
    console.log('Web UI initialization complete');
});

/**
 * Setup navigation tab switching
 */
function setupNavigation() {
    navLinks.forEach(link => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            
            // Remove active class from all links
            navLinks.forEach(l => l.classList.remove('active'));
            // Add active class to clicked link
            link.classList.add('active');
            
            // Hide all tabs
            tabContents.forEach(tab => tab.classList.remove('active'));
            
            // Show selected tab
            const tabName = link.getAttribute('data-tab');
            const tab = document.getElementById(`${tabName}-tab`);
            if (tab) {
                tab.classList.add('active');
                
                // Load tab-specific data
                if (tabName === 'weather') {
                    loadWeatherData();
                } else if (tabName === 'locations') {
                    loadLocations();
                } else if (tabName === 'images') {
                    loadImages();
                } else if (tabName === 'settings') {
                    loadAllSettings();
                }
            }
        });
    });
}

/**
 * Setup form submissions
 */
function setupForms() {
    const forms = [
        { id: 'general-form', handler: saveGeneralSettings },
        { id: 'image-form', handler: saveImageSettings },
        { id: 'video-form', handler: saveVideoSettings },
        { id: 'music-form', handler: saveMusicSettings },
        { id: 'alerts-form', handler: saveAlertSettings },
        { id: 'radar-form', handler: saveRadarSettings }
    ];
    
    forms.forEach(({ id, handler }) => {
        const form = document.getElementById(id);
        if (form) {
            form.addEventListener('submit', (e) => {
                e.preventDefault();
                handler();
            });
        }
    });
}

/**
 * Load system status
 */
async function loadStatus() {
    try {
        const response = await fetch(`${API_BASE_URL}/status`);
        const data = await response.json();
        
        const statusEl = document.getElementById('server-status');
        const headerDot = document.getElementById('header-status-dot');
        const headerText = document.getElementById('header-status-text');
        const headerVersion = document.getElementById('header-version');
        const versionEl = document.getElementById('app-version');
        const versionBottomEl = document.getElementById('btm-version');

        if (response.ok) {
            if (statusEl) {
                statusEl.innerHTML = '<span class="status-indicator active"></span>Running';
            }
            if (headerDot) {
                headerDot.classList.remove('inactive');
                headerDot.classList.add('active');
            }
            if (headerText) headerText.textContent = 'Running';

            if (versionEl && data.version) versionEl.textContent = data.version;
            if (headerVersion && data.version) headerVersion.textContent = data.version;
            if (versionBottomEl && data.version) versionBottomEl.textContent = data.version;

            // update last update timestamp
            const lastUpdateEl = document.getElementById('last-update');
            if (lastUpdateEl) {
                const now = new Date();
                lastUpdateEl.textContent = now.toLocaleTimeString();
            }

            if (data.version) {
                document.title = `WSG - ${data.version}`;
            }
        } else {
            // Non-ok responses
            if (statusEl) statusEl.innerHTML = '<span class="status-indicator inactive"></span>Offline';
            if (headerDot) {
                headerDot.classList.remove('active');
                headerDot.classList.add('inactive');
            }
            if (headerText) headerText.textContent = 'Offline';
            if (versionBottomEl) versionBottomEl.textContent = 'Unknown';
            if (headerVersion) headerVersion.textContent = 'Unknown';
        }
    } catch (error) {
        console.error('Error loading status:', error);
        const statusEl = document.getElementById('server-status');
        if (statusEl) {
            statusEl.innerHTML = '<span class="status-indicator inactive"></span>Offline';
        }
        const versionEl = document.getElementById('app-version');
        if (versionEl) {
            versionEl.textContent = 'Unknown';
        }
        const versionBottomEl = document.getElementById('btm-version');
        if (versionBottomEl) {
            versionBottomEl.textContent = 'Unknown';
        }
        const headerDot = document.getElementById('header-status-dot');
        if (headerDot) {
            headerDot.classList.remove('active');
            headerDot.classList.add('inactive');
        }
        const headerText = document.getElementById('header-status-text');
        if (headerText) headerText.textContent = 'Offline';
    }
}

/**
 * Show a notification message
 */
function showNotification(message, type = 'info') {
    // Ensure notifications container
    let container = document.getElementById('notifications');
    if (!container) {
        container = document.createElement('div');
        container.id = 'notifications';
        container.className = 'notifications';
        document.body.appendChild(container);
    }

    const notification = document.createElement('div');
    notification.className = `notification ${type}`;
    notification.textContent = message;

    container.appendChild(notification);

    // Auto-remove with fade
    setTimeout(() => {
        notification.style.opacity = '0';
        setTimeout(() => notification.remove(), 300);
    }, 3000);
}

// Add slide animations
const style = document.createElement('style');
style.textContent = `
    @keyframes slideIn {
        from {
            transform: translateX(400px);
            opacity: 0;
        }
        to {
            transform: translateX(0);
            opacity: 1;
        }
    }
    
    @keyframes slideOut {
        from {
            transform: translateX(0);
            opacity: 1;
        }
        to {
            transform: translateX(400px);
            opacity: 0;
        }
    }
`;
document.head.appendChild(style);

/**
 * Update a location name
 */
async function updateLocation(idx, name) {
    try {
        const response = await fetch(`${API_BASE_URL}/config/locations`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ index: idx, name })
        });

        if (response.ok) {
            showNotification('Location updated', 'success');
            loadLocations();
        } else {
            showNotification('Failed to update location', 'error');
        }
    } catch (err) {
        console.error('Error updating location:', err);
        showNotification('Error updating location', 'error');
    }
}

/**
 * Update a location API selection
 */
async function updateLocationApi(idx, api) {
    try {
        const response = await fetch(`${API_BASE_URL}/config/locations`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ index: idx, api: parseInt(api, 10) })
        });

        if (response.ok) {
            showNotification('Location API updated', 'success');
            loadLocations();
        } else {
            showNotification('Failed to update location API', 'error');
        }
    } catch (err) {
        console.error('Error updating location API:', err);
        showNotification('Error updating location API', 'error');
    }
}

/**
 * Remove a location
 */
async function removeLocation(idx) {
    try {
        const response = await fetch(`${API_BASE_URL}/config/locations`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ index: idx, action: 'remove' })
        });

        if (response.ok) {
            showNotification('Location removed', 'success');
            loadLocations();
        } else {
            showNotification('Failed to remove location', 'error');
        }
    } catch (err) {
        console.error('Error removing location:', err);
        showNotification('Error removing location', 'error');
    }
}

/**
 * Refresh all data
 */
async function refreshData() {
    console.log('refreshData() called');
    loadStatus();
}

/**
 * Load weather data
 */
async function loadWeatherData() {
    try {
        const currentResponse = await fetch(`${API_BASE_URL}/weather/current`);
        const forecastResponse = await fetch(`${API_BASE_URL}/weather/forecast`);
        
        const currentData = await currentResponse.json();
        const forecastData = await forecastResponse.json();
        
        // Populate current weather
        const weatherContainer = document.getElementById('weather-container');
        if (weatherContainer) {
            if (currentResponse.ok && currentData) {
                weatherContainer.innerHTML = `
                    <div class="card">
                        <p><strong>Status:</strong> ${currentData.status || 'Unknown'}</p>
                        <p><strong>Message:</strong> ${currentData.message || 'No data'}</p>
                    </div>
                `;
            } else {
                weatherContainer.innerHTML = '<p class="loading">No current weather data available</p>';
            }
        }
        
        // Populate forecast
        const forecastContainer = document.getElementById('forecast-container');
        if (forecastContainer) {
            if (forecastResponse.ok && forecastData) {
                forecastContainer.innerHTML = `
                    <div class="card">
                        <p><strong>Locations:</strong> ${forecastData.locations || 0}</p>
                        <p><strong>Status:</strong> ${forecastData.status || 'Unknown'}</p>
                    </div>
                `;
            } else {
                forecastContainer.innerHTML = '<p class="loading">No forecast data available</p>';
            }
        }
    } catch (error) {
        console.error('Error loading weather data:', error);
        const weatherContainer = document.getElementById('weather-container');
        const forecastContainer = document.getElementById('forecast-container');
        if (weatherContainer) weatherContainer.innerHTML = '<p class="error">Error loading weather data</p>';
        if (forecastContainer) forecastContainer.innerHTML = '<p class="error">Error loading forecast data</p>';
    }
}

/**
 * Load and display locations
 */
async function loadLocations() {
    try {
        const response = await fetch(`${API_BASE_URL}/config/locations`);
        const data = await response.json();
        
        const tbody = document.getElementById('locations-tbody');
        if (!tbody) return;
        
        if (response.ok && data.locations && data.locations.length > 0) {
            let html = '';
            data.locations.forEach((loc, idx) => {
                html += `
                    <tr>
                        <td>${idx}</td>
                        <td><input type="text" value="${loc.name || ''}" style="width: 100%;" onchange="updateLocation(${idx}, this.value)"></td>
                        <td>
                            <select onchange="updateLocationApi(${idx}, this.value)" style="width: 100%;">
                                <option value="0" ${loc.api === 0 ? 'selected' : ''}>OpenMeteo</option>
                                <option value="1" ${loc.api === 1 ? 'selected' : ''}>ECCC</option>
                            </select>
                        </td>
                        <td>
                            <button class="btn btn-secondary" style="padding: 0.5rem;" onclick="removeLocation(${idx})">Remove</button>
                        </td>
                    </tr>
                `;
            });
            tbody.innerHTML = html;
        } else {
            tbody.innerHTML = '<tr><td colspan="4">No locations configured</td></tr>';
        }
    } catch (error) {
        console.error('Error loading locations:', error);
        const tbody = document.getElementById('locations-tbody');
        if (tbody) tbody.innerHTML = '<tr><td colspan="4" class="error">Error loading locations</td></tr>';
    }
}

/**
 * Load images list
 */
async function loadImages() {
    try {
        const response = await fetch(`${API_BASE_URL}/images/list`);
        const data = await response.json();
        
        const imagesContainer = document.getElementById('images-container');
        const countEl = document.getElementById('image-count');
        
        if (response.ok && data.images && data.images.length > 0) {
            let html = '';
            
            data.images.forEach(image => {
                html += `
                    <div class="image-card">
                        <img src="/api/images/${encodeURIComponent(image.filename)}" alt="${image.filename}" onerror="this.src='data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22300%22 height=%22200%22%3E%3Crect fill=%22%23f0f0f0%22 width=%22300%22 height=%22200%22/%3E%3Ctext x=%2250%25%22 y=%2250%25%22 text-anchor=%22middle%22 dy=%22.3em%22 fill=%22%23999%22%3EImage not found%3C/text%3E%3C/svg%3E'">
                        <div class="image-card-content">
                            <h3>${image.filename}</h3>
                            <p><small>${new Date().toLocaleString()}</small></p>
                            <a href="/api/images/${encodeURIComponent(image.filename)}" target="_blank" class="btn btn-primary" style="margin-top: 0.5rem; text-align: center;">↓ Download</a>
                        </div>
                    </div>
                `;
            });
            
            imagesContainer.innerHTML = html;
            if (countEl) countEl.textContent = `Total: ${data.images.length}`;
        } else {
            imagesContainer.innerHTML = '<p class="loading">No images available</p>';
            if (countEl) countEl.textContent = 'Total: 0';
        }
    } catch (error) {
        console.error('Error loading images:', error);
        const imagesContainer = document.getElementById('images-container');
        if (imagesContainer) imagesContainer.innerHTML = '<p class="error">Error loading images</p>';
    }
}

/**
 * Load all settings
 */
async function loadAllSettings() {
    try {
        const response = await fetch(`${API_BASE_URL}/config/full`);
        const config = await response.json();
        
        if (response.ok) {
            // General Settings
            const refreshInterval = document.getElementById('refresh-interval');
            const theme = document.getElementById('theme');
            if (refreshInterval) refreshInterval.value = config.RefreshTimeMinutes || 10;
            if (theme) theme.value = config.Theme || 'Blue';
            
            // Image Settings
            const imageWidth = document.getElementById('image-width');
            const imageHeight = document.getElementById('image-height');
            const imageFormat = document.getElementById('image-format');
            const imageMargin = document.getElementById('image-margin');
            if (imageWidth) imageWidth.value = config.ImageGeneration?.ImageWidth || 1920;
            if (imageHeight) imageHeight.value = config.ImageGeneration?.ImageHeight || 1080;
            if (imageFormat) imageFormat.value = config.ImageGeneration?.ImageFormat || 'png';
            if (imageMargin) imageMargin.value = config.ImageGeneration?.MarginPixels || 50;
            
            // Video Settings
            const videoQuality = document.getElementById('video-quality');
            const videoResolution = document.getElementById('video-resolution');
            const videoFps = document.getElementById('video-fps');
            const videoCodec = document.getElementById('video-codec');
            const videoHardware = document.getElementById('video-hardware');
            const videoBitrate = document.getElementById('video-bitrate');
            const videoStatic = document.getElementById('video-static');
            const videoTotalDuration = document.getElementById('video-total-duration');
            const videoTotal = document.getElementById('video-total');
            const videoFade = document.getElementById('video-fade');
            const videoFadeDuration = document.getElementById('video-fade-duration');
            
            const video = config.Video || {};
            if (videoQuality) videoQuality.value = video.QualityPreset || 'Balanced';
            if (videoResolution) videoResolution.value = video.ResolutionMode || 'Mode1080p';
            if (videoFps) videoFps.value = video.FrameRate || 30;
            if (videoCodec) videoCodec.value = video.VideoCodec || 'libx264';
            if (videoHardware) videoHardware.checked = video.EnableHardwareEncoding || false;
            if (videoBitrate) videoBitrate.value = video.VideoBitrate || '4M';
            if (videoStatic) videoStatic.value = video.StaticDurationSeconds || 6;
            if (videoTotalDuration) videoTotalDuration.checked = video.UseTotalDuration || false;
            if (videoTotal) videoTotal.value = video.TotalDurationSeconds || 30;
            if (videoFade) videoFade.checked = video.EnableFadeTransitions || false;
            if (videoFadeDuration) videoFadeDuration.value = video.FadeDurationSeconds || 0.1;
            
            // Alert Settings
            const alertsEnabled = document.getElementById('alerts-enabled');
            const alertLanguage = document.getElementById('alert-language');
            const alertHighRisk = document.getElementById('alert-high-risk');
            const alertExcludeWeather = document.getElementById('alert-exclude-weather');
            const alertIncludeTests = document.getElementById('alert-include-tests');
            const alertMaxAge = document.getElementById('alert-max-age');
            
            const alerts = config.AlertReady || {};
            if (alertsEnabled) alertsEnabled.checked = alerts.Enabled || false;
            if (alertLanguage) alertLanguage.value = alerts.PreferredLanguage || 'f';
            if (alertHighRisk) alertHighRisk.checked = alerts.HighRiskOnly || false;
            if (alertExcludeWeather) alertExcludeWeather.checked = alerts.ExcludeWeatherAlerts || false;
            if (alertIncludeTests) alertIncludeTests.checked = alerts.IncludeTests || false;
            if (alertMaxAge) alertMaxAge.value = alerts.MaxAgeHours || 24;
            
            // Radar Settings
            const radarProvince = document.getElementById('radar-province');
            const radarWeatherMaps = document.getElementById('radar-weather-maps');
            const radarFrames = document.getElementById('radar-frames');
            const radarStep = document.getElementById('radar-step');
            
            const eccc = config.ECCC || {};
            if (radarProvince) radarProvince.checked = eccc.EnableProvinceRadar || false;
            if (radarWeatherMaps) radarWeatherMaps.checked = config.ImageGeneration?.EnableWeatherMaps || false;
            if (radarFrames) radarFrames.value = eccc.ProvinceFrames || 8;
            if (radarStep) radarStep.value = eccc.ProvinceFrameStepMinutes || 6;
            
            // Music Settings
            const musicEnable = document.getElementById('music-enable');
            const musicRandom = document.getElementById('music-random');
            const musicTrack = document.getElementById('music-track');
            
            const music = config.Music || {};
            if (musicEnable) musicEnable.checked = music.enableMusicInVideo || false;
            if (musicRandom) musicRandom.checked = music.useRandomMusic || false;
            
            // Load music tracks
            if (music.musicTracks && Array.isArray(music.musicTracks)) {
                const options = '<option value="">No Music</option>' +
                    music.musicTracks.map((t, i) => `<option value="${i}">${t.name}</option>`).join('');
                if (musicTrack) musicTrack.innerHTML = options;
            }
        }
    } catch (error) {
        console.error('Error loading settings:', error);
        showNotification('Error loading settings', 'error');
    }
}

/**
 * Save handlers
 */
async function saveGeneralSettings() {
    const settings = {
        refreshTimeMinutes: parseInt(document.getElementById('refresh-interval').value),
        theme: document.getElementById('theme').value
    };
    
    try {
        const response = await fetch(`${API_BASE_URL}/config/general`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });
        
        if (response.ok) {
            showNotification('General settings saved successfully!', 'success');
            loadAllSettings();
        } else {
            showNotification('Failed to save general settings', 'error');
        }
    } catch (error) {
        console.error('Error saving general settings:', error);
        showNotification('Error saving general settings', 'error');
    }
}

async function saveImageSettings() {
    const settings = {
        imageWidth: parseInt(document.getElementById('image-width').value),
        imageHeight: parseInt(document.getElementById('image-height').value),
        imageFormat: document.getElementById('image-format').value,
        marginPixels: parseInt(document.getElementById('image-margin').value)
    };
    
    try {
        const response = await fetch(`${API_BASE_URL}/config/image`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });
        
        if (response.ok) {
            showNotification('Image settings saved successfully!', 'success');
            loadAllSettings();
        } else {
            showNotification('Failed to save image settings', 'error');
        }
    } catch (error) {
        console.error('Error saving image settings:', error);
        showNotification('Error saving image settings', 'error');
    }
}

async function saveVideoSettings() {
    const settings = {
        qualityPreset: document.getElementById('video-quality').value,
        resolutionMode: document.getElementById('video-resolution').value,
        frameRate: parseInt(document.getElementById('video-fps').value),
        videoCodec: document.getElementById('video-codec').value,
        enableHardwareEncoding: document.getElementById('video-hardware').checked,
        videoBitrate: document.getElementById('video-bitrate').value,
        staticDurationSeconds: parseFloat(document.getElementById('video-static').value),
        useTotalDuration: document.getElementById('video-total-duration').checked,
        totalDurationSeconds: parseInt(document.getElementById('video-total').value),
        enableFadeTransitions: document.getElementById('video-fade').checked,
        fadeDurationSeconds: parseFloat(document.getElementById('video-fade-duration').value)
    };
    
    try {
        const response = await fetch(`${API_BASE_URL}/config/video`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });
        
        if (response.ok) {
            showNotification('Video settings saved successfully!', 'success');
            loadAllSettings();
        } else {
            showNotification('Failed to save video settings', 'error');
        }
    } catch (error) {
        console.error('Error saving video settings:', error);
        showNotification('Error saving video settings', 'error');
    }
}

async function saveMusicSettings() {
    const settings = {
        enableMusicInVideo: document.getElementById('music-enable').checked,
        useRandomMusic: document.getElementById('music-random').checked,
        selectedMusicIndex: parseInt(document.getElementById('music-track').value) || -1
    };
    
    try {
        const response = await fetch(`${API_BASE_URL}/config/music`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });
        
        if (response.ok) {
            showNotification('Music settings saved successfully!', 'success');
            loadAllSettings();
        } else {
            showNotification('Failed to save music settings', 'error');
        }
    } catch (error) {
        console.error('Error saving music settings:', error);
        showNotification('Error saving music settings', 'error');
    }
}

async function saveAlertSettings() {
    const settings = {
        enabled: document.getElementById('alerts-enabled').checked,
        preferredLanguage: document.getElementById('alert-language').value,
        highRiskOnly: document.getElementById('alert-high-risk').checked,
        excludeWeatherAlerts: document.getElementById('alert-exclude-weather').checked,
        includeTests: document.getElementById('alert-include-tests').checked,
        maxAgeHours: parseInt(document.getElementById('alert-max-age').value)
    };
    
    try {
        const response = await fetch(`${API_BASE_URL}/config/alerts`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });
        
        if (response.ok) {
            showNotification('Alert settings saved successfully!', 'success');
            loadAllSettings();
        } else {
            showNotification('Failed to save alert settings', 'error');
        }
    } catch (error) {
        console.error('Error saving alert settings:', error);
        showNotification('Error saving alert settings', 'error');
    }
}

async function saveRadarSettings() {
    const settings = {
        enableProvinceRadar: document.getElementById('radar-province').checked,
        enableWeatherMaps: document.getElementById('radar-weather-maps').checked,
        provinceFrames: parseInt(document.getElementById('radar-frames').value),
        provinceFrameStepMinutes: parseInt(document.getElementById('radar-step').value)
    };
    
    try {
        const response = await fetch(`${API_BASE_URL}/config/radar`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });
        
        if (response.ok) {
            showNotification('Radar settings saved successfully!', 'success');
            loadAllSettings();
        } else {
            showNotification('Failed to save radar settings', 'error');
        }
    } catch (error) {
        console.error('Error saving radar settings:', error);
        showNotification('Error saving radar settings', 'error');
    }
}

/**
 * Action handlers
 */
async function startCycle() {
    console.log('startCycle() called');
    try {
        const response = await fetch(`${API_BASE_URL}/actions/start-cycle`, { method: 'POST' });
        console.log('startCycle response:', response.status);
        if (response.ok) {
            updateCycleStatus('Running');
            showNotification('Cycle started', 'success');
        } else {
            showNotification('Failed to start cycle', 'error');
        }
    } catch (error) {
        console.error('Error starting cycle:', error);
        showNotification('Error starting cycle', 'error');
    }
}

async function stopCycle() {
    console.log('stopCycle() called');
    try {
        const response = await fetch(`${API_BASE_URL}/actions/stop-cycle`, { method: 'POST' });
        console.log('stopCycle response:', response.status);
        if (response.ok) {
            updateCycleStatus('Stopped');
            showNotification('Cycle stopped', 'success');
        } else {
            showNotification('Failed to stop cycle', 'error');
        }
    } catch (error) {
        console.error('Error stopping cycle:', error);
        showNotification('Error stopping cycle', 'error');
    }
}

async function generateStill() {
    console.log('generateStill() called');
    try {
        const response = await fetch(`${API_BASE_URL}/actions/generate-still`, { method: 'POST' });
        console.log('generateStill response:', response.status);
        if (response.ok) {
            showNotification('Generating still images...', 'success');
        } else {
            showNotification('Failed to generate still', 'error');
        }
    } catch (error) {
        console.error('Error generating still:', error);
        showNotification('Error generating still', 'error');
    }
}

async function generateVideo() {
    console.log('generateVideo() called');
    try {
        const response = await fetch(`${API_BASE_URL}/actions/generate-video`, { method: 'POST' });
        console.log('generateVideo response:', response.status);
        if (response.ok) {
            showNotification('Generating video...', 'success');
        } else {
            showNotification('Failed to generate video', 'error');
        }
    } catch (error) {
        console.error('Error generating video:', error);
        showNotification('Error generating video', 'error');
    }
}

function updateCycleStatus(status) {
    const el = document.getElementById('cycle-status');
    if (el) el.textContent = status;
}

function updateAlerts() {
    // Called when alert settings change
}

async function updateLocation(idx, name) {
    // Update location name
}

async function updateLocationApi(idx, api) {
    // Update location API
}

async function removeLocation(idx) {
    // Remove location
    showNotification('Location removed', 'success');
}

/**
 * Show a notification message
 */
function showNotification(message, type = 'info') {
    // Create notification element
    const notification = document.createElement('div');
    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        padding: 1rem 1.5rem;
        background-color: ${
            type === 'success' ? '#43a047' :
            type === 'error' ? '#e53935' :
            '#1976d2'
        };
        color: white;
        border-radius: 4px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
        z-index: 9999;
        animation: slideIn 0.3s ease-in-out;
    `;
    notification.textContent = message;
    
    document.body.appendChild(notification);
    
    // Remove after 3 seconds
    setTimeout(() => {
        notification.style.animation = 'slideOut 0.3s ease-in-out';
        setTimeout(() => notification.remove(), 300);
    }, 3000);
}

// Add slide animations
const style = document.createElement('style');
style.textContent = `
    @keyframes slideIn {
        from {
            transform: translateX(400px);
            opacity: 0;
        }
        to {
            transform: translateX(0);
            opacity: 1;
        }
    }
    
    @keyframes slideOut {
        from {
            transform: translateX(0);
            opacity: 1;
        }
        to {
            transform: translateX(400px);
            opacity: 0;
        }
    }
`;
document.head.appendChild(style);
