using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace WhaleMovie.SceneManage
{
    [DisallowMultipleComponent]
    public sealed class ContentController : MonoBehaviour, ICueHandler
    {
        [Header("内容")]
        [SerializeField] private ContentId contentId = ContentId.None;
        [SerializeField] private string sceneName;
        [Min(0)] [SerializeField] private int orderInScene;
        [SerializeField] private bool defaultContent;

        [Header("对象和时间轴")]
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private PlayableDirector timeline;
        [SerializeField] private CueReceiver cueReceiver;
        [SerializeField] private bool playTimelineOnEnter = true;
        [SerializeField] private bool deactivateRootOnExit = true;

        public ContentId Id => contentId;
        public string SceneName => string.IsNullOrWhiteSpace(sceneName) ? gameObject.scene.name : sceneName;
        public int OrderInScene => orderInScene;
        public bool IsDefaultContent => defaultContent;
        public PlayableDirector Timeline => timeline;

        private void Awake()
        {
            ResolveReferences();
        }

        public void ConfigureRuntime(
            ContentId id,
            string owningScene,
            PlayableDirector director,
            CueReceiver receiver,
            bool deactivateOnExit)
        {
            contentId = id;
            sceneName = owningScene;
            timeline = director;
            cueReceiver = receiver;
            deactivateRootOnExit = deactivateOnExit;
            defaultContent = true;
            ResolveReferences();
        }

        public void PrepareEnter()
        {
            ResolveReferences();

            if (contentRoot != null && !contentRoot.activeSelf)
                contentRoot.SetActive(true);

            if (cueReceiver != null)
            {
                cueReceiver.SetHandlerRoot(contentRoot != null ? contentRoot : gameObject);
                cueReceiver.RefreshHandlers();
            }

            if (timeline == null || timeline.playableAsset == null)
                return;

            timeline.Stop();
            timeline.time = 0d;
            timeline.Evaluate();
        }

        public void PlayTimeline()
        {
            if (!playTimelineOnEnter || timeline == null || timeline.playableAsset == null)
                return;

            timeline.Play();
        }

        public void Exit()
        {
            if (timeline != null)
                timeline.Stop();

            if (deactivateRootOnExit && contentRoot != null)
                contentRoot.SetActive(false);
        }

        public void HandleCue(CueContext cue)
        {
            if (!string.Equals(cue.Name, CueNames.SwitchContent, StringComparison.Ordinal))
                return;

            if (ContentFlowManager.Instance != null)
                ContentFlowManager.Instance.NextContent();
        }

        private void ResolveReferences()
        {
            if (contentRoot == null)
                contentRoot = gameObject;

            if (string.IsNullOrWhiteSpace(sceneName))
                sceneName = gameObject.scene.name;

            if (timeline == null)
                timeline = GetComponent<PlayableDirector>();

            if (cueReceiver == null)
                cueReceiver = GetComponent<CueReceiver>();

            if (cueReceiver == null)
                cueReceiver = gameObject.AddComponent<CueReceiver>();

            if (timeline != null)
            {
                timeline.playOnAwake = false;
                BindCueTracks();
            }
        }

        private void BindCueTracks()
        {
            if (!(timeline.playableAsset is TimelineAsset timelineAsset) || cueReceiver == null)
                return;

            foreach (TrackAsset track in timelineAsset.GetOutputTracks())
            {
                if (track is CueTrack)
                    timeline.SetGenericBinding(track, cueReceiver);
            }
        }
    }
}
