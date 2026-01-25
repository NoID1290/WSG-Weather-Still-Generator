# Enhanced Web UI - Complete Control & Settings Guide

## Overview

The Weather Image Generator now features a comprehensive **Enhanced Web UI** that provides complete remote control of your application, matching the functionality of the PC client while offering a modern, responsive interface suitable for any device.

## Key Features

### 🎛️ Complete Control

- **Start/Stop Cycles**: Begin or halt automatic update cycles from anywhere
- **Generate Still Images**: Trigger immediate image generation
- **Generate Videos**: Create videos on-demand with current settings
- **Real-time Status**: Monitor cycle status and last update time

### ⚙️ Comprehensive Settings Management

The web UI provides full access to all application settings organized into logical sections:

#### General Settings
- Refresh interval (update frequency in minutes)
- Theme selection (Blue, Dark, Light)

#### Image Generation Settings
- Image dimensions (width/height in pixels)
- Output format (PNG, JPEG, BMP)
- Margin size around images
- Font family and sizing
- Enable/disable province radar and weather maps

#### Video Generation Settings
- Quality presets (Fast, Balanced, High Quality)
- Resolution modes (720p, 1080p, 1440p, 4K)
- Frame rate (24-60 fps)
- Video codec selection (H.264, H.265, NVIDIA HEVC)
- Hardware encoding support (NVENC)
- Bitrate selection (2-12 Mbps)
- Slide duration and total duration settings
- Fade transition controls

#### Music Settings
- Enable/disable music in videos
- Select specific track or use random selection
- Music track management

#### Alerts & Radar Settings
- **Emergency Alerts**
  - Enable/disable Alert Ready feed
  - Language preference (English/Français)
  - High-risk only filtering
  - Weather alert inclusion/exclusion
  - Test alert inclusion
  - Maximum alert age configuration

- **Radar Animation**
  - Enable/disable province radar
  - Enable/disable weather maps
  - Frame count configuration
  - Frame step interval (minutes)

### 📍 Location Management

- View all configured weather locations (up to 9)
- Manage location names and API sources
- Quick add/remove interface

### 🎨 Modern Interface Design

- **Responsive Layout**: Works perfectly on desktop, tablet, and mobile
- **Tab-based Navigation**: Organized sections for easy navigation
- **Real-time Feedback**: Notifications for all actions
- **Professional Styling**: Clean, modern design with smooth animations
- **Accessibility**: Properly labeled controls and semantic HTML

## Tabs Overview

### Dashboard
Quick overview of system status and immediate actions:
- Server status indicator
- Last update time
- Application version
- Cycle status
- Quick action buttons

### Weather
View current weather and forecast data from configured APIs

### Locations
Manage your weather monitoring locations:
- View all 9 available location slots
- Edit location names
- Select API source (OpenMeteo or ECCC) for each location
- Add or remove locations

### Images
Browse and download all generated images:
- Gallery view with thumbnails
- Image metadata
- Direct download links
- Reload image list

### Alerts
Configure emergency alert handling:
- Alert feed settings
- Radar animation controls
- High-risk filtering
- Language and age preferences

### Video
Complete video generation configuration:
- Quality and resolution presets
- Codec and bitrate settings
- Hardware acceleration options
- Timing and transition effects
- Music track selection

### Settings
General application and image generation settings:
- Refresh intervals
- Theme selection
- Image dimensions and format
- Margin and styling options

## API Endpoints

All configuration changes are made through RESTful API endpoints:

### Configuration Endpoints

**GET /api/config/full**
- Retrieves complete application configuration
- Used on initial settings page load

**GET /api/config/locations**
- Returns all configured locations and their API sources

**POST /api/config/general**
- Updates general settings (refresh interval, theme)

**POST /api/config/image**
- Updates image generation settings

**POST /api/config/video**
- Updates video generation settings

**POST /api/config/music**
- Updates music configuration

**POST /api/config/alerts**
- Updates alert and emergency notification settings

**POST /api/config/radar**
- Updates radar animation settings

### Action Endpoints

**POST /api/actions/start-cycle**
- Starts the automatic update cycle

**POST /api/actions/stop-cycle**
- Stops the automatic update cycle

**POST /api/actions/generate-still**
- Triggers still image generation

**POST /api/actions/generate-video**
- Triggers video generation

### Status Endpoints

**GET /api/status**
- Returns current server status and version

**GET /api/weather/current**
- Returns current weather data

**GET /api/weather/forecast**
- Returns forecast data

**GET /api/images/list**
- Returns list of generated images

**GET /api/images/{filename}**
- Downloads a specific image

## Usage Workflow

### Basic Setup

1. **Enable Web UI** (PC Client)
   - Open Settings → 🌐 Web UI tab
   - Enable the server
   - Choose port (default 5000)
   - Save settings

