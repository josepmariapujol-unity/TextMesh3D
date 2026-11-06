using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Softgraph.TextMesh3D.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Softgraph.TextMesh3D.Editor
{
    /// <summary>
    /// Saves the generated output of a <see cref="TextMesh3DContainer"/> as a standalone prefab.
    /// </summary>
    /// <remarks>
    /// The prefab is baked: plain GameObjects with MeshFilter / MeshRenderer (Surface, Mesh) or SplineContainer
    /// (Spline), and no TextMesh3D component, so it does not depend on this package or the font at runtime.
    /// Generated meshes (and any in-memory materials) are copied and stored inside the .prefab file, so the prefab
    /// stays valid when the source text is regenerated or deleted.
    /// </remarks>
    public static class TextMesh3DPrefabExporter
    {
        const string k_DialogTitle = "Save TextMesh3D as Prefab";

        /// <summary>True when the container has generated output that can be saved (press Apply first otherwise).</summary>
        public static bool HasOutput(TextMesh3DContainer container)
        {
            if (container == null) return false;
            if (container.GeneratedRoot != null) return true;
            return container.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null; // Group Output
        }

        /// <summary>Asks for a location in the project, saves the prefab there and pings it. Returns null if cancelled or failed.</summary>
        public static GameObject SaveAsPrefabWithDialog(TextMesh3DContainer container)
        {
            if (!HasOutput(container))
            {
                EditorUtility.DisplayDialog(k_DialogTitle, "There is no generated text to save. Press Apply first.", "OK");
                return null;
            }

            var path = EditorUtility.SaveFilePanelInProject(
                k_DialogTitle,
                GetDefaultFileName(container),
                "prefab",
                "Choose where to save the generated text. The prefab contains the meshes and has no TextMesh3D component.");
            if (string.IsNullOrEmpty(path)) return null;

            var prefab = SaveAsPrefab(container, path);
            if (prefab != null) EditorGUIUtility.PingObject(prefab);
            return prefab;
        }

        /// <summary>
        /// Saves the container's current output as a baked prefab at <paramref name="assetPath"/> ("Assets/....prefab").
        /// An existing prefab at that path is replaced. Returns the prefab asset, or null (with a console error) on failure.
        /// </summary>
        public static GameObject SaveAsPrefab(TextMesh3DContainer container, string assetPath)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            assetPath = assetPath?.Replace('\\', '/');
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"TextMesh3D: cannot save to '{assetPath}'. The path must be inside Assets/ and end with .prefab.", container);
                return null;
            }
            if (!HasOutput(container))
            {
                Debug.LogError("TextMesh3D: nothing to save. Press Apply to generate the text first.", container);
                return null;
            }

            // Build the copy in a preview scene so the open scene is not touched (or marked dirty).
            var scene = EditorSceneManager.NewPreviewScene();
            var copies = new List<Object>(); // in-memory mesh/material copies; destroyed if they never reach the asset
            try
            {
                var baked = BuildBakedCopy(container, scene);

                // Remember which mesh / materials each component uses, then copy the in-memory ones.
                // Components are matched by index after the round trip below (hierarchy and component order are preserved).
                var meshCopies = new Dictionary<Mesh, Mesh>();
                var materialCopies = new Dictionary<Material, Material>();

                var filters = baked.GetComponentsInChildren<MeshFilter>(true);
                var filterMeshes = new Mesh[filters.Length];
                for (int i = 0; i < filters.Length; i++)
                {
                    filterMeshes[i] = GetSavable(filters[i].sharedMesh, meshCopies, copies, GetMeshName(container, filters[i]));
                    filters[i].sharedMesh = null; // never let the prefab reference the scene's mesh
                }

                var renderers = baked.GetComponentsInChildren<Renderer>(true);
                var rendererMaterials = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++)
                {
                    rendererMaterials[i] = renderers[i].sharedMaterials
                        .Select(m => GetSavable(m, materialCopies, copies, m != null ? m.name : null))
                        .ToArray();
                }

                EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
                RemoveEmbeddedObjects(assetPath); // replacing a previous save: drop its meshes / materials

                // Objects can only be embedded in an asset that exists, so: write the prefab, embed the copies,
                // then point the prefab's components at them and save it again.
                var prefab = PrefabUtility.SaveAsPrefabAsset(baked, assetPath, out bool saved);
                if (!saved || prefab == null)
                {
                    Debug.LogError($"TextMesh3D: could not save prefab at '{assetPath}'.", container);
                    return null;
                }

                if (copies.Count > 0)
                {
                    foreach (var copy in copies) AssetDatabase.AddObjectToAsset(copy, prefab);
                    copies.Clear(); // now owned by the asset

                    var contents = PrefabUtility.LoadPrefabContents(assetPath);
                    try
                    {
                        var savedFilters = contents.GetComponentsInChildren<MeshFilter>(true);
                        var savedRenderers = contents.GetComponentsInChildren<Renderer>(true);
                        for (int i = 0; i < savedFilters.Length && i < filterMeshes.Length; i++)
                            savedFilters[i].sharedMesh = filterMeshes[i];
                        for (int i = 0; i < savedRenderers.Length && i < rendererMaterials.Length; i++)
                            savedRenderers[i].sharedMaterials = rendererMaterials[i];

                        PrefabUtility.SaveAsPrefabAsset(contents, assetPath, out saved);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }

                    if (!saved)
                    {
                        Debug.LogError($"TextMesh3D: could not store the meshes in '{assetPath}'.", container);
                        return null;
                    }
                }

                AssetDatabase.SaveAssets();
                return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"TextMesh3D: saving prefab failed: {e.Message}", container);
                Debug.LogException(e, container);
                return null;
            }
            finally
            {
                foreach (var copy in copies)
                {
                    if (copy != null && !EditorUtility.IsPersistent(copy)) Object.DestroyImmediate(copy);
                }
                EditorSceneManager.ClosePreviewScene(scene); // destroys the baked copy
            }
        }

        /// <summary>A clean root holding the output only: the grouped mesh (if any) and a copy of the generated hierarchy.</summary>
        static GameObject BuildBakedCopy(TextMesh3DContainer container, Scene scene)
        {
            var root = new GameObject(container.name);
            SceneManager.MoveGameObjectToScene(root, scene);

            // Group Output: the merged mesh lives on the container itself
            if (container.TryGetComponent<MeshFilter>(out var sourceFilter) && sourceFilter.sharedMesh != null)
            {
                root.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                var renderer = root.AddComponent<MeshRenderer>();
                if (container.TryGetComponent<MeshRenderer>(out var sourceRenderer))
                {
                    EditorUtility.CopySerialized(sourceRenderer, renderer); // materials, shadows, probes, ...
                }
            }

            if (container.GeneratedRoot != null)
            {
                var output = Object.Instantiate(container.GeneratedRoot, root.transform, false);
                output.name = container.GeneratedRoot.name;
            }
            return root;
        }

        /// <summary>Assets are referenced as-is; in-memory objects are copied once (shared references stay shared).</summary>
        static T GetSavable<T>(T source, Dictionary<T, T> map, List<Object> copies, string name) where T : Object
        {
            if (source == null) return null;
            if (EditorUtility.IsPersistent(source)) return source;
            if (!map.TryGetValue(source, out var copy))
            {
                copy = Object.Instantiate(source);
                copy.name = string.IsNullOrEmpty(name) ? source.name : name;
                map[source] = copy;
                copies.Add(copy);
            }
            return copy;
        }

        static string GetMeshName(TextMesh3DContainer container, MeshFilter filter)
        {
            // The grouped mesh sits on the root; glyph meshes are named after their object ("Glyph - A")
            return filter.transform.parent == null ? Sanitize(container.Text, container.name) : filter.gameObject.name;
        }

        static void RemoveEmbeddedObjects(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null) return;
            foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
            {
                if (obj is Mesh || obj is Material)
                {
                    AssetDatabase.RemoveObjectFromAsset(obj);
                    Object.DestroyImmediate(obj, true);
                }
            }
        }

        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        internal static string GetDefaultFileName(TextMesh3DContainer container)
        {
            var name = Sanitize(container.Text, container.name);
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name) sb.Append(k_InvalidFileNameChars.IndexOf(ch) >= 0 ? '_' : ch);
            return sb.ToString().Trim('.', ' ');
        }

        // Fixed set (not Path.GetInvalidFileNameChars, which differs per OS) so names are valid everywhere the project is opened
        const string k_InvalidFileNameChars = "\\/:*?\"<>|";

        /// <summary>Single line, no control characters, at most 40 characters; <paramref name="fallback"/> when empty.</summary>
        static string Sanitize(string text, string fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text) sb.Append(char.IsControl(ch) ? ' ' : ch);
            var result = string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (result.Length > 40) result = result.Substring(0, 40).TrimEnd();
            return result.Length > 0 ? result : fallback;
        }
    }
}
