using SixLabors.ImageSharp;
using Xunit;

namespace Vulcard.AtlasPacker.Tests;

public class ImporterTests
{
    // ── TryParseSize ──────────────────────────────────────────────────────────

    [Fact]
    public void TryParseSize_SingleToken_ReturnsSquare()
    {
        Assert.True(AtlasPackerImporter.TryParseSize(["64"], out int w, out int h));
        Assert.Equal(64, w);
        Assert.Equal(64, h);
    }

    [Fact]
    public void TryParseSize_TwoTokens_ReturnsWidthHeight()
    {
        Assert.True(AtlasPackerImporter.TryParseSize(["64", "128"], out int w, out int h));
        Assert.Equal(64, w);
        Assert.Equal(128, h);
    }

    [Fact]
    public void TryParseSize_ZeroZero_Succeeds()
    {
        Assert.True(AtlasPackerImporter.TryParseSize(["0", "0"], out int w, out int h));
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void TryParseSize_Empty_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseSize([], out _, out _));

    [Fact]
    public void TryParseSize_NonInteger_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseSize(["abc"], out _, out _));

    [Fact]
    public void TryParseSize_ThreeTokens_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseSize(["1", "2", "3"], out _, out _));

    // ── TryParseRect ──────────────────────────────────────────────────────────

    [Fact]
    public void TryParseRect_OneToken_ReturnsOriginSquare()
    {
        Assert.True(AtlasPackerImporter.TryParseRect(["48"], out Rectangle r));
        Assert.Equal(new Rectangle(0, 0, 48, 48), r);
    }

    [Fact]
    public void TryParseRect_TwoTokens_ReturnsOffsetSquare()
    {
        Assert.True(AtlasPackerImporter.TryParseRect(["10", "48"], out Rectangle r));
        Assert.Equal(new Rectangle(10, 10, 48, 48), r);
    }

    [Fact]
    public void TryParseRect_FourTokens_ReturnsExplicit()
    {
        Assert.True(AtlasPackerImporter.TryParseRect(["10", "5", "48", "32"], out Rectangle r));
        Assert.Equal(new Rectangle(10, 5, 48, 32), r);
    }

    [Fact]
    public void TryParseRect_Empty_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseRect([], out _));

    [Fact]
    public void TryParseRect_NonInteger_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseRect(["abc", "def"], out _));

    [Fact]
    public void TryParseRect_ThreeTokens_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseRect(["1", "2", "3"], out _));

    // ── TryParseGlobalDirective ───────────────────────────────────────────────

    [Fact]
    public void TryParseGlobalDirective_SizeLine_ReturnsTrueWithParts()
    {
        Assert.True(AtlasPackerImporter.TryParseGlobalDirective("size 64 64", "size", out var parts));
        Assert.Equal<string[]>(["64", "64"], parts);
    }

    [Fact]
    public void TryParseGlobalDirective_RectLine_ReturnsTrueWithParts()
    {
        Assert.True(AtlasPackerImporter.TryParseGlobalDirective("rect 10 10 48 48", "rect", out var parts));
        Assert.Equal<string[]>(["10", "10", "48", "48"], parts);
    }

    [Fact]
    public void TryParseGlobalDirective_HasDot_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseGlobalDirective("button.png size 64", "size", out _));

    [Fact]
    public void TryParseGlobalDirective_HasSlash_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseGlobalDirective("sprites/size 64", "size", out _));

    [Fact]
    public void TryParseGlobalDirective_HasWildcard_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseGlobalDirective("Effects/*", "size", out _));

    [Fact]
    public void TryParseGlobalDirective_WrongKeyword_ReturnsFalse() =>
        Assert.False(AtlasPackerImporter.TryParseGlobalDirective("rect 64", "size", out _));

    [Fact]
    public void TryParseGlobalDirective_CaseInsensitive()
    {
        Assert.True(AtlasPackerImporter.TryParseGlobalDirective("SIZE 64", "size", out var parts));
        Assert.Equal<string[]>(["64"], parts);
    }

    // ── ParseAnnotations ──────────────────────────────────────────────────────

    [Fact]
    public void ParseAnnotations_NoAnnotations_ReturnsLineAsGlob()
    {
        AtlasPackerImporter.ParseAnnotations("button.png", out var glob, out var rect, out var size);
        Assert.Equal("button.png", glob);
        Assert.Null(rect);
        Assert.Null(size);
    }

    [Fact]
    public void ParseAnnotations_WithSizeSquare_ExtractsSize()
    {
        AtlasPackerImporter.ParseAnnotations("button.png size 64", out var glob, out _, out var size);
        Assert.Equal("button.png", glob);
        Assert.Equal((64, 64), size);
    }

    [Fact]
    public void ParseAnnotations_WithSizeWidthHeight_ExtractsSize()
    {
        AtlasPackerImporter.ParseAnnotations("button.png size 64 128", out var glob, out _, out var size);
        Assert.Equal("button.png", glob);
        Assert.Equal((64, 128), size);
    }

    [Fact]
    public void ParseAnnotations_SizeZeroZero_ReturnsExplicitDisable()
    {
        AtlasPackerImporter.ParseAnnotations("button.png size 0 0", out var glob, out _, out var size);
        Assert.Equal("button.png", glob);
        Assert.Equal((0, 0), size);
    }

    [Fact]
    public void ParseAnnotations_WithRect_ExtractsRect()
    {
        AtlasPackerImporter.ParseAnnotations("button.png rect 10 5 48 32", out var glob, out var rect, out var size);
        Assert.Equal("button.png", glob);
        Assert.Equal(new Rectangle(10, 5, 48, 32), rect);
        Assert.Null(size);
    }

    [Fact]
    public void ParseAnnotations_WithRectAndSize_ExtractsBoth()
    {
        AtlasPackerImporter.ParseAnnotations("button.png rect 10 5 48 48 size 128 128", out var glob, out var rect, out var size);
        Assert.Equal("button.png", glob);
        Assert.Equal(new Rectangle(10, 5, 48, 48), rect);
        Assert.Equal((128, 128), size);
    }

    [Fact]
    public void ParseAnnotations_GlobPattern_ExtractsAnnotation()
    {
        AtlasPackerImporter.ParseAnnotations("Effects/* size 64", out var glob, out _, out var size);
        Assert.Equal("Effects/*", glob);
        Assert.Equal((64, 64), size);
    }

    [Fact]
    public void ParseAnnotations_PathWithSizeInDirName_DoesNotConsumeDir()
    {
        // Directory named "button size large" must not be treated as a size annotation.
        AtlasPackerImporter.ParseAnnotations("button size large/icon.png", out var glob, out _, out var size);
        Assert.Equal("button size large/icon.png", glob);
        Assert.Null(size);
    }

    [Fact]
    public void ParseAnnotations_WithSubdir_SizeApplied()
    {
        AtlasPackerImporter.ParseAnnotations("sprites/button.png size 64", out var glob, out _, out var size);
        Assert.Equal("sprites/button.png", glob);
        Assert.Equal((64, 64), size);
    }
}
