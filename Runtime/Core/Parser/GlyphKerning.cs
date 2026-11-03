using System;
using System.Collections.Generic;
using Typography.OpenFont;
using Typography.OpenFont.Tables;

namespace Softgraph.TextMesh3D.Runtime
{
    /// <summary>Kerning adjustment for one glyph pair, in font units.</summary>
    internal struct PairAdjustment
    {
        /// <summary>Added to the first glyph's advance (negative pulls the second glyph closer).</summary>
        public short FirstAdvance;
        /// <summary>Added to the second glyph's advance.</summary>
        public short SecondAdvance;
        /// <summary>Placement offsets of each glyph relative to its pen position.</summary>
        public short FirstOffsetX, FirstOffsetY, SecondOffsetX, SecondOffsetY;

        public bool IsZero => FirstAdvance == 0 && SecondAdvance == 0 &&
                              FirstOffsetX == 0 && FirstOffsetY == 0 && SecondOffsetX == 0 && SecondOffsetY == 0;
    }

    /// <summary>
    /// Pair kerning from a font. Uses the OpenType GPOS 'kern' feature when present (most modern fonts, e.g. Roboto),
    /// otherwise the legacy TrueType 'kern' table (e.g. Futura, Garamond). Within a lookup only the first subtable that
    /// matches a pair is applied, as the OpenType spec requires.
    /// </summary>
    internal sealed class GlyphKerning
    {
        readonly Typeface m_Typeface;
        readonly GPOS m_Gpos;
        readonly List<ushort> m_KernLookups = new List<ushort>();
        readonly bool m_UseLegacyKernTable;
        readonly PairBuffer m_Buffer;
        readonly Dictionary<uint, PairAdjustment> m_Cache = new Dictionary<uint, PairAdjustment>();

        public bool HasKerning => m_KernLookups.Count > 0 || m_UseLegacyKernTable;

        /// <param name="fontData">Raw font file, used to detect a legacy 'kern' table (Typography throws if it is missing).</param>
        public GlyphKerning(Typeface typeface, byte[] fontData)
        {
            m_Typeface = typeface;
            m_Buffer = new PairBuffer(typeface);

            try
            {
                m_Gpos = typeface.GPOSTable;
                if (m_Gpos != null) CollectKernLookups();
            }
            catch (Exception)
            {
                m_KernLookups.Clear(); // malformed GPOS: fall back to the kern table, or no kerning
            }

            m_UseLegacyKernTable = m_KernLookups.Count == 0 && HasTable(fontData, "kern");
        }

        public PairAdjustment GetAdjustment(ushort first, ushort second)
        {
            uint key = ((uint)first << 16) | second;
            if (m_Cache.TryGetValue(key, out var cached)) return cached;

            var result = new PairAdjustment();
            try
            {
                if (m_KernLookups.Count > 0) result = FromGpos(first, second);
                else if (m_UseLegacyKernTable) result.FirstAdvance = m_Typeface.GetKernDistance(first, second);
            }
            catch (Exception)
            {
                result = new PairAdjustment(); // a broken table must not break text generation
            }

            m_Cache[key] = result;
            return result;
        }

        PairAdjustment FromGpos(ushort first, ushort second)
        {
            var total = new PairAdjustment();
            foreach (var lookupIndex in m_KernLookups)
            {
                var subTables = m_Gpos.LookupList[lookupIndex].SubTables;
                if (subTables == null) continue;

                foreach (var subTable in subTables)
                {
                    m_Buffer.Reset(first, second);
                    subTable.DoGlyphPosition(m_Buffer, 0, 2);
                    if (!m_Buffer.Touched) continue;

                    total.FirstAdvance += m_Buffer.Advance0;
                    total.SecondAdvance += m_Buffer.Advance1;
                    total.FirstOffsetX += m_Buffer.OffsetX0;
                    total.FirstOffsetY += m_Buffer.OffsetY0;
                    total.SecondOffsetX += m_Buffer.OffsetX1;
                    total.SecondOffsetY += m_Buffer.OffsetY1;
                    break; // first matching subtable wins
                }
            }
            return total;
        }

