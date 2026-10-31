using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace WhaleMovie.SceneManage
{
    [Serializable]
    [DisplayName("Cue")]
    [CustomStyle("SignalEmitter")]
    public sealed class CueMarker : Marker, INotification, INotificationOptionProvider
    {
        [InspectorName("Cue ID")]
        [Tooltip("逻辑脚本用于匹配此字符串，例如：切换内容。")]
        [SerializeField] private string cueName;
        [SerializeField] private bool retroactive;
        [SerializeField] private bool emitOnce = true;

        public string CueName
        {
            get => cueName;
            set => cueName = value;
        }

        public bool Retroactive
        {
            get => retroactive;
            set => retroactive = value;
        }

        public bool EmitOnce
        {
            get => emitOnce;
            set => emitOnce = value;
        }

        PropertyName INotification.id => new PropertyName(cueName ?? string.Empty);

        NotificationFlags INotificationOptionProvider.flags =>
            (retroactive ? NotificationFlags.Retroactive : default(NotificationFlags)) |
            (emitOnce ? NotificationFlags.TriggerOnce : default(NotificationFlags));
    }
}
