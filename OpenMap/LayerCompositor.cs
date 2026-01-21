using System.Drawing;

namespace OpenMap;

/// <summary>
/// Helper class for compositing multiple layers (map + weather overlays)
/// </summary>
public class LayerCompositor
{
    /// <summary>
    /// Composite multiple layers into a single image
    /// </summary>
    /// <param name="baseLayers">List of layers to composite, in order from bottom to top</param>
    /// <param name="outputWidth">Output width</param>
    /// <param name="outputHeight">Output height</param>
    /// <returns>Composited bitmap</returns>
    public static Bitmap CompositeLayers(List<LayerInfo> baseLayers, int outputWidth, int outputHeight)
    {
        var result = new Bitmap(outputWidth, outputHeight);
        
        using (var g = Graphics.FromImage(result))
        {
            // Enable high quality rendering
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

            // Clear to transparent
            g.Clear(Color.Transparent);

            foreach (var layer in baseLayers)
            {
                if (layer.Image == null) continue;

                if (layer.Opacity < 1.0f)
                {
                    // Apply opacity
                    var colorMatrix = new System.Drawing.Imaging.ColorMatrix
                    {
                        Matrix33 = layer.Opacity
                    };
                    var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
                    imageAttributes.SetColorMatrix(colorMatrix);

                    var destRect = GetDestinationRectangle(layer, outputWidth, outputHeight);
                    g.DrawImage(layer.Image,
                        destRect,
                        0, 0, layer.Image.Width, layer.Image.Height,
                        GraphicsUnit.Pixel,
                        imageAttributes);
                }
                else
                {
                    // Draw without opacity modification
                    var destRect = GetDestinationRectangle(layer, outputWidth, outputHeight);
                    g.DrawImage(layer.Image, destRect);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Create a layer from a file path
    /// </summary>
    public static LayerInfo CreateLayerFromFile(string path, float opacity = 1.0f, LayerAlignment alignment = LayerAlignment.Fill)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Layer image not found: {path}");

        var image = new Bitmap(path);
        return new LayerInfo
        {
            Image = image,
            Opacity = opacity,
            Alignment = alignment,
            Name = Path.GetFileNameWithoutExtension(path)
        };
    }

    private static Rectangle GetDestinationRectangle(LayerInfo layer, int outputWidth, int outputHeight)
    {
        return layer.Alignment switch
        {
            LayerAlignment.Fill => new Rectangle(0, 0, outputWidth, outputHeight),
            LayerAlignment.Center => CenterRectangle(layer.Image.Width, layer.Image.Height, outputWidth, outputHeight),
            LayerAlignment.TopLeft => new Rectangle(0, 0, layer.Image.Width, layer.Image.Height),
            LayerAlignment.TopRight => new Rectangle(outputWidth - layer.Image.Width, 0, layer.Image.Width, layer.Image.Height),
            LayerAlignment.BottomLeft => new Rectangle(0, outputHeight - layer.Image.Height, layer.Image.Width, layer.Image.Height),
            LayerAlignment.BottomRight => new Rectangle(outputWidth - layer.Image.Width, outputHeight - layer.Image.Height, layer.Image.Width, layer.Image.Height),
            _ => new Rectangle(0, 0, outputWidth, outputHeight)
        };
    }

    private static Rectangle CenterRectangle(int imageWidth, int imageHeight, int containerWidth, int containerHeight)
    {
        var x = (containerWidth - imageWidth) / 2;
        var y = (containerHeight - imageHeight) / 2;
        return new Rectangle(x, y, imageWidth, imageHeight);
    }
}

/// <summary>
/// Information about a layer to be composited
/// </summary>
public class LayerInfo : IDisposable
{
    public Bitmap? Image { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public LayerAlignment Alignment { get; set; } = LayerAlignment.Fill;
    public string Name { get; set; } = string.Empty;

    public void Dispose()
    {
        Image?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// How to align a layer within the output
/// </summary>
public enum LayerAlignment
{
    Fill,          // Stretch to fill entire output
    Center,        // Center without stretching
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}
