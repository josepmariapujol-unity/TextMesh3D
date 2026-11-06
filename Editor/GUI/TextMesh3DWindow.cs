using UnityEditor;
using UnityEngine;
using Softgraph.TextMesh3D.Runtime;

namespace Softgraph.TextMesh3D.Editor
{
    /// <summary>Edits the TextMesh3D object that is currently selected (follows the selection).</summary>
    public class TextMesh3DWindow : EditorWindow
    {
        private SerializedObject serializedObject;
        private SerializedProperty m_Text;
        private SerializedProperty m_Font;
        private SerializedProperty m_Output;
        private SerializedProperty m_Extrusion;
        private SerializedProperty m_Material;
        private SerializedProperty m_Spacing;
        private SerializedProperty m_Kerning;
        private SerializedProperty m_GroupOutput;
        private SerializedProperty m_Size;
        private SerializedProperty m_CurveSegments;

        private TextMesh3DContainer targetContainer;

        private bool showInfo;

        [MenuItem("Window/TextMesh3D")]
        public static void OpenWindow()
        {
            var window = GetWindow<TextMesh3DWindow>("TextMesh3D");
            window.minSize = new Vector2(300, 250);
            window.Show();
        }

        private void OnEnable() => BindToSelection();
        private void OnSelectionChange() { BindToSelection(); Repaint(); }
        private void OnHierarchyChange()
        {
            // The edited object may have been deleted (Unity's == null is true for destroyed objects)
            if (targetContainer == null && serializedObject != null) { BindToSelection(); Repaint(); }
        }

        private void BindToSelection()
        {
            var selected = Selection.activeGameObject;
            var container = selected != null ? selected.GetComponentInParent<TextMesh3DContainer>() : null;
            if (container == targetContainer && serializedObject != null) return;

            targetContainer = container;
            serializedObject = container != null ? new SerializedObject(container) : null;
            if (serializedObject == null) return;

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
        }

        private void OnGUI()
        {
            if (targetContainer == null || serializedObject == null || serializedObject.targetObject == null)
            {
                EditorGUILayout.HelpBox("Select a TextMesh3D object to edit it here.", MessageType.Info);
                if (GUILayout.Button("Create TextMesh3D"))
                {
                    TextMesh3DMenu.CreateTextGameObject(null);
                    BindToSelection();
                }
                return;
            }

            EditorGUILayout.LabelField("Editing", targetContainer.name, EditorStyles.boldLabel);
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Text, new GUIContent("Text"));
            EditorGUILayout.PropertyField(m_Font, new GUIContent("Font"));
            EditorGUILayout.PropertyField(m_Output, new GUIContent("Output"));

            EditorGUILayout.PropertyField(m_Size, new GUIContent("Font Size"));

            if (m_Output.enumValueIndex != 0)
            {
                EditorGUILayout.PropertyField(m_Material, new GUIContent("Material"));
            }

            if (m_Output.enumValueIndex == 2)
            {
                EditorGUILayout.PropertyField(m_Extrusion, new GUIContent("Extrusion"));
            }

            showInfo = EditorGUILayout.Foldout(showInfo, "Advanced");

            if (showInfo)
            {
                EditorGUILayout.PropertyField(m_Spacing, new GUIContent("Spacing"));
                EditorGUILayout.PropertyField(m_Kerning, new GUIContent("Kerning"));
                if (m_Output.enumValueIndex != 0) // Surface / Mesh only
                {
                    EditorGUILayout.PropertyField(m_GroupOutput, new GUIContent("Group Output"));
                    EditorGUILayout.PropertyField(m_CurveSegments, new GUIContent("Curve Segments"));
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Apply"))
            {
                targetContainer.Generate();
            }

            using (new EditorGUI.DisabledScope(!TextMesh3DPrefabExporter.HasOutput(targetContainer)))
            {
                if (GUILayout.Button(new GUIContent("Save as Prefab...", TextMesh3DContainerEditor.k_SaveAsPrefabTooltip)))
                {
                    TextMesh3DPrefabExporter.SaveAsPrefabWithDialog(targetContainer);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
