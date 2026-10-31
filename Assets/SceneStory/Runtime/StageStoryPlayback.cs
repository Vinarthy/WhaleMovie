using UnityEngine;
using UnityEngine.Playables;

namespace AIBuilder.CreatureStage
{
    public sealed class StageStoryPlayback : MonoBehaviour
    {
        public PlayableDirector director;
        public bool playing = true;
        public bool repeat;
        public Camera sceneCamera;
        public Transform actor;
        private int resetFrame = -1;
        private DirectorUpdateMode previousUpdateMode;
        private bool ownsClock;


        private void Start()
        {
            if (director != null)
            {
                var autoPlay = playing;
                previousUpdateMode = director.timeUpdateMode; ownsClock = true;
                // 原生连续播放才能调度 Timeline 音频；逐帧 Evaluate 只适合定位预览。
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                Restart(); playing = autoPlay;
                if (!playing) director.Pause();
            }
        }
        private void Update()
        {
            if (director != null && ownsClock)
            {
                if (playing && director.time >= director.duration)
                {
                    if (repeat) Restart();
                    else playing = false;
                }
                if (playing && director.state != PlayState.Playing) director.Play();
                else if (!playing && director.state == PlayState.Playing) director.Pause();
            }
            else if (Time.frameCount != resetFrame) AdvancePlayback(Time.unscaledDeltaTime);
        }
        public void Restart()
        {
            if (director == null) return;
            resetFrame = Time.frameCount;
            director.time = 0;
            director.Evaluate();
            playing = true;
            if (ownsClock) director.Play();
        }
        public void AdvancePlayback(double elapsed)
        {
            if (director == null || !playing || !StageMotionPlan.IsFinite(elapsed) || elapsed < 0) return;
            // 加载或切换编辑器造成长帧时，不把数秒耗时一次灌进剧情时间。
            var next = director.time + System.Math.Min(.1, elapsed);
            if (next >= director.duration)
            {
                if (repeat) { Restart(); return; }
                next = director.duration; playing = false;
            }
            director.time = next; director.Evaluate();
        }
        private void OnDisable()
        {
            if (director != null && ownsClock)
            {
                director.Stop();
                director.timeUpdateMode = previousUpdateMode;
            }
            ownsClock = false;
        }
    }
}
