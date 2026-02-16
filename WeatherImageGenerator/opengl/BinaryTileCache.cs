using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WeatherImageGenerator.OpenGL
{
    /// <summary>
    /// High-performance binary tile cache with index for fast lookups.
    /// Uses a single .bin file to store all tiles and an index for quick access.
    /// Includes an in-memory LRU cache layer to avoid redundant disk reads.
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

        // ── In-memory LRU cache ──────────────────────────────────────────
        // Holds decoded tile bytes in RAM so repeated reads never touch disk.
        // Tiles are compressed PNG (avg ~30-60 KB), so 4096 entries ≈ ~160 MB actual RAM.
        // Generous limit avoids disk reads when panning over recently-viewed areas.
        private const int MAX_RAM_CACHE_ENTRIES = 4096;
        private readonly ConcurrentDictionary<(int z, int x, int y), byte[]> _ramCache
            = new ConcurrentDictionary<(int z, int x, int y), byte[]>();
        private readonly ConcurrentDictionary<(int z, int x, int y), long> _ramCacheLastUsed
            = new ConcurrentDictionary<(int z, int x, int y), long>();
        private long _ramCacheTotalBytes = 0;
        private const long MAX_RAM_CACHE_BYTES = 1024L * 1024 * 1024; // 1 GB hard limit

        // ── Batched index persistence ────────────────────────────────────
        private int _indexDirtyCount = 0;
        private const int INDEX_SAVE_BATCH_SIZE = 50; // save index every N writes
        
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
        /// Gets a tile from the cache (RAM first, then disk)
        /// </summary>
        public async Task<byte[]?> GetTileAsync(int z, int x, int y)
        {
            var key = (z, x, y);
            
            // 1. Check RAM cache first (zero-copy, no locks)
            if (_ramCache.TryGetValue(key, out var cached))
            {
                _ramCacheLastUsed[key] = DateTime.UtcNow.Ticks;
                return cached;
            }

            if (!_index.TryGetValue(key, out var entry))
                return null;

            // Check if tile is expired
            if (DateTime.UtcNow - entry.Timestamp > TimeSpan.FromDays(7))
            {
                _index.TryRemove(key, out _);
                return null;
            }

            // 2. Read from disk
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

                // 3. Promote to RAM cache
                AddToRamCache(key, buffer);

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

                // Promote to RAM cache immediately
                AddToRamCache(key, data);

                // Persist index in batches (not every write)
                _indexDirtyCount++;
                if (_indexDirtyCount >= INDEX_SAVE_BATCH_SIZE)
                {
                    _indexDirtyCount = 0;
                    await SaveIndexAsync();
                }

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
                CacheFilePath = _cacheFilePath,
                RamCacheEntries = _ramCache.Count,
                RamCacheBytes = _ramCacheTotalBytes
            };
        }

        // ── RAM cache helpers ────────────────────────────────────────────

        private void AddToRamCache((int z, int x, int y) key, byte[] data)
        {
            if (data.Length > 2 * 1024 * 1024) return; // skip tiles >2 MB

            _ramCache[key] = data;
            _ramCacheLastUsed[key] = DateTime.UtcNow.Ticks;
            Interlocked.Add(ref _ramCacheTotalBytes, data.Length);

            EvictRamCacheIfNeeded();
        }

        private void EvictRamCacheIfNeeded()
        {
            if (_ramCache.Count <= MAX_RAM_CACHE_ENTRIES && 
                Interlocked.Read(ref _ramCacheTotalBytes) <= MAX_RAM_CACHE_BYTES)
                return;

            // Evict ~25% oldest entries in one pass
            int toEvict = Math.Max(1, _ramCache.Count / 4);
            var oldest = _ramCacheLastUsed
                .OrderBy(kv => kv.Value)
                .Take(toEvict)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var k in oldest)
            {
                if (_ramCache.TryRemove(k, out var removed))
                    Interlocked.Add(ref _ramCacheTotalBytes, -removed.Length);
                _ramCacheLastUsed.TryRemove(k, out _);
            }
        }

        /// <summary>
        /// Compacts the cache by removing expired entries and rewriting the .bin file
        /// to reclaim disk space from deleted/expired tiles.
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
                {
                    removed++;
                    _ramCache.TryRemove(key, out var removedBytes);
                    if (removedBytes != null)
                        Interlocked.Add(ref _ramCacheTotalBytes, -removedBytes.Length);
                    _ramCacheLastUsed.TryRemove(key, out _);
                }
            }

            if (removed > 0)
            {
                await SaveIndexAsync();
            }

            // Rewrite bin file if it's significantly larger than needed (>50% waste)
            await RewriteBinFileIfNeeded();

            return removed;
        }

        /// <summary>
        /// Rewrites the .bin file to reclaim disk space, keeping only tiles still in the index.
        /// Triggered when the file has >50% wasted space or exceeds 500 MB.
        /// </summary>
        private async Task RewriteBinFileIfNeeded()
        {
            try
            {
                if (!File.Exists(_cacheFilePath)) return;

                var fileInfo = new FileInfo(_cacheFilePath);
                long totalIndexedBytes = _index.Values.Sum(e => (long)e.Length);
                long fileSize = fileInfo.Length;

                // Only rewrite if >50% wasted space or file > 500 MB
                double wasteRatio = totalIndexedBytes > 0 ? 1.0 - ((double)totalIndexedBytes / fileSize) : 1.0;
                if (fileSize < 50 * 1024 * 1024 || (wasteRatio < 0.5 && fileSize < 500 * 1024 * 1024))
                    return;

                Console.WriteLine($"[BinaryTileCache] Compacting bin file: {fileSize / 1024 / 1024} MB -> ~{totalIndexedBytes / 1024 / 1024} MB ({wasteRatio * 100:F0}% waste)");

                await _writeLock.WaitAsync();
                try
                {
                    var tempPath = _cacheFilePath + ".tmp";
                    var entries = _index.ToArray();

                    using (var readFs = new FileStream(_cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var writeFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        foreach (var kvp in entries)
                        {
                            var entry = kvp.Value;
                            readFs.Seek(entry.Offset, SeekOrigin.Begin);
                            byte[] buffer = new byte[entry.Length];
                            int read = await readFs.ReadAsync(buffer, 0, entry.Length);
                            if (read != entry.Length) continue;

                            long newOffset = writeFs.Position;
                            await writeFs.WriteAsync(buffer, 0, buffer.Length);

                            // Update index with new offset
                            _index[kvp.Key] = new TileCacheEntry
                            {
                                Offset = newOffset,
                                Length = entry.Length,
                                Timestamp = entry.Timestamp
                            };
                        }

                        _currentFilePosition = writeFs.Position;
                    }

                    // Swap files
                    File.Delete(_cacheFilePath);
                    File.Move(tempPath, _cacheFilePath);
                    await SaveIndexAsync();

                    Console.WriteLine($"[BinaryTileCache] Compaction complete: {_currentFilePosition / 1024 / 1024} MB");
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BinaryTileCache] Compaction failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the entire cache (disk + RAM)
        /// </summary>
        public async Task ClearCacheAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                _index.Clear();
                _currentFilePosition = 0;

                // Clear RAM cache
                _ramCache.Clear();
                _ramCacheLastUsed.Clear();
                Interlocked.Exchange(ref _ramCacheTotalBytes, 0);

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
            // Flush any pending index changes
            if (_indexDirtyCount > 0)
            {
                try { SaveIndexAsync().GetAwaiter().GetResult(); } catch { }
            }
            _ramCache.Clear();
            _ramCacheLastUsed.Clear();
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
        public int RamCacheEntries { get; set; }
        public long RamCacheBytes { get; set; }
        
        public string TotalSizeMB => $"{TotalSizeBytes / 1024.0 / 1024.0:F2} MB";
        public string RamCacheMB => $"{RamCacheBytes / 1024.0 / 1024.0:F2} MB";
    }
}
