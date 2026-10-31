using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WhaleMovie.Art.Editor
{
    [CustomEditor(typeof(ArtObjectShake))]
    public sealed class ArtObjectShakeBakerEditor : UnityEditor.Editor
    {
        private int bakeFrameRate = 60;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            bakeFrameRate = EditorGUILayout.IntSlider("烘焙采样帧率", bakeFrameRate, 15, 120);
            EditorGUILayout.HelpBox(
                "在编辑模式下直接采样脚本，不需要启动游戏。生成的 AnimationClip 可放入 Timeline，并通过拖动播放头预览。",
                MessageType.Info);

            ArtObjectShake shake = (ArtObjectShake)target;
            if (shake.GetComponent<Animator>() == null)
            {
                EditorGUILayout.HelpBox(
                    "Timeline 的 Animation Track 需要绑定 Animator。烘焙仍可继续；预览时请在目标物体上添加 Animator 并绑定该物体。",
                    MessageType.Warning);
            }

            if (bakeFrameRate < shake.peakFrequency * 3f)
            {
                EditorGUILayout.HelpBox(
                    "采样率相对最高抖动频率偏低，建议提高到最高频率的约 3 倍或以上。",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("烘焙为 AnimationClip"))
                    Bake((ArtObjectShake)target, bakeFrameRate);
            }
        }

        private static void Bake(ArtObjectShake source, int requestedFrameRate)
        {
            if (source == null)
                return;

            float duration = Mathf.Max(0.1f, source.duration);
            int frameRate = Mathf.Clamp(requestedFrameRate, 1, 120);

            string requestedPath = EditorUtility.SaveFilePanelInProject(
                "保存烘焙动画",
                source.gameObject.name + "_Shake_Baked",
                "anim",
                "选择 AnimationClip 的保存位置。");

            if (string.IsNullOrEmpty(requestedPath))
                return;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(requestedPath);
            GameObject sampleObject = null;
            AnimationClip bakedClip = null;

            try
            {
                // Sample on a temporary object so the scene object is never moved while baking.
                sampleObject = new GameObject(source.gameObject.name + "_ShakeBakeSample");
                sampleObject.hideFlags = HideFlags.HideAndDontSave;
                sampleObject.transform.localPosition = source.transform.localPosition;
                sampleObject.transform.localRotation = source.transform.localRotation;
                sampleObject.transform.localScale = source.transform.localScale;

                ArtObjectShake sampler = sampleObject.AddComponent<ArtObjectShake>();
                EditorUtility.CopySerialized(source, sampler);
                sampler.enabled = true;
                sampler.CaptureBaseline();

                List<Keyframe>[] positionKeys = CreateKeyLists(3);
                List<Keyframe>[] rotationKeys = CreateKeyLists(4);
                int sampleCount = Mathf.Max(1, Mathf.CeilToInt(duration * frameRate));

                for (int i = 0; i <= sampleCount; i++)
                {
                    float time = i == sampleCount
                        ? duration
                        : Mathf.Min(duration, i / (float)frameRate);

                    sampler.EvaluateAt(time);

                    Vector3 position = sampleObject.transform.localPosition;
                    Quaternion rotation = sampleObject.transform.localRotation;

                    AddKey(positionKeys[0], time, position.x);
                    AddKey(positionKeys[1], time, position.y);
                    AddKey(positionKeys[2], time, position.z);
                    AddKey(rotationKeys[0], time, rotation.x);
                    AddKey(rotationKeys[1], time, rotation.y);
                    AddKey(rotationKeys[2], time, rotation.z);
                    AddKey(rotationKeys[3], time, rotation.w);
                }

                bakedClip = new AnimationClip
                {
                    name = Path.GetFileNameWithoutExtension(assetPath),
                    frameRate = frameRate,
                    legacy = false
                };

                SetTransformCurve(bakedClip, "m_LocalPosition.x", positionKeys[0]);
                SetTransformCurve(bakedClip, "m_LocalPosition.y", positionKeys[1]);
                SetTransformCurve(bakedClip, "m_LocalPosition.z", positionKeys[2]);
                SetTransformCurve(bakedClip, "m_LocalRotation.x", rotationKeys[0]);
                SetTransformCurve(bakedClip, "m_LocalRotation.y", rotationKeys[1]);
                SetTransformCurve(bakedClip, "m_LocalRotation.z", rotationKeys[2]);
                SetTransformCurve(bakedClip, "m_LocalRotation.w", rotationKeys[3]);
                bakedClip.EnsureQuaternionContinuity();

                AssetDatabase.CreateAsset(bakedClip, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Selection.activeObject = bakedClip;
                EditorGUIUtility.PingObject(bakedClip);
                EditorUtility.DisplayDialog(
                    "烘焙完成",
                    "AnimationClip 已生成。将它拖到 Timeline 的 Animation Track，再拖动时间轴播放头即可在编辑模式预览。",
                    "确定");
                Debug.Log("Shake animation baked to " + assetPath, source);
            }
            catch (Exception exception)
            {
                if (bakedClip != null && !AssetDatabase.Contains(bakedClip))
                    DestroyImmediate(bakedClip);

                Debug.LogException(exception, source);
            }
            finally
            {
                if (sampleObject != null)
                    DestroyImmediate(sampleObject);
            }
        }

        private static List<Keyframe>[] CreateKeyLists(int count)
        {
            List<Keyframe>[] lists = new List<Keyframe>[count];
            for (int i = 0; i < count; i++)
                lists[i] = new List<Keyframe>();
            return lists;
        }

        private static void AddKey(List<Keyframe> keys, float time, float value)
        {
            keys.Add(new Keyframe(time, value));
        }

        private static void SetTransformCurve(
            AnimationClip clip,
            string propertyName,
            List<Keyframe> keys)
        {
            Keyframe[] smoothKeys = CalculateTangents(keys);
            AnimationCurve curve = new AnimationCurve(smoothKeys);
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                string.Empty,
                typeof(Transform),
                propertyName);

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static Keyframe[] CalculateTangents(List<Keyframe> source)
        {
            Keyframe[] keys = source.ToArray();

            for (int i = 0; i < keys.Length; i++)
            {
                int previous = Mathf.Max(0, i - 1);
                int next = Mathf.Min(keys.Length - 1, i + 1);
                float timeDelta = keys[next].time - keys[previous].time;
                float tangent = timeDelta > 0f
                    ? (keys[next].value - keys[previous].value) / timeDelta
                    : 0f;

                keys[i].inTangent = tangent;
                keys[i].outTangent = tangent;
            }

            return keys;
        }
    }
}