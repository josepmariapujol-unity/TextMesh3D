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
        readonly byte[] m_FontData;
        readonly int m_GposOffset; // offset of the GPOS table in m_FontData, or -1
        readonly Dictionary<uint, PairAdjustment> m_Cache = new Dictionary<uint, PairAdjustment>();

        public bool HasKerning => m_KernLookups.Count > 0 || m_UseLegacyKernTable;

        /// <param name="fontData">Raw font file, used to detect a legacy 'kern' table (Typography throws if it is missing).</param>
        public GlyphKerning(Typeface typeface, byte[] fontData)
        {
            m_Typeface = typeface;
            m_FontData = fontData;
            m_GposOffset = FindTable(fontData, "GPOS");

            try
            {
                m_Gpos = typeface.GPOSTable;
                if (m_Gpos != null && m_GposOffset >= 0) CollectKernLookups();
            }
            catch (Exception)
            {
                m_KernLookups.Clear(); // malformed GPOS: fall back to the kern table, or no kerning
            }

            m_UseLegacyKernTable = m_KernLookups.Count == 0 && FindTable(fontData, "kern") >= 0;
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

        // ---------- GPOS pair positioning, read directly from the font file ----------
        // Typography decides which lookups belong to the 'kern' feature (above), but the pair adjustments are read
        // here: Typography's PairPos format 2 skips glyphs that their ClassDef doesn't list, while the OpenType spec
        // says those are class 0. Roboto keeps most pairs in class 0 rows ("AV", "AT", ...), so they lost their kerning.

        PairAdjustment FromGpos(ushort first, ushort second)
        {
            var total = new PairAdjustment();
            var d = m_FontData;
            int lookupList = m_GposOffset + U16(d, m_GposOffset + 8);

            foreach (var lookupIndex in m_KernLookups)
            {
                int lookup = lookupList + U16(d, lookupList + 2 + 2 * lookupIndex);
                int type = U16(d, lookup);
                int subTableCount = U16(d, lookup + 4);

                for (int s = 0; s < subTableCount; s++)
                {
                    int subTable = lookup + U16(d, lookup + 6 + 2 * s);
                    int subType = type;
                    if (type == 9) // extension: the real subtable is at a 32-bit offset
                    {
                        subType = U16(d, subTable + 2);
                        subTable += (int)U32(d, subTable + 4);
                    }
                    if (subType != 2) continue; // only pair adjustment is kerning

                    if (TryPairPos(subTable, first, second, ref total)) break; // first matching subtable wins
                }
            }
            return total;
        }

        /// <summary>PairPos formats 1 (glyph pairs) and 2 (class pairs). Returns false when the subtable doesn't apply.</summary>
        bool TryPairPos(int subTable, ushort first, ushort second, ref PairAdjustment total)
        {
            var d = m_FontData;
            int format = U16(d, subTable);
            int coverage = subTable + U16(d, subTable + 2);
            int valueFormat1 = U16(d, subTable + 4);
            int valueFormat2 = U16(d, subTable + 6);
            int size1 = ValueRecordSize(valueFormat1);
            int size2 = ValueRecordSize(valueFormat2);

            int coverageIndex = CoverageIndex(coverage, first);
            if (coverageIndex < 0) return false;

            int record;
            if (format == 1)
            {
                int pairSetCount = U16(d, subTable + 8);
                if (coverageIndex >= pairSetCount) return false;
                int pairSet = subTable + U16(d, subTable + 10 + 2 * coverageIndex);
                int count = U16(d, pairSet);
                int recordSize = 2 + size1 + size2;

                // PairValueRecords are sorted by second glyph
                int lo = 0, hi = count - 1;
                record = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int at = pairSet + 2 + mid * recordSize;
                    int glyph = U16(d, at);
                    if (glyph == second) { record = at + 2; break; }
                    if (glyph < second) lo = mid + 1; else hi = mid - 1;
                }
                if (record < 0) return false; // first glyph covered but no pair: try the next subtable
            }
            else if (format == 2)
            {
                int classDef1 = subTable + U16(d, subTable + 8);
                int classDef2 = subTable + U16(d, subTable + 10);
                int class1Count = U16(d, subTable + 12);
                int class2Count = U16(d, subTable + 14);
                int class1 = ClassOf(classDef1, first);   // glyphs not listed are class 0
                int class2 = ClassOf(classDef2, second);
                if (class1 >= class1Count || class2 >= class2Count) return false;
                record = subTable + 16 + (class1 * class2Count + class2) * (size1 + size2);
            }
            else
            {
                return false;
            }

            ReadValueRecord(record, valueFormat1, out var xPlacement1, out var yPlacement1, out var xAdvance1);
            ReadValueRecord(record + size1, valueFormat2, out var xPlacement2, out var yPlacement2, out var xAdvance2);
            total.FirstAdvance += xAdvance1;
            total.FirstOffsetX += xPlacement1;
            total.FirstOffsetY += yPlacement1;
            total.SecondAdvance += xAdvance2;
            total.SecondOffsetX += xPlacement2;
            total.SecondOffsetY += yPlacement2;
            return true;
        }

        /// <summary>Index of the glyph in a Coverage table, or -1.</summary>
        int CoverageIndex(int table, ushort glyph)
        {
            var d = m_FontData;
            int format = U16(d, table);
            int count = U16(d, table + 2);
            int lo = 0, hi = count - 1;
            if (format == 1)
            {
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int g = U16(d, table + 4 + 2 * mid);
                    if (g == glyph) return mid;
                    if (g < glyph) lo = mid + 1; else hi = mid - 1;
                }
            }
            else if (format == 2)
            {
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int range = table + 4 + 6 * mid;
                    int start = U16(d, range), end = U16(d, range + 2);
                    if (glyph < start) hi = mid - 1;
                    else if (glyph > end) lo = mid + 1;
                    else return U16(d, range + 4) + glyph - start;
                }
            }
            return -1;
        }

        /// <summary>Class of the glyph in a ClassDef table; 0 when it isn't listed (OpenType spec).</summary>
        int ClassOf(int table, ushort glyph)
        {
            var d = m_FontData;
            int format = U16(d, table);
            if (format == 1)
            {
                int startGlyph = U16(d, table + 2);
                int count = U16(d, table + 4);
                int i = glyph - startGlyph;
                return i >= 0 && i < count ? U16(d, table + 6 + 2 * i) : 0;
            }
            if (format == 2)
            {
                int count = U16(d, table + 2);
                int lo = 0, hi = count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int range = table + 4 + 6 * mid;
                    int start = U16(d, range), end = U16(d, range + 2);
                    if (glyph < start) hi = mid - 1;
                    else if (glyph > end) lo = mid + 1;
                    else return U16(d, range + 4);
                }
            }
            return 0;
        }

        // ValueRecord: XPlacement, YPlacement, XAdvance, YAdvance, then 4 device-table offsets, each present if its bit is set.
        static int ValueRecordSize(int valueFormat)
        {
            int bits = valueFormat & 0xFF, size = 0;
            for (; bits != 0; bits >>= 1) size += (bits & 1) * 2;
            return size;
        }

        void ReadValueRecord(int at, int valueFormat, out short xPlacement, out short yPlacement, out short xAdvance)
        {
            var d = m_FontData;
            xPlacement = yPlacement = xAdvance = 0;
            if ((valueFormat & 0x0001) != 0) { xPlacement = (short)U16(d, at); at += 2; }
            if ((valueFormat & 0x0002) != 0) { yPlacement = (short)U16(d, at); at += 2; }
            if ((valueFormat & 0x0004) != 0) { xAdvance = (short)U16(d, at); }
            // YAdvance and device tables (hinting) don't apply to horizontal layout of outlines
        }

        static int U16(byte[] d, int offset) => (d[offset] << 8) | d[offset + 1];
        static uint U32(byte[] d, int offset) => ((uint)d[offset] << 24) | ((uint)d[offset + 1] << 16) | ((uint)d[offset + 2] << 8) | d[offset + 3];

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

        /// <summary>Offset of a table in the sfnt table directory, or -1 if the font has no such table.</summary>
        static int FindTable(byte[] data, string tag)
        {
            if (data == null || data.Length < 12 || tag.Length != 4) return -1;
            int numTables = (data[4] << 8) | data[5];
            for (int i = 0; i < numTables; i++)
            {
                int record = 12 + i * 16;
                if (record + 16 > data.Length) return -1;
                if (data[record] == tag[0] && data[record + 1] == tag[1] && data[record + 2] == tag[2] && data[record + 3] == tag[3])
                {
                    long offset = U32(data, record + 8);
                    return offset < data.Length ? (int)offset : -1;
                }
            }
            return -1;
        }
    }
}
