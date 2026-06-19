using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Xna.Framework.Content.Pipeline;
using SixLabors.ImageSharp;

namespace Vulcard.AtlasPacker;

/// <summary>
/// Imports a .atlaspack manifest file — a plain-text list of glob patterns (one per line,
/// # for comments) relative to the manifest's directory.
///
/// <b>Format:</b>
/// <code>
///   # "size ..." or "rect ..." alone on a line (no path chars) set the global default
///   # for all entries below.  Per-entry annotations override the global.
///   # "rect 0 0 0 0" clears a previously set global rect (mirrors "size 0 0").
///   size 64 64
///   size 64         # square shorthand
///   rect 10 10 48 48
///   rect 48         # (0, 0, 48, 48)
///   rect 10 48      # (10, 10, 48, 48)
///   rect 0 0 0 0    # clear global rect
///
///   # Per-line annotations (any combination, any order after the glob pattern):
///   #   size W H        — target packed-slot size (centred; "size 0 0" disables global)
///   #   size WH         — square shorthand
///   #   rect X Y W H   — explicit source crop (overrides auto-trim and global rect)
///   #   rect size       — (0, 0, size, size)
///   #   rect offset size — (offset, offset, size, size)
///
///   button.png size 128 128
///   sprite.png rect 10 5 48 48
///   sprite.png rect 10 5 48 48 size 128 128
///   Effects/*
///   icon.png size 0 0
/// </code>
/// </summary>
[ContentImporter(".atlaspack",
    DisplayName = "Atlas Packer Importer",
    DefaultProcessor = "Atlas Packer Processor")]
public class AtlasPackerImporter : ContentImporter<AtlasPackEntry[]>
{
    public override AtlasPackEntry[] Import(string filename, ContentImporterContext context)
    {
        context.AddDependency(filename);
        var dir = Path.GetDirectoryName(filename)!;
        var results = new List<AtlasPackEntry>();
        (int W, int H)? globalSize = null;
        Rectangle? globalRect = null;

        foreach (var rawLine in File.ReadAllLines(filename))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // Global directives: "size ..." or "rect ..." with no path characters
            if (TryParseGlobalDirective(line, "size", out var sizeParts) && TryParseSize(sizeParts, out var gw, out var gh))
            {
                globalSize = (gw == 0 && gh == 0) ? null : (gw, gh);
                continue;
            }
            if (TryParseGlobalDirective(line, "rect", out var rectParts) && TryParseRect(rectParts, out var gr))
            {
                // "rect 0 0 0 0" (zero Width and Height) acts as a clear directive, mirroring
                // how "size 0 0" disables the global size.
                globalRect = (gr.Width == 0 && gr.Height == 0) ? null : gr;
                continue;
            }

            // Parse annotations and glob pattern from the line
            ParseAnnotations(line, out var glob, out var sourceRect, out var targetSize);

            // Effective target size and rect:
            //   explicit inline "size ..." → use it (null for "size 0 0" = explicitly disabled)
            //   inline rect (no inline size) → suppress global size (rect defines the region)
            //   no inline annotations → inherit global size
            Rectangle? effectiveRect = null;
            (int W, int H)? effectiveSize = null;
            if (targetSize is (0, 0))
                effectiveSize = null;
            else if (targetSize.HasValue || sourceRect.HasValue)
            {
                effectiveSize = targetSize;
                effectiveRect = sourceRect;
            }
            else
            {
                effectiveSize = globalSize;
                effectiveRect = globalRect;
            }

            var paths = ExpandGlob(dir, glob);
            if (paths.Length == 0)
                context.Logger.LogWarning(null, null, "Glob pattern '{0}' matched no files in '{1}'.", glob, dir);
            foreach (var path in paths)
                results.Add(new AtlasPackEntry { FilePath = path, SourceRect = effectiveRect, TargetSize = effectiveSize });
        }

        // Deduplicate: the same file can be matched by multiple glob patterns.
        // Keep the first occurrence (and its annotations) to avoid double-packing.
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        results.RemoveAll(e => !seen.Add(e.FilePath));

