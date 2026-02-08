using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EAS
{
    /// <summary>
    /// Minimal interface for alert providers (Alert Ready, NWS, etc.)
    /// </summary>
    public interface IAlertProvider : IDisposable
    {
        event EventHandler<AlertReceivedEventArgs>? AlertReceived;

        /// <summary>
        /// Fetch alerts (e.g., HTTP feed). Returns shared AlertEntry models.
        /// </summary>
        Task<List<WeatherImageGenerator.Models.AlertEntry>> FetchAlertsAsync(IEnumerable<string>? filterAreas = null);

        /// <summary>
        /// Start any streaming/TCP listeners if applicable.
        /// </summary>
        void StartTcpStreams();
    }
}