2. **Access from Browser**
   - Local: `http://localhost:5000`
   - Remote: `http://[hostname]:5000` or `http://[ip-address]:5000`

3. **Configure Settings**
   - Navigate to desired settings tab
   - Adjust parameters as needed
   - Save changes (posted to API)

### Managing Locations

1. Go to **Locations** tab
2. View all 9 available location slots
3. Edit location names
4. Select API source for each (OpenMeteo=0, ECCC=1)
5. Locations update immediately

### Generating Content

1. Go to **Dashboard** tab
2. Click action buttons:
   - **▶️ Start Cycle** - Begin automatic updates
   - **⏹️ Stop Cycle** - Halt updates
   - **📸 Generate Still** - Create images immediately
   - **🎥 Make Video** - Generate video with current settings
3. Monitor cycle status in real-time

### Configuring Alerts

1. Go to **Alerts** tab
2. Enable/disable emergency alert feed
3. Select language and filters
4. Configure radar animation settings
5. Save changes

### Optimizing Video Output

1. Go to **Video** tab
2. Select quality preset (Fast/Balanced/High Quality)
3. Choose resolution (720p to 4K)
4. Configure codec and bitrate
5. Enable hardware acceleration if available
6. Set timing and transition effects
7. Save settings

## Remote Access Setup

### Local Network Access

1. Find your computer's IP address:
   - Windows: Open Command Prompt, type `ipconfig`
   - Look for "IPv4 Address"

2. From another computer on the same network:
   - `http://[your-ip]:5000`
   - Example: `http://192.168.1.100:5000`

### Internet Access (Advanced)

For access from outside your network, configure:
- Port forwarding on your router
- Dynamic DNS (if IP address changes)
- Firewall rules to allow the port

## Mobile Access

The enhanced web UI is fully responsive and works great on mobile devices:
- Optimized layout for small screens
- Touch-friendly buttons and controls
- Efficient tab navigation
- Real-time notifications

## Settings Synchronization

All settings changed in the web UI are:
- Saved to `appsettings.json`
- Synchronized with PC client
- Persistent across restarts
- Automatically applied to next cycle

## Notifications

Real-time feedback notifications appear for:
- ✅ Successful setting saves
- ❌ Configuration errors
- ⚠️ Action results
- ℹ️ Status updates

## Browser Compatibility

The enhanced web UI works on:
- Chrome/Chromium (latest)
- Firefox (latest)
- Safari (latest)
- Edge (latest)
- Mobile browsers (Chrome Mobile, Safari iOS)

## Performance Tips

1. **Refresh Interval**: Set appropriate update frequency (5-60 minutes)
2. **Video Presets**: Use "Balanced" for most use cases
3. **Radar Frames**: 6-8 frames balances quality and speed
4. **Cache**: Enable tile caching for maps (enabled by default)

## Troubleshooting

### Can't Access Web UI
- Ensure server is enabled in PC client settings
- Check firewall allows the port
- Verify you're using correct IP/hostname and port
- Look for error messages in application logs

### Settings Not Saving
- Check browser console for errors (F12)
- Verify API endpoints are responding
- Ensure you have write permissions to appsettings.json

### Performance Issues
- Reduce video resolution
- Increase refresh interval
- Disable unnecessary features
- Check system resources

### Images Not Displaying
- Verify images are generated and in output directory
- Check file permissions
- Clear browser cache
- Verify image format is correct

## Advanced Features

### API Integration

You can integrate the Web UI API with other tools:

```javascript
// Example: Start cycle from external tool
fetch('http://localhost:5000/api/actions/start-cycle', {
    method: 'POST'
})
.then(response => response.json())
.then(data => console.log(data));
```

### Custom Scheduling

Use external schedulers to call API endpoints:
- Windows Task Scheduler
- Cron (Linux)
- Third-party automation tools

### Monitoring

Check status periodically:
```javascript
fetch('http://localhost:5000/api/status')
.then(r => r.json())
.then(data => console.log(data));
```

## File Locations

```
WeatherImageGenerator/
├── wwwroot/
│   ├── index.html          # Web UI page
│   ├── css/
│   │   └── style.css       # Styling
│   └── js/
│       └── app.js          # Client-side logic
├── Services/
│   └── WebUIService.cs     # API implementation
└── appsettings.json        # Configuration
```

## Version History

**Enhanced Web UI v1.0**
- Complete feature parity with PC client
- 7 main tabs (Dashboard, Weather, Locations, Images, Alerts, Video, Settings)
- 20+ API endpoints
- Responsive mobile-friendly design
- Real-time status and notifications
- Full settings management

## Support & Contributing

For issues or suggestions related to the Web UI:
1. Check the troubleshooting section
2. Review application logs
3. Check browser console for errors
4. Submit issues with detailed information

## License

The Enhanced Web UI is part of the Weather Image Generator project and follows the same license terms.