        return results.ToArray();
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Detects a global directive line: starts with <paramref name="keyword"/> followed by
    /// a space, contains no path characters (no '.', '/', '\', '*'), and returns the
    /// remaining tokens after the keyword.
    /// </summary>
    internal static bool TryParseGlobalDirective(string line, string keyword, out string[] parts)
    {
        parts = [];
        if (line.IndexOfAny(['.', '/', '\\', '*']) >= 0) return false;
        if (!line.StartsWith(keyword + ' ', System.StringComparison.OrdinalIgnoreCase)) return false;
        parts = line[(keyword.Length + 1)..].Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    /// <summary>
    /// Parses per-line annotations out of a manifest line.
    /// Annotations are recognised by keyword (" size " or " rect ") and may appear in any order.
    /// The remainder — after stripping all recognised annotations — is the glob pattern.
    /// </summary>
    internal static void ParseAnnotations(
        string line,
        out string glob,
        out Rectangle? sourceRect,
        out (int W, int H)? targetSize)
    {
        sourceRect = null;
        targetSize = null;

        // Only search for annotations in the filename portion (after the last path separator)
        // to prevent directory names like "button size large" from being consumed as annotations.
        int lastSep = line.LastIndexOfAny(['/', '\\']);
        string annotationZone = lastSep >= 0 ? line[(lastSep + 1)..] : line;

        // Parse "size ..." annotation — raw value kept; (0, 0) means "explicitly disabled"
        int sizeIdx = annotationZone.LastIndexOf(" size ", System.StringComparison.OrdinalIgnoreCase);
        if (sizeIdx >= 0) sizeIdx += lastSep + 1; // adjust to full-line index
        if (sizeIdx >= 0)
        {
            var parts = line[(sizeIdx + 6)..].Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (TryParseSize(parts, out int tw, out int th))
            {
                targetSize = (tw, th);
                line = line[..sizeIdx];
                // Recompute annotation zone after stripping size annotation
                lastSep = line.LastIndexOfAny(['/', '\\']);
                annotationZone = lastSep >= 0 ? line[(lastSep + 1)..] : line;
            }
        }

        // Parse "rect ..." annotation (re-search after potentially stripping size)
        int rectIdx = annotationZone.LastIndexOf(" rect ", System.StringComparison.OrdinalIgnoreCase);
        if (rectIdx >= 0) rectIdx += lastSep + 1; // adjust to full-line index
        if (rectIdx >= 0)
        {
            var parts = line[(rectIdx + 6)..].Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (TryParseRect(parts, out var rect))
            {
                sourceRect = rect;
                line = line[..rectIdx];
            }
        }

        glob = line.Trim();
    }

    // -------------------------------------------------------------------------

    /// <summary>Parses "WH" (square) or "W H" into w and h.</summary>
    internal static bool TryParseSize(string[] parts, out int w, out int h)
    {
        w = h = 0;
        if (parts.Length == 1 && int.TryParse(parts[0], out int wh)) { w = h = wh; return true; }
        return parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h);
    }

    /// <summary>
    /// Parses rect variants:<br/>
    ///   "size"         → (0, 0, size, size)<br/>
    ///   "offset size"  → (offset, offset, size, size)<br/>
    ///   "X Y W H"      → (X, Y, W, H)
    /// </summary>
    internal static bool TryParseRect(string[] parts, out Rectangle rect)
    {
        rect = default;
        if (parts.Length == 1 && int.TryParse(parts[0], out int size))
        {
            rect = new Rectangle(0, 0, size, size); return true;
        }
        if (parts.Length == 2 && int.TryParse(parts[0], out int offset) && int.TryParse(parts[1], out int size2))
        {
            rect = new Rectangle(offset, offset, size2, size2); return true;
        }
        if (parts.Length == 4
            && int.TryParse(parts[0], out int rx) && int.TryParse(parts[1], out int ry)
            && int.TryParse(parts[2], out int rw) && int.TryParse(parts[3], out int rh))
        {
            rect = new Rectangle(rx, ry, rw, rh); return true;
        }
        return false;
    }

    private static string[] ExpandGlob(string baseDir, string glob)
    {
        var matcher = new Matcher();
        matcher.AddInclude(glob);
        return [.. matcher.GetResultsInFullPath(baseDir)];
    }
}
