using System.Collections.Generic;
using NUnit.Framework;
using Softgraph.TextMesh3D.Editor;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;
using static Softgraph.TextMesh3D.Tests.TextMesh3DTestUtility;

namespace Softgraph.TextMesh3D.Tests
{
    /// <summary>Live preview (always on): debounced regeneration after edits, merged into the edit's undo step.</summary>
    class TextMesh3DAutoApplyTests
    {
        readonly List<TextMesh3DContainer> m_Created = new List<TextMesh3DContainer>();

        TextMesh3DContainer Create(string text)
        {
            var c = CreateContainer(text, TextMesh3DContainer.OutputMode.Spline);
            m_Created.Add(c);
            return c;
        }

        static int GlyphCount(TextMesh3DContainer c) => c.GeneratedRoot != null ? c.GeneratedRoot.transform.childCount : 0;

        [TearDown]
        public void TearDown()
        {
            TextMesh3DAutoApply.Cancel();
            foreach (var c in m_Created) TextMesh3DTestUtility.Destroy(c);
            m_Created.Clear();
        }

        [Test]
        public void Schedule_IsDebounced_FlushRegenerates()
        {
            var c = Create("AB");
            c.Generate();

            c.Text = "ABC";
            TextMesh3DAutoApply.Schedule(new Object[] { c });

            Assert.IsTrue(TextMesh3DAutoApply.IsPending);
            Assert.AreEqual(2, GlyphCount(c), "Nothing regenerates before the delay");

            TextMesh3DAutoApply.Flush();

            Assert.IsFalse(TextMesh3DAutoApply.IsPending);
            Assert.AreEqual(3, GlyphCount(c));
        }

        [Test]
        public void ManyEdits_RegenerateOnce()
        {
            var c = Create("A");
            c.Generate();
            var firstRoot = c.GeneratedRoot;

            foreach (var text in new[] { "AB", "ABC", "ABCD" })
            {
                c.Text = text;
                TextMesh3DAutoApply.Schedule(new Object[] { c });
            }
            Assert.AreSame(firstRoot, c.GeneratedRoot, "Edits within the delay must not regenerate");

            TextMesh3DAutoApply.Flush();
            Assert.AreEqual(4, GlyphCount(c));
        }

        [Test]
        public void TextWithoutOutput_IsGeneratedWhenShown()
        {
            var withoutOutput = Create("AB");             // never generated, like a component added by hand
            var withOutput = Create("CD");
            withOutput.Generate();
            var root = withOutput.GeneratedRoot;

            TextMesh3DAutoApply.ScheduleIfMissingOutput(new Object[] { withoutOutput, withOutput });
            TextMesh3DAutoApply.Flush();

            Assert.AreEqual(2, GlyphCount(withoutOutput));
            Assert.AreSame(root, withOutput.GeneratedRoot, "Text that already has output must not be regenerated just by showing it");
        }

        [Test]
        public void EmptyText_IsNotScheduledWhenShown()
        {
            var c = Create("");
            TextMesh3DAutoApply.ScheduleIfMissingOutput(new Object[] { c });
            Assert.IsFalse(TextMesh3DAutoApply.IsPending);
        }

        [Test]
        public void DestroyedBeforeFlush_IsSkipped()
        {
            var c = Create("AB");
            TextMesh3DAutoApply.Schedule(new Object[] { c });
            Object.DestroyImmediate(c.gameObject);

            Assert.DoesNotThrow(TextMesh3DAutoApply.Flush);
        }

        [Test]
        public void OneUndo_RevertsEditAndItsOutputTogether()
        {
            var c = Create("AB");
            Undo.IncrementCurrentGroup();
            c.Generate();
            Undo.IncrementCurrentGroup();

            // An inspector edit: recorded, then scheduled
            Undo.RecordObject(c, "Change Text");
            c.Text = "ABCD";
            TextMesh3DAutoApply.Schedule(new Object[] { c });
            TextMesh3DAutoApply.Flush();
            Assert.AreEqual(4, GlyphCount(c));
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo();

            Assert.AreEqual("AB", c.Text);
            Assert.AreEqual(2, GlyphCount(c), "The output must be reverted in the same undo step as the text");
        }

        [Test]
        public void UndoBeforeFlush_CancelsPending()
        {
            var c = Create("AB");
            Undo.IncrementCurrentGroup();
            c.Generate();
            Undo.IncrementCurrentGroup();

            Undo.RecordObject(c, "Change Text");
            c.Text = "ABCD";
            TextMesh3DAutoApply.Schedule(new Object[] { c });
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo(); // back to "AB", which the current output already shows

            Assert.IsFalse(TextMesh3DAutoApply.IsPending);
            Assert.AreEqual("AB", c.Text);
            Assert.AreEqual(2, GlyphCount(c));
        }
    }
}
