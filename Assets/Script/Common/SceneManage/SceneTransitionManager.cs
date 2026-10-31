using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhaleMovie.SceneManage
{
    [DisallowMultipleComponent]
    public sealed class SceneTransitionManager : MonoBehaviour
    {
        public const string EntrySceneName = "Entry";

        [Min(0f)] [SerializeField] private float fadeToBlackDuration = 0.3f;
        [Min(0f)] [SerializeField] private float fadeFromBlackDuration = 0.8f;

        private XRBlackoutSphere blackout;

        public bool LastLoadSucceeded { get; private set; }
        public Scene LastLoadedScene { get; private set; }
        public float FadeToBlackDuration => fadeToBlackDuration;
        public float FadeFromBlackDuration => fadeFromBlackDuration;

        private void Awake()
        {
            blackout = GetComponent<XRBlackoutSphere>();
            if (blackout == null)
                blackout = gameObject.AddComponent<XRBlackoutSphere>();
        }

        public void BindFadeCamera(Camera camera)
        {
            blackout.BindToCamera(camera);
        }

        public IEnumerator FadeToBlack()
        {
            yield return blackout.FadeTo(1f, fadeToBlackDuration);
        }

        public IEnumerator FadeFromBlack()
        {
            yield return blackout.FadeTo(0f, fadeFromBlackDuration);
        }

        public IEnumerator LoadContentSceneAdditive(string sceneName)
        {
            LastLoadSucceeded = false;
            LastLoadedScene = default(Scene);

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("Cannot load content because the scene name is empty.", this);
                yield break;
            }

            Scene existing = SceneManager.GetSceneByName(sceneName);
            if (existing.IsValid() && existing.isLoaded)
            {
                LastLoadedScene = existing;
                LastLoadSucceeded = true;
                PrepareContentScene(existing);
                yield break;
            }

            if (FindBuildIndex(sceneName) < 0)
            {
                Debug.LogError($"Content scene '{sceneName}' is not enabled in Build Settings.", this);
                yield break;
            }

            AsyncOperation operation = null;
            try
            {
                operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            if (operation == null)
                yield break;

            while (!operation.isDone)
                yield return null;

            Scene loaded = SceneManager.GetSceneByName(sceneName);
            if (!loaded.IsValid() || !loaded.isLoaded)
            {
                Debug.LogError($"Content scene '{sceneName}' did not finish loading.", this);
                yield break;
            }

            PrepareContentScene(loaded);
            LastLoadedScene = loaded;
            LastLoadSucceeded = true;
            yield return null;
        }

        public IEnumerator UnloadContentScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                string.Equals(scene.name, EntrySceneName, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
                yield break;

            while (!operation.isDone)
                yield return null;
        }

        public List<GameObject> SuspendContentScene(Scene scene)
        {
            var suspended = new List<GameObject>();
            if (!scene.IsValid() || !scene.isLoaded ||
                string.Equals(scene.name, EntrySceneName, StringComparison.OrdinalIgnoreCase))
                return suspended;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!root.activeSelf || transform.IsChildOf(root.transform))
                    continue;

                // Direct scene debugging can keep the XR camera in this scene instead of Entry.
                bool containsPlayerCamera = false;
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.isActiveAndEnabled && camera.CompareTag("MainCamera"))
                    {
                        containsPlayerCamera = true;
                        break;
                    }
                }
                if (containsPlayerCamera)
                    continue;

                suspended.Add(root);
                root.SetActive(false);
            }

            return suspended;
        }

        public void ResumeContentScene(List<GameObject> suspended)
        {
            foreach (GameObject root in suspended)
            {
                if (root != null)
                    root.SetActive(true);
            }
            suspended.Clear();
        }

        public static int FindBuildIndex(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(name, sceneName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        public void PrepareLoadedContentScene(Scene scene)
        {
            PrepareContentScene(scene);
        }

        private static void PrepareContentScene(Scene scene)
        {
            SceneManager.SetActiveScene(scene);

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
                for (int j = 0; j < cameras.Length; j++)
                {
                    AudioListener listener = cameras[j].GetComponent<AudioListener>();
                    if (cameras[j].CompareTag("MainCamera") || listener != null)
                        cameras[j].enabled = false;
                    if (listener != null)
                        listener.enabled = false;
                }
            }
        }
    }
}
