using System.Collections.Generic;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEngine;

namespace Softgraph.TextMesh3D.Editor
{
    /// <summary>
    /// Auto Apply (live preview): regenerates TextMesh3D objects shortly after their settings are edited in the
    /// inspector or the TextMesh3D window, instead of requiring Apply.
    /// </summary>
    /// <remarks>
    /// - Debounced: edits made in quick succession (typing, dragging a value) regenerate once, after
    ///   <see cref="DelaySeconds"/> without further changes.
    /// - Undo: the regeneration is merged into the undo step of the edit that triggered it, so one Undo reverts the
    ///   setting and its output together (no step where the output doesn't match the settings).
    /// - The on/off choice is a per-user editor preference, not stored in the scene.
    /// </remarks>
    public static class TextMesh3DAutoApply
    {
        const string k_PrefKey = "Softgraph.TextMesh3D.AutoApply";

        /// <summary>Seconds without further edits before regenerating.</summary>
        public const double DelaySeconds = 0.25;

        internal static readonly GUIContent ToggleLabel = new GUIContent("Auto Apply",
            "Regenerate automatically shortly after a setting changes. Turn off for very long text or slow fonts and press Apply instead.");

        static readonly HashSet<TextMesh3DContainer> s_Pending = new HashSet<TextMesh3DContainer>();
        static double s_Deadline;
        static int s_UndoGroup = -1;
        static bool s_Hooked;

        /// <summary>Whether settings changes regenerate automatically (editor preference, on by default).</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(k_PrefKey, true);
            set
            {
                if (value == Enabled) return;
                EditorPrefs.SetBool(k_PrefKey, value);
                if (!value) Cancel();
            }
        }

        /// <summary>True while a regeneration is waiting for the debounce delay.</summary>
        public static bool IsPending => s_Pending.Count > 0;

        /// <summary>Regenerates the containers after the debounce delay (restarted by every call). No-op when disabled.</summary>
        public static void Schedule(IEnumerable<Object> containers)
        {
            if (!Enabled) return;

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

        /// <summary>Drops pending regenerations (e.g. Apply was pressed, which regenerates right away).</summary>
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

        /// <summary>Removes containers from the pending set, e.g. because they are being regenerated right now.</summary>
        static void Remove(IEnumerable<Object> containers)
        {
            foreach (var obj in containers)
            {
                if (obj is TextMesh3DContainer container) s_Pending.Remove(container);
            }
            if (s_Pending.Count == 0) Cancel();
        }

        static void Update()
        {
            if (EditorApplication.timeSinceStartup >= s_Deadline) Flush();
        }

        /// <summary>"Auto Apply" toggle plus Apply button, shared by the inspector and the TextMesh3D window.</summary>
        internal static void DrawApplyControls(Object[] targets, string applyLabel)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft(ToggleLabel, Enabled, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck())
                {
                    Enabled = enabled;
                    if (enabled) Schedule(targets); // bring the output up to date with the current settings
                }

                if (GUILayout.Button(applyLabel))
                {
                    Remove(targets); // regenerating now; a pending one would only repeat the work
                    foreach (var t in targets)
                    {
                        if (t is TextMesh3DContainer container && container != null) container.Generate();
                    }
                }
            }
        }
    }
}
