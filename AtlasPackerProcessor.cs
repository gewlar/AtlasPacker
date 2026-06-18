using System;
using System.ComponentModel;
using System.Text;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Vulcard.AtlasPacker;

[ContentProcessor(DisplayName = "Atlas Packer Processor")]
public class AtlasPackerProcessor : ContentProcessor<AtlasPackEntry[], Texture2DContent>
{
    [DefaultValue(0)]
    public int Padding { get; set; } = 0;

    /// <summary>
    /// When true, the atlas is written as a raw .png into the output folder.<br/>
    /// When false (default), a normal .xnb is produced.
    /// </summary>
    [DefaultValue(false)]
    public bool OutputAsPng { get; set; } = false;

    /// <summary>
    /// When true (default), atlas dimensions are rounded up to the next power of two.<br/>
    /// Set to false to use the exact packed dimensions — saves memory on modern hardware
    /// that supports non-power-of-two textures.
    /// </summary>
    [DefaultValue(true)]
    public bool PowerOfTwo { get; set; } = true;

    // Layout-only info — no image data kept in memory between the two passes.
    private record Entry(string FilePath, string Name, Rectangle SourceRegion, int PackedW, int PackedH);
    private record Placement(Entry Entry, Rectangle Dest);

    public override Texture2DContent Process(AtlasPackEntry[] input, ContentProcessorContext context)
    {
        // --- Pass 1: layout ---
        // Load each image just long enough to compute trim bounds, then dispose.
        // Images with an explicit SourceRect don't need to be loaded at all.
        var entries = new List<Entry>();
        foreach (var item in input)
        {
            context.AddDependency(item.FilePath);

            Rectangle sourceRegion;
            if (item.SourceRect is Rectangle sr)
            {
                sourceRegion = sr;
            }
            else
            {
                using var image = Image.Load<Rgba32>(item.FilePath);
                sourceRegion = GetTrimBounds(image);
            }

            int packedW = item.TargetSize?.W ?? sourceRegion.Width;
            int packedH = item.TargetSize?.H ?? sourceRegion.Height;
            entries.Add(new Entry(item.FilePath, Path.GetFileNameWithoutExtension(item.FilePath), sourceRegion, packedW, packedH));
        }

        // Detect name collisions: two different files with the same base name would produce
        // duplicate keys in the atlas metadata, silently discarding one sprite.
        var collisions = entries
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        if (collisions.Count > 0)
        {
            var details = string.Join("\n", collisions.Select(g =>
                $"  '{g.Key}': {string.Join(", ", g.Select(e => e.FilePath))}"));
            throw new InvalidOperationException($"Atlas name collision(s) detected:\n{details}");
        }

        var placements = BinPack(entries);

        if (placements.Count == 0)
            return new Texture2DContent();

        int rawW = placements.Max(p => p.Dest.X + p.Dest.Width);
        int rawH = placements.Max(p => p.Dest.Y + p.Dest.Height);
        int atlasWidth  = PowerOfTwo ? NextPowerOfTwo(rawW) : rawW;
        int atlasHeight = PowerOfTwo ? NextPowerOfTwo(rawH) : rawH;

        // --- Pass 2: composite ---
        // Load each source image individually, blit it into the atlas, then immediately dispose.
        // Only the atlas image and one source image are in memory at any time.
        var atlasImage = new Image<Rgba32>(atlasWidth, atlasHeight);
        foreach (var placement in placements)
        {
            using var image = Image.Load<Rgba32>(placement.Entry.FilePath);
            BlitCentered(image, placement.Entry.SourceRegion, atlasImage, placement.Dest);
        }

        var outputDir = Path.GetDirectoryName(context.OutputFilename)!;
        var baseName  = Path.GetFileNameWithoutExtension(context.OutputFilename);

        if (OutputAsPng)
            atlasImage.Save(Path.Combine(outputDir, baseName + ".png"), new PngEncoder());

        WriteAtlasFile(Path.Combine(outputDir, baseName + ".atlas"), placements);

        using (atlasImage)
            return ToTexture2DContent(atlasImage);
    }

    // -------------------------------------------------------------------------
    // Binary tree bin packing — jakesgordon.com/writing/bin-packing/

    private class Node
    {
        public int X, Y, W, H;
        public bool Used;
        public Node? Down, Right;
        public Node(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    }

    private List<Placement> BinPack(List<Entry> entries)
    {
        // Sort by area descending — better atlas utilisation than sorting by max dimension.
        var sorted = entries
            .OrderByDescending(e => e.PackedW * e.PackedH)
            .ToList();

        if (sorted.Count == 0) return [];

        // Round down to even so Padding/2 produces symmetric gutters on both sides.
        int effectivePadding = (Padding / 2) * 2;

        var first = sorted[0];
        var root = new Node(0, 0, first.PackedW + effectivePadding, first.PackedH + effectivePadding);

        var placements = new List<Placement>();
        foreach (var entry in sorted)
        {
            int w = entry.PackedW + effectivePadding;
            int h = entry.PackedH + effectivePadding;
            var node = FindNode(root, w, h) ?? GrowNode(ref root, w, h);
            var placed = SplitNode(node, w, h);
            // Dest is the inner content rect, centred within the padded slot.
            placements.Add(new Placement(entry, new Rectangle(
                placed.X + effectivePadding / 2,
                placed.Y + effectivePadding / 2,
                entry.PackedW,
                entry.PackedH)));
        }
        return placements;
    }

