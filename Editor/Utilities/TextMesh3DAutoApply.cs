using System.Collections.Generic;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;

namespace Softgraph.TextMesh3D.Editor
{
    /// <summary>
    /// Live preview: regenerates TextMesh3D objects shortly after their settings are edited in the inspector or the
    /// TextMesh3D window. Always on; there is no Apply button.
    /// </summary>
    /// <remarks>
    /// - Debounced: edits made in quick succession (typing, dragging a value) regenerate once, after
    ///   <see cref="DelaySeconds"/> without further changes.
    /// - Undo: the regeneration is merged into the undo step of the edit that triggered it, so one Undo reverts the
    ///   setting and its output together (no step where the output doesn't match the settings).
    /// - Text that has no output yet (component added by hand, scene saved without output) is generated when the
    ///   inspector or window shows it.
    /// </remarks>
    public static class TextMesh3DAutoApply
    {
        /// <summary>Seconds without further edits before regenerating.</summary>
        public const double DelaySeconds = 0.25;

        static readonly HashSet<TextMesh3DContainer> s_Pending = new HashSet<TextMesh3DContainer>();
        static double s_Deadline;
        static int s_UndoGroup = -1;
        static bool s_Hooked;

        /// <summary>True while a regeneration is waiting for the debounce delay.</summary>
        public static bool IsPending => s_Pending.Count > 0;

        /// <summary>Regenerates the containers after the debounce delay (restarted by every call).</summary>
        public static void Schedule(IEnumerable<Object> containers)
        {
            foreach (var obj in containers)
            {
                if (obj is TextMesh3DContainer container && container != null) s_Pending.Add(container);
            }
            if (s_Pending.Count == 0) return;

            // Remember the undo step of the first edit; the regeneration is collapsed into it.
            if (s_UndoGroup < 0) s_UndoGroup = Undo.GetCurrentGroup();
            s_Deadline = EditorApplication.timeSinceStartup + DelaySeconds;

            if (!s_Hooked)
            {
                EditorApplication.update += Update;
                Undo.undoRedoPerformed += OnUndoRedo;
                s_Hooked = true;
            }
        }

        /// <summary>Regenerates everything pending now, without waiting for the delay.</summary>
        public static void Flush()
        {
            if (s_Pending.Count == 0)
            {
                Cancel();
                return;
            }

            var containers = new List<TextMesh3DContainer>(s_Pending);
            int undoGroup = s_UndoGroup;
            Cancel();

            foreach (var container in containers)
            {
                if (container != null) container.Generate(); // destroyed meanwhile: Unity's == null is true
            }

            if (undoGroup >= 0 && !Application.isPlaying)
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>Drops pending regenerations.</summary>
        public static void Cancel()
        {
            s_Pending.Clear();
            s_UndoGroup = -1;
            if (s_Hooked)
            {
                EditorApplication.update -= Update;
                Undo.undoRedoPerformed -= OnUndoRedo;
                s_Hooked = false;
            }
        }

        // Undoing an edit before it was applied restores the settings the current output was made from,
        // so the pending regeneration is no longer needed (and must not land in the wrong undo step).
        static void OnUndoRedo() => Cancel();

        static void Update()
        {
            if (EditorApplication.timeSinceStartup >= s_Deadline) Flush();
        }

        /// <summary>Schedules the containers that have text but no generated output yet.</summary>
        public static void ScheduleIfMissingOutput(IEnumerable<Object> containers)
        {
            var missing = new List<Object>();
            foreach (var obj in containers)
            {
                if (obj is TextMesh3DContainer container && container != null
                    && !string.IsNullOrEmpty(container.Text) && !TextMesh3DPrefabExporter.HasOutput(container)
                    && !EditorUtility.IsPersistent(container)) // not a prefab asset in the Project window
                {
                    missing.Add(container);
                }
            }
            if (missing.Count > 0) Schedule(missing);
        }
    }
}
