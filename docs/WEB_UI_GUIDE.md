# Weather Image Generator - Web UI Integration Guide

## Overview

The Weather Image Generator now includes a **Web UI (Web User Interface)** that allows you to access and control your application from any web browser, including from other computers on your network or the internet.

## Features

- 📱 **Remote Access**: Access your weather interface from any device with a web browser
- 🌐 **Network Access**: Configure your app to be accessible from other computers on your network
- ⚙️ **Easy Configuration**: Simple port settings in the new Web UI settings tab
- 🎨 **Modern Interface**: Clean, responsive web design that works on desktop and mobile
- 📊 **Dashboard**: View system status, weather data, and generated images
- 🖼️ **Image Gallery**: Browse and download generated weather images
- ⚡ **Real-time Updates**: Live status updates and data refresh

## Quick Start

### 1. Enable Web UI in Settings

1. Launch the **Weather Image Generator** application
2. Open **Settings** (⚙️ button or Settings menu)
3. Click on the **🌐 Web UI** tab
4. Check **"Enable Web UI Server"**
5. Configure the **Port** (default: 5000)
6. Check **"Allow Remote Access (other computers on network)"** if you want remote access
7. Click **✔ Save**

### 2. Access the Web UI

**From the same computer:**
- Open your web browser and go to: `http://localhost:5000`

**From another computer on the network:**
- Replace `localhost` with your computer's hostname or IP address
- Example: `http://MyComputerName:5000` or `http://192.168.1.100:5000`

### 3. Using the Web Interface

The Web UI provides several tabs:

#### Dashboard Tab
- View system status
- See last update time
- Quick access to settings and refresh

#### Weather Tab
- Current weather information
- Weather forecast data
- Data is fetched from your configured API sources

#### Images Tab
- Browse all generated weather images
- Download images directly
- View image details and timestamps

#### Settings Tab
- Adjust refresh intervals
- Modify image dimensions
- Save configuration changes remotely

## Configuration

### Web UI Settings in appsettings.json

```json
{
  "WebUI": {
    "Enabled": false,
    "Port": 5000,
    "AllowRemoteAccess": true,
    "EnableCORS": true,
    "CORSOrigins": ["*"]
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Enable/disable Web UI server | `false` |
| `Port` | Port number for web server | `5000` |
| `AllowRemoteAccess` | Allow access from other computers | `true` |
| `EnableCORS` | Enable CORS for cross-origin requests | `true` |
| `CORSOrigins` | List of allowed CORS origins | `["*"]` |

## Accessing from Different Locations

### Local Network Access
If your computer is on a network with other devices:

1. Find your computer's hostname or IP address
2. Use `http://<hostname>:5000` or `http://<ip-address>:5000`

**Finding your IP address:**
- Windows: Open Command Prompt and type `ipconfig`
- Look for "IPv4 Address" under your network adapter

### Internet Access (Advanced)
To access from the internet:

1. Configure port forwarding on your router
2. Obtain your public IP address or use a dynamic DNS service
3. Access via `http://<public-ip>:port`

⚠️ **Security Note**: Opening your application to the internet requires proper security measures. Consider:
- Using a VPN
- Firewall rules
- Running on a non-standard port
- Setting up authentication (future feature)

## API Endpoints

The Web UI communicates with the following REST API endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/status` | GET | Get server status and version |
| `/api/weather/current` | GET | Get current weather data |
| `/api/weather/forecast` | GET | Get weather forecast data |
| `/api/images/list` | GET | Get list of generated images |
| `/api/images/{filename}` | GET | Download a specific image |
| `/api/settings/web` | GET | Get Web UI settings |
| `/api/settings/web` | POST | Update Web UI settings |

## Browser Compatibility

The Web UI is compatible with:
- Chrome/Chromium (latest)
- Firefox (latest)
- Safari (latest)
- Edge (latest)
- Mobile browsers (iOS Safari, Chrome Mobile)

## Troubleshooting

### Connection Refused
- Ensure Web UI is enabled in settings
- Check the port number is correct
- Verify the application is running
- Check firewall settings

### Cannot Access from Another Computer
- Ensure "Allow Remote Access" is enabled
- Verify both computers are on the same network
- Check the hostname/IP address is correct
- Disable Windows Firewall temporarily to test

### Port Already in Use
- Change the port number to an unused port (e.g., 5001, 8000, 9000)
- Ensure the port is between 1024-65535
- Check if another application is using the port

### Web UI Not Starting
- Check the application logs for error messages
- Verify appsettings.json has valid WebUI configuration
- Try restarting the application

## Testing the Connection

1. Click the **🔗 Test Connection** button in the Web UI settings tab
2. The status will show if the server is running and accessible
3. Use the **🌐 Open in Browser** button to launch the web interface

## Security Considerations

⚠️ **Important**: The Web UI is designed for local network use. If exposing to the internet:

1. **Authentication**: Consider implementing password protection
2. **HTTPS**: Use SSL/TLS certificates for encrypted connections
3. **Firewall**: Restrict access to specific IP addresses
4. **VPN**: Use a VPN tunnel for secure remote access
5. **Updates**: Keep the application updated for security patches

## Performance

- The Web UI runs on a separate ASP.NET Core server
- Does not interfere with the main image generation process
- Lightweight requests have minimal impact on system resources
- Image downloads are streamed efficiently

## Future Enhancements

Planned improvements:
- User authentication and login
- HTTPS/SSL support
- Data export options
- Real-time notifications
- Mobile app
- Multi-user support

## File Structure

Web UI files are located in:
```
WeatherImageGenerator/
├── wwwroot/                 # Static web files
│   ├── index.html          # Main HTML page
│   ├── css/
│   │   └── style.css       # Styling
│   └── js/
│       └── app.js          # JavaScript functionality
├── Services/
│   └── WebUIService.cs     # Web server implementation
└── Models/
    └── WebUISettings.cs    # Configuration model
```

## Support

For issues or questions:
1. Check the troubleshooting section above
2. Review application logs
3. Verify appsettings.json configuration
4. Test with a different port number

## Example URLs

| Scenario | URL |
|----------|-----|
| Local access | `http://localhost:5000` |
| Same network (hostname) | `http://COMPUTERNAME:5000` |
| Same network (IP) | `http://192.168.1.100:5000` |
| Custom port | `http://localhost:8080` |
| Windows startup with Web UI | Application starts with Web UI automatically enabled |

---

**Version**: 1.0.0  
**Last Updated**: January 2026