    private static Node? FindNode(Node? node, int w, int h)
    {
        if (node == null) return null;
        if (node.Used) return FindNode(node.Right, w, h) ?? FindNode(node.Down, w, h);
        if (w <= node.W && h <= node.H) return node;
        return null;
    }

    private static Node SplitNode(Node node, int w, int h)
    {
        node.Used = true;
        node.Down  = new Node(node.X,     node.Y + h, node.W,     node.H - h);
        node.Right = new Node(node.X + w, node.Y,     node.W - w, h);
        return node;
    }

    private static Node GrowNode(ref Node root, int w, int h)
    {
        bool canGrowDown  = w <= root.W;
        bool canGrowRight = h <= root.H;
        bool shouldGrowRight = canGrowRight && root.H >= root.W + w;
        bool shouldGrowDown  = canGrowDown  && root.W >= root.H + h;

        if (shouldGrowRight) return GrowRight(ref root, w, h);
        if (shouldGrowDown)  return GrowDown(ref root, w, h);
        if (canGrowRight)    return GrowRight(ref root, w, h);
        return GrowDown(ref root, w, h);
    }

    private static Node GrowRight(ref Node root, int w, int h)
    {
        var newRoot = new Node(0, 0, root.W + w, root.H)
        {
            Used = true,
            Down = root,
            Right = new Node(root.W, 0, w, root.H)
        };
        root = newRoot;
        var node = FindNode(root, w, h)!;
        return SplitNode(node, w, h);
    }

    private static Node GrowDown(ref Node root, int w, int h)
    {
        var newRoot = new Node(0, 0, root.W, root.H + h)
        {
            Used = true,
            Right = root,
            Down = new Node(0, root.H, root.W, h)
        };
        root = newRoot;
        var node = FindNode(root, w, h)!;
        return SplitNode(node, w, h);
    }

    // -------------------------------------------------------------------------

    private static Rectangle GetTrimBounds(Image<Rgba32> image)
    {
        int minX = image.Width, minY = image.Height, maxX = 0, maxY = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    if (row[x].A == 0) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        });
        return maxX >= minX
            ? new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : new Rectangle(0, 0, 1, 1);
    }

    /// <summary>
    /// Copies <paramref name="srcRegion"/> from <paramref name="src"/> into
    /// <paramref name="dst"/>, centred within <paramref name="dstSlot"/>.
    /// If the source region is larger than the slot it is centre-cropped.
    /// Pixels outside the copied area remain transparent (zero-initialised).
    /// </summary>
    private static void BlitCentered(
        Image<Rgba32> src, Rectangle srcRegion,
        Image<Rgba32> dst, Rectangle dstSlot)
    {
        int dstOffX = (dstSlot.Width  - srcRegion.Width)  / 2;
        int dstOffY = (dstSlot.Height - srcRegion.Height) / 2;

        int srcOffX = Math.Max(0, -dstOffX);
        int srcOffY = Math.Max(0, -dstOffY);
        dstOffX = Math.Max(0, dstOffX);
        dstOffY = Math.Max(0, dstOffY);

        int copyW = Math.Min(srcRegion.Width  - srcOffX, dstSlot.Width  - dstOffX);
        int copyH = Math.Min(srcRegion.Height - srcOffY, dstSlot.Height - dstOffY);
        if (copyW <= 0 || copyH <= 0) return;

        var srcRect = new Rectangle(srcRegion.X + srcOffX, srcRegion.Y + srcOffY, copyW, copyH);
        var dstRect = new Rectangle(dstSlot.X   + dstOffX, dstSlot.Y   + dstOffY, copyW, copyH);

        src.ProcessPixelRows(dst, (srcAcc, dstAcc) =>
        {
            for (int row = 0; row < copyH; row++)
            {
                var srcRow = srcAcc.GetRowSpan(srcRect.Y + row);
                var dstRow = dstAcc.GetRowSpan(dstRect.Y + row);
                srcRow.Slice(srcRect.X, copyW).CopyTo(dstRow.Slice(dstRect.X));
            }
        });
    }

    private static Texture2DContent ToTexture2DContent(Image<Rgba32> image)
    {
        var bitmap = new PixelBitmapContent<XnaColor>(image.Width, image.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < image.Width; x++)
                {
                    var p = row[x];
                    bitmap.SetPixel(x, y, new XnaColor(p.R, p.G, p.B, p.A));
                }
            }
        });
        var content = new Texture2DContent();
        content.Faces[0].Add(bitmap);
        return content;
    }

    private void WriteAtlasFile(string path, List<Placement> placements)
    {
        var sb = new StringBuilder();
        sb.AppendLine(OutputAsPng ? "# format png" : "# format xnb");
        foreach (var p in placements)
        {
            sb.AppendLine(p.Entry.Name);
            sb.AppendLine($"loc {p.Dest.X} {p.Dest.Y} {p.Dest.Width} {p.Dest.Height}");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static int NextPowerOfTwo(int value)
    {
        var p = 1;
        while (p < value) p <<= 1;
        return p;
    }
}
