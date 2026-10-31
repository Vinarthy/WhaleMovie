using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace WhaleMovie.SceneManage
{
    [DisallowMultipleComponent]
    public sealed class CueReceiver : MonoBehaviour, INotificationReceiver
    {
        [SerializeField] private GameObject handlerRoot;
        [SerializeField] private bool includeInactiveHandlers = true;

        private readonly List<ICueHandler> handlers = new List<ICueHandler>();

        public event Action<CueContext> CueTriggered;

        private void Awake()
        {
            if (handlerRoot == null)
                handlerRoot = gameObject;

            RefreshHandlers();
        }

        private void OnEnable()
        {
            RefreshHandlers();
        }

        public void SetHandlerRoot(GameObject root)
        {
            handlerRoot = root != null ? root : gameObject;
            RefreshHandlers();
        }

        public void RefreshHandlers()
        {
            handlers.Clear();
            GameObject root = handlerRoot != null ? handlerRoot : gameObject;
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(includeInactiveHandlers);

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICueHandler handler)
                    handlers.Add(handler);
            }
        }

        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (!Application.isPlaying || !(notification is CueMarker marker))
                return;

            string cueName = marker.CueName ?? string.Empty;
            var cue = new CueContext(cueName, marker.time, origin, marker);
            CueTriggered?.Invoke(cue);

            // Work on a snapshot so a cue handler may enable/disable content safely.
            ICueHandler[] snapshot = handlers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ICueHandler handler = snapshot[i];
                if (handler == null)
                    continue;

                if (handler is UnityEngine.Object unityObject && unityObject == null)
                    continue;

                handler.HandleCue(cue);
            }
        }
    }
}
