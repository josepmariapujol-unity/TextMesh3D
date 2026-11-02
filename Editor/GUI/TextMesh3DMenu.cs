using UnityEditor;
using UnityEngine;
using Softgraph.TextMesh3D.Runtime;

namespace Softgraph.TextMesh3D.Editor
{
    public class TextMesh3DMenu : UnityEditor.Editor
    {
        const string k_MenuPath = "GameObject/3D Object/Text - TextMesh3D";
        const int k_MenuPriority = 30;

        // Defaults ship with the package, so load them by exact path (a name search could pick up a project asset).
        const string k_PackageRoot = "Packages/com.softgraph.textmesh3d";
        const string k_DefaultMaterialPath = k_PackageRoot + "/Editor/Resources/Materials/FontMaterial.mat";
        const string k_DefaultFontPath = k_PackageRoot + "/Editor/Resources/Fonts/Roboto.ttf"; // Apache 2.0, freely redistributable

        internal static GameObject CreateTextGameObject(GameObject parent)
        {
            var name = GameObjectUtility.GetUniqueNameForSibling(parent != null ? parent.transform : null, "Text (TextMesh3D)");
            var gameObject = ObjectFactory.CreateGameObject(name, typeof(TextMesh3DContainer));

            // Parents to the context object and positions it according to the user's scene-view placement preference
            ObjectFactory.PlaceGameObject(gameObject, parent);

            var container = gameObject.GetComponent<TextMesh3DContainer>();
            container.Text = "New Text";
            container.Font = LoadDefault<Font>(k_DefaultFontPath);
            container.Output = TextMesh3DContainer.OutputMode.Mesh;
            container.Size = 1f;
            container.Material = LoadDefault<Material>(k_DefaultMaterialPath);
            container.Extrusion = 0.2f; // m_Size is the em height in Unity units, so 1 => capitals ~0.7 units tall
            container.Spacing = 0f;
            container.GroupOutput = false;

            // Show the text right away instead of an empty object
            if (container.Font != null)
            {
                container.Generate();
            }

            Selection.activeGameObject = gameObject;
            return gameObject;
        }

        static T LoadDefault<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogWarning($"TextMesh3D: default {typeof(T).Name} not found at '{path}'. Assign one in the inspector.");
            }
            return asset;
        }

        [MenuItem(k_MenuPath, false, k_MenuPriority)]
        static void CreateNewText(MenuCommand menuCommand)
        {
            CreateTextGameObject(menuCommand.context as GameObject);
            ActiveEditorTracker.sharedTracker.RebuildIfNecessary();
        }
    }
}
