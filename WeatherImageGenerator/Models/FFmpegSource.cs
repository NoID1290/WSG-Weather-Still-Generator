namespace WeatherImageGenerator.Models
{
    /// <summary>
    /// Specifies the source for FFmpeg binaries.
    /// </summary>
    public enum FFmpegSource
    {
        /// <summary>
        /// Use bundled FFmpeg from Xabe.FFmpeg.Downloader (downloads automatically if needed).
        /// </summary>
        Bundled = 0,

        /// <summary>
        /// Use FFmpeg from the system PATH environment variable.
        /// </summary>
        SystemPath = 1,

        /// <summary>
        /// Use FFmpeg from a custom user-specified path.
        /// </summary>
        Custom = 2
    }
}
