# Music Directory

This directory contains music tracks that can be used for video generation.

## Demo Tracks

The following demo tracks are included:
- **demo_calm_ambient.mp3** - Calm ambient background music
- **demo_upbeat_energy.mp3** - Upbeat energetic music
- **demo_smooth_jazz.mp3** - Smooth jazz background
- **demo_electronic_chill.mp3** - Electronic chill music

## Adding Your Own Music

You can add your own music files through the Music management form in the application. Supported formats include:
- MP3 (.mp3)
- WAV (.wav)
- OGG (.ogg)
- M4A (.m4a)
- AAC (.aac)
- FLAC (.flac)
- WMA (.wma)

## About BIN/DAT Files

You asked about using .BIN or .DAT files for music:

### What are BIN/DAT files?
- **.BIN** files are generic binary data files - they could contain anything
- **.DAT** files are also generic data files
- Neither format is specifically for music

### Why NOT use them for music:
1. **No standard format** - BIN/DAT files have no specific structure, so FFmpeg wouldn't know how to read them
2. **Compatibility issues** - Video editing tools expect standard audio formats
3. **Complexity** - You'd need custom code to convert BIN/DAT to actual audio
4. **Size concerns** - These formats don't provide compression benefits

### Better alternatives:
- **MP3** - Universal compatibility, good compression
- **AAC** - Better quality than MP3 at same bitrate
- **OGG/Vorbis** - Open source, good quality
- **FLAC** - Lossless compression for highest quality

### If you really need to embed music in the executable:
Instead of BIN/DAT files, you can:
1. **Embed as resources** - Add audio files as embedded resources in your C# project
2. **Base64 encoding** - Convert small audio files to base64 strings
3. **Resource DLL** - Create a separate DLL containing audio data

But for this application, keeping music as regular audio files in this folder is the simplest and most flexible approach!

## How Music Selection Works

1. **Random Mode** (default): A random track is selected each time you generate a video
2. **Specific Mode**: The selected track from your list is always used
3. Tracks marked as "Demo" are pre-included with the application
4. You can add custom tracks from anywhere on your system

## License Note

Remember to only use music you have the rights to use in your videos!
