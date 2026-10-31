using System;
using System.Linq;
using System.Collections.Generic;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    public static class StageStoryMultiActor
    {
        private static TimelineAsset Timeline(PlayableDirector director)
        {
            if (director == null || !(director.playableAsset is TimelineAsset timeline)) throw new InvalidOperationException("请选择带 Timeline 的场景剧情对象。");
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode()) throw new InvalidOperationException("请先停止运行和 Timeline Preview，再管理角色。");
            return timeline;
        }

        private static void ValidateActor(PlayableDirector director, StageActorDriver actor)
        {
            if (actor == null || actor.animator == null || actor.gameObject.scene != director.gameObject.scene)
                throw new InvalidOperationException("角色必须位于同一场景，且已绑定 Animator。");
        }

        public static StagePerformanceTrack AddActor(PlayableDirector director, StageActorDriver actor)
        {
            var timeline = Timeline(director); ValidateActor(director, actor);
            var existing = timeline.GetOutputTracks().OfType<StagePerformanceTrack>().FirstOrDefault(t => director.GetGenericBinding(t) == actor);
            if (existing != null && (existing.animationTrack != null || existing.GetClips().Any())) return existing;
            if (existing == null && UnityEngine.Object.FindObjectsOfType<PlayableDirector>(true).Any(d => d != director && d.enabled && d.playableAsset is TimelineAsset t &&
                t.GetOutputTracks().OfType<StagePerformanceTrack>().Any(track => d.GetGenericBinding(track) == actor)))
                throw new InvalidOperationException("该角色已有独立剧情，请使用“并入剧情并停用原播放”，以保留片段并避免重复驱动。");
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("添加剧情角色");
            try
            {
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { timeline, director }, "添加剧情角色");
                var track = CreateActorTracks(director, actor);
                Finish(director); Undo.CollapseUndoOperations(group); return track;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        public static StagePerformanceTrack AddSceneObject(PlayableDirector director, GameObject model)
        {
            Timeline(director);
            if (model == null || EditorUtility.IsPersistent(model) || !model.scene.IsValid() || model.scene != director.gameObject.scene)
                throw new InvalidOperationException("请选中与主 Timeline 同一场景中的物体。");
            var existing = model.GetComponentInParent<StageActorDriver>(true);
            if (existing != null) return AddActor(director, existing);
            if (model.GetComponentInChildren<StageActorDriver>(true) != null ||
                model.GetComponentInChildren<PlayableDirector>(true) != null)
                throw new InvalidOperationException("请选择具体模型，不要选择包含剧情或其他角色的场景总节点。");
            if (PrefabUtility.IsPartOfPrefabInstance(model) && !PrefabUtility.IsOutermostPrefabInstanceRoot(model))
                throw new InvalidOperationException("请选择该 Prefab 的最外层根对象，以保留 Prefab 结构。");
            var parent = model.transform.parent;
            if (parent != null && (parent.lossyScale - Vector3.one).sqrMagnitude > .00001f)
                throw new InvalidOperationException("物体的父级世界缩放必须为 1，请先将具体模型放到无缩放的父级下。");
            var animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                var candidates = model.GetComponentsInChildren<Animator>(true);
                if (candidates.Length > 1) throw new InvalidOperationException("选中对象包含多个 Animator，请选择要加入的具体角色模型。");
                animator = candidates.FirstOrDefault();
            }
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("将选中物体加入剧情角色");
            try
            {
                var actorRoot = new GameObject(model.name + " · 路线驱动");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actorRoot, model.scene);
                Undo.RegisterCreatedObjectUndo(actorRoot, "创建剧情角色根节点");
                actorRoot.transform.SetParent(parent, false);
                actorRoot.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
                actorRoot.transform.SetSiblingIndex(model.transform.GetSiblingIndex());
                Undo.SetTransformParent(model.transform, actorRoot.transform, "绑定剧情角色");
                if (animator == null) animator = Undo.AddComponent<Animator>(model);
                var actor = Undo.AddComponent<StageActorDriver>(actorRoot);
                actor.animator = animator;
                EditorUtility.SetDirty(actor);
                var track = AddActor(director, actor);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                return track;
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        private static StagePerformanceTrack CreateActorTracks(PlayableDirector director, StageActorDriver actor, bool withAnimation = true)
        {
            var timeline = (TimelineAsset)director.playableAsset;
            var route = timeline.GetOutputTracks().OfType<StagePerformanceTrack>().FirstOrDefault(t => director.GetGenericBinding(t) == actor);
            if (route == null)
            {
                route = timeline.CreateTrack<StagePerformanceTrack>(null, actor.name + " · 路线与停留");
                Undo.RegisterCreatedObjectUndo(route, "添加剧情角色");
            }
            else Undo.RegisterCompleteObjectUndo(route, "并入角色剧情");
            director.SetGenericBinding(route, actor);
            if (!withAnimation) return route;
            if (route.animationTrack == null)
            {
                var candidates = timeline.GetOutputTracks().OfType<AnimationTrack>().Where(t => director.GetGenericBinding(t) == actor.animator).ToArray();
                if (candidates.Length > 1) throw new InvalidOperationException("该角色有多条动画轨，请先在角色管理中明确关联动画轨。");
                var animation = candidates.FirstOrDefault();
                if (animation == null)
                {
                    animation = timeline.CreateTrack<AnimationTrack>(null, actor.name + " · 动画");
                    animation.trackOffset = TrackOffset.ApplySceneOffsets;
                    Undo.RegisterCreatedObjectUndo(animation, "添加剧情角色");
                }
                route.animationTrack = animation;
            }
            director.SetGenericBinding(route, actor);
            director.SetGenericBinding(route.animationTrack, actor.animator);
            return route;
        }

        public static StagePerformanceTrack[] Import(PlayableDirector target, PlayableDirector source)
        {
            var targetTimeline = Timeline(target); var sourceTimeline = Timeline(source);
            if (target == source || targetTimeline == sourceTimeline || target.gameObject.scene != source.gameObject.scene)
                throw new InvalidOperationException("请选择同场景中另一条独立的角色 Timeline。");
            var sourceTracks = sourceTimeline.GetOutputTracks().OfType<StagePerformanceTrack>().ToArray();
            if (sourceTracks.Length == 0) throw new InvalidOperationException("来源没有角色表演轨。");
            var actors = new HashSet<StageActorDriver>();
            var included = new HashSet<TrackAsset>(sourceTracks);
            foreach (var track in sourceTracks)
            {
                var actor = source.GetGenericBinding(track) as StageActorDriver; ValidateActor(target, actor);
                if (!actors.Add(actor)) throw new InvalidOperationException("来源存在同一角色的重复表演轨，请先整理。");
                if (track.animationTrack != null)
                {
                    if (track.animationTrack.timelineAsset != sourceTimeline || source.GetGenericBinding(track.animationTrack) != actor.animator)
                        throw new InvalidOperationException(track.name + "：关联动画轨或 Animator 绑定不匹配。");
                    included.Add(track.animationTrack);
                }
                if (track.GetClips().Any(c => !(c.asset is StagePerformanceClip))) throw new InvalidOperationException("来源包含不支持的角色片段。");
                foreach (var clip in track.GetClips())
                {
                    var data = (StagePerformanceClip)clip.asset;
                    if (data.mode == StagePerformanceMode.Move ? data.route.Resolve(source) == null : data.station.Resolve(source) == null)
                        throw new InvalidOperationException(clip.displayName + "：来源场景引用缺失，请先修复。");
                }
                var existing = targetTimeline.GetOutputTracks().OfType<StagePerformanceTrack>().Where(t => target.GetGenericBinding(t) == actor).ToArray();
                if (existing.Length > 1 || existing.Any(t => t.GetClips().Any() || t.lockedInHierarchy))
                    throw new InvalidOperationException(actor.name + " 在目标中已有片段或锁定轨道，未覆盖；请选择空的角色轨道或另一目标剧情。");
                if (existing.Any(t => t.animationTrack != null && (t.animationTrack.timelineAsset != targetTimeline || target.GetGenericBinding(t.animationTrack) != actor.animator)))
                    throw new InvalidOperationException(actor.name + " 的目标动画关联不匹配，请先修复。");
                var targetAnimations = targetTimeline.GetOutputTracks().OfType<AnimationTrack>().Where(t => target.GetGenericBinding(t) == actor.animator).ToArray();
                if (targetAnimations.Length > 1 || targetAnimations.Any(t => t.GetClips().Any() || t.GetMarkerCount() > 0 || t.infiniteClip != null || t.lockedInHierarchy))
                    throw new InvalidOperationException(actor.name + " 在目标中已有动画内容或锁定轨道，未覆盖。");
                if (track.animationTrack == null && targetAnimations.Length > 0)
                    throw new InvalidOperationException(actor.name + " 使用旧式片段动画，请并入没有该角色动画轨的目标。");
            }
            // 不静默丢弃音频、信号、子轨或无限动画。此入口只合并角色剧情轨对。
            foreach (var track in sourceTimeline.GetOutputTracks())
                if ((!included.Contains(track) && track.GetClips().Any()) || track.GetMarkerCount() > 0 || track.GetChildTracks().Any() || track.mutedInHierarchy || track.curves != null)
                    throw new InvalidOperationException("来源包含音频、信号、子轨或静音轨，请先在原生 Timeline 单独处理这些内容。");
            if (sourceTimeline.markerTrack != null && sourceTimeline.markerTrack.GetMarkerCount() > 0)
                throw new InvalidOperationException("来源包含 Timeline 标记，请先在原生 Timeline 处理。");
            foreach (var animation in included.OfType<AnimationTrack>())
                if (animation.infiniteClip != null || animation.GetClips().Any(c => !(c.asset is AnimationPlayableAsset) || c.recordable))
                    throw new InvalidOperationException("请先将无限动画或录制片段整理为普通动画片段，再并入。");

            target.Stop(); source.Stop();
            Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("并入多角色剧情");
            try
            {
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { targetTimeline, target, source }, "并入多角色剧情");
                var result = new List<StagePerformanceTrack>();
                foreach (var track in sourceTracks)
                {
                    var actor = (StageActorDriver)source.GetGenericBinding(track);
                    var copy = CreateActorTracks(target, actor, track.animationTrack != null);
                    foreach (var clip in track.GetClips())
                    {
                        var next = copy.CreateClip<StagePerformanceClip>(); CopyClip(clip, next);
                        var old = (StagePerformanceClip)clip.asset; var data = (StagePerformanceClip)next.asset;
                        data.route.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
                        data.station.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
                        target.SetReferenceValue(data.route.exposedName, old.route.Resolve(source));
                        target.SetReferenceValue(data.station.exposedName, old.station.Resolve(source));
                    }
                    if (track.animationTrack != null)
                    {
                        var from = track.animationTrack; var to = copy.animationTrack;
                        Undo.RegisterCompleteObjectUndo(to, "复制角色动画轨");
                        to.position = from.position; to.rotation = from.rotation; to.trackOffset = from.trackOffset;
                        to.avatarMask = from.avatarMask; to.applyAvatarMask = from.applyAvatarMask; to.matchTargetFields = from.matchTargetFields;
                        foreach (var clip in from.GetClips())
                        {
                            var next = clip.asset is StageGaitAnimationAsset ? to.CreateClip<StageGaitAnimationAsset>() : to.CreateClip<AnimationPlayableAsset>();
                            CopyClip(clip, next);
                        }
                        EditorUtility.SetDirty(to);
                    }
                    EditorUtility.SetDirty(copy); result.Add(copy);
                }
                if (sourceTimeline.durationMode == TimelineAsset.DurationMode.FixedLength && sourceTimeline.duration > targetTimeline.duration)
                {
                    targetTimeline.fixedDuration = sourceTimeline.duration;
                    targetTimeline.durationMode = TimelineAsset.DurationMode.FixedLength;
                }
                source.playOnAwake = false; source.enabled = false;
                foreach (var playback in UnityEngine.Object.FindObjectsOfType<StageStoryPlayback>(true).Where(p => p.director == source))
                {
                    Undo.RecordObject(playback, "停用原剧情播放"); playback.enabled = false; EditorUtility.SetDirty(playback);
                }
                EditorUtility.SetDirty(source); Finish(target);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); return result.ToArray();
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        private static void CopyClip(TimelineClip source, TimelineClip target)
        {
            Undo.RegisterCreatedObjectUndo(target.asset, "复制剧情片段");
            EditorUtility.CopySerialized(source.asset, target.asset);
            target.displayName = source.displayName; target.start = source.start; target.duration = source.duration;
            target.clipIn = source.clipIn; target.timeScale = source.timeScale;
            target.easeInDuration = source.easeInDuration; target.easeOutDuration = source.easeOutDuration;
            target.blendInDuration = source.blendInDuration; target.blendOutDuration = source.blendOutDuration;
            target.blendInCurveMode = source.blendInCurveMode; target.blendOutCurveMode = source.blendOutCurveMode;
            target.mixInCurve = new AnimationCurve(source.mixInCurve.keys); target.mixOutCurve = new AnimationCurve(source.mixOutCurve.keys);
            // Unity 2022 的外推属性只有 internal setter，通过轨道序列化属性保留。
            var serialized = new SerializedObject(target.GetParentTrack());
            var clips = target.GetParentTrack().GetClips().ToArray();
            var property = serialized.FindProperty("m_Clips").GetArrayElementAtIndex(Array.IndexOf(clips, target));
            property.FindPropertyRelative("m_PreExtrapolationMode").intValue = (int)source.preExtrapolationMode;
            property.FindPropertyRelative("m_PostExtrapolationMode").intValue = (int)source.postExtrapolationMode;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (source.curves != null) { target.CreateCurves(source.curves.name); Undo.RegisterCreatedObjectUndo(target.curves, "复制片段曲线"); EditorUtility.CopySerialized(source.curves, target.curves); }
            EditorUtility.SetDirty(target.asset);
        }

        private static void Finish(PlayableDirector director)
        {
            EditorUtility.SetDirty(director.playableAsset); EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene); director.RebuildGraph();
            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
        }
    }
}
