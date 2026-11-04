using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Moves the imported boat lights into the scene's Light group once.</summary>
[InitializeOnLoad]
internal static class MoveBoatLights
{
    static MoveBoatLights()
    {
        EditorApplication.delayCall += MoveIfNeeded;
        EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += MoveIfNeeded;
    }

    [MenuItem("Tools/WhaleMovie/Move Boat Lights To Light Group")]
    private static void MoveIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "Ocean")
            return;

        GameObject boat = null;
        GameObject target = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "boat 剧情代理")
                boat = root;
            else if (root.name == "Light")
                target = root;
        }
        if (boat == null || target == null)
            return;

        Light[] originals = boat.GetComponentsInChildren<Light>(true);
        if (originals.Length == 0)
            return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Move boat lights to Light group");
        HashSet<GameObject> originalObjects = new HashSet<GameObject>();
        foreach (Light original in originals)
        {
            originalObjects.Add(original.gameObject);
            GameObject copy = new GameObject(original.gameObject.name);
            Undo.RegisterCreatedObjectUndo(copy, "Copy boat light");
            Undo.SetTransformParent(copy.transform, target.transform, "Parent copied boat light");
            copy.transform.position = original.transform.position;
            copy.transform.rotation = original.transform.rotation;
            copy.transform.localScale = original.transform.lossyScale;
            copy.SetActive(original.gameObject.activeSelf);

            Light copiedLight = Undo.AddComponent<Light>(copy);
            EditorUtility.CopySerialized(original, copiedLight);

            UniversalAdditionalLightData extra = original.GetComponent<UniversalAdditionalLightData>();
            if (extra != null)
            {
                UniversalAdditionalLightData copiedExtra = Undo.AddComponent<UniversalAdditionalLightData>(copy);
                EditorUtility.CopySerialized(extra, copiedExtra);
            }
        }

        foreach (GameObject originalObject in originalObjects)
        {
            Component[] components = originalObject.GetComponents<Component>();
            bool lightOnly = originalObject.transform.childCount == 0;
            foreach (Component component in components)
                lightOnly &= component is Transform || component is Light || component is UniversalAdditionalLightData;

            if (lightOnly)
            {
                Undo.DestroyObjectImmediate(originalObject);
                continue;
            }

            foreach (Light light in originalObject.GetComponents<Light>())
                Undo.DestroyObjectImmediate(light);
            foreach (UniversalAdditionalLightData extra in originalObject.GetComponents<UniversalAdditionalLightData>())
                Undo.DestroyObjectImmediate(extra);
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Moved {originals.Length} boat lights under the scene Light group.");
    }
}
