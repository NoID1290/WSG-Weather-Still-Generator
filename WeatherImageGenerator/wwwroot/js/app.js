// API Base URL
const API_BASE_URL = '/api';

// DOM Elements
const navLinks = document.querySelectorAll('.nav-link');
const tabContents = document.querySelectorAll('.tab-content');
const settingsForm = document.getElementById('settings-form');

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    setupNavigation();
    loadStatus();
    refreshData();
    
    // Set up periodic refresh
    setInterval(refreshData, 30000); // Refresh every 30 seconds
    
    // Set up settings form
    if (settingsForm) {
        settingsForm.addEventListener('submit', saveSettings);
    }
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
                } else if (tabName === 'images') {
                    loadImages();
                } else if (tabName === 'settings') {
                    loadSettings();
                }
            }
        });
    });
}

/**
 * Load system status
 */
async function loadStatus() {
    try {
        const response = await fetch(`${API_BASE_URL}/status`);
        const data = await response.json();
        
        if (response.ok) {
            const statusEl = document.getElementById('server-status');
            if (statusEl) {
                statusEl.innerHTML = '<span class="status-indicator active"></span>Running';
            }
            
            const versionEl = document.getElementById('app-version');
            if (versionEl && data.version) {
                versionEl.textContent = data.version;
            }
            
            const lastUpdateEl = document.getElementById('last-update');
            if (lastUpdateEl) {
                const now = new Date();
                lastUpdateEl.textContent = now.toLocaleTimeString();
            }
        }
    } catch (error) {
        console.error('Error loading status:', error);
        const statusEl = document.getElementById('server-status');
        if (statusEl) {
            statusEl.innerHTML = '<span class="status-indicator inactive"></span>Offline';
        }
    }
}

/**
 * Refresh all data
 */
async function refreshData() {
    loadStatus();
    const activeTab = document.querySelector('.tab-content.active');
    
    if (activeTab.id === 'dashboard-tab') {
        // Dashboard is already updated by loadStatus
    } else if (activeTab.id === 'weather-tab') {
        loadWeatherData();
    } else if (activeTab.id === 'images-tab') {
        loadImages();
    }
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
        document.getElementById('weather-container').innerHTML = '<p class="error">Error loading weather data</p>';
        document.getElementById('forecast-container').innerHTML = '<p class="error">Error loading forecast data</p>';
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
        
        if (response.ok && data.images && data.images.length > 0) {
            let html = '';
            
            data.images.forEach(image => {
                html += `
                    <div class="image-card">
                        <img src="/api/images/${encodeURIComponent(image.filename)}" alt="${image.filename}" onerror="this.src='data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22300%22 height=%22200%22%3E%3Crect fill=%22%23f0f0f0%22 width=%22300%22 height=%22200%22/%3E%3Ctext x=%2250%25%22 y=%2250%25%22 text-anchor=%22middle%22 dy=%22.3em%22 fill=%22%23999%22%3EImage not found%3C/text%3E%3C/svg%3E'">
                        <div class="image-card-content">
                            <h3>${image.filename}</h3>
                            <p><small>${new Date().toLocaleString()}</small></p>
                            <a href="/api/images/${encodeURIComponent(image.filename)}" target="_blank" class="btn btn-primary" style="margin-top: 0.5rem;">↓ Download</a>
                        </div>
                    </div>
                `;
            });
            
            imagesContainer.innerHTML = html;
        } else {
            imagesContainer.innerHTML = '<p class="loading">No images available</p>';
        }
    } catch (error) {
        console.error('Error loading images:', error);
        document.getElementById('images-container').innerHTML = '<p class="error">Error loading images</p>';
    }
}

/**
 * Load settings
 */
async function loadSettings() {
    try {
        const response = await fetch(`${API_BASE_URL}/settings/web`);
        const data = await response.json();
        
        if (response.ok) {
            // Populate form with current settings
            const refreshInterval = document.getElementById('refresh-interval');
            const imageWidth = document.getElementById('image-width');
            const imageHeight = document.getElementById('image-height');
            
            // These would be fetched from the full config
            // For now, we just show defaults
            if (refreshInterval) refreshInterval.value = 10;
            if (imageWidth) imageWidth.value = 1920;
            if (imageHeight) imageHeight.value = 1080;
        }
    } catch (error) {
        console.error('Error loading settings:', error);
    }
}

/**
 * Save settings
 */
async function saveSettings(e) {
    e.preventDefault();
    
    const settings = {
        refreshInterval: parseInt(document.getElementById('refresh-interval').value),
        imageWidth: parseInt(document.getElementById('image-width').value),
        imageHeight: parseInt(document.getElementById('image-height').value)
    };
    
    try {
        const response = await fetch(`${API_BASE_URL}/settings/web`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(settings)
        });
        
        if (response.ok) {
            showNotification('Settings saved successfully!', 'success');
        } else {
            showNotification('Failed to save settings', 'error');
        }
    } catch (error) {
        console.error('Error saving settings:', error);
        showNotification('Error saving settings', 'error');
    }
}

/**
 * Open settings in new tab
 */
function openSettings() {
    const settingsLink = document.querySelector('[data-tab="settings"]');
    if (settingsLink) {
        settingsLink.click();
    }
}

/**
 * Show notification
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
