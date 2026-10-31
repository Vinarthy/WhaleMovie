using System;
using UnityEngine;

namespace AIBuilder.CreatureStage
{
    [Serializable]
    public sealed class StageLegBinding
    {
        [Tooltip("从 SkeletonRoot 起算的完整相对路径；保留 HorseLink。")]
        public string thigh;
        public string calf;
        public string horseLink;
        public string foot;
    }

    [CreateAssetMenu(menuName = "AIBuilder/角色剧情/角色配置", fileName = "StageActor")]
    public sealed class StageActorProfile : ScriptableObject
    {
        public string actorTypeId;
        public GameObject prefab;
        [Tooltip("相对模型根的路径，空字符串表示模型根本身。")]
        public string skeletonRootPath = "";
        [Tooltip("相对 SkeletonRoot 的路径；只登记人工确认的运动来源。")]
        public string motionSourcePath = "";
        public float uniformScale = 1;
        public bool bindingsReviewed;
        public StageLegBinding leftLeg = new StageLegBinding();
        public StageLegBinding rightLeg = new StageLegBinding();
        public StageActionDefinition[] actions = Array.Empty<StageActionDefinition>();
    }
}
