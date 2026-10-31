using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIBuilder.CreatureStage
{
    public enum StageActionKind { Hold, Locomotion, Turn, OneShot, Hover }

    [CreateAssetMenu(menuName = "AIBuilder/角色剧情/动作定义", fileName = "StageAction")]
    public sealed class StageActionDefinition : ScriptableObject
    {
        public string actionId;
        public string displayName;
        public StageActionKind kind;
        public AnimationClip clip;
        public double clipIn;
        public double clipOut = 1;
        public bool calibrated;
        [Tooltip("校准循环的等效前进距离，单位米。")]
        public float cycleDistance = 1;
        public float minPlaybackRate = 0.8f;
        public float maxPlaybackRate = 1.2f;
        [TextArea] public string calibrationNotes;

        public string[] Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(actionId)) errors.Add("ACTION_ID：动作 ID 不能为空。");
            if (clip == null) errors.Add("ACTION_CLIP：缺少动画。");
            else
            {
                if (clip.legacy) errors.Add("ACTION_LEGACY：不支持 Legacy 动画。");
                if (clip.isHumanMotion) errors.Add("ACTION_HUMANOID：当前仅验证 Generic 动画。");
                if (clipOut > clip.length + 0.000001) errors.Add("ACTION_RANGE：采样终点超过动画时长。");
            }
            if (!StageMotionPlan.IsFinite(clipIn) || !StageMotionPlan.IsFinite(clipOut) || clipIn < 0 || clipOut <= clipIn)
                errors.Add("ACTION_RANGE：采样区间必须有限、非负且结束晚于开始。");
            if (!StageMotionPlan.IsFinite(minPlaybackRate) || !StageMotionPlan.IsFinite(maxPlaybackRate) ||
                minPlaybackRate <= 0 || maxPlaybackRate < minPlaybackRate)
                errors.Add("ACTION_RATE：倍率范围无效。");
            if (kind == StageActionKind.Locomotion && (!StageMotionPlan.IsFinite(cycleDistance) || cycleDistance <= 0))
                errors.Add("ACTION_DISTANCE：移动循环距离必须大于零。");
            if (!calibrated) errors.Add("ACTION_UNCALIBRATED：动作尚未完成校准，不能作为已批准数据使用。");
            return errors.ToArray();
        }
    }
}
