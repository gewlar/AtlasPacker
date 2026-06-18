using SixLabors.ImageSharp;

namespace Vulcard.AtlasPacker;

/// <summary>
/// A single sprite entry produced by <see cref="AtlasPackerImporter"/> and consumed by
/// <see cref="AtlasPackerProcessor"/>.
/// </summary>
public struct AtlasPackEntry
{
    /// <summary>Absolute path to the source PNG file.</summary>
    public string FilePath;

    /// <summary>
    /// Explicit source crop rectangle in source-image pixels.
    /// When null the processor auto-trims transparent borders.
    /// </summary>
    public Rectangle? SourceRect;

    /// <summary>
    /// Target packed-slot size (Width, Height). The source content is centred within
    /// this slot; transparent padding fills any gap, or the content is centre-cropped
    /// when it exceeds the target. When null, the slot equals the source/trim size.
    /// </summary>
    public (int W, int H)? TargetSize;
}
