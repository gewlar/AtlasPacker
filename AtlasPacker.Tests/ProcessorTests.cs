using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Vulcard.AtlasPacker.Tests;

public class ProcessorTests
{
    // ── NextPowerOfTwo ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1,   1)]
    [InlineData(2,   2)]
    [InlineData(3,   4)]
    [InlineData(4,   4)]
    [InlineData(5,   8)]
    [InlineData(128, 128)]
    [InlineData(129, 256)]
    [InlineData(500, 512)]
    [InlineData(1000, 1024)]
    public void NextPowerOfTwo_ReturnsExpected(int input, int expected) =>
        Assert.Equal(expected, AtlasPackerProcessor.NextPowerOfTwo(input));

    // ── GetTrimBounds ─────────────────────────────────────────────────────────

    [Fact]
    public void GetTrimBounds_FullyTransparent_ReturnsNull()
    {
        using var img = new Image<Rgba32>(10, 10);
        Assert.Null(AtlasPackerProcessor.GetTrimBounds(img));
    }

    [Fact]
    public void GetTrimBounds_FullyOpaque_ReturnsFullImage()
    {
        using var img = new Image<Rgba32>(10, 10, new Rgba32(255, 0, 0, 255));
        Assert.Equal(new Rectangle(0, 0, 10, 10), AtlasPackerProcessor.GetTrimBounds(img));
    }

    [Fact]
    public void GetTrimBounds_SingleCenterPixel_ReturnsSinglePixelRect()
    {
        using var img = new Image<Rgba32>(10, 10);
        img[5, 5] = new Rgba32(255, 0, 0, 255);
        Assert.Equal(new Rectangle(5, 5, 1, 1), AtlasPackerProcessor.GetTrimBounds(img));
    }

    [Fact]
    public void GetTrimBounds_TwoCornerPixels_ReturnsTightBounds()
    {
        using var img = new Image<Rgba32>(10, 10);
        img[2, 3] = new Rgba32(255, 0, 0, 255);
        img[7, 8] = new Rgba32(0, 255, 0, 255);
        // minX=2 minY=3 maxX=7 maxY=8 → width=6 height=6
        Assert.Equal(new Rectangle(2, 3, 6, 6), AtlasPackerProcessor.GetTrimBounds(img));
    }

    [Fact]
    public void GetTrimBounds_ZeroAlphaPixels_NotCounted()
    {
        using var img = new Image<Rgba32>(10, 10);
        img[0, 0] = new Rgba32(255, 0, 0, 0);  // fully transparent — must not be counted
        img[5, 5] = new Rgba32(255, 0, 0, 255);
        Assert.Equal(new Rectangle(5, 5, 1, 1), AtlasPackerProcessor.GetTrimBounds(img));
    }

    // ── BlitCentered ──────────────────────────────────────────────────────────

    [Fact]
    public void BlitCentered_SameSize_CopiesExactly()
    {
        using var src = new Image<Rgba32>(4, 4, new Rgba32(255, 0, 0, 255));
        using var dst = new Image<Rgba32>(8, 8);
        AtlasPackerProcessor.BlitCentered(src, new Rectangle(0, 0, 4, 4), dst, new Rectangle(0, 0, 4, 4));

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(new Rgba32(255, 0, 0, 255), dst[x, y]);

        Assert.Equal(new Rgba32(0, 0, 0, 0), dst[4, 4]);
    }

    [Fact]
    public void BlitCentered_SrcSmallerThanSlot_CentresContent()
    {
        // 2×2 source centred in a 6×6 slot → content at offset (2,2)
        using var src = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 255));
        using var dst = new Image<Rgba32>(10, 10);
        AtlasPackerProcessor.BlitCentered(src, new Rectangle(0, 0, 2, 2), dst, new Rectangle(0, 0, 6, 6));

        Assert.Equal(new Rgba32(255, 0, 0, 255), dst[2, 2]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), dst[3, 3]);
        Assert.Equal(new Rgba32(0, 0, 0, 0),     dst[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0),     dst[5, 5]);
    }

    [Fact]
    public void BlitCentered_SrcLargerThanSlot_CentreCrops()
    {
        // 6×6 source into 4×4 slot → srcOffX=srcOffY=1; copies src[1..4, 1..4] → dst[0..3, 0..3]
        using var src = new Image<Rgba32>(6, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                src[x, y] = new Rgba32((byte)(x * 40), (byte)(y * 40), 0, 255);

        using var dst = new Image<Rgba32>(8, 8);
        AtlasPackerProcessor.BlitCentered(src, new Rectangle(0, 0, 6, 6), dst, new Rectangle(0, 0, 4, 4));

        Assert.Equal(src[1, 1], dst[0, 0]);
        Assert.Equal(src[4, 4], dst[3, 3]);
    }

    [Fact]
    public void BlitCentered_SlotOffset_PlacesAtCorrectPosition()
    {
        using var src = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 255, 255));
        using var dst = new Image<Rgba32>(10, 10);
        AtlasPackerProcessor.BlitCentered(src, new Rectangle(0, 0, 2, 2), dst, new Rectangle(4, 4, 2, 2));

        Assert.Equal(new Rgba32(0, 0, 255, 255), dst[4, 4]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), dst[5, 5]);
        Assert.Equal(new Rgba32(0, 0, 0, 0),     dst[3, 4]);
        Assert.Equal(new Rgba32(0, 0, 0, 0),     dst[6, 6]);
    }

    [Fact]
    public void BlitCentered_SubregionOfSource_CopiesCorrectPixels()
    {
        using var src = new Image<Rgba32>(10, 10);
        // Paint a distinct 4×4 region starting at (3,3)
        for (int y = 3; y < 7; y++)
            for (int x = 3; x < 7; x++)
                src[x, y] = new Rgba32(0, 255, 0, 255);

        using var dst = new Image<Rgba32>(10, 10);
        AtlasPackerProcessor.BlitCentered(src, new Rectangle(3, 3, 4, 4), dst, new Rectangle(1, 1, 4, 4));

        for (int y = 1; y < 5; y++)
            for (int x = 1; x < 5; x++)
                Assert.Equal(new Rgba32(0, 255, 0, 255), dst[x, y]);

        Assert.Equal(new Rgba32(0, 0, 0, 0), dst[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), dst[5, 5]);
    }
}
