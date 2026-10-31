using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// A scene-only tail hitbox. The ocean tiles are visual planes, so contact is tested
// against their shared water height instead of relying on physics trigger callbacks.
[ExecuteAlways]
[RequireComponent(typeof(BoxCollider))]
public sealed class WhaleTailWaterSplash : MonoBehaviour
{
    [InspectorName("鲸鱼根物体")] public Transform whaleRoot;
    [InspectorName("尾部骨骼")] public Transform tailBone;
    [InspectorName("尾鳍上端")] public Transform upperFinTip;
    [InspectorName("尾鳍下端")] public Transform lowerFinTip;
    [InspectorName("水花预制件")] public GameObject splashPrefab;
    [InspectorName("水面世界高度")] public float waterSurfaceY;
    [InspectorName("尾鳍位置微调（鲸鱼局部坐标）")] public Vector3 localOffset;
    [InspectorName("碰撞箱世界尺寸")] public Vector3 worldBoxSize = new Vector3(3f, 0.8f, 1.6f);
    [Min(0f), InspectorName("最低向下拍水速度")]
    public float minimumDownwardSpeed = 0.25f;
    [Min(0f), InspectorName("再次抬起高度")]
    public float rearmHeight = 0.15f;
    [Min(0f), InspectorName("水面接触余量")]
    public float contactTolerance = 0.12f;
    [Min(0f), InspectorName("水花冷却时间")]
    public float cooldown = 0.7f;

    private BoxCollider tailBox;
    private Vector3 lastRootPosition;
    private float lastHeight;
    private float nextSplashTime;
    private bool hasSample;
    private bool armed;
    private bool warnedMissingBone;

    private void OnEnable()
    {
        tailBox = GetComponent<BoxCollider>();
        ResolveBones();
        UpdateTailBox();
        hasSample = false;
#if UNITY_EDITOR
        EditorApplication.update += EditorSync;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorSync;
#endif
    }

#if UNITY_EDITOR
    private void EditorSync()
    {
        if (Application.isPlaying) return;
        UpdateTailBox();
    }
#endif

    private void LateUpdate()
    {
        if (!Application.isPlaying || !UpdateTailBox() || tailBox == null) return;
        var centerY = tailBox.bounds.center.y;
        if (!hasSample || Time.deltaTime <= 0f ||
            (whaleRoot.position - lastRootPosition).sqrMagnitude > 9f)
        {
            lastHeight = centerY;
            lastRootPosition = whaleRoot.position;
            armed = tailBox.bounds.max.y > waterSurfaceY + rearmHeight;
            hasSample = true;
            return;
        }

        var verticalSpeed = (centerY - lastHeight) / Time.deltaTime;
        lastHeight = centerY;
        lastRootPosition = whaleRoot.position;
        if (verticalSpeed > minimumDownwardSpeed * 0.5f &&
            tailBox.bounds.max.y > waterSurfaceY + rearmHeight)
            armed = true;

        var intersectsSurface = tailBox.bounds.min.y <= waterSurfaceY + contactTolerance &&
                                tailBox.bounds.max.y >= waterSurfaceY - contactTolerance;
        if (!armed || !intersectsSurface || verticalSpeed > -minimumDownwardSpeed ||
            Time.time < nextSplashTime) return;

        armed = false;
        nextSplashTime = Time.time + cooldown;
        SpawnSplash(tailBox.bounds.center);
    }

    private void ResolveBones()
    {
        if (whaleRoot == null) whaleRoot = transform.parent;
        if (whaleRoot == null ||
            tailBone != null && upperFinTip != null && lowerFinTip != null) return;

        foreach (var bone in whaleRoot.GetComponentsInChildren<Transform>(true))
        {
            if (tailBone == null && bone.name == "MCH-ROT-tail") tailBone = bone;
            else if (upperFinTip == null && bone.name == "DEF-back_fin.T.002.Bk_end") upperFinTip = bone;
            else if (lowerFinTip == null && bone.name == "DEF-back_fin.B.001.Bk.002_end") lowerFinTip = bone;
        }
    }

    private bool UpdateTailBox()
    {
        if (tailBone == null || whaleRoot == null) ResolveBones();
        if (tailBone == null || whaleRoot == null)
        {
            if (Application.isPlaying && !warnedMissingBone)
            {
                Debug.LogWarning("鲸鱼尾鳍拍水：找不到 MCH-ROT-tail 骨骼。", this);
                warnedMissingBone = true;
            }
            return false;
        }
        if (tailBox == null) tailBox = GetComponent<BoxCollider>();

        var tipCenter = upperFinTip != null && lowerFinTip != null
            ? (upperFinTip.position + lowerFinTip.position) * 0.5f
            : tailBone.position;
        var position = Vector3.Lerp(tailBone.position, tipCenter, 0.7f) +
                       whaleRoot.TransformVector(localOffset);
        if ((transform.position - position).sqrMagnitude > 0.00000001f ||
            Quaternion.Angle(transform.rotation, whaleRoot.rotation) > 0.001f)
            transform.SetPositionAndRotation(position, whaleRoot.rotation);

        if (tailBox != null)
        {
            var scale = whaleRoot.lossyScale;
            var size = new Vector3(
                Mathf.Max(0.01f, worldBoxSize.x / Mathf.Max(0.001f, Mathf.Abs(scale.x))),
                Mathf.Max(0.01f, worldBoxSize.y / Mathf.Max(0.001f, Mathf.Abs(scale.y))),
                Mathf.Max(0.01f, worldBoxSize.z / Mathf.Max(0.001f, Mathf.Abs(scale.z))));
            if ((tailBox.size - size).sqrMagnitude > 0.00000001f) tailBox.size = size;
            if (!tailBox.isTrigger) tailBox.isTrigger = true;
        }
        return true;
    }

    private void SpawnSplash(Vector3 contact)
    {
        if (splashPrefab == null) return;
        contact.y = waterSurfaceY + 0.02f;
        var splash = Instantiate(splashPrefab, contact, Quaternion.identity);
        foreach (var particles in splash.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Clear(false);
            particles.Play(false);
        }
        Destroy(splash, 5f);
    }
}
