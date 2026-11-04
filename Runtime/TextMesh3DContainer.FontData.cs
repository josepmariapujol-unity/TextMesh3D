using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Typography.OpenFont;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Softgraph.TextMesh3D.Runtime
{
    // Font loading: the editor caches the font file's bytes so generation also works in player builds.
    public partial class TextMesh3DContainer
    {
        // Raw bytes of m_Font's source file (.ttf/.otf). Unity's Font asset does not expose the
        // original file at runtime, so the editor caches it here to make generation work in builds.
        [SerializeField, HideInInspector] private byte[] m_FontData;
        [SerializeField, HideInInspector] private string m_FontDataSource;

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheFontData();
        }

        /// <summary>Editor only: reads the font file behind m_Font into m_FontData when the font changes.</summary>
        public void CacheFontData()
        {
            var path = m_Font != null ? AssetDatabase.GetAssetPath(m_Font) : null;

            if (string.IsNullOrEmpty(path))
            {
                if (m_FontData != null)
                {
                    m_FontData = null;
                    m_FontDataSource = null;
                    MarkDirty();
                }
                return;
            }

            if (m_FontData != null && m_FontData.Length > 0 && m_FontDataSource == path)
                return;

            var fullPath = Path.GetFullPath(path); // resolves Packages/... to the real location
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"TextMesh3D: font file not found at '{path}'.", this);
                return;
            }

            m_FontData = File.ReadAllBytes(fullPath);
            m_FontDataSource = path;
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (!Application.isPlaying)
                EditorUtility.SetDirty(this);
        }
#endif

        // Parsed font, reused across Generate() calls. Keyed on the m_FontData array itself: assigning a new font,
        // Undo/Redo and domain reloads all produce a new array, so a stale typeface can never be used.
        [NonSerialized] private Typeface m_Typeface;
        [NonSerialized] private byte[] m_TypefaceSource;
        [NonSerialized] private GlyphKerning m_TypefaceKerning;

        /// <summary>Number of times the font file was parsed (for tests and diagnostics).</summary>
        internal int TypefaceParseCount { get; private set; }

        /// <summary>Kerning for the cached typeface (built once per font, pair lookups are cached inside).</summary>
        private GlyphKerning GetKerning(Typeface typeface)
        {
            if (m_TypefaceKerning == null || !ReferenceEquals(typeface, m_Typeface))
            {
                m_TypefaceKerning = new GlyphKerning(typeface, m_FontData);
            }
            return m_TypefaceKerning;
        }

        private Typeface LoadTypeface()
        {
#if UNITY_EDITOR
            CacheFontData();
#endif
            if (m_FontData == null || m_FontData.Length == 0)
            {
                ClearTypefaceCache();
                Debug.LogError("TextMesh3D: no font data. Assign a .ttf/.otf font in the editor before generating.", this);
                return null;
            }

            if (m_Typeface != null && ReferenceEquals(m_TypefaceSource, m_FontData))
            {
                return m_Typeface;
            }

            ClearTypefaceCache();
            var typeface = ParseTypeface();
            if (typeface != null)
            {
                m_Typeface = typeface;
                m_TypefaceSource = m_FontData;
            }
            return typeface;
        }

        private void ClearTypefaceCache()
        {
            m_Typeface = null;
            m_TypefaceSource = null;
            m_TypefaceKerning = null;
        }

        private Typeface ParseTypeface()
        {
            TypefaceParseCount++;
            Typeface typeface = null;
            try
            {
                using (var stream = new MemoryStream(m_FontData, false))
                {
                    typeface = new OpenFontReader().Read(stream);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"TextMesh3D: could not read font '{(m_Font != null ? m_Font.name : "?")}': {e.Message}", this);
                return null;
            }

            if (typeface == null)
            {
                Debug.LogError($"TextMesh3D: font '{(m_Font != null ? m_Font.name : "?")}' is not a supported TrueType/OpenType font.", this);
            }
            return typeface;
        }
    }
}
