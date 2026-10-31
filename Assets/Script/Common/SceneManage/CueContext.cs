using UnityEngine.Playables;

namespace WhaleMovie.SceneManage
{
    public readonly struct CueContext
    {
        public CueContext(string name, double time, Playable origin, CueMarker marker)
        {
            Name = name;
            Time = time;
            Origin = origin;
            Marker = marker;
        }

        public string Name { get; }
        public double Time { get; }
        public Playable Origin { get; }
        public CueMarker Marker { get; }
    }

    public interface ICueHandler
    {
        void HandleCue(CueContext cue);
    }
}
