# Vulcard.AtlasPacker

A MonoGame content pipeline extension that packs sprites into a texture atlas from a plain-text `.atlaspack` manifest. The output is designed to integrate directly with [MLEM](https://mlem.ellpeck.de/)'s `DataTextureAtlas`, which loads the companion `.atlas` file to give you named `TextureRegion2D` lookups at runtime.

## Features

- Glob-pattern sprite selection
- Per-sprite `size` (target slot) and `rect` (source crop) overrides
- Global default `size`/`rect` directives
- Auto-trim of transparent borders when no explicit rect is given
- Binary-tree bin packing for compact atlas layout
- Optional power-of-two atlas dimensions
- Writes a companion `.atlas` metadata file alongside the built texture

## Installation

```
dotnet add package Vulcard.AtlasPacker
```

The package targets file automatically registers the pipeline assembly with
`MonoGame.Content.Builder.Task` — no manual `.mgcb` edits required for
MSBuild-driven builds.

For projects that build content via the MGCB editor or call `dotnet-mgcb` directly,
add the reference manually to your `.mgcb` file:

```
/reference:path/to/Vulcard.AtlasPacker.dll
```

The MSBuild property `$(VulcardAtlasPacker_AssemblyPath)` exposes the exact path
after the package is restored.

## Manifest format

Create a `.atlaspack` file, set its importer to `Atlas Packer Importer` and
processor to `Atlas Packer Processor` in the MGCB editor.

```
# Lines starting with # are comments.

# Global default: every sprite below is packed into a 64×64 slot.
size 64

# Override for one sprite.
button.png size 128 128

# Explicit source crop, no slot resize.
sprite.png rect 10 5 48 48

# Crop + slot together.
sprite.png rect 10 5 48 48 size 128 128

# Disable the global size for this sprite.
icon.png size 0 0

# Glob — all PNGs in a subfolder.
Effects/*
```

### Annotations

| Syntax | Meaning |
|---|---|
| `size WH` | Square target slot |
| `size W H` | Target slot (source centred; transparent padding fills gaps) |
| `size 0 0` | Disable global size for this entry |
| `rect S` | Source crop `(0, 0, S, S)` |
| `rect O S` | Source crop `(O, O, S, S)` |
| `rect X Y W H` | Explicit source crop rectangle |

When neither `size` nor `rect` is specified, the sprite is auto-trimmed.

## Processor properties

| Property | Default | Description |
|---|---|---|
| `Padding` | `0` | Pixel gap around each sprite in the atlas |
| `OutputAsPng` | `false` | Write a `.png` alongside the `.xnb` (useful for debugging) |
| `PowerOfTwo` | `true` | Round atlas dimensions up to the next power of two |

## Atlas file format

The processor writes a `<name>.atlas` text file next to the built texture:

```
# format xnb
SpriteName
loc X Y W H

AnotherSprite
loc X Y W H
```

## Using with MLEM's DataTextureAtlas

The `.atlas` file uses the same format as MLEM's [`DataTextureAtlas`](https://mlem.ellpeck.de/api/MLEM.Data.DataTextureAtlas). Load it at runtime with:

```csharp
using MLEM.Data;
using MLEM.Textures;

// Load the texture, then hand it to MLEM together with the .atlas file.
var texture = content.Load<Texture2D>("Sprites/Icons/icons");
DataTextureAtlas atlas = DataTextureAtlas.LoadAtlasData(
    new TextureRegion(texture), content, "Sprites/Icons/icons.atlas");

// Look up a region by name (matches the PNG filename without extension).
TextureRegion region = atlas["iconattack"];
```

The processor writes a `# format xnb` or `# format png` header at the top of every `.atlas` file. MLEM's parser ignores `#` lines, so the header does not affect loading — it is there for your own loader code to detect which texture type to load. A helper that handles both cases:

```csharp
public static DataTextureAtlas LoadTextureAtlas(this ContentManager content, string name)
{
    string format = "xnb";
    using (var peek = new StreamReader(TitleContainer.OpenStream(content.RootDirectory + "/" + name + ".atlas")))
    {
        var first = peek.ReadLine() ?? "";
        if (first.StartsWith("# format "))
            format = first["# format ".Length..].Trim();
    }

    Texture2D texture;
    if (format == "png")
    {
        using var stream = TitleContainer.OpenStream(content.RootDirectory + "/" + name + ".png");
        texture = Texture2D.FromStream(Screen.GraphicsDeviceManager.GraphicsDevice, stream).PremultipliedCopy();
    }
    else
    {
        texture = content.Load<Texture2D>(name);
    }

    return DataTextureAtlas.LoadAtlasData(new TextureRegion(texture), content, name + ".atlas");
}
```
