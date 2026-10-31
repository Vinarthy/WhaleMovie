using System;
using UnityEngine;

namespace AIBuilder.CreatureStage
{
    public enum StageGroundNameMatch { Prefix, Contains, Exact }
    public enum StageGroundScanMode { Name, StaticMeshes }

    [DisallowMultipleComponent, AddComponentMenu("AIBuilder/角色剧情/地面绑定设置")]
    public sealed class StageGroundSettings : MonoBehaviour
    {
        [Tooltip("限定查找范围；未指定时查找当前场景。")]
        public Transform groundRoot;
        public string groundName = "Ground";
        public StageGroundNameMatch match = StageGroundNameMatch.Prefix;
        public StageGroundScanMode scanMode;
        public Transform[] excludedRoots = Array.Empty<Transform>();
        public Transform[] includedRoots = Array.Empty<Transform>();
        public string excludedKeywords = "grass,plant,vegetation,foliage,bush,tree,草,灌木,植被,树";
        [Min(0)] public float maxStepUp = .5f;
        [Min(.1f)] public float maxDrop = 100;
        [Range(0, 90)] public float maxSlope = 60;
        [HideInInspector] public Collider[] boundSurfaces = Array.Empty<Collider>();

        public bool Includes(MeshFilter mesh, out string reason)
        {
            if (mesh == null || mesh.sharedMesh == null) { reason = "没有有效 Mesh"; return false; }
            if (mesh.gameObject.scene != gameObject.scene) { reason = "不属于当前场景"; return false; }
            if (Under(mesh.transform, excludedRoots)) { reason = "明确排除（含子物体）"; return false; }
            if (!mesh.gameObject.activeInHierarchy) { reason = "物体未启用"; return false; }
            if (Under(mesh.transform, includedRoots)) { reason = "强制包含（含子物体）"; return true; }
            if (scanMode == StageGroundScanMode.Name) { var yes = Matches(mesh.name); reason = yes ? "名称匹配" : "名称不匹配"; return yes; }
            if (!mesh.gameObject.isStatic) { reason = "不是静态物体"; return false; }
            var keywords = (excludedKeywords ?? "").Split(new[] { ',', '，', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var node = mesh.transform; node != null; node = node.parent)
            {
                foreach (var keyword in keywords)
                    if (keyword.Trim().Length > 0 && node.name.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    { reason = "名称排除：" + keyword.Trim(); return false; }
                if (node == groundRoot) break;
            }
            reason = "静态 Mesh"; return true;
        }

        private static bool Under(Transform value, Transform[] roots)
        {
            if (roots != null) foreach (var root in roots)
                if (root != null && (root == value || value.IsChildOf(root))) return true;
            return false;
        }

        public bool Matches(string objectName)
        {
            var pattern = groundName == null ? "" : groundName.Trim();
            if (pattern.Length == 0 || string.IsNullOrEmpty(objectName)) return false;
            switch (match)
            {
                case StageGroundNameMatch.Prefix: return objectName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
                case StageGroundNameMatch.Contains: return objectName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                case StageGroundNameMatch.Exact: return string.Equals(objectName, pattern, StringComparison.OrdinalIgnoreCase);
                default: return false;
            }
        }
    }
}
