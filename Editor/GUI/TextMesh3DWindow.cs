using UnityEditor;
using UnityEngine;
using Softgraph.TextMesh3D.Runtime;

namespace Softgraph.TextMesh3D.Editor
{
    public class TextMesh3DWindow : EditorWindow
    {
        // Serialized properties for the editor window
        private SerializedObject serializedObject;
        private SerializedProperty m_Text;
        private SerializedProperty m_Font;
        private SerializedProperty m_Output;
        private SerializedProperty m_Extrusion;
        private SerializedProperty m_Material;
        private SerializedProperty m_Spacing;
        private SerializedProperty m_GroupOutput;
        private SerializedProperty m_Size;
        private SerializedProperty m_CurveSegments;

        // A reference to the target TextMesh3DContainer script
        private TextMesh3DContainer targetContainer;

        private bool showInfo;

        [MenuItem("Window/TextMesh3D")]
        public static void OpenWindow()
        {
            // Open the window
            TextMesh3DWindow window = (TextMesh3DWindow)GetWindow(typeof(TextMesh3DWindow));
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            // Optionally find and assign a TextMesh3DContainer in the scene
            targetContainer = FindFirstObjectByType<TextMesh3DContainer>();

            if (targetContainer != null)
            {
                serializedObject = new SerializedObject(targetContainer);

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
        }

        private void OnGUI()
        {
            if (targetContainer == null)
            {
                EditorGUILayout.HelpBox("No TextMesh3DContainer found in the scene.", MessageType.Warning);
                if (GUILayout.Button("Refresh"))
                {
                    targetContainer = FindFirstObjectByType<TextMesh3DContainer>();
                    if (targetContainer != null)
                    {
                        serializedObject = new SerializedObject(targetContainer);
                        OnEnable();
                    }
                }
                return;
            }

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
                EditorGUILayout.PropertyField(m_GroupOutput, new GUIContent("Group Output"));
                if (m_Output.enumValueIndex != 0)
                    EditorGUILayout.PropertyField(m_CurveSegments, new GUIContent("Curve Segments"));
            }

            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Apply"))
            {
                targetContainer.Function();
            }
        }
    }
}
