using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Softgraph.TextMesh3D.Runtime;
using UnityEngine;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>
    /// Golden-master tests: record what the generator produces today and fail if it changes.
    ///
    /// - First run: no baseline exists, so it is written to Tests/Editor/Baselines and the test passes with a warning.
    ///   Commit the baseline files.
    /// - Later runs: output is compared with the baseline. On mismatch a "*.received.txt" file is written next
    ///   to it so you can diff the two.
    /// - Intended change (e.g. fixing glyph spacing): review the diff, then delete the baseline (or replace it
    ///   with the .received file) and re-run to record the new behaviour.
    ///
    /// Only order-independent measurements are stored (counts, bounds, areas, positions), so the snapshots
    /// don't depend on HashSet iteration order inside the triangulator.
    /// </summary>
    class TextMesh3DSnapshotTests
    {
        static string BaselineDir => Path.GetFullPath(PackageRoot + "/Tests/Editor/Baselines");

        // 20.48 = Roboto's UnitsPerEm / 100, i.e. the scale the package used before sizes were em-based.
        // Keeps magnitudes large enough for 3-decimal rounding and makes old/new baselines directly comparable.
        const float k_Size = 20.48f;

        public static IEnumerable<TestCaseData> Cases()
        {
            yield return Case("Mesh_HelloWorld", "Hello World", TextMesh3DContainer.OutputMode.Mesh);
            yield return Case("Mesh_Holes", "ABOabdeg8", TextMesh3DContainer.OutputMode.Mesh);
            yield return Case("Mesh_MultiContour", "i?%&@", TextMesh3DContainer.OutputMode.Mesh);
            yield return Case("Mesh_Size2_Extrude5", "Rq", TextMesh3DContainer.OutputMode.Mesh, extrusion: 5f, size: 2f * k_Size);
            yield return Case("Surface_Mixed", "AaBb 12", TextMesh3DContainer.OutputMode.Surface);
            yield return Case("Spline_Mixed", "AaBb gR", TextMesh3DContainer.OutputMode.Spline);
            yield return Case("Spline_Spacing", "WAVE", TextMesh3DContainer.OutputMode.Spline, spacing: 1.5f);
            yield return Case("Spline_NarrowGlyphs", "il1 jt", TextMesh3DContainer.OutputMode.Spline);
        }

        static TestCaseData Case(string name, string text, TextMesh3DContainer.OutputMode mode, float extrusion = 1f, float size = k_Size, float spacing = 0f) =>
            new TestCaseData(name, text, mode, extrusion, size, spacing).SetName("Snapshot_" + name);

        [TestCaseSource(nameof(Cases))]
        public void MatchesBaseline(string name, string text, TextMesh3DContainer.OutputMode mode, float extrusion, float size, float spacing)
        {
            var container = CreateContainer(text, mode, extrusion, size, spacing);
            string received;
            try
            {
                received = Describe(Generate(container), mode);
            }
            finally
            {
                TextMesh3DTestUtility.Destroy(container);
            }

            Directory.CreateDirectory(BaselineDir);
            var baselinePath = Path.Combine(BaselineDir, name + ".txt");
            var receivedPath = Path.Combine(BaselineDir, name + ".received.txt");

            if (!File.Exists(baselinePath))
            {
                File.WriteAllText(baselinePath, received);
                Debug.LogWarning($"TextMesh3D snapshot '{name}': no baseline existed, recorded one at {baselinePath}. Review and commit it.");
                Assert.Pass("Baseline recorded.");
            }

            var expected = File.ReadAllText(baselinePath);
            var diff = FirstDifference(expected, received);
            if (diff != null)
            {
                File.WriteAllText(receivedPath, received);
                Assert.Fail($"Output differs from baseline '{name}': {diff}\nFull output written to {receivedPath}");
            }

            if (File.Exists(receivedPath)) File.Delete(receivedPath);
        }

        // ---------- Snapshot format: one line per glyph, values rounded to stable precision ----------

        static string Describe(List<Transform> glyphs, TextMesh3DContainer.OutputMode mode)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"mode={mode} glyphs={glyphs.Count}");
            foreach (var g in glyphs)
            {
                sb.Append($"{g.name} | pos={F(g.localPosition)}");

                if (mode == TextMesh3DContainer.OutputMode.Spline)
                {
                    var sc = GetSplines(g);
                    var knots = new List<string>();
                    var min = Vector3.positiveInfinity;
                    var max = Vector3.negativeInfinity;
                    if (sc != null)
                    {
                        foreach (var s in sc.Splines)
                        {
                            knots.Add(s.Count.ToString());
                            for (int k = 0; k < s.Count; k++)
                            {
                                Vector3 p = s[k].Position;
                                min = Vector3.Min(min, p);
                                max = Vector3.Max(max, p);
                            }
                        }
                    }
                    sb.Append($" | contours={knots.Count} knots=[{string.Join(",", knots)}]");
                    if (knots.Count > 0) sb.Append($" | min={F(min)} max={F(max)}");
                }
                else
                {
                    var mesh = GetMesh(g);
                    if (mesh != null)
                    {
                        MeasureAreas(mesh, out var up, out var down, out var side);
                        sb.Append($" | verts={mesh.vertexCount} tris={mesh.triangles.Length / 3}");
                        sb.Append($" | min={F(mesh.bounds.min)} max={F(mesh.bounds.max)}");
                        sb.Append($" | area up={F(up)} down={F(down)} side={F(side)}");
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string F(float v) => Math.Round(v, 3).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        static string F(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";

        // Decimal values may differ by float noise in the last printed digit (e.g. summation order), so they are
        // compared with a small tolerance. Integers (vertex / triangle / knot counts) and all text must match exactly.
        const double k_DecimalTolerance = 0.002;
        const string k_Decimal = @"-?\d+\.\d+";

        static string FirstDifference(string expected, string received)
        {
            var e = expected.Replace("\r\n", "\n").TrimEnd().Split('\n');
            var r = received.Replace("\r\n", "\n").TrimEnd().Split('\n');
            for (int i = 0; i < Math.Max(e.Length, r.Length); i++)
            {
                var el = i < e.Length ? e[i] : "<missing>";
                var rl = i < r.Length ? r[i] : "<missing>";
                if (!LinesMatch(el, rl)) return $"line {i + 1}\n  expected: {el}\n  received: {rl}";
            }
            return null;
        }

        static bool LinesMatch(string expected, string received)
        {
            if (expected == received) return true;
            if (System.Text.RegularExpressions.Regex.Replace(expected, k_Decimal, "#") != System.Text.RegularExpressions.Regex.Replace(received, k_Decimal, "#")) return false;

            var e = System.Text.RegularExpressions.Regex.Matches(expected, k_Decimal);
            var r = System.Text.RegularExpressions.Regex.Matches(received, k_Decimal);
            for (int i = 0; i < e.Count; i++)
            {
                double a = double.Parse(e[i].Value, System.Globalization.CultureInfo.InvariantCulture);
                double b = double.Parse(r[i].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (Math.Abs(a - b) > k_DecimalTolerance) return false;
            }
            return true;
        }
    }
}
