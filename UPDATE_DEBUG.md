# Update System Debugging

When an update is triggered, the updater helper creates a detailed log file for troubleshooting:

## Log Location
```
%TEMP%\WSG_Updater.log
```

In Windows, you can open this with:
```
echo %TEMP%
```

Then navigate to that folder and open `WSG_Updater.log`

## What to Look For

1. **Process ID**: Check if it waited for the main process to exit
2. **File Detection**: Verify staging directory and files were found
3. **File Copying**: Each copied file is logged individually
4. **Errors**: Any failed file operations are logged with details
5. **Launch**: Whether the new app version was successfully launched

## Common Issues

### Staging directory not found
- Check if Update Service properly created files
- Verify temp folder has write permissions

### Files still locked
- The updater waits 10 seconds for files to unlock
- Increase timeout if needed

### Launch fails
- Check if WSG.exe path is correct
- Verify the main executable exists after update

## Example Log Output
```
[2026-02-04 16:53:14] Updater started
[2026-02-04 16:53:14] Arguments: S:\VScodeProjects\weather-still-api\WeatherImageGenerator\artifacts\WeatherImageGenerator-1.8.4.0204 | 12345
[2026-02-04 16:53:14] App Directory: S:\VScodeProjects\weather-still-api\WeatherImageGenerator\artifacts\WeatherImageGenerator-1.8.4.0204
[2026-02-04 16:53:14] Staging Directory: C:\Users\username\AppData\Local\Temp\WSG_Update_Staging
[2026-02-04 16:53:14] Waiting for process 12345 to exit...
[2026-02-04 16:53:15] Process 12345 exited
[2026-02-04 16:53:15] Waiting for application files to be unlocked...
[2026-02-04 16:53:15] All files unlocked
[2026-02-04 16:53:15] Found 45 files in staging directory
[2026-02-04 16:53:16] Updated: WSG.exe
[2026-02-04 16:53:16] Updated: WeatherImageGenerator.dll
...
[2026-02-04 16:53:20] Update complete: 45 files applied, 0 failed
[2026-02-04 16:53:20] Launching application: S:\VScodeProjects\weather-still-api\WeatherImageGenerator\artifacts\WeatherImageGenerator-1.8.4.0204\WSG.exe
[2026-02-04 16:53:20] Application launched successfully
```
