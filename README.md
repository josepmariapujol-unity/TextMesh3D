# TextMesh3D

Convert fonts into 3D meshes, flat surfaces or splines in Unity.

TextMesh3D reads TrueType (`.ttf`) and OpenType (`.otf`) fonts and turns text into geometry built from the real glyph outlines: extruded solid letters, flat filled shapes, or editable spline contours.

## Features

- **Three outputs:** Mesh (extruded solids with side walls), Surface (flat, double-sided), Spline (one closed spline per glyph contour, using Unity Splines)
- **Correct typography:** font advance widths, kerning (OpenType GPOS and legacy `kern` table), line breaks (`\n`) and tabs
- **Font-independent sizing:** `Size` is the em size in Unity units, so text has the same size in every font
- **Ready for rendering:** UVs (in em units) and tangents, so textured and normal-mapped materials work
- **Group Output:** merge the whole text into one mesh on the object (one draw call)
- **Flat or upright:** lay text out on the XZ plane or stand it up on XY
- **Adjustable curve quality:** Curve Segments sets how finely curves are flattened
- **Live preview:** Auto Apply regenerates shortly after each edit (debounced, one Undo step per edit)
- **Editor friendly:** one Undo step per Apply, multi-object editing, a TextMesh3D window that follows the selection
- **Works in player builds:** the font is stored with the object when you assign it in the editor

## Requirements

- Unity 2022.3 or newer
- Unity Splines 2.8.1 or newer (installed automatically)

## Getting started

1. **GameObject > 3D Object > Text - TextMesh3D.** This creates "New Text" with the default font (Roboto) and material.
2. In the inspector, edit the text, font, output and size. With **Auto Apply** on (the default) the text updates as you edit; with it off, press **Apply**.
3. Optional: **Window > TextMesh3D** edits the selected text in a separate window.

### Saving as a prefab

After **Apply**, press **Save as Prefab...** (inspector or TextMesh3D window, or the component's context menu) to save the generated text as a `.prefab`.

- The prefab is **baked**: plain objects with MeshFilter/MeshRenderer (Surface, Mesh) or SplineContainer (Spline), without the TextMesh3D component. It does not need this package or the font at runtime.
- The meshes are stored **inside the .prefab file**, so the prefab stays valid when you regenerate or delete the original text.
- Saving to an existing prefab replaces its content and meshes; its instances in scenes update.
- To edit the text later, keep the original TextMesh3D object, change it, and save again to the same prefab.

From editor scripts: `TextMesh3DPrefabExporter.SaveAsPrefab(container, "Assets/Text/Title.prefab")` (namespace `Softgraph.TextMesh3D.Editor`).

### From code

```csharp
using Softgraph.TextMesh3D.Runtime;

[SerializeField] TextMesh3DContainer label;   // set up in the editor

void SetScore(int score)
{
    label.Text = $"Score {score}";
    label.Generate();
}
```

Import **TextMesh3D Examples** from the Package Manager (Samples tab) for a complete example.

## Settings

| Setting | Description |
|---|---|
| Text | The text. Supports `\n` (new line) and `\t` (4 spaces). |
| Font | A `.ttf` or `.otf` font asset. |
| Output | Spline, Surface or Mesh. |
| Plane | XZ: lying flat, extruded up (+Y). XY: standing up facing -Z (towards the default camera), extruded back along +Z. |
| Size | Em size in Unity units (capital letters are about 0.7 × Size tall). |
| Extrusion | Mesh only: depth of the letters. |
| Material | Surface/Mesh: material for the generated meshes. |
| Spacing *(Advanced)* | Extra space after every character, in Unity units. |
| Line Spacing *(Advanced)* | Distance between lines as a multiple of the font's line height (default 1). |
| Kerning *(Advanced)* | Apply the font's kerning (on by default). |
| Group Output *(Advanced)* | Surface/Mesh: one merged mesh on this object instead of one child per character. |
| Curve Segments *(Advanced)* | Surface/Mesh: points per curved outline segment (1–32, default 3). |

## Notes and limitations

- Text is laid out on the XZ plane (lying flat, extruded along +Y) by default. Set **Plane** to XY to stand it up: the front face is at z = 0 and the depth goes back along +Z.
- Text created entirely from code in a player build has no font data; set up the object in the editor and change its text at runtime instead.
- Characters missing from the font are drawn as the font's "missing glyph" shape and reported in the console.
- Right-to-left scripts, ligatures and complex shaping are not supported.

## Upgrading from 2.x

See [CHANGELOG.md](CHANGELOG.md). In short: `Enum` is now `OutputMode`, `Function()` is now `Generate()`, settings are properties (`Text`, `Font`, …) instead of public fields, and `Size` is now the em size (multiply old values by about 20 to keep the same size with 2048-unit fonts).

## Third-party software

This package includes third-party components:

| Component | License |
|---|---|
| [Typography.OpenFont](https://github.com/LayoutFarm/Typography) (font parsing) | Apache License 2.0 |
| System.Numerics.Vectors (.NET, dependency of Typography) | MIT |
| [Habrador Computational Geometry Library](https://github.com/Habrador/Computational-geometry) (triangulation, modified) | MIT |
| [Roboto](https://github.com/googlefonts/roboto) font | Apache License 2.0 |

Full license texts and copyright notices are in [Third Party Notices.md](Third%20Party%20Notices.md).

## License

TextMesh3D is released under the MIT License, see [LICENSE.md](LICENSE.md).
