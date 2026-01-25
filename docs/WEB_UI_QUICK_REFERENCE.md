# Enhanced Web UI - Quick Reference

## Access Points

| Access Type | URL |
|-------------|-----|
| Local (same computer) | `http://localhost:5000` |
| Local network | `http://[your-ip]:5000` |
| Hostname | `http://[computer-name]:5000` |

## Tab Navigation

| Tab | Purpose | Key Features |
|-----|---------|--------------|
| 🏠 **Dashboard** | System overview | Status, last update, quick actions |
| 🌦️ **Weather** | Weather data | Current conditions, forecast |
| 📍 **Locations** | Location management | Add/edit/remove weather locations |
| 📸 **Images** | Image gallery | View and download generated images |
| ⚠️ **Alerts** | Alert configuration | Emergency alerts, radar settings |
| 🎥 **Video** | Video generation | Codec, quality, music, timing |
| ⚙️ **Settings** | General configuration | Theme, refresh, image format |

## Quick Actions

| Action | Button | Effect |
|--------|--------|--------|
| Start Updates | ▶️ Start Cycle | Begin automatic generation |
| Stop Updates | ⏹️ Stop Cycle | Halt automatic generation |
| Refresh Now | 🔄 Refresh | Immediate data refresh |
| Generate Stills | 📸 Generate Still | Create images immediately |
| Make Video | 🎥 Make Video | Generate video from images |

## Common Settings

### Essential Video Settings
```
Quality: Balanced
Resolution: 1080p
Codec: H.264
Bitrate: 4 Mbps
FPS: 30
Slide Duration: 6 seconds
```

### Recommended Refresh
- For hourly updates: 60 minutes
- For real-time: 5-10 minutes
- For on-demand: Variable

### Optimal Image Dimensions
- **16:9 ratio**: 1920x1080 (1080p)
- **16:9 ratio**: 1280x720 (720p)
- **4:3 ratio**: 1600x1200
- **Custom**: Any width/height within limits

## Configuration Shortcuts

### Enable All Features
1. Go to **Video** → Enable Fade, Set Duration
2. Go to **Alerts** → Enable Alerts, Enable Radar
3. Go to **Music** → Enable Music, Select Track
4. Save all

### Performance Mode
1. Go to **Video** → Set Quality: Fast
2. Go to **Alerts** → Reduce Radar Frames to 4
3. Set Refresh Interval: 30 minutes
4. Save

### High Quality Mode
1. Go to **Video** → Set Quality: High Quality
2. Set Resolution: 1440p or 2160p
3. Enable Hardware Encoding
4. Set Bitrate: 8M or higher
5. Save

## API Quick Reference

### Get Current Status
```bash
curl http://localhost:5000/api/status
```

### Get Configuration
```bash
curl http://localhost:5000/api/config/full
```

### Start Cycle
```bash
curl -X POST http://localhost:5000/api/actions/start-cycle
```

### Stop Cycle
```bash
curl -X POST http://localhost:5000/api/actions/stop-cycle
```

### Generate Still
```bash
curl -X POST http://localhost:5000/api/actions/generate-still
```

### Generate Video
```bash
curl -X POST http://localhost:5000/api/actions/generate-video
```

### Save Settings
```bash
curl -X POST http://localhost:5000/api/config/[section] \
  -H "Content-Type: application/json" \
  -d '{...settings...}'
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+S` | Save current tab (if applicable) |
| `Ctrl+R` | Refresh page |
| `Tab` | Navigate between form fields |
| `Enter` | Submit form |

## Notification Types

| Type | Color | Meaning |
|------|-------|---------|
| ✅ Success | Green | Operation completed successfully |
| ❌ Error | Red | Operation failed |
| ℹ️ Info | Blue | Informational message |

## File Size Limits

| Setting | Min | Max | Default |
|---------|-----|-----|---------|
| Image Width | 320 px | 7680 px | 1920 px |
| Image Height | 240 px | 4320 px | 1080 px |
| Refresh Interval | 1 min | 1440 min | 10 min |
| Bitrate | 1 Mbps | 20 Mbps | 4 Mbps |
| FPS | 24 fps | 240 fps | 30 fps |

## Troubleshooting Checklist

- [ ] Server enabled in PC client
- [ ] Correct IP/hostname and port
- [ ] Port not blocked by firewall
- [ ] Browser cache cleared
- [ ] JavaScript enabled
- [ ] Using supported browser
- [ ] Network connection stable
- [ ] Sufficient disk space

## Browser Developer Tools

Open with: `F12` or `Ctrl+Shift+I`

**Console Tab**: Check for JavaScript errors
**Network Tab**: Verify API calls are successful
**Application Tab**: Check local storage and cookies
**Performance Tab**: Monitor page load time

## Mobile Optimization Tips

1. Portrait mode for better readability
2. Use landscape for form entry
3. Double-tap to zoom on form fields
4. Use mobile-friendly fonts
5. Reduce image size if slow

## Regular Maintenance

- **Weekly**: Check disk space for images
- **Monthly**: Review and archive old images
- **Monthly**: Update video quality settings if needed
- **Quarterly**: Check for application updates
- **Quarterly**: Review alert filters

## Performance Benchmarks

| Setting | Speed | Quality |
|---------|-------|---------|
| Fast | 5-10 min | 720p |
| Balanced | 10-15 min | 1080p |
| High Quality | 20-30 min | 1440p+ |

## Default Locations

| Setting | Default Value |
|---------|---------------|
| Web Port | 5000 |
| Image Output | WeatherImages/ |
| Video Output | WeatherImages/ |
| Refresh Interval | 10 minutes |
| Video Duration | 30 seconds |
| Slide Duration | 6 seconds |

## Contact & Support

For issues or questions:
1. Check WEB_UI_ENHANCED.md for detailed documentation
2. Review application logs
3. Check browser console (F12)
4. Verify network connectivity
5. Test with different browser
