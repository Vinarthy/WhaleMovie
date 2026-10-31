using System;
using System.IO;
using System.Linq;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    public static class StageBasicExample
    {
        [MenuItem("Tools/场景剧情/创建基础示例", false, 30)]
        public static void CreateExample()
        {
            StageStoryWindow.StopAllPreviews();
            // Unity 不允许在未命名场景旁叠加场景；由用户选择保存位置，取消则不创建。
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(open.path) && !EditorSceneManager.SaveScene(open)) return;
            }
            var folder = AssetDatabase.GenerateUniqueAssetPath("Assets/SceneStoryExample");
            var director = Build(folder);
            Selection.activeGameObject = director.gameObject;
            StageStoryWindow.ShowDirector(director);
            TimelineEditor.GetOrCreateWindow().SetTimeline(director);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(Vector3.zero, Vector3.one * 12), false);
            Debug.Log("基础示例已创建：" + folder + "/BasicStory.unity。原场景保留打开，可分别保存。");
        }

        [MenuItem("Tools/场景剧情/为选中对象创建剧情", false, 20)]
        public static void CreateForSelection()
        {
            var model = Selection.activeGameObject;
            if (model == null || !model.scene.IsValid() || EditorUtility.IsPersistent(model))
            { Debug.LogWarning("请在 Hierarchy 选中需要编排的场景对象。"); return; }
            if (model.GetComponentInParent<StageActorDriver>() != null)
            { Debug.LogWarning("该对象已在剧情角色中，请直接打开编排窗口。"); return; }
            var path = EditorUtility.SaveFilePanelInProject("保存剧情 Timeline", model.name + "_Story", "playable", "选择新 Timeline 的保存位置");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) { Debug.LogWarning("请选择尚未使用的文件名，以免覆盖已有剧情。"); return; }
            StageStoryWindow.StopAllPreviews();
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            var root = new GameObject(model.name + " · 路线驱动");
            SceneManager.MoveGameObjectToScene(root, model.scene);
            root.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
            Undo.RegisterCreatedObjectUndo(root, "创建场景剧情");
            Undo.SetTransformParent(model.transform, root.transform, "绑定剧情角色");
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(model);
            var actor = Undo.AddComponent<StageActorDriver>(root); actor.animator = animator;
            var director = CreateStory(root.name, actor, path, model.scene);
            Undo.RegisterCreatedObjectUndo(director.gameObject, "创建场景剧情");
            var track = ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<StagePerformanceTrack>().Single();
            AddHold(director, track, root.transform.position, 0, 3, "起始停留");
            EditorUtility.SetDirty(track); EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(model.scene); AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = director.gameObject;
            StageStoryWindow.ShowDirector(director); TimelineEditor.GetOrCreateWindow().SetTimeline(director);
        }

        public static PlayableDirector Build(string folder)
        {
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || Directory.Exists(folder))
                throw new InvalidOperationException("示例必须写入 Assets 下的新文件夹。");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var groundMaterial = MaterialAsset(folder, "Ground", new Color(.16f, .23f, .29f));
            var actorMaterial = MaterialAsset(folder, "Actor", new Color(.12f, .7f, .85f));
            var accentMaterial = MaterialAsset(folder, "Accent", new Color(1, .62f, .16f));
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground_Plane";
            ground.transform.localScale = Vector3.one * 1.5f;
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            var light = new GameObject("Directional Light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(9, 10, -12); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .1f, .16f);
            var root = new GameObject("基础几何体 · 路线驱动"); root.transform.position = new Vector3(-4, 0, -3);
            var rig = new GameObject("AnimationRoot"); rig.transform.SetParent(root.transform, false);
            var animator = rig.AddComponent<Animator>();
            var visual = new GameObject("Visual"); visual.transform.SetParent(rig.transform, false);
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule); capsule.name = "Capsule";
            capsule.transform.SetParent(visual.transform, false); capsule.transform.localPosition = Vector3.up;
            capsule.GetComponent<Renderer>().sharedMaterial = actorMaterial; UnityEngine.Object.DestroyImmediate(capsule.GetComponent<Collider>());
            var front = GameObject.CreatePrimitive(PrimitiveType.Cube); front.name = "ForwardMarker";
            front.transform.SetParent(visual.transform, false); front.transform.localPosition = new Vector3(0, 1, .65f);
            front.transform.localScale = Vector3.one * .35f; front.GetComponent<Renderer>().sharedMaterial = accentMaterial;
            UnityEngine.Object.DestroyImmediate(front.GetComponent<Collider>());
            var actor = root.AddComponent<StageActorDriver>(); actor.animator = animator;
            var director = CreateStory("基础剧情（选我编辑）", actor, folder + "/BasicStory.playable", scene);
            var track = ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<StagePerformanceTrack>().Single();
            var settings = director.gameObject.AddComponent<StageGroundSettings>(); settings.groundRoot = ground.transform; settings.groundName = "Ground_";
            var idle = AnimationAsset(folder, "Pulse", true);
            var spin = AnimationAsset(folder, "Spin", false);
            AddHold(director, track, root.transform.position, 0, 2, "原地放大缩小");
            AddMove(director, track, root.transform.position, new Vector3(0, 0, 2), 2, 4, true, ground.GetComponent<Collider>(), "贴地移动");
            AddHold(director, track, new Vector3(0, 0, 2), 6, 2, "原地旋转");
            AddMove(director, track, new Vector3(0, 0, 2), new Vector3(4, 2, 0), 8, 4, false, ground.GetComponent<Collider>(), "空间移动（不贴地）");
            AddHold(director, track, new Vector3(4, 2, 0), 12, 2, "空中停留");
            AddAnimation(track.animationTrack, idle, 0, 6.4);
            AddAnimation(track.animationTrack, spin, 6, 6.4);
            AddAnimation(track.animationTrack, idle, 12, 2);
            var clips = track.animationTrack.GetClips().OrderBy(c => c.start).ToArray();
            for (var i = 1; i < clips.Length; i++) { var overlap = clips[i-1].end - clips[i].start; clips[i-1].blendOutDuration = overlap; clips[i].blendInDuration = overlap; }
            var playback = director.gameObject.GetComponent<StageStoryPlayback>(); playback.sceneCamera = camera; playback.actor = root.transform;
            director.time = 0; director.Evaluate(); director.Stop();
            EditorUtility.SetDirty(director.playableAsset); EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene, folder + "/BasicStory.unity");
            return director;
        }

        private static PlayableDirector CreateStory(string name, StageActorDriver actor, string path, Scene scene)
        {
            var story = new GameObject(name); SceneManager.MoveGameObjectToScene(story, scene);
            var director = story.AddComponent<PlayableDirector>(); director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.GameTime; director.extrapolationMode = DirectorWrapMode.Hold;
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>(); AssetDatabase.CreateAsset(timeline, path);
            var track = timeline.CreateTrack<StagePerformanceTrack>(null, "路线与停留");
            var animation = timeline.CreateTrack<AnimationTrack>(null, "动画（重叠片段做过渡）"); animation.trackOffset = TrackOffset.ApplySceneOffsets;
            track.animationTrack = animation; director.playableAsset = timeline;
            director.SetGenericBinding(track, actor); director.SetGenericBinding(animation, actor.animator);
            var playback = story.AddComponent<StageStoryPlayback>(); playback.director = director;
            return director;
        }
        private static Transform Point(PlayableDirector director, string name, Vector3 position)
        {
            var point = new GameObject(name).transform; point.SetParent(director.transform, false); point.position = position; return point;
        }
        private static void AddHold(PlayableDirector director, StagePerformanceTrack track, Vector3 position, double start, double duration, string name)
        {
            var clip = track.CreateClip<StagePerformanceClip>(); clip.start = start; clip.duration = duration; clip.displayName = name;
            var data = (StagePerformanceClip)clip.asset; data.mode = StagePerformanceMode.Hold;
            data.station.exposedName = new PropertyName(Guid.NewGuid().ToString()); director.SetReferenceValue(data.station.exposedName, Point(director, "剧情点 " + start, position));
        }
        private static void AddMove(PlayableDirector director, StagePerformanceTrack track, Vector3 from, Vector3 to, double start, double duration, bool grounded, Collider ground, string name)
        {
            var route = new GameObject("路线 " + start).AddComponent<StageRoute>(); route.transform.SetParent(director.transform, false);
            route.startAnchor = Point(director, "剧情点 " + start, from); route.endAnchor = Point(director, "到达点 " + start, to);
            route.points = new[] { from, (from + to) * .5f + Vector3.right * .5f, to }; route.curved = true;
            route.groundSurfaces = new[] { ground }; route.snapToGround = grounded;
            var clip = track.CreateClip<StagePerformanceClip>(); clip.start = start; clip.duration = duration; clip.displayName = name;
            var data = (StagePerformanceClip)clip.asset; data.mode = StagePerformanceMode.Move;
            data.groundMode = grounded ? StageGroundMode.Ground : StageGroundMode.FreeSpace;
            data.route.exposedName = new PropertyName(Guid.NewGuid().ToString()); director.SetReferenceValue(data.route.exposedName, route);
        }
        private static void AddAnimation(AnimationTrack track, AnimationClip animation, double start, double duration)
        {
            var clip = track.CreateClip<AnimationPlayableAsset>(); clip.start = start; clip.duration = duration; clip.displayName = animation.name;
            var data = (AnimationPlayableAsset)clip.asset; data.clip = animation; data.loop = AnimationPlayableAsset.LoopMode.On; data.removeStartOffset = false;
        }
        private static AnimationClip AnimationAsset(string folder, string name, bool pulse)
        {
            var clip = new AnimationClip { name = name, frameRate = 60 };
            foreach (var axis in new[] { "x", "y", "z" })
            {
                var scale = pulse ? new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1.4f), new Keyframe(2, 1)) : AnimationCurve.Constant(0, 2, 1);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Visual", typeof(Transform), "m_LocalScale." + axis), scale);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Visual", typeof(Transform), "localEulerAnglesRaw." + axis), AnimationCurve.Linear(0, 0, 2, !pulse && axis == "y" ? 360 : 0));
            }
            AssetDatabase.CreateAsset(clip, folder + "/" + name + ".anim"); return clip;
        }
        private static Material MaterialAsset(string folder, string name, Color color)
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var shaderName = pipeline == null ? "Standard" :
                pipeline.GetType().Name.Contains("HDRender") ? "HDRP/Lit" : "Universal Render Pipeline/Lit";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("当前渲染管线缺少可用材质 Shader。");
            var material = new Material(shader) { name = name, color = color }; AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat"); return material;
        }
    }
}

