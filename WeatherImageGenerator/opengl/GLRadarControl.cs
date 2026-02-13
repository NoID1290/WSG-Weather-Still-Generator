using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Net.Http;
using System.Linq;
using OpenTK.WinForms;
using OpenTK.Graphics.OpenGL4;

namespace WeatherImageGenerator.OpenGL
{
    public class GLRadarControl : GLControl
    {
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _texture = 0;
        private Shader? _shader;

        // Overlay resources (crosshair, markers)
        private Shader? _overlayShader;
        private int _overlayVao = 0;
        private int _overlayVbo = 0;

        // Map tile support
        private TileProvider? _tileProvider;
        private string? _localTileFolder = null;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), int> _tileTextures = new System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), int>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), long> _tileLastUsed = new System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), long>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), DateTime> _blockedTiles = new System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), DateTime>();
        private HttpClient _tileHttpClient = new HttpClient();
        private int _mapZoom = 4; // tile zoom (default to show Canada)
        private double _centerLat = 56.1304; // Canada centroid latitude
        private double _centerLon = -106.3468; // Canada centroid longitude
        private int _fallbackTexture = 0;

        // Background composite metadata (used so composite pans/zooms with map)
        private double _bgCenterLat = 0.0;
        private double _bgCenterLon = 0.0;
        private int _bgSourceZoom = 0;
        private int _bgPixelWidth = 0;
        private int _bgPixelHeight = 0;

        private const int MAX_TILE_TEXTURES = 300;
        private const int PREFETCH_RADIUS = 1;

        // Radar frame buffer for ghosting/animation
        private readonly System.Collections.Generic.List<int> _radarFrames = new System.Collections.Generic.List<int>();
        private const int MAX_RADAR_FRAMES = 6;

        private float _zoom = 1.0f;
        private Vector2 _pan = Vector2.Zero;

        private readonly string _vertexSourceFallback = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform mat3 uTransform;
out vec2 vTex;
void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
}";

        private readonly string _fragmentSourceFallback = @"#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
