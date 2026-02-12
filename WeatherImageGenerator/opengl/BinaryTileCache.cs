using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WeatherImageGenerator.OpenGL
{
    /// <summary>
    /// High-performance binary tile cache with index for fast lookups.
    /// Uses a single .bin file to store all tiles and an index for quick access.
    /// </summary>
    public class BinaryTileCache : IDisposable
    {
        private readonly string _cacheFilePath;
        private readonly string _indexFilePath;
        private readonly ConcurrentDictionary<(int z, int x, int y), TileCacheEntry> _index;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _readLock = new SemaphoreSlim(1, 1);
        private FileStream? _cacheFileStream;
        private long _currentFilePosition = 0;
        
        public BinaryTileCache(string cacheDirectory)
        {
            if (!Directory.Exists(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            _cacheFilePath = Path.Combine(cacheDirectory, "tiles.bin");
            _indexFilePath = Path.Combine(cacheDirectory, "tiles.idx");
            _index = new ConcurrentDictionary<(int z, int x, int y), TileCacheEntry>();
            
            LoadIndex();
            UpdateFilePosition();
        }

        /// <summary>
        /// Gets a tile from the cache
        /// </summary>
        public async Task<byte[]?> GetTileAsync(int z, int x, int y)
        {
            var key = (z, x, y);
            
            if (!_index.TryGetValue(key, out var entry))
                return null;

            // Check if tile is expired
            if (DateTime.UtcNow - entry.Timestamp > TimeSpan.FromDays(7))
            {
                _index.TryRemove(key, out _);
                return null;
            }

            try
            {
                await _readLock.WaitAsync();
                
                if (!File.Exists(_cacheFilePath))
                    return null;

                using var fs = new FileStream(_cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                
                byte[] buffer = new byte[entry.Length];
                int read = await fs.ReadAsync(buffer, 0, entry.Length);
                
                if (read != entry.Length)
                    return null;

                return buffer;
            }
            catch
            {
                return null;
            }
            finally
            {
                _readLock.Release();
            }
        }

        /// <summary>
        /// Stores a tile in the cache
        /// </summary>
        public async Task<bool> PutTileAsync(int z, int x, int y, byte[] data)
        {
            if (data == null || data.Length == 0)
                return false;

            var key = (z, x, y);

            try
            {
                await _writeLock.WaitAsync();

                // Open or create cache file in append mode
                using var fs = new FileStream(_cacheFilePath, FileMode.Append, FileAccess.Write, FileShare.None);
                
                long offset = fs.Position;
                await fs.WriteAsync(data, 0, data.Length);
                await fs.FlushAsync();

                var entry = new TileCacheEntry
                {
                    Offset = offset,
                    Length = data.Length,
                    Timestamp = DateTime.UtcNow
                };

                _index[key] = entry;
                _currentFilePosition = fs.Position;

                // Persist index periodically
                await SaveIndexAsync();

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Checks if a tile exists in cache (without loading it)
        /// </summary>
        public bool HasTile(int z, int x, int y)
        {
            var key = (z, x, y);
            if (!_index.TryGetValue(key, out var entry))
                return false;

            // Check expiry
            if (DateTime.UtcNow - entry.Timestamp > TimeSpan.FromDays(7))
            {
                _index.TryRemove(key, out _);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        public CacheStats GetStats()
        {
            return new CacheStats
            {
                TileCount = _index.Count,
                TotalSizeBytes = _currentFilePosition,
                CacheFilePath = _cacheFilePath
            };
        }

        /// <summary>
        /// Compacts the cache by removing expired entries
        /// </summary>
        public async Task<int> CompactCacheAsync()
        {
            int removed = 0;
            var expiredKeys = new List<(int z, int x, int y)>();

            // Find expired entries
            foreach (var kvp in _index)
            {
                if (DateTime.UtcNow - kvp.Value.Timestamp > TimeSpan.FromDays(7))
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            // Remove from index
            foreach (var key in expiredKeys)
            {
                if (_index.TryRemove(key, out _))
                    removed++;
            }

            if (removed > 0)
            {
                await SaveIndexAsync();
            }

            return removed;
        }

        /// <summary>
        /// Clears the entire cache
        /// </summary>
        public async Task ClearCacheAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                _index.Clear();
                _currentFilePosition = 0;

                if (File.Exists(_cacheFilePath))
                    File.Delete(_cacheFilePath);
                
                if (File.Exists(_indexFilePath))
                    File.Delete(_indexFilePath);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private void LoadIndex()
        {
            try
            {
                if (!File.Exists(_indexFilePath))
                    return;

                using var fs = new FileStream(_indexFilePath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                int version = br.ReadInt32();
                if (version != 1)
                    return; // Unsupported version

                int count = br.ReadInt32();
                
                for (int i = 0; i < count; i++)
                {
                    int z = br.ReadInt32();
                    int x = br.ReadInt32();
                    int y = br.ReadInt32();
                    long offset = br.ReadInt64();
                    int length = br.ReadInt32();
                    long timestampTicks = br.ReadInt64();

                    var entry = new TileCacheEntry
                    {
                        Offset = offset,
                        Length = length,
                        Timestamp = new DateTime(timestampTicks, DateTimeKind.Utc)
                    };

                    _index[(z, x, y)] = entry;
                }
            }
            catch
            {
                // If index is corrupted, start fresh
                _index.Clear();
            }
        }

        private async Task SaveIndexAsync()
        {
            try
            {
                using var fs = new FileStream(_indexFilePath, FileMode.Create, FileAccess.Write);
                using var bw = new BinaryWriter(fs);

                bw.Write(1); // Version
                bw.Write(_index.Count);

                foreach (var kvp in _index)
                {
                    bw.Write(kvp.Key.z);
                    bw.Write(kvp.Key.x);
                    bw.Write(kvp.Key.y);
                    bw.Write(kvp.Value.Offset);
                    bw.Write(kvp.Value.Length);
                    bw.Write(kvp.Value.Timestamp.Ticks);
                }

                await fs.FlushAsync();
            }
            catch
            {
                // Ignore index save errors
            }
        }

        private void UpdateFilePosition()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var fi = new FileInfo(_cacheFilePath);
                    _currentFilePosition = fi.Length;
                }
            }
            catch
            {
                _currentFilePosition = 0;
            }
        }

        public void Dispose()
        {
            _cacheFileStream?.Dispose();
            _writeLock?.Dispose();
            _readLock?.Dispose();
        }

        private struct TileCacheEntry
        {
            public long Offset;
            public int Length;
            public DateTime Timestamp;
        }
    }

    public class CacheStats
    {
        public int TileCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public string CacheFilePath { get; set; } = "";
        
        public string TotalSizeMB => $"{TotalSizeBytes / 1024.0 / 1024.0:F2} MB";
    }
}
