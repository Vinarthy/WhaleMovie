using System;
using System.ComponentModel;
using UnityEngine.Timeline;

namespace WhaleMovie.SceneManage
{
    [Serializable]
    [DisplayName("Cue Track")]
    [TrackBindingType(typeof(CueReceiver))]
    [TrackColor(0.15f, 0.65f, 0.9f)]
    public sealed class CueTrack : MarkerTrack
    {
    }
}
