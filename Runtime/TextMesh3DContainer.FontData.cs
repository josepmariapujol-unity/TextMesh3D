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
    public partial class TextMesh3DContainer : ISerializationCallbackReceiver
    {
        // m_Font's source file (.ttf/.otf), base64-encoded. Unity's Font asset does not expose the original file at
        // runtime, so the editor caches it here to make generation work in builds.
        // Stored as one string, not a byte[]: Undo diffs a byte[] element by element, so a ~170 KB font became
        // ~170,000 property modifications whose de-duplication is quadratic and froze the editor for minutes.
        // A string is a single property: one modification, however large the font.
        [SerializeField, HideInInspector] private string m_FontFile;
        [SerializeField, HideInInspector] private string m_FontDataSource;

        // Before 3.0.0 the bytes were stored here. Converted to m_FontFile when an old scene or prefab is loaded.
        [SerializeField, HideInInspector] private byte[] m_FontData;

        // m_FontFile decoded, and the string it was decoded from
        [NonSerialized] private byte[] m_FontBytes;
        [NonSerialized] private string m_FontBytesSource;

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Migration from the byte[] field. Done while loading, outside any undo recording.
            if (m_FontData != null && m_FontData.Length > 0)
            {
                if (string.IsNullOrEmpty(m_FontFile)) m_FontFile = Convert.ToBase64String(m_FontData);
                m_FontData = null;
            }
        }

        /// <summary>The font file's bytes, or null. Decoded once per font (compared by value: Unity deserializes a new
        /// string instance on every inspector change, which must not cause a re-decode and re-parse).</summary>
        private byte[] GetFontBytes()
        {
            if (string.IsNullOrEmpty(m_FontFile))
            {
                m_FontBytes = null;
                m_FontBytesSource = null;
                return null;
            }
            if (m_FontBytes == null || m_FontBytesSource != m_FontFile)
            {
                try
                {
                    m_FontBytes = Convert.FromBase64String(m_FontFile);
                }
                catch (FormatException)
                {
                    m_FontBytes = null;
                }
                m_FontBytesSource = m_FontFile;
            }
            return m_FontBytes;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheFontData();
        }

        /// <summary>Editor only: reads the font file behind m_Font into m_FontFile when the font changes.</summary>
        public void CacheFontData()
        {
            var path = m_Font != null ? AssetDatabase.GetAssetPath(m_Font) : null;

            if (string.IsNullOrEmpty(path))
            {
                if (!string.IsNullOrEmpty(m_FontFile))
                {
                    m_FontFile = null;
                    m_FontDataSource = null;
                    MarkDirty();
                }
                return;
            }

            if (!string.IsNullOrEmpty(m_FontFile) && m_FontDataSource == path)
                return;

            var fullPath = Path.GetFullPath(path); // resolves Packages/... to the real location
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"TextMesh3D: font file not found at '{path}'.", this);
                return;
            }

            m_FontFile = Convert.ToBase64String(File.ReadAllBytes(fullPath));
            m_FontDataSource = path;
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (!Application.isPlaying)
                EditorUtility.SetDirty(this);
        }
#endif

        // Parsed font, reused across Generate() calls. Keyed on the decoded byte array, which is replaced whenever
        // the stored font changes (new font, Undo/Redo to another font, domain reload), so a stale typeface is never used.
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
                m_TypefaceKerning = new GlyphKerning(typeface, m_TypefaceSource);
            }
            return m_TypefaceKerning;
        }

        private Typeface LoadTypeface()
        {
#if UNITY_EDITOR
            CacheFontData();
#endif
            var bytes = GetFontBytes();
            if (bytes == null || bytes.Length == 0)
            {
                ClearTypefaceCache();
                Debug.LogError("TextMesh3D: no font data. Assign a .ttf/.otf font in the editor before generating.", this);
                return null;
            }

            if (m_Typeface != null && ReferenceEquals(m_TypefaceSource, bytes))
            {
                return m_Typeface;
            }

            ClearTypefaceCache();
            var typeface = ParseTypeface(bytes);
            if (typeface != null)
            {
                m_Typeface = typeface;
                m_TypefaceSource = bytes;
            }
            return typeface;
        }

        private void ClearTypefaceCache()
        {
            m_Typeface = null;
            m_TypefaceSource = null;
            m_TypefaceKerning = null;
        }

        private Typeface ParseTypeface(byte[] bytes)
        {
            TypefaceParseCount++;
            Typeface typeface = null;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
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
