using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Softgraph.TextMesh3D.Runtime;

namespace Softgraph.TextMesh3D.Editor
{
    public class TextMesh3DMenu : UnityEditor.Editor
    {
        const string k_MenuPath = "GameObject/3D Object/Text - TextMesh3D";
        const string materialName = "FontMaterial";
        const string fontName = "ComicSans";

        private static GameObject CreateTextGameObject(MenuCommand menuCommand)
        {
            var name = GameObjectUtility.GetUniqueNameForSibling(null, "Text (TextMesh3D)");
            var gameObject = ObjectFactory.CreateGameObject(name, typeof(TextMesh3DContainer));

            ObjectFactory.PlaceGameObject(gameObject, menuCommand.context as GameObject);

            var container = gameObject.GetComponent<TextMesh3DContainer>();

            // Load the material from the AssetDatabase
            var materialGuids = AssetDatabase.FindAssets(materialName + " t:Material");
            var fontGuids = AssetDatabase.FindAssets(fontName + " t:Font");

            if (materialGuids.Length == 0)
            {
                Debug.LogError("Material not found in AssetDatabase!");
            }

            string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[0]);
            Debug.Log(materialPath + " - materialPath");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            string fontPath = AssetDatabase.GUIDToAssetPath(fontGuids[0]);
            Debug.Log(fontPath + " - fontPath");
            Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);

            // Load the font and material from the package path

            container.m_Text = "New Text";
            container.m_Font = font;
            container.m_Output = TextMesh3DContainer.Enum.Mesh;
            container.m_Size = 1;
            container.m_Material = material;
            container.m_Extrusion = 0.2f; // m_Size is the em height in Unity units, so 1 => capitals ~0.7 units tall
            container.m_Spacing = 0;
            container.m_GroupOutput = false;

            Selection.activeGameObject = gameObject;
            return gameObject;
        }

        const int k_MenuPriority = 30;

        [MenuItem(k_MenuPath, false, k_MenuPriority)]
        static void CreateNewText(MenuCommand menuCommand)
        {
            var gameObject = CreateTextGameObject(menuCommand);

            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;

            Selection.activeObject = gameObject;
            ActiveEditorTracker.sharedTracker.RebuildIfNecessary();
        }
    }
}
