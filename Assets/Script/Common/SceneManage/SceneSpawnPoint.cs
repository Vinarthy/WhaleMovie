using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhaleMovie.SceneManage
{
    /// <summary>Places the persistent XR player at a content scene's floor-level spawn point.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneSpawnPoint : MonoBehaviour
    {
        [Header("出生点")]
        [Tooltip("拖入场景中的空物体，使用它的世界坐标。留空时使用本物体。Y 表示脚下地面高度。")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("让玩家的水平视线朝向出生点的蓝色 Z 轴；不会修改头显的俯仰。")]
        [SerializeField] private bool matchFacingDirection = true;

        [Tooltip("补偿头显在游玩区域内的水平偏移，让头显正下方落在出生点。关闭时直接对齐 XR Origin 根节点。")]
        [SerializeField] private bool alignHeadsetHorizontally = true;

        [Header("玩家（通常无需填写）")]
        [Tooltip("留空时从当前 MainCamera 的父级自动查找 XR Origin，适用于 Entry 常驻玩家。")]
        [SerializeField] private XROrigin xrOrigin;

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

        [ContextMenu("将玩家移动到出生点（运行时）")]
        public void PlacePlayer()
        {
            if (Application.isPlaying)
                TryPlacePlayer();
        }

        public bool TryPlacePlayer()
        {
            XROrigin origin = xrOrigin;
            if (origin == null && Camera.main != null)
                origin = Camera.main.GetComponentInParent<XROrigin>();

            if (origin == null || origin.Camera == null || !origin.Camera.isActiveAndEnabled)
            {
                Debug.LogWarning("SceneSpawnPoint：找不到正在使用的 XR Origin 和摄像机，请检查 Entry 玩家配置。", this);
                return false;
            }

            Transform player = origin.Origin != null ? origin.Origin.transform : origin.transform;
            Transform head = origin.Camera.transform;
            Transform point = SpawnPoint;
            if (!head.IsChildOf(player) || point.IsChildOf(player))
            {
                Debug.LogWarning("SceneSpawnPoint：摄像机必须在玩家根节点下，出生点必须放在玩家层级之外。", this);
                return false;
            }

            // Read the destination before moving anything. Never write to the tracked camera.
            Vector3 destination = point.position;
            Vector3 destinationForward = Vector3.ProjectOnPlane(point.forward, Vector3.up);
            CharacterController[] controllers = player.GetComponentsInChildren<CharacterController>(true);
            var restoreEnabled = new bool[controllers.Length];
            for (int i = 0; i < controllers.Length; i++)
            {
                restoreEnabled[i] = controllers[i].enabled;
                if (restoreEnabled[i])
                    controllers[i].enabled = false;
            }

            try
            {
                if (matchFacingDirection && destinationForward.sqrMagnitude > 0.0001f)
                {
                    Vector3 headForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                    // Looking straight up/down has no heading; use the rig's heading instead.
                    if (headForward.sqrMagnitude < 0.0001f)
                        headForward = Vector3.ProjectOnPlane(player.forward, Vector3.up);

                    float yaw = Vector3.SignedAngle(headForward, destinationForward, Vector3.up);
                    player.Rotate(Vector3.up, yaw, Space.World);
                }

                if (alignHeadsetHorizontally)
                {
                    Vector3 headOffset = head.position - player.position;
                    destination -= new Vector3(headOffset.x, 0f, headOffset.z);
                }

                player.position = destination;
                Physics.SyncTransforms();
            }
            finally
            {
                for (int i = 0; i < controllers.Length; i++)
                {
                    if (controllers[i] != null && restoreEnabled[i])
                        controllers[i].enabled = true;
                }
            }

            return true;
        }

        /// <summary>Search only the destination scene; Entry and the outgoing scene remain loaded.</summary>
        public static bool ApplyForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            SceneSpawnPoint selected = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SceneSpawnPoint candidate in root.GetComponentsInChildren<SceneSpawnPoint>(true))
                {
                    if (!candidate.isActiveAndEnabled)
                        continue;

                    if (selected != null)
                    {
                        Debug.LogWarning($"场景 '{scene.name}' 有多个启用的 SceneSpawnPoint，未移动玩家。请只保留一个启用的出生点。", candidate);
                        return false;
                    }

                    selected = candidate;
                }
            }

            // Existing scenes without a spawn point keep their current placement.
            return selected != null && selected.TryPlacePlayer();
        }

        private void OnDrawGizmosSelected()
        {
            Transform point = SpawnPoint;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(point.position, 0.2f);
            Gizmos.DrawLine(point.position, point.position + point.forward);
        }
    }
}
