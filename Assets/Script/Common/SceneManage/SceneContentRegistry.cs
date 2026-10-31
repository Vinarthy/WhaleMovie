using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace WhaleMovie.SceneManage
{
    [DisallowMultipleComponent]
    public sealed class SceneContentRegistry : MonoBehaviour
    {
        [SerializeField] private List<ContentController> contents = new List<ContentController>();

        public IReadOnlyList<ContentController> Contents => contents;

        private void Awake()
        {
            Refresh();
        }

        public void Refresh()
        {
            contents.Clear();
            Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                ContentController[] found = roots[i].GetComponentsInChildren<ContentController>(true);
                for (int j = 0; j < found.Length; j++)
                {
                    if (!contents.Contains(found[j]))
                        contents.Add(found[j]);
                }
            }

            contents.Sort((a, b) => a.OrderInScene.CompareTo(b.OrderInScene));
        }

        public ContentController Find(ContentId id)
        {
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] != null && contents[i].Id == id)
                    return contents[i];
            }

            return null;
        }

        public ContentController GetDefault()
        {
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] != null && contents[i].IsDefaultContent)
                    return contents[i];
            }

            return contents.Count > 0 ? contents[0] : null;
        }

        public void ExitAllExcept(ContentController target)
        {
            for (int i = 0; i < contents.Count; i++)
            {
                ContentController content = contents[i];
                if (content != null && content != target)
                    content.Exit();
            }
        }

        public static SceneContentRegistry GetOrCreate(Scene scene, ContentId preferredId = ContentId.None)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            SceneContentRegistry registry = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && registry == null; i++)
                registry = roots[i].GetComponentInChildren<SceneContentRegistry>(true);

            if (registry == null)
            {
                var registryObject = new GameObject("[Scene Content Registry]");
                SceneManager.MoveGameObjectToScene(registryObject, scene);
                registry = registryObject.AddComponent<SceneContentRegistry>();
            }

            registry.Refresh();
            if (registry.contents.Count == 0)
                registry.CreateRuntimeFallback(preferredId);

            registry.Refresh();
            return registry;
        }

        private void CreateRuntimeFallback(ContentId preferredId)
        {
            ContentId id = preferredId;
            if (id == ContentId.None)
                ContentCatalog.TryGetDefaultId(gameObject.scene.name, out id);

            PlayableDirector director = FindDirectorInScene(gameObject.scene);
            GameObject host = director != null ? director.gameObject : gameObject;
            CueReceiver receiver = host.GetComponent<CueReceiver>();
            if (receiver == null)
                receiver = host.AddComponent<CueReceiver>();

            ContentController controller = host.GetComponent<ContentController>();
            if (controller == null)
                controller = host.AddComponent<ContentController>();

            controller.ConfigureRuntime(id, gameObject.scene.name, director, receiver, false);
            contents.Add(controller);
        }

        private static PlayableDirector FindDirectorInScene(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                PlayableDirector director = roots[i].GetComponentInChildren<PlayableDirector>(true);
                if (director != null)
                    return director;
            }

            return null;
        }
    }
}
