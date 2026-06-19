# Vulcard.AtlasPacker

A MonoGame content pipeline extension that packs sprites into a texture atlas from a plain-text `.atlaspack` manifest.

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
