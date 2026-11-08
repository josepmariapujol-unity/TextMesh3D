# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this package adheres to [Semantic Versioning](https://semver.org/).

## [3.0.0] - 2026-11-03

### Breaking changes
- `TextMesh3DContainer.Enum` renamed to `OutputMode`; `Function()` renamed to `Generate()` (`Function()` still works but is obsolete).
- Settings are now properties (`Text`, `Font`, `Output`, `Size`, `Extrusion`, `Material`, `Spacing`, `Kerning`, `GroupOutput`, `CurveSegments`) instead of public fields. Serialized data is unchanged, so existing scenes keep their values.
- `Size` is now the em size in Unity units, the same for every font. Previously text was about 20× larger for 2048-unit fonts and half that for 1000-unit fonts. To keep the old size of a 2048-unit font, multiply `Size` by 20.48.
- Glyph spacing now uses the font's advance widths and kerning, so existing text is laid out differently after regenerating.

### Added
- Kerning from the OpenType GPOS `kern` feature, with the legacy `kern` table as fallback (Kerning setting, on by default).
- Line breaks (`\n`) and tabs (`\t`); other control characters take no space.
- Line Spacing setting: distance between lines as a multiple of the font's line height.
- Plane setting: XZ (lying flat, the previous behavior and default) or XY (standing up, facing -Z, extruded back along +Z). Works with Group Output and Save as Prefab.
- Group Output: merges all glyphs into one mesh on the object.
- Curve Segments setting for curve quality.
- UVs (em units) and tangents on generated meshes.
- Save as Prefab: saves the generated text as a baked prefab (no TextMesh3D component) with its meshes stored inside the .prefab file. Available in the inspector, the TextMesh3D window and the component's context menu, and from editor scripts via `TextMesh3DPrefabExporter.SaveAsPrefab()`.
- Undo support: each Apply is one undo step.
- Live preview: the text regenerates 0.12 s after the last edit in the inspector or the TextMesh3D window, in the same undo step as the edit. There is no Apply button; text without output is generated when the inspector shows it, and **Regenerate** in the component's context menu forces it.
- Multi-object editing in the inspector; the TextMesh3D window follows the selection.
- Warnings for characters the font does not contain.
- Test suite (edit mode and play mode), including snapshot tests of the generated geometry. The Unity Test Framework is a dependency, so the Test Runner window is available; see "Running the tests" in the README.
- Sample: TextMesh3D Examples (a Hello World scene, updating text from code).

### Fixed
- Missing kerning for many pairs in fonts that use class-based GPOS kerning (e.g. Roboto "AV", "AT", "AW"): glyphs not listed in a class definition must be treated as class 0. Pair kerning is now read directly from the font's GPOS table.
- Undo after regenerating could remove the new output without restoring the previous one (the reference to it was not recorded).
- The editor could freeze for minutes when generating text for the first time or changing its font: the cached font file was a byte array, which Undo records one byte at a time. It is now stored as a single string; scenes from earlier versions are converted when loaded.
- Curves of TrueType fonts (quadratic Béziers) were converted to slightly wrong cubic curves, distorting round letters. They are now converted exactly.
- Player builds failed to compile (runtime code used `UnityEditor`). The font file is now cached with the object.
- Empty meshes for fonts with dense outlines (e.g. Garamond "H"), caused by fixed tolerances in the triangulator.
- Holes filled solid in fonts that list holes before the outline (e.g. Helvetica / Futura "B", "8", "&", "$").
- Holes assigned to the wrong outline in glyphs like "%".
- Mesh and material leaks when regenerating; temporary meshes are destroyed.
- Invalid settings, empty text, corrupt fonts and malformed outlines now log a clear message instead of throwing.
- The create menu could crash when the default material was not found, ignored the scene-view placement preference and picked up project assets with the same name.

### Changed
- The font file is parsed once per object and reused by later `Generate()` calls (also the kerning tables and pair lookups).
- Glyph meshes are cached per glyph: repeated letters share one mesh and are triangulated once, and later `Generate()` calls only build glyphs that are new (e.g. a score counter only builds each digit once). The cache is rebuilt when Font, Size, Extrusion, Curve Segments, Plane or Output change; while playing, the old meshes are destroyed.
- Layout no longer creates a temporary GameObject and SplineContainer per character: Group Output creates no per-character objects at all, and Surface/Mesh glyph objects get only their MeshFilter and MeshRenderer.
- Default font for new text is Roboto.
- Core folders renamed (no numbers or spaces); `TextMesh3DContainer` split into partial files.

### Removed
- Unused dependency on `com.unity.burst` and unused UGUI assembly references.
- Dead code (unused fields and helpers, commented-out old implementations).

## [2.0.0]

- Initial release.
