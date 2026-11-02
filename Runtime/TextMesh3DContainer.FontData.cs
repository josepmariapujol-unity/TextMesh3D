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

        private Typeface LoadTypeface()
        {
#if UNITY_EDITOR
            CacheFontData();
#endif
            if (m_FontData == null || m_FontData.Length == 0)
            {
                Debug.LogError("TextMesh3D: no font data. Assign a .ttf/.otf font in the editor before generating.", this);
                return null;
            }

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
