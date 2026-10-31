using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhaleMovie.SceneManage
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class ContentFlowManager : MonoBehaviour
    {
        private const string CatalogResourcePath = "SceneManage/ContentCatalog";

        [SerializeField] private ContentCatalog catalog;
        [SerializeField] private SequenceEndMode sequenceEndMode = SequenceEndMode.Stop;
        [SerializeField] private bool fadeForSameSceneContent = true;

        private readonly List<ContentDefinition> definitions = new List<ContentDefinition>();
        private SceneTransitionManager transition;
        private ContentController currentController;
        private ContentId currentContentId = ContentId.None;
        private Scene currentContentScene;
        private bool initialized;
        private bool transitioning;

        public static ContentFlowManager Instance { get; private set; }

        public ContentId CurrentContentId => currentContentId;
        public bool IsTransitioning => transitioning;

        public event Action<ContentId> ContentChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<ContentFlowManager>() != null)
                return;

            var root = new GameObject("[WhaleMovie App Root]");
            DontDestroyOnLoad(root);
            root.AddComponent<XRBlackoutSphere>();
            root.AddComponent<SceneTransitionManager>();
            root.AddComponent<ContentFlowManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (!string.Equals(gameObject.scene.name, SceneTransitionManager.EntrySceneName, StringComparison.OrdinalIgnoreCase))
                DontDestroyOnLoad(gameObject);
            transition = GetComponent<SceneTransitionManager>();
            if (transition == null)
                transition = gameObject.AddComponent<SceneTransitionManager>();

            LoadDefinitions();
        }

        private void Start()
        {
            StartCoroutine(InitializeRoutine());
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void NextContent()
        {
            if (!initialized || transitioning)
                return;

            List<ContentDefinition> sequence = BuildSequence();
            if (sequence.Count == 0)
            {
                Debug.LogError("No content scenes are enabled in Build Settings.", this);
                return;
            }

            int currentIndex = sequence.FindIndex(item => item.id == currentContentId);
            int nextIndex = currentIndex + 1;
            if (currentIndex < 0)
                nextIndex = 0;

            if (nextIndex >= sequence.Count)
            {
                if (sequenceEndMode == SequenceEndMode.Loop)
                    nextIndex = 0;
                else
                    return;
            }

            SwitchTo(sequence[nextIndex].id);
        }

        public void SwitchTo(ContentId target)
        {
            if (target == ContentId.None || transitioning)
                return;

            ContentDefinition definition = FindDefinition(target);
            if (definition == null)
            {
                Debug.LogError($"Content '{target}' is not present in the content catalog.", this);
                return;
            }

            StartCoroutine(SwitchRoutine(definition));
        }

        public void ReplayCurrentContent()
        {
            if (transitioning || currentController == null)
                return;

            currentController.PrepareEnter();
            currentController.PlayTimeline();
        }

        private IEnumerator InitializeRoutine()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (string.Equals(activeScene.name, SceneTransitionManager.EntrySceneName, StringComparison.OrdinalIgnoreCase))
            {
                List<ContentDefinition> sequence = BuildSequence();
                if (sequence.Count == 0)
                {
                    initialized = true;
                    Debug.LogError("Entry loaded, but no content scenes are enabled in Build Settings.", this);
                    yield break;
                }

                yield return SwitchRoutine(sequence[0], true);
                yield break;
            }

            // A directly opened content scene must keep its own XR camera active.
            ContentDefinition initialDefinition = FindFirstDefinitionForScene(activeScene.name);
            ContentId preferredId = initialDefinition != null ? initialDefinition.id : ContentId.None;
            SceneContentRegistry registry = SceneContentRegistry.GetOrCreate(activeScene, preferredId);
            currentController = registry != null
                ? (registry.Find(preferredId) ?? registry.GetDefault())
                : null;

            if (currentController != null)
            {
                registry.ExitAllExcept(currentController);
                currentController.PrepareEnter();
                currentContentId = currentController.Id;
                currentContentScene = activeScene;
            }

            SceneSpawnPoint.ApplyForScene(activeScene);
            yield return transition.FadeFromBlack();
            currentController?.PlayTimeline();
            initialized = true;
            ContentChanged?.Invoke(currentContentId);
        }

        private IEnumerator SwitchRoutine(ContentDefinition target, bool initialEntryLoad = false)
        {
            if (transitioning)//如果是传送门
                yield break;

            transitioning = true;
            if (!initialEntryLoad)
                yield return transition.FadeToBlack();

            ContentController previousController = currentController;
            Scene previousScene = currentContentScene;
            bool sameScene = previousScene.IsValid() && previousScene.isLoaded &&
                string.Equals(previousScene.name, target.sceneName, StringComparison.OrdinalIgnoreCase);

            if (sameScene)
            {
                SceneContentRegistry registry = SceneContentRegistry.GetOrCreate(previousScene, target.id);
                ContentController next = registry != null ? registry.Find(target.id) : null;
                if (next == null)
                {
                    Debug.LogError($"Content '{target.id}' was not found in scene '{target.sceneName}'.", this);
                    yield return transition.FadeFromBlack();
                    transitioning = false;
                    yield break;
                }

                registry.ExitAllExcept(next);
                next.PrepareEnter();
                currentController = next;
                currentContentId = next.Id;

                SceneSpawnPoint.ApplyForScene(previousScene);
                if (fadeForSameSceneContent)
                    yield return transition.FadeFromBlack();
                else
                    GetComponent<XRBlackoutSphere>().SetAlphaImmediate(0f);

                next.PlayTimeline();
                initialized = true;
                transitioning = false;
                ContentChanged?.Invoke(currentContentId);
                yield break;
            }

            previousController?.Exit();
            // The blackout does not stop underlying rendering. Suspend the outgoing scene
            // while loading so expensive effects (especially school's clouds) stop running.
            List<GameObject> suspendedRoots = transition.SuspendContentScene(previousScene);
            yield return transition.LoadContentSceneAdditive(target.sceneName);
            if (!transition.LastLoadSucceeded)
            {
                transition.ResumeContentScene(suspendedRoots);
                previousController?.PrepareEnter();
                yield return transition.FadeFromBlack();
                previousController?.PlayTimeline();
                initialized = true;
                transitioning = false;
                yield break;
            }

            Scene loadedScene = transition.LastLoadedScene;
            SceneContentRegistry loadedRegistry = SceneContentRegistry.GetOrCreate(loadedScene, target.id);
            ContentController loadedController = loadedRegistry != null
                ? (loadedRegistry.Find(target.id) ?? loadedRegistry.GetDefault())
                : null;

            if (loadedController == null)
            {
                Debug.LogError($"No ContentController was found in scene '{target.sceneName}'.", this);
                transition.ResumeContentScene(suspendedRoots);
                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                    previousController?.PrepareEnter();
                }

                yield return transition.UnloadContentScene(loadedScene);
                yield return transition.FadeFromBlack();
                previousController?.PlayTimeline();
                initialized = true;
                transitioning = false;
                yield break;
            }

            loadedRegistry.ExitAllExcept(loadedController);
            loadedController.PrepareEnter();
            currentController = loadedController;
            currentContentId = target.id;
            currentContentScene = loadedScene;

            if (previousScene.IsValid() && previousScene.isLoaded && previousScene != loadedScene)
                yield return transition.UnloadContentScene(previousScene);

            suspendedRoots.Clear();
            SceneSpawnPoint.ApplyForScene(loadedScene);
            yield return transition.FadeFromBlack();
            loadedController.PlayTimeline();
            initialized = true;
            transitioning = false;
            ContentChanged?.Invoke(currentContentId);
        }

        private void LoadDefinitions()
        {
            definitions.Clear();
            if (catalog == null)
                catalog = Resources.Load<ContentCatalog>(CatalogResourcePath);

            if (catalog != null && catalog.Contents != null && catalog.Contents.Count > 0)
            {
                for (int i = 0; i < catalog.Contents.Count; i++)
                    definitions.Add(catalog.Contents[i]);
            }
            else
            {
                definitions.AddRange(ContentCatalog.CreateDefaultDefinitions());
            }
        }

        private List<ContentDefinition> BuildSequence()
        {
            var sequence = new List<ContentDefinition>();
            for (int i = 0; i < definitions.Count; i++)
            {
                ContentDefinition definition = definitions[i];
                if (definition != null && definition.includeInSequence &&
                    SceneTransitionManager.FindBuildIndex(definition.sceneName) > 0)
                {
                    sequence.Add(definition);
                }
            }

            sequence.Sort((a, b) =>
            {
                int contentOrder = a.id.CompareTo(b.id);
                return contentOrder != 0 ? contentOrder : a.orderInScene.CompareTo(b.orderInScene);
            });
            return sequence;
        }

        private ContentDefinition FindDefinition(ContentId id)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null && definitions[i].id == id)
                    return definitions[i];
            }

            return null;
        }

        private ContentDefinition FindFirstDefinitionForScene(string sceneName)
        {
            ContentDefinition result = null;
            for (int i = 0; i < definitions.Count; i++)
            {
                ContentDefinition candidate = definitions[i];
                if (candidate == null ||
                    !string.Equals(candidate.sceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (result == null || candidate.orderInScene < result.orderInScene)
                    result = candidate;
            }

            return result;
        }
    }
}
