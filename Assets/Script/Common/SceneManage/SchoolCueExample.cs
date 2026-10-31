using System;
using UnityEngine;

namespace WhaleMovie.SceneManage
{
    [DisallowMultipleComponent]
    public sealed class SchoolCueExample : MonoBehaviour, ICueHandler
    {
        public const string DebugCueId = "测试日志";

        [SerializeField] private string debugMessage = "school 时间轴的测试 Cue 已触发。";

        public void HandleCue(CueContext cue)
        {
            switch (cue.Name)
            {
                case DebugCueId:
                    Debug.Log($"[School Cue @ {cue.Time:F2}s] {debugMessage}", this);
                    break;

                case CueNames.SwitchContent:
                    // ContentController 统一处理内容切换；这里保留 case 作为写法示例。
                    break;

                default:
                    return;
            }
        }
    }
}
