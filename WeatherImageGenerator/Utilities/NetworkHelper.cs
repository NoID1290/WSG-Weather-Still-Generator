using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Helper class for network-related operations
    /// </summary>
    public static class NetworkHelper
    {
        /// <summary>
        /// Gets the local IP address (IPv4) of the machine on the local network
        /// </summary>
        /// <returns>Local IP address as a string, or "Unable to determine" if not found</returns>
        public static string GetLocalIPAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    // Connect to a public DNS server (doesn't actually send anything)
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to get local IP address (method 1): {ex.Message}", Logger.LogLevel.Debug);
            }

            // Fallback method
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var localIP = host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                                         !IPAddress.IsLoopback(ip));

                if (localIP != null)
                {
                    return localIP.ToString();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to get local IP address (method 2): {ex.Message}", Logger.LogLevel.Debug);
            }

            return "Unable to determine";
        }

        /// <summary>
        /// Gets the public/outgoing IP address (WAN IP) by querying external services
        /// </summary>
        /// <returns>Public IP address as a string, or "Unable to determine" if not found</returns>
        public static async Task<string> GetPublicIPAddressAsync()
        {
            string[] ipServices = new[]
            {
                "https://api.ipify.org",
                "https://icanhazip.com",
                "https://ipinfo.io/ip",
                "https://ifconfig.me/ip"
            };

            using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                foreach (var service in ipServices)
                {
                    try
                    {
                        var response = await httpClient.GetStringAsync(service);
                        var ipAddress = response.Trim();

                        // Validate that it's a valid IP address
                        if (IPAddress.TryParse(ipAddress, out _))
                        {
                            return ipAddress;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to get public IP from {service}: {ex.Message}", Logger.LogLevel.Debug);
                    }
                }
            }

            return "Unable to determine";
        }

        /// <summary>
        /// Gets the public/outgoing IP address synchronously
        /// </summary>
        /// <returns>Public IP address as a string, or "Unable to determine" if not found</returns>
        public static string GetPublicIPAddress()
        {
            try
            {
                return GetPublicIPAddressAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to get public IP address: {ex.Message}", Logger.LogLevel.Debug);
                return "Unable to determine";
            }
        }
    }
}
