using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WeatherImageGenerator.Services
{
    public static class IconGenerator
    {
        public static void GenerateAll(string outputDir)
        {
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            GenerateSunny(Path.Combine(outputDir, "sunny.png"));
            GeneratePartlyCloudy(Path.Combine(outputDir, "partly_cloudy.png"));
            GenerateCloudy(Path.Combine(outputDir, "cloudy.png"));
            GenerateFog(Path.Combine(outputDir, "fog.png"));
            GenerateRain(Path.Combine(outputDir, "rain.png"));
            GenerateFreezingRain(Path.Combine(outputDir, "freezing_rain.png"));
            GenerateSnow(Path.Combine(outputDir, "snow.png"));
            GenerateStorm(Path.Combine(outputDir, "storm.png"));

            Console.WriteLine($"Generated 8 icons in {outputDir}");
        }

        private static void Save(Bitmap bmp, string path)
        {
            bmp.Save(path, ImageFormat.Png);
        }

        private static Bitmap CreateBase(int width = 256, int height = 256)
        {
            return new Bitmap(width, height);
        }

        private static Graphics GetGraphics(Bitmap bmp)
        {
            var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        private static void DrawSun(Graphics g, float x, float y, float size)
        {
            // Glow
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x - size * 0.2f, y - size * 0.2f, size * 1.4f, size * 1.4f);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(100, 255, 220, 100);
                    brush.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(brush, path);
                }
            }

            // Core
            using (var brush = new LinearGradientBrush(
                new RectangleF(x, y, size, size),
                Color.FromArgb(255, 255, 240, 160),
                Color.FromArgb(255, 255, 180, 0),
                LinearGradientMode.ForwardDiagonal))
            {
                g.FillEllipse(brush, x, y, size, size);
            }
        }

        private static void DrawCloud(Graphics g, float x, float y, float width, float height, Color baseColor)
        {
            // Simple puff cloud
            using (var brush = new LinearGradientBrush(
                new RectangleF(x, y - height * 0.5f, width, height * 1.5f),
                Color.White,
                baseColor,
                LinearGradientMode.Vertical))
            {
                // Three puffs
                float puffSize = width * 0.6f;
                g.FillEllipse(brush, x, y, puffSize, puffSize); // Left
                g.FillEllipse(brush, x + width - puffSize, y, puffSize, puffSize); // Right
                g.FillEllipse(brush, x + width * 0.2f, y - puffSize * 0.5f, puffSize * 1.1f, puffSize * 1.1f); // Top Center
            }
        }

        public static void GenerateSunny(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);
            DrawSun(g, 48, 48, 160);
            Save(bmp, path);
        }

        public static void GeneratePartlyCloudy(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);
            
            DrawSun(g, 100, 30, 120);
            DrawCloud(g, 30, 100, 180, 100, Color.FromArgb(255, 220, 220, 230));
            
            Save(bmp, path);
        }

        public static void GenerateCloudy(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);

            // Back cloud
            DrawCloud(g, 80, 60, 160, 90, Color.FromArgb(255, 180, 180, 190));
            // Front cloud
            DrawCloud(g, 20, 100, 180, 100, Color.FromArgb(255, 220, 220, 230));

            Save(bmp, path);
        }

        public static void GenerateFog(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);

            // Hazy lines
            using (var brush = new LinearGradientBrush(new Rectangle(0,0,256,256), Color.FromArgb(150, 200, 200, 210), Color.Transparent, LinearGradientMode.Horizontal))
            {
                for(int i=0; i<5; i++)
                {
                    g.FillRectangle(brush, 20, 60 + i * 35, 216, 20);
                }
            }
            DrawCloud(g, 40, 80, 180, 100, Color.FromArgb(200, 220, 220, 230));

            Save(bmp, path);
        }

        public static void GenerateRain(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);

            DrawCloud(g, 30, 60, 200, 110, Color.FromArgb(255, 180, 180, 190));

            // Drops
            using (var pen = new Pen(Color.FromArgb(200, 100, 180, 255), 4))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                for(int i=0; i<5; i++)
                {
                    float dx = 60 + i * 35;
                    float dy = 180 + (i % 2) * 20;
                    g.DrawLine(pen, dx, dy, dx - 10, dy + 30);
                }
            }

            Save(bmp, path);
        }

        public static void GenerateFreezingRain(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);

            DrawCloud(g, 30, 60, 200, 110, Color.FromArgb(255, 180, 180, 190));

            // Drops + Ice
            using (var pen = new Pen(Color.FromArgb(200, 100, 220, 255), 4))
            {
                for(int i=0; i<5; i++)
                {
                    float dx = 60 + i * 35;
                    float dy = 180 + (i % 2) * 20;
                    g.DrawLine(pen, dx, dy, dx - 5, dy + 25);
                    // Ice crystal
                    g.FillEllipse(Brushes.White, dx - 8, dy + 25, 6, 6);
                }
            }

            Save(bmp, path);
        }

        public static void GenerateSnow(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);

            DrawCloud(g, 30, 60, 200, 110, Color.FromArgb(255, 200, 200, 210));

            // Snowflakes
            using (var pen = new Pen(Color.White, 3))
            {
                for(int i=0; i<5; i++)
                {
                    float dx = 50 + i * 40;
                    float dy = 180 + (i % 2) * 25;
                    
                    g.DrawLine(pen, dx - 8, dy, dx + 8, dy);
                    g.DrawLine(pen, dx, dy - 8, dx, dy + 8);
                    g.DrawLine(pen, dx - 6, dy - 6, dx + 6, dy + 6);
                    g.DrawLine(pen, dx - 6, dy + 6, dx + 6, dy - 6);
                }
            }

            Save(bmp, path);
        }

        public static void GenerateStorm(string path)
        {
            using var bmp = CreateBase();
            using var g = GetGraphics(bmp);

            // Dark cloud
            DrawCloud(g, 30, 60, 200, 110, Color.FromArgb(255, 100, 100, 110));

            // Lightning
            using (var pathBolt = new GraphicsPath())
            {
                float bx = 128;
                float by = 160;
                pathBolt.AddLines(new[] {
                    new PointF(bx, by),
                    new PointF(bx - 20, by + 40),
                    new PointF(bx + 10, by + 40),
                    new PointF(bx - 10, by + 90),
                    new PointF(bx + 30, by + 30),
                    new PointF(bx, by + 30),
                    new PointF(bx + 20, by)
                });
                
                using (var brush = new LinearGradientBrush(new RectangleF(bx-20, by, 50, 90), Color.Yellow, Color.Orange, LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, pathBolt);
                }
                using (var pen = new Pen(Color.White, 2))
                {
                    g.DrawPath(pen, pathBolt);
                }
            }

            Save(bmp, path);
        }
    }
}
