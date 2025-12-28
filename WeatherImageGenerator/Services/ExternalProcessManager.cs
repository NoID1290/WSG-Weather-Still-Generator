using System;
using System.Collections.Generic;
using System.Diagnostics;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    internal static class ExternalProcessManager
    {
        private static readonly object _lock = new object();
        private static readonly List<Process> _procs = new List<Process>();

        public static void RegisterProcess(Process p)
        {
            if (p == null) return;
            lock (_lock)
            {
                _procs.Add(p);
            }
        }

        public static void UnregisterProcess(Process p)
        {
            if (p == null) return;
            lock (_lock)
            {
                _procs.RemoveAll(x => x == p);
            }
        }

        public static void CancelAll()
        {
            lock (_lock)
            {
                foreach (var p in _procs.ToArray())
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
                _procs.Clear();
            }
            Logger.Log("[INFO] External processes cancelled by user.");
        }
    }
}