        /// <summary>Lookups of the 'kern' feature for Latin (or the default script), in lookup-list order.</summary>
        void CollectKernLookups()
        {
            var scriptList = m_Gpos.ScriptList;
            var featureList = m_Gpos.FeatureList;
            if (scriptList == null || featureList == null || featureList.featureTables == null) return;

            var preferred = new[] { "latn", "DFLT" };
            object chosen = null;
            foreach (var tag in preferred)
            {
                foreach (var script in scriptList.Values)
                {
                    if (script.ScriptTagName == tag) { chosen = script; break; }
                }
                if (chosen != null) break;
            }

            var indices = new SortedSet<ushort>();
            foreach (var script in scriptList.Values)
            {
                if (chosen != null && !ReferenceEquals(script, chosen)) continue;

                var lang = script.defaultLang ?? (script.langSysTables != null && script.langSysTables.Length > 0 ? script.langSysTables[0] : null);
                if (lang?.featureIndexList == null) continue;

                foreach (var featureIndex in lang.featureIndexList)
                {
                    if (featureIndex >= featureList.featureTables.Length) continue;
                    var feature = featureList.featureTables[featureIndex];
                    if (feature.TagName != "kern" || feature.LookupListIndices == null) continue;
                    foreach (var lookupIndex in feature.LookupListIndices)
                    {
                        if (lookupIndex < m_Gpos.LookupList.Count) indices.Add(lookupIndex);
                    }
                }
                if (chosen == null) break; // no Latin/default script: use the first one
            }
            m_KernLookups.AddRange(indices);
        }

        /// <summary>Checks the sfnt table directory for a table tag.</summary>
        static bool HasTable(byte[] data, string tag)
        {
            if (data == null || data.Length < 12 || tag.Length != 4) return false;
            int numTables = (data[4] << 8) | data[5];
            for (int i = 0; i < numTables; i++)
            {
                int record = 12 + i * 16;
                if (record + 4 > data.Length) return false;
                if (data[record] == tag[0] && data[record + 1] == tag[1] && data[record + 2] == tag[2] && data[record + 3] == tag[3])
                    return true;
            }
            return false;
        }

        /// <summary>Two-glyph buffer that records what a GPOS subtable does to the pair.</summary>
        sealed class PairBuffer : IGlyphPositions
        {
            readonly Typeface m_Typeface;
            ushort m_Glyph0, m_Glyph1;
            short m_Width0, m_Width1;

            public bool Touched;
            public short Advance0, Advance1, OffsetX0, OffsetY0, OffsetX1, OffsetY1;

            public PairBuffer(Typeface typeface) { m_Typeface = typeface; }

            public void Reset(ushort first, ushort second)
            {
                m_Glyph0 = first;
                m_Glyph1 = second;
                m_Width0 = (short)m_Typeface.GetAdvanceWidthFromGlyphIndex(first);
                m_Width1 = (short)m_Typeface.GetAdvanceWidthFromGlyphIndex(second);
                Touched = false;
                Advance0 = Advance1 = OffsetX0 = OffsetY0 = OffsetX1 = OffsetY1 = 0;
            }

            public int Count => 2;

            public GlyphClassKind GetGlyphClassKind(int index) => m_Typeface.GetGlyph(index == 0 ? m_Glyph0 : m_Glyph1).GlyphClass;

            public void AppendGlyphOffset(int index, short appendOffsetX, short appendOffsetY)
            {
                Touched = true;
                if (index == 0) { OffsetX0 += appendOffsetX; OffsetY0 += appendOffsetY; }
                else { OffsetX1 += appendOffsetX; OffsetY1 += appendOffsetY; }
            }

            public void AppendGlyphAdvance(int index, short appendAdvX, short appendAdvY)
            {
                Touched = true;
                if (index == 0) Advance0 += appendAdvX;
                else Advance1 += appendAdvX;
            }

            public ushort GetGlyph(int index, out short advW)
            {
                advW = index == 0 ? m_Width0 : m_Width1;
                return index == 0 ? m_Glyph0 : m_Glyph1;
            }

            public ushort GetGlyph(int index, out ushort inputOffset, out short offsetX, out short offsetY, out short advW)
            {
                inputOffset = (ushort)index;
                GetOffset(index, out offsetX, out offsetY);
                return GetGlyph(index, out advW);
            }

            public void GetOffset(int index, out short offsetX, out short offsetY)
            {
                offsetX = index == 0 ? OffsetX0 : OffsetX1;
                offsetY = index == 0 ? OffsetY0 : OffsetY1;
            }
        }
    }
}
