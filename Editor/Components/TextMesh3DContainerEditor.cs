using UnityEngine;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;

namespace Softgraph.TextMesh3D.Editor
{
    [CustomEditor(typeof(TextMesh3DContainer))]
    [CanEditMultipleObjects]
    public class TextMesh3DContainerEditor : UnityEditor.Editor
    {
        SerializedProperty m_Text;
        SerializedProperty m_Font;
        SerializedProperty m_Output;
        SerializedProperty m_Extrusion;
        SerializedProperty m_Material;
        SerializedProperty m_Spacing;
        SerializedProperty m_Kerning;
        SerializedProperty m_GroupOutput;
        SerializedProperty m_Size;
        SerializedProperty m_CurveSegments;
        SerializedProperty m_Plane;
        SerializedProperty m_LineSpacing;

        bool showInfo;

        public void OnEnable()
        {
            m_Text = serializedObject.FindProperty("m_Text");
            m_Font = serializedObject.FindProperty("m_Font");
            m_Output = serializedObject.FindProperty("m_Output");
            m_Extrusion = serializedObject.FindProperty("m_Extrusion");
            m_Material = serializedObject.FindProperty("m_Material");
            m_Spacing = serializedObject.FindProperty("m_Spacing");
            m_Kerning = serializedObject.FindProperty("m_Kerning");
            m_GroupOutput = serializedObject.FindProperty("m_GroupOutput");
            m_Size = serializedObject.FindProperty("m_Size");
            m_CurveSegments = serializedObject.FindProperty("m_CurveSegments");
            m_Plane = serializedObject.FindProperty("m_Plane");
            m_LineSpacing = serializedObject.FindProperty("m_LineSpacing");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Text, new GUIContent());
            EditorGUILayout.PropertyField(m_Font);
            EditorGUILayout.PropertyField(m_Output);
            EditorGUILayout.PropertyField(m_Plane);
            EditorGUILayout.PropertyField(m_Size);

            if (m_Output.enumValueIndex != 0)
            {
                EditorGUILayout.PropertyField(m_Material);
            }
            if (m_Output.enumValueIndex == 2)
            {
                EditorGUILayout.PropertyField(m_Extrusion);
            }

            showInfo = EditorGUILayout.Foldout(showInfo , "Advanced");

            if(showInfo)
            {
                EditorGUILayout.PropertyField(m_Spacing);
                EditorGUILayout.PropertyField(m_LineSpacing);
                EditorGUILayout.PropertyField(m_Kerning);
                if (m_Output.enumValueIndex != 0) // Surface / Mesh only
                {
                    EditorGUILayout.PropertyField(m_GroupOutput);
                    EditorGUILayout.PropertyField(m_CurveSegments);
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button(targets.Length > 1 ? $"Apply ({targets.Length} objects)" : "Apply"))
            {
                foreach (var t in targets)
                {
                    ((TextMesh3DContainer) t).Generate();
                }
            }

            var container = (TextMesh3DContainer) target;
            using (new EditorGUI.DisabledScope(targets.Length != 1 || !TextMesh3DPrefabExporter.HasOutput(container)))
            {
                if (GUILayout.Button(new GUIContent("Save as Prefab...", k_SaveAsPrefabTooltip)))
                {
                    TextMesh3DPrefabExporter.SaveAsPrefabWithDialog(container);
                    GUIUtility.ExitGUI(); // the save dialog breaks the current layout pass
                }
            }
        }

        internal const string k_SaveAsPrefabTooltip =
            "Save the generated text as a prefab with its meshes stored inside, without the TextMesh3D component. Press Apply first.";

        [MenuItem("CONTEXT/TextMesh3DContainer/Save as Prefab...")]
        static void SaveAsPrefabContextMenu(MenuCommand command)
        {
            TextMesh3DPrefabExporter.SaveAsPrefabWithDialog((TextMesh3DContainer) command.context);
        }

        [MenuItem("CONTEXT/TextMesh3DContainer/Save as Prefab...", true)]
        static bool ValidateSaveAsPrefabContextMenu(MenuCommand command)
        {
            return TextMesh3DPrefabExporter.HasOutput(command.context as TextMesh3DContainer);
        }
    }
}