void main() {
    FragColor = texture(uTexture, vTex);
}";

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Max(0.1f, Math.Min(10f, value));
                Invalidate();
            }
        }

        public GLRadarControl() : base()
        {
            // DoubleBuffer is enabled by GLControl base
            this.Load += GLRadarControl_Load;
            this.Paint += GLRadarControl_Paint;
            this.Resize += GLRadarControl_Resize;
            this.MouseWheel += GLRadarControl_MouseWheel;
            this.MouseDown += GLRadarControl_MouseDown;
            this.MouseMove += GLRadarControl_MouseMove;
            this.MouseUp += GLRadarControl_MouseUp;
        }

        private void GLRadarControl_Load(object? sender, EventArgs e)
        {
            MakeCurrent();
            GL.ClearColor(0.12f, 0.12f, 0.12f, 1.0f);

            // Build simple quad
            float[] vertices = new float[]
            {
                // positions    // tex
                -1f, -1f, 0f, 0f,
                 1f, -1f, 1f, 0f,
                 1f,  1f, 1f, 1f,
                -1f,  1f, 0f, 1f
            };
            uint[] indices = new uint[] { 0,1,2, 2,3,0 };

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            // Load shader from disk with fallback
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var vPath = Path.Combine(baseDir, "opengl", "shaders", "vertex.glsl");
            var fPath = Path.Combine(baseDir, "opengl", "shaders", "fragment.glsl");
            string vSrc, fSrc;
            try { vSrc = File.ReadAllText(vPath); } catch { vSrc = _vertexSourceFallback; }
            try { fSrc = File.ReadAllText(fPath); } catch { fSrc = _fragmentSourceFallback; }

            _shader = new Shader(vSrc, fSrc);
            _shader.Use();
            _shader.SetInt("uTexture", 0);
            _shader.SetFloat("uOpacity", 1.0f);

            // create fallback tile texture (neutral background) used when tiles are missing/blocked
            _fallbackTexture = CreateFallbackTexture(256, 256);

            // Tile shader (simple texture copy) - load from disk if present
            var tileVPath = Path.Combine(baseDir, "opengl", "shaders", "tile.vert.glsl");
            var tileFPath = Path.Combine(baseDir, "opengl", "shaders", "tile.frag.glsl");
            string tileV, tileF;
            try { tileV = File.ReadAllText(tileVPath); } catch { tileV = _vertexSourceFallback; }
            try { tileF = File.ReadAllText(tileFPath); } catch { tileF = "#version 330 core\nin vec2 vTex; out vec4 FragColor; uniform sampler2D uTexture; void main(){ FragColor = texture(uTexture, vTex); }"; }
            _tileShader = new Shader(tileV, tileF);

            // Enable alpha blending for radar overlays
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Prepare overlay (crosshair/markers)
            var overlayVPath = Path.Combine(baseDir, "opengl", "shaders", "overlay.vert.glsl");
            var overlayFPath = Path.Combine(baseDir, "opengl", "shaders", "overlay.frag.glsl");
            string ovSrcV, ovSrcF;
            try { ovSrcV = File.ReadAllText(overlayVPath); } catch { ovSrcV = "#version 330 core\nlayout(location=0) in vec2 aPos; void main(){ gl_Position = vec4(aPos,0,1); }"; }
            try { ovSrcF = File.ReadAllText(overlayFPath); } catch { ovSrcF = "#version 330 core\nout vec4 FragColor; uniform vec3 uColor; uniform float uAlpha; void main(){ FragColor = vec4(uColor,uAlpha); }"; }

            _overlayShader = new Shader(ovSrcV, ovSrcF);

            // Initialize tile provider
            _tileProvider = new TileProvider();

            // If caller set a local tile folder earlier, pass it through
            if (!string.IsNullOrEmpty(_localTileFolder)) _tileProvider.LocalTilesRoot = _localTileFolder;

            // Ensure tile shader has texture unit set
            _tileShader!.Use();
            _tileShader!.SetInt("uTexture", 0);

            // Setup overlay buffers (we'll fill data on resize)
            _overlayVao = GL.GenVertexArray();
            _overlayVbo = GL.GenBuffer();
            GL.BindVertexArray(_overlayVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _overlayVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 4 * 2 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        }

        private void GLRadarControl_Resize(object? sender, EventArgs e)
        {
            MakeCurrent();
            GL.Viewport(0, 0, Width, Height);
            UpdateOverlayVertices();
            Invalidate();
        }

        private void UpdateOverlayVertices()
        {
            // Build a small crosshair in NDC coordinates centered at (0,0)
            // length in pixels
            int lenPx = 16;
            float hx = (lenPx * 2f) / Math.Max(1, Width);
            float hy = (lenPx * 2f) / Math.Max(1, Height);

            // horizontal then vertical lines
            float[] verts = new float[]
            {
                -hx, 0f,
                 hx, 0f,
                 0f,-hy,
                 0f, hy
            };

            GL.BindBuffer(BufferTarget.ArrayBuffer, _overlayVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, verts.Length * sizeof(float), verts);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        private void GLRadarControl_Paint(object? sender, PaintEventArgs e)
        {
            if (DesignMode) return;
            MakeCurrent();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            if (_shader == null)
            {
                SwapBuffers();
                return;
            }

            _shader.Use();

            // Build transform matrix (mat3) to apply zoom and pan
            // We keep it simple: scale then translate in NDC space
            float sx = _zoom;
            float sy = _zoom;
            float tx = _pan.X;
            float ty = _pan.Y;

            // Column-major 3x3 matrix for 2D affine transform
            float[] transform = new float[]
            {
                sx, 0f, 0f,
                0f, sy, 0f,
                tx, ty, 1f
            };

            _shader.SetMatrix3("uTransform", transform);

            // Draw map tiles using tile shader.
            // If a pre-composited background (`_texture`) is present, draw it full-screen instead
            // of iterating map tiles — the background was produced by RadarImageService and
            // should not be recoloured by the radar palette shader.
            if (_texture != 0 && _hasBackgroundTexture && _bgSourceZoom != 0)
            {
                // Draw the composite background anchored to world coordinates so it will pan/zoom with tiles.
                _tileShader.Use();
                GL.ActiveTexture(TextureUnit.Texture0);

                // compute center pixel for composite at current map zoom (convert lon/lat -> pixel at _mapZoom)
                int z = _mapZoom;
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);

                // image center at requested background center, computed at current zoom (so it moves with map)
                double imgCenterPx = LonToPixelX(_bgCenterLon, z);
                double imgCenterPy = LatToPixelY(_bgCenterLat, z);

                // account for difference between source zoom and current zoom when scaling image size
                double scaleFactor = Math.Pow(2.0, z - _bgSourceZoom);
                double imgWidthMapPx = _bgPixelWidth * scaleFactor;
                double imgHeightMapPx = _bgPixelHeight * scaleFactor;

                // screen position of image center in pixels
                double screenCenterX = (imgCenterPx - cx) + Width / 2.0;
                double screenCenterY = (imgCenterPy - cy) + Height / 2.0;

                // image size in NDC
                float imgWnd = (float)(imgWidthMapPx / (Width / 2.0));
                float imgHnd = (float)(imgHeightMapPx / (Height / 2.0));

                float tileSx = imgWnd / 2f;
                float tileSy = imgHnd / 2f;
                float centerNdcX = (float)(screenCenterX / (Width / 2.0) - 1.0);
                float centerNdcY = (float)(1.0 - screenCenterY / (Height / 2.0));

                float[] tmat = new float[] { tileSx, 0f, 0f, 0f, tileSy, 0f, centerNdcX, centerNdcY, 1f };
                _tileShader.SetMatrix3("uTransform", tmat);

                GL.BindTexture(TextureTarget.Texture2D, _texture);
                GL.BindVertexArray(_vao);
                GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
            else
            {
                _tileShader.Use();
                GL.ActiveTexture(TextureUnit.Texture0);

                // Compute world/pixel metrics
                int z = _mapZoom;
                double n = Math.Pow(2.0, z);
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);

                int tilesWide = (int)Math.Ceiling((double)Width / 256.0) + 2;
                int tilesHigh = (int)Math.Ceiling((double)Height / 256.0) + 2;

                // tile coordinates for center
                int centerTileX = (int)Math.Floor(cx / 256.0);
                int centerTileY = (int)Math.Floor(cy / 256.0);

                for (int dx = -tilesWide/2; dx <= tilesWide/2; dx++)
            {
                for (int dy = -tilesHigh/2; dy <= tilesHigh/2; dy++)
                {
                    int tileX = centerTileX + dx;
                    int tileY = centerTileY + dy;
                    int wrap = (int)Math.Pow(2, z);
                    int wrappedX = ((tileX % wrap) + wrap) % wrap;
                    if (tileY < 0 || tileY >= (1 << z)) continue; // out of lat range

                    var key = (z, wrappedX, tileY);
                    int texToBind = _fallbackTexture;
                    if (_tileTextures.TryGetValue(key, out int texId))
                    {
                        texToBind = texId;
                        _tileLastUsed[key] = DateTime.UtcNow.Ticks;
                    }
                    else if (_blockedTiles.ContainsKey(key))
                    {
                        // blocked tile known -> keep fallback and don't re-request frequently
                    }
                    else
                    {
                        // schedule download if not already queued
                        _ = EnsureTileLoadedAsync(z, wrappedX, tileY);
                    }

                    // compute tile's top-left pixel in global pixel space
                    double tilePx = tileX * 256.0;
                    double tilePy = tileY * 256.0;

                    // screen position of tile center
                    double screenCenterX = (tilePx - cx) + Width / 2.0 + 128.0;
                    double screenCenterY = (tilePy - cy) + Height / 2.0 + 128.0;

                    // tile size in NDC
                    float tileW = (float)((256.0) / (Width / 2.0));
                    float tileH = (float)((256.0) / (Height / 2.0));

                    // center NDC coords
                    float centerNdcX = (float)(screenCenterX / (Width / 2.0) - 1.0);
                    float centerNdcY = (float)(1.0 - screenCenterY / (Height / 2.0));

                    // tile transform: scale then translate
                    float tileSx = tileW / 2f;
                    float tileSy = tileH / 2f;
                    float txf = centerNdcX;
                    float tyf = centerNdcY;

                    float[] tmat = new float[] { tileSx, 0f, 0f, 0f, tileSy, 0f, txf, tyf, 1f };

                    _tileShader.SetMatrix3("uTransform", tmat);

                    GL.BindTexture(TextureTarget.Texture2D, texToBind);
                    GL.BindVertexArray(_vao);
                    GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                    GL.BindVertexArray(0);
                }
            }
            }

            // Draw radar frames (oldest first) with fading alpha
            if (_radarFrames.Count > 0)
            {
                GL.Enable(EnableCap.Blend);
                // Use tile shader to preserve overlay colors (not radar palette shader)
                _tileShader.Use();
                for (int i = 0; i < _radarFrames.Count; i++)
                {
                    int tex = _radarFrames[i];
                    // fading alpha for animation effect
                    float alpha = (float)(i + 1) / (_radarFrames.Count + 1);
                    
                    // Identity transform (fullscreen)
                    float[] tmat = new float[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
                    _tileShader.SetMatrix3("uTransform", tmat);
                    
                    GL.BindTexture(TextureTarget.Texture2D, tex);
                    GL.BindVertexArray(_vao);
                    GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                    GL.BindVertexArray(0);
                }
                GL.Disable(EnableCap.Blend);
            }

            // Draw overlay crosshair/marker in NDC with overlay shader
            if (_overlayShader != null)
            {
                GL.Disable(EnableCap.DepthTest);
                _overlayShader.Use();
                _overlayShader.SetFloat("uAlpha", 1.0f);
                // draw crosshair in green
                var colorLoc = GL.GetUniformLocation(_overlayShader.Handle, "uColor");
                GL.Uniform3(colorLoc, 0.6f, 1.0f, 0.2f);

                GL.BindVertexArray(_overlayVao);
                GL.LineWidth(2f);
                GL.DrawArrays(PrimitiveType.Lines, 0, 4);
                GL.BindVertexArray(0);

                GL.PointSize(6f);
                GL.BindVertexArray(_overlayVao);
                GL.DrawArrays(PrimitiveType.Points, 0, 1);
                GL.BindVertexArray(0);
                GL.Enable(EnableCap.DepthTest);
            }

            SwapBuffers();
        }

        private bool _dragging = false;
        private System.Drawing.Point _lastMousePos;

        private Shader? _tileShader;

        private void GLRadarControl_MouseWheel(object? sender, MouseEventArgs e)
        {
            // If Shift is down, adjust map zoom (tile zoom), else adjust local GL zoom
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (shift)
            {
                // map zoom
                if (e.Delta > 0) SetMapZoom(Math.Min(20, _mapZoom + 1));
                else SetMapZoom(Math.Max(0, _mapZoom - 1));
            }
            else
            {
                var oldZoom = _zoom;
                var delta = e.Delta > 0 ? 1.1f : 1f / 1.1f;
                _zoom *= delta;
                _zoom = Math.Max(0.1f, Math.Min(10f, _zoom));

                // Optionally adjust pan to zoom towards cursor (nice UX)
                if (Width > 0 && Height > 0)
                {
                    var nx = (2f * e.X / Width) - 1f;
                    var ny = 1f - (2f * e.Y / Height);
                    // adjust pan so the point under cursor stays roughly under cursor
                    _pan.X = (nx - (nx - _pan.X) * (oldZoom / _zoom));
                    _pan.Y = (ny - (ny - _pan.Y) * (oldZoom / _zoom));
                }
            }

            UpdateTiles();
            Invalidate();
        }

        private void GLRadarControl_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _lastMousePos = e.Location;
                this.Cursor = Cursors.SizeAll;
            }
        }

        private void GLRadarControl_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var dx = e.X - _lastMousePos.X;
            var dy = e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;

            // Instead of NDC pan, update map center based on pixel delta
            if (Width > 0 && Height > 0)
            {
                // pixel delta in screen
                double ndx = dx;
                double ndy = dy;

                // convert to world pixel space at current zoom
                double worldPerPx = Math.Pow(2.0, _mapZoom) * 256.0 / (Math.Pow(2.0, _mapZoom) * 256.0); // 1.0 (kept for clarity)

                // compute how many global pixels we moved
                // Positive dx means move center to left (lon decreases)
                double cx = LonToPixelX(_centerLon, _mapZoom);
                double cy = LatToPixelY(_centerLat, _mapZoom);

                cx -= ndx;
                cy -= ndy;

                _centerLon = PixelXToLon(cx, _mapZoom);
                _centerLat = PixelYToLat(cy, _mapZoom);

                // Only invalidate during drag - don't fetch tiles until drag ends
                Invalidate();
            }
        }

        private void GLRadarControl_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                this.Cursor = Cursors.Hand;

                // After dragging ends, refresh tiles for the new position
                UpdateTiles();
            }
        }

        // Backwards-compatible single-arg API - delegates to the metadata-aware variant with null metadata.
        public void SetImageBytes(byte[] data)
        {
            SetImageBytes(data, null, null, null);
        }

        // Metadata-aware overload — sourceCenter/zoom tell the control how to anchor the composite image
        public void SetImageBytes(byte[] data, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom)
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetImageBytes(data, sourceCenterLat, sourceCenterLon, sourceZoom)));
                return;
            }

            using var ms = new MemoryStream(data);
            using var bmp = new Bitmap(ms);
            ProcessIncomingBitmap(bmp, sourceCenterLat, sourceCenterLon, sourceZoom);
        }

        /// <summary>
        /// Clears any overlay/background texture
        /// </summary>
        public void ClearOverlay()
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => ClearOverlay()));
                return;
            }

            MakeCurrent();
            
            // Clear background texture
            if (_texture != 0)
            {
                try { GL.DeleteTexture(_texture); } catch { }
                _texture = 0;
                _hasBackgroundTexture = false;
                BackgroundTextureChanged?.Invoke(false);
            }
            
            // Clear radar frames
            foreach (var t in _radarFrames)
            {
                try { GL.DeleteTexture(t); } catch { }
            }
            _radarFrames.Clear();
            
            Invalidate();
        }


        // Extracted helper so both overloads use the same logic and we can pass metadata
        private void ProcessIncomingBitmap(Bitmap bmp, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom)
        {
            // If incoming image is fully opaque (composited base map + radar),
            // treat it as a pre-composited background and upload to the background
            // texture so it won't be recoloured by the radar palette shader.
            bool hasAlphaChannel = Image.IsAlphaPixelFormat(bmp.PixelFormat);
            bool anyTransparent = false;
            if (hasAlphaChannel)
            {
                // quick scan for any transparent pixel (sample every 8th pixel to be fast)
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var dataBmp = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = dataBmp.Stride;
                    int bytes = Math.Abs(stride) * bmp.Height;
                    var buf = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(dataBmp.Scan0, buf, 0, bytes);
                    // consider only *fully* transparent pixels (or nearly fully transparent) as "transparent".
                    // Ignore semi-transparent pixels (alpha > 0) since composites often contain semi-opaque overlay pixels.
                    const int TRANSPARENT_THRESHOLD = 8; // alpha < 8 treated as transparent
                    for (int y = 0; y < bmp.Height && !anyTransparent; y += 8)
                    {
                        for (int x = 0; x < bmp.Width; x += 8)
                        {
                            int idx = y * stride + x * 4;
                            if (idx + 3 >= buf.Length) break;
                            byte a = buf[idx + 3];
                            if (a < TRANSPARENT_THRESHOLD) { anyTransparent = true; break; }
                        }
                    }
                }
                finally { bmp.UnlockBits(dataBmp); }
            }

            if (!hasAlphaChannel || !anyTransparent)
            {
                Console.WriteLine("[GLRadarControl] Incoming image classified: BACKGROUND (pre-composited)");
                // opaque/composited image -> upload as background (replace any radar frames)
                _radarFrames.ForEach(t => { try { GL.DeleteTexture(t); } catch { } });
                _radarFrames.Clear();

                // delete previous background texture if any (we will replace it)
                if (_texture != 0) { try { GL.DeleteTexture(_texture); } catch { } _texture = 0; }

                // store metadata if provided so the background image will pan/zoom with the map
                if (sourceCenterLat.HasValue && sourceCenterLon.HasValue && sourceZoom.HasValue)
                {
                    _bgCenterLat = sourceCenterLat.Value;
                    _bgCenterLon = sourceCenterLon.Value;
                    _bgSourceZoom = sourceZoom.Value;
                    _bgPixelWidth = bmp.Width;
                    _bgPixelHeight = bmp.Height;
                }

                UploadBitmapToTexture(bmp);
                _hasBackgroundTexture = true;
                BackgroundTextureChanged?.Invoke(true);
            }
            else
            {
                Console.WriteLine("[GLRadarControl] Incoming image classified: OVERLAY (transparent) -> radar frame");
                // if we previously had a background texture, clear it so overlays composite over tiles
                if (_hasBackgroundTexture)
                {
                    if (_texture != 0) { try { GL.DeleteTexture(_texture); } catch { } _texture = 0; }
                    _hasBackgroundTexture = false;
                    BackgroundTextureChanged?.Invoke(false);
                }

                // transparent image -> normal radar overlay processing
                AddRadarFrameFromBitmap(bmp);
            }

            Invalidate();
        }
        private void AddRadarFrameFromBitmap(Bitmap bmp)
        {
            MakeCurrent();
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);

            _radarFrames.Add(tex);
            if (_radarFrames.Count > MAX_RADAR_FRAMES)
            {
                var del = _radarFrames[0];
                _radarFrames.RemoveAt(0);
                try { GL.DeleteTexture(del); } catch { }
            }
        }

        private void UploadBitmapToTexture(Bitmap bmp)
        {
            MakeCurrent();
            if (_texture == 0)
                _texture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, _texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MakeCurrent();
                if (_texture != 0)
                {
                    GL.DeleteTexture(_texture);
                    _texture = 0;
                    if (_hasBackgroundTexture) { _hasBackgroundTexture = false; BackgroundTextureChanged?.Invoke(false); }
                }
                if (_fallbackTexture != 0) GL.DeleteTexture(_fallbackTexture);
                if (_shader != null) _shader.Dispose();
                if (_tileShader != null) _tileShader.Dispose();
                if (_overlayShader != null) _overlayShader.Dispose();

                // delete tile textures
                foreach (var kv in _tileTextures)
                {
                    try { GL.DeleteTexture(kv.Value); } catch { }
                }

                // delete radar frames
                foreach (var t in _radarFrames)
                {
                    try { GL.DeleteTexture(t); } catch { }
                }

                if (_vbo != 0) GL.DeleteBuffer(_vbo);
                if (_ebo != 0) GL.DeleteBuffer(_ebo);
                if (_vao != 0) GL.DeleteVertexArray(_vao);
                if (_overlayVbo != 0) GL.DeleteBuffer(_overlayVbo);
                if (_overlayVao != 0) GL.DeleteVertexArray(_overlayVao);

                _tileProvider?.Dispose();
            }
            base.Dispose(disposing);
        }

        // Minimal struct helper for pan vector
        private struct Vector2
        {
            public float X;
            public float Y;
            public static Vector2 Zero => new Vector2 { X = 0f, Y = 0f };
            public Vector2(float x, float y) { X = x; Y = y; }
        }

        // Notify host UI about tile status changes
        public event Action<string, System.Drawing.Color>? TileStatusChanged;
        // Fired when a pre-composited full-screen background texture is set or cleared
        public event Action<bool>? BackgroundTextureChanged;
        private bool _hasBackgroundTexture = false;

        private void NotifyTileStatus(string text, System.Drawing.Color color)
        {
            try
            {
                if (this.IsHandleCreated && this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => TileStatusChanged?.Invoke(text, color)));
                }
                else
                {
                    TileStatusChanged?.Invoke(text, color);
                }
            }
            catch { }
        }

        private void EvictTilesIfNeeded()
        {
            try
            {
                while (_tileTextures.Count > MAX_TILE_TEXTURES)
                {
                    // find oldest used
                    var oldest = _tileLastUsed.OrderBy(kv => kv.Value).FirstOrDefault();
                    if (oldest.Key == default) break;
                    if (_tileTextures.TryRemove(oldest.Key, out int tex))
                    {
                        try { GL.DeleteTexture(tex); } catch { }
                    }
                    _tileLastUsed.TryRemove(oldest.Key, out _);
                }
            }
            catch { }
        }

        private int CreateFallbackTexture(int w, int h)
        {
            MakeCurrent();
            using var bmp = new System.Drawing.Bitmap(w, h);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.Clear(System.Drawing.Color.FromArgb(245, 245, 235));
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 220, 200));
            for (int x = 0; x < w; x += 32) g.DrawLine(pen, x, 0, x, h);
            for (int y = 0; y < h; y += 32) g.DrawLine(pen, 0, y, w, y);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 200, 200));
            g.DrawString("No Tile", new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold), brush, 8, 8);

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }
        // Map projection helpers
        private static double LonToPixelX(double lon, int z)
        {
            double n = Math.Pow(2.0, z);
            return ((lon + 180.0) / 360.0) * 256.0 * n;
        }

        private static double LatToPixelY(double lat, int z)
        {
            var latRad = lat * Math.PI / 180.0;
            var n = Math.Pow(2.0, z);
            return ( (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 ) * 256.0 * n;
        }

        private static double PixelXToLon(double px, int z)
        {
            double n = Math.Pow(2.0, z) * 256.0;
            return (px / n) * 360.0 - 180.0;
        }

        private static double PixelYToLat(double py, int z)
        {
            double n = Math.Pow(2.0, z) * 256.0;
            double y = py / n;
            double latRad = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y)));
            return latRad * 180.0 / Math.PI;
        }

        public void SetCenterLatLon(double lat, double lon)
        {
            _centerLat = lat;
            _centerLon = lon;
            UpdateTiles();
            Invalidate();
        }

        // Raised whenever the map/tile zoom changes (UI and mouse-wheel shifts)
        public event Action<int>? MapZoomChanged;

        public void SetMapZoom(int z)
        {
            if (z == _mapZoom) return;
            _mapZoom = z;
            UpdateTiles();
            Invalidate();
            try { MapZoomChanged?.Invoke(_mapZoom); } catch { }
        }

        /// <summary>
        /// Use a local folder containing tiles in z/x/y.png layout. If set, TileProvider will prefer local tiles.
        /// </summary>
        public void SetLocalTilesFolder(string? folder)
        {
            _localTileFolder = folder;
            if (_tileProvider == null) _tileProvider = new TileProvider();
            _tileProvider.LocalTilesRoot = folder;
            UpdateTiles();
            Invalidate();
        }

        private async System.Threading.Tasks.Task EnsureTileLoadedAsync(int z, int x, int y)
        {
            try
            {
                if (_tileProvider == null) _tileProvider = new TileProvider();
                var key = (z, x, y);
                if (_tileTextures.ContainsKey(key) || _blockedTiles.ContainsKey(key)) return;

                var (bytes, status) = await _tileProvider.GetTileBytesAsync(z, x, y);
                if (status == TileFetchStatus.Blocked)
                {
                    _blockedTiles[key] = DateTime.UtcNow;
                    NotifyTileStatus("Tiles: Blocked", System.Drawing.Color.OrangeRed);
                    return;
                }
                if (status == TileFetchStatus.NotFound || status == TileFetchStatus.Error || bytes == null)
                {
                    // mark as temporarily blocked to avoid spamming
                    _blockedTiles[key] = DateTime.UtcNow;
                    NotifyTileStatus("Tiles: Missing", System.Drawing.Color.Gray);
                    return;
                }

                // upload texture on UI/GL thread
                this.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        MakeCurrent();
                        int tex = GL.GenTexture();
                        GL.BindTexture(TextureTarget.Texture2D, tex);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                        using var ms = new System.IO.MemoryStream(bytes);
                        using var bmp = new System.Drawing.Bitmap(ms);
                        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                        }
                        finally
                        {
                            bmp.UnlockBits(data);
                        }

                        GL.BindTexture(TextureTarget.Texture2D, 0);
                        _tileTextures.TryAdd(key, tex);
                        _tileLastUsed[key] = DateTime.UtcNow.Ticks;
                        NotifyTileStatus("Tiles: Remote", System.Drawing.Color.LightGreen);
                        EvictTilesIfNeeded();
                        Invalidate();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private void UpdateTiles()
        {
            try
            {
                if (_tileProvider == null) _tileProvider = new TileProvider();
                int z = _mapZoom;
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);

                int centerTileX = (int)Math.Floor(cx / 256.0);
                int centerTileY = (int)Math.Floor(cy / 256.0);

                int tilesWide = (int)Math.Ceiling((double)Width / 256.0) + 2;
                int tilesHigh = (int)Math.Ceiling((double)Height / 256.0) + 2;

                for (int dx = -tilesWide/2; dx <= tilesWide/2; dx++)
                {
                    for (int dy = -tilesHigh/2; dy <= tilesHigh/2; dy++)
                    {
                        int tileX = centerTileX + dx;
                        int tileY = centerTileY + dy;
                        int wrap = (int)Math.Pow(2, z);
                        int wrappedX = ((tileX % wrap) + wrap) % wrap;
                        if (tileY < 0 || tileY >= (1 << z)) continue;

                        var key = (z, wrappedX, tileY);
                        if (!_tileTextures.ContainsKey(key))
                        {
                            _ = EnsureTileLoadedAsync(z, wrappedX, tileY);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
