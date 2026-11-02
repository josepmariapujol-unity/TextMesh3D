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
        SerializedProperty m_GroupOutput;
        SerializedProperty m_Size;
        SerializedProperty m_CurveSegments;

        const string k_Font = "Font";
        const string k_Output = "Splines/Surfaces/Meshes";
        const string k_Extrusion = "Extrusion";
        const string k_Material = "Material";
        const string k_Spacing = "Adding Spacing";
        const string k_GroupOutput = "Group Output";
        const string k_Size = "Font Size";

        bool showInfo;

        public void OnEnable()
        {
            m_Text = serializedObject.FindProperty("m_Text");
            m_Font = serializedObject.FindProperty("m_Font");
            m_Output = serializedObject.FindProperty("m_Output");
            m_Extrusion = serializedObject.FindProperty("m_Extrusion");
            m_Material = serializedObject.FindProperty("m_Material");
            m_Spacing = serializedObject.FindProperty("m_Spacing");
            m_GroupOutput = serializedObject.FindProperty("m_GroupOutput");
            m_Size = serializedObject.FindProperty("m_Size");
            m_CurveSegments = serializedObject.FindProperty("m_CurveSegments");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Text, new GUIContent());
            EditorGUILayout.PropertyField(m_Font, new GUIContent(k_Font));
            EditorGUILayout.PropertyField(m_Output, new GUIContent(k_Output));
            EditorGUILayout.PropertyField(m_Size, new GUIContent(k_Size));

            if (m_Output.enumValueIndex != 0)
            {
                EditorGUILayout.PropertyField(m_Material, new GUIContent(k_Material));
            }
            if (m_Output.enumValueIndex == 2)
            {
                EditorGUILayout.PropertyField(m_Extrusion, new GUIContent(k_Extrusion));
            }

            showInfo = EditorGUILayout.Foldout(showInfo , "Advanced");

            if(showInfo)
            {
                EditorGUILayout.PropertyField(m_Spacing, new GUIContent(k_Spacing));
                if (m_Output.enumValueIndex != 0) // Surface / Mesh only
                {
                    EditorGUILayout.PropertyField(m_GroupOutput, new GUIContent(k_GroupOutput));
                    EditorGUILayout.PropertyField(m_CurveSegments, new GUIContent("Curve Segments"));
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
        }
    }
}
