using System;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Manages wind field streamline animation using a texture-advection approach
    /// inspired by earth.nullschool.net. Maintains a particle trail texture that
    /// is ping-ponged each frame: particles are advected by sampling the U/V wind grid,
    /// leaving fading trails that create beautiful animated streamlines.
    ///
    /// The rendering backend handles the actual FBO/texture ping-pong in GPU memory;
    /// this class manages the CPU-side particle seed positions and animation parameters.
    /// </summary>
    public sealed class WindFieldAnimator : IDisposable
    {
        /// <summary>Trail texture resolution. Ping-pong between two textures of this size.</summary>
        public int TrailWidth { get; private set; } = 512;
        public int TrailHeight { get; private set; } = 512;

        /// <summary>Number of seed particles per frame injection.</summary>
        public int SeedCount { get; set; } = 4096;

        /// <summary>Trail decay factor per frame (0-1). Lower = longer trails.</summary>
        public float TrailDecay { get; set; } = 0.96f;

        /// <summary>Speed scale multiplier for advection.</summary>
        public float SpeedScale { get; set; } = 0.02f;

        /// <summary>Seed particle positions as normalized UV pairs (interleaved x,y).</summary>
        public float[] SeedPositions { get; private set; } = Array.Empty<float>();

        /// <summary>Current frame index for ping-pong (0 or 1).</summary>
        public int CurrentFrame { get; private set; }

        /// <summary>Animation elapsed time in seconds.</summary>
        public float Time { get; private set; }

        /// <summary>Whether animation data is ready for rendering.</summary>
        public bool IsReady { get; private set; }

        private readonly Random _rng = new();

        /// <summary>
        /// Initializes the animator with a specific trail resolution.
        /// </summary>
        public void Initialize(int trailWidth = 512, int trailHeight = 512)
        {
            TrailWidth = trailWidth;
            TrailHeight = trailHeight;
            SeedPositions = new float[SeedCount * 2];
            RegenerateSeedPositions();
            IsReady = true;
        }

        /// <summary>
        /// Advances the animation by one frame. Call once per render frame.
        /// </summary>
        /// <param name="dt">Delta time in seconds</param>
        public void Update(float dt)
        {
            if (!IsReady) return;

            Time += dt;
            CurrentFrame = 1 - CurrentFrame; // ping-pong

            // Randomly regenerate a portion of seed positions each frame for continuous coverage
            int refreshCount = SeedCount / 8;
            for (int i = 0; i < refreshCount; i++)
            {
                int idx = _rng.Next(SeedCount);
                SeedPositions[idx * 2]     = (float)_rng.NextDouble();
                SeedPositions[idx * 2 + 1] = (float)_rng.NextDouble();
            }
        }

        /// <summary>
        /// Gets the shader parameters for the current frame.
        /// </summary>
        public WindShaderParams GetShaderParams()
        {
            return new WindShaderParams
            {
                TrailDecay = TrailDecay,
                SpeedScale = SpeedScale,
                Time = Time,
                ReadIndex = CurrentFrame,
                WriteIndex = 1 - CurrentFrame,
            };
        }

        private void RegenerateSeedPositions()
        {
            for (int i = 0; i < SeedCount; i++)
            {
                SeedPositions[i * 2]     = (float)_rng.NextDouble();
                SeedPositions[i * 2 + 1] = (float)_rng.NextDouble();
            }
        }

        public void Dispose()
        {
            IsReady = false;
            SeedPositions = Array.Empty<float>();
        }
    }

    /// <summary>
    /// Parameters passed to the wind streamline shader each frame.
    /// </summary>
    public struct WindShaderParams
    {
        public float TrailDecay;
        public float SpeedScale;
        public float Time;
        public int ReadIndex;
        public int WriteIndex;
    }
}
