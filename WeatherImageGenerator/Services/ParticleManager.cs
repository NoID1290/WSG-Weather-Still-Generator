using System;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Generates and updates particle system data for rain/snow precipitation effects.
    /// Particles are spawned based on precipitation intensity from the GRIB2 grid,
    /// with position/velocity/type stored in flat arrays for GPU upload.
    /// </summary>
    public sealed class ParticleManager : IDisposable
    {
        /// <summary>Maximum simultaneous particles. Shader renders as point sprites or instanced quads.</summary>
        public const int MaxParticles = 8192;

        /// <summary>Particle data: x, y, z, life (vec4 per particle). Upload as RGBA32F texture or SSBO.</summary>
        public float[] Positions { get; private set; } = new float[MaxParticles * 4];

        /// <summary>Particle velocity: vx, vy, size, type (vec4 per particle). type: 0=rain, 1=snow, 2=mix.</summary>
        public float[] Velocities { get; private set; } = new float[MaxParticles * 4];

        /// <summary>Number of active particles this frame.</summary>
        public int ActiveCount { get; private set; }

        private readonly Random _rng = new();
        private float _time;

        /// <summary>
        /// Updates particle system for one frame.
        /// </summary>
        /// <param name="dt">Delta time in seconds</param>
        /// <param name="precipData">Precipitation grid in mm/h (display units), or null to fade out</param>
        /// <param name="gridWidth">Grid Ni</param>
        /// <param name="gridHeight">Grid Nj</param>
        /// <param name="viewMinLat">Viewport minimum latitude</param>
        /// <param name="viewMinLon">Viewport minimum longitude</param>
        /// <param name="viewMaxLat">Viewport maximum latitude</param>
        /// <param name="viewMaxLon">Viewport maximum longitude</param>
        /// <param name="windU">Average U wind component (m/s) for drift. 0 if unavailable.</param>
        /// <param name="windV">Average V wind component (m/s) for drift. 0 if unavailable.</param>
        /// <param name="temperature">Average temperature (°C) to determine rain vs snow. null = rain.</param>
        public void Update(
            float dt,
            float[]? precipData,
            int gridWidth, int gridHeight,
            double viewMinLat, double viewMinLon,
            double viewMaxLat, double viewMaxLon,
            float windU = 0f, float windV = 0f,
            float? temperature = null)
        {
            _time += dt;

            // Determine precipitation type
            float precipType = 0f; // rain
            if (temperature.HasValue)
            {
                if (temperature.Value < -2f) precipType = 1f;      // snow
                else if (temperature.Value < 2f) precipType = 2f;   // mix
            }

            // Move existing particles
            int alive = 0;
            for (int i = 0; i < ActiveCount; i++)
            {
                int pi = i * 4;
                float life = Positions[pi + 3] - dt;
                if (life <= 0f) continue;

                // Advect
                float vx = Velocities[alive * 4];
                float vy = Velocities[alive * 4 + 1];
                float pType = Velocities[alive * 4 + 3];

                Positions[alive * 4]     = Positions[pi]     + vx * dt;
                Positions[alive * 4 + 1] = Positions[pi + 1] + vy * dt;
                Positions[alive * 4 + 2] = Positions[pi + 2]; // z unused for 2D
                Positions[alive * 4 + 3] = life;

                // Snow: add sinusoidal horizontal sway
                if (pType > 0.5f)
                {
                    Positions[alive * 4] += MathF.Sin(_time * 2f + i * 0.7f) * 0.002f * dt;
                }

                Velocities[alive * 4]     = vx;
                Velocities[alive * 4 + 1] = vy;
                Velocities[alive * 4 + 2] = Velocities[pi / 4 * 4 + 2]; // size
                Velocities[alive * 4 + 3] = pType;
                alive++;
            }

            // Spawn new particles based on precipitation intensity
            if (precipData != null && gridWidth > 0 && gridHeight > 0)
            {
                int spawnBudget = Math.Min(MaxParticles - alive, 256); // max spawn per frame
                int spawned = 0;

                for (int attempt = 0; attempt < spawnBudget * 2 && spawned < spawnBudget; attempt++)
                {
                    // Random viewport position in normalized [-1, 1]
                    float nx = (float)(_rng.NextDouble() * 2.0 - 1.0);
                    float ny = (float)(_rng.NextDouble() * 2.0 - 1.0);

                    // Map to lat/lon
                    double lat = viewMinLat + (ny * 0.5 + 0.5) * (viewMaxLat - viewMinLat);
                    double lon = viewMinLon + (nx * 0.5 + 0.5) * (viewMaxLon - viewMinLon);

                    // Sample precipitation at this point (nearest-neighbor)
                    int gi = (int)((lon - viewMinLon) / (viewMaxLon - viewMinLon) * gridWidth);
                    int gj = (int)((viewMaxLat - lat) / (viewMaxLat - viewMinLat) * gridHeight);
                    gi = Math.Clamp(gi, 0, gridWidth - 1);
                    gj = Math.Clamp(gj, 0, gridHeight - 1);
                    int idx = gj * gridWidth + gi;

                    if (idx >= precipData.Length) continue;
                    float intensity = precipData[idx];
                    if (intensity < 0.1f) continue;

                    // Spawn probability proportional to intensity (capped)
                    float prob = Math.Min(intensity / 10f, 1f);
                    if (_rng.NextDouble() > prob) continue;

                    int pi = alive * 4;
                    Positions[pi]     = nx;  // screen x [-1,1]
                    Positions[pi + 1] = 1.1f + (float)_rng.NextDouble() * 0.2f; // start above top
                    Positions[pi + 2] = 0f;
                    Positions[pi + 3] = 1.5f + (float)_rng.NextDouble() * 2f; // lifetime seconds

                    float speed = precipType > 0.5f ? 0.3f : 0.8f; // snow falls slower
                    float size = precipType > 0.5f ? 3f + (float)_rng.NextDouble() * 2f : 1f + (float)_rng.NextDouble() * 1.5f;
                    float windDrift = windU * 0.01f;

                    Velocities[pi]     = windDrift + (float)(_rng.NextDouble() - 0.5) * 0.05f;
                    Velocities[pi + 1] = -speed - (float)_rng.NextDouble() * 0.3f;
                    Velocities[pi + 2] = size;
                    Velocities[pi + 3] = precipType;

                    alive++;
                    spawned++;
                }
            }

            ActiveCount = alive;
        }

        /// <summary>Resets all particles.</summary>
        public void Clear()
        {
            ActiveCount = 0;
            Array.Clear(Positions);
            Array.Clear(Velocities);
        }

        public void Dispose() => Clear();
    }
}
