using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Timeline;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIBuilder.EditorTools.CreatureStage
{
    [InitializeOnLoad]
    public static class StageRouteOverlay
    {
        private sealed class Display
        {
            public StageRoute route;
            public Color color;
            public Vector3[] points;
            public Vector3[] controls;
        }
        private static readonly List<Display> displays = new List<Display>();
        private static double nextRefresh;
        static StageRouteOverlay()
        {
            SceneView.duringSceneGui += Draw;
            EditorApplication.update += RefreshLines;
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
            Selection.selectionChanged += Invalidate;
        }
        private static void Invalidate() { nextRefresh = 0; }
        private static void RefreshLines()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextRefresh) return;
            // 鼠标事件只用缓存，路线关系与采样在这里统一更新。
            nextRefresh = EditorApplication.timeSinceStartup + (GUIUtility.hotControl != 0 ? .1 : .2);
            var chosen = new Dictionary<StageRoute, StagePerformanceClip>();
            foreach (var director in UnityEngine.Object.FindObjectsOfType<PlayableDirector>())
            {
                if (!(director.playableAsset is TimelineAsset timeline)) continue;
                foreach (var track in timeline.GetOutputTracks().OfType<StagePerformanceTrack>())
                foreach (var clip in track.GetClips())
                {
                    var data = (StagePerformanceClip)clip.asset;
                    if (data.mode != StagePerformanceMode.Move) continue;
                    var route = data.route.Resolve(director);
                    if (route == null) continue;
                    if (!chosen.ContainsKey(route) || clip == TimelineEditor.selectedClip) chosen[route] = data;
                }
            }
            displays.Clear();
            foreach (var route in UnityEngine.Object.FindObjectsOfType<StageRoute>())
            {
                route.previewGroundMode = chosen.TryGetValue(route, out var data) ? (bool?)data.UsesGround(route) : null;
                var line = route.GetComponent<StageRouteLine>();
                if (route.PointCount < 2 || !route.IsUsedByStory() || !route.gameObject.scene.IsValid())
                { if (line != null) line.ApplyDisplay(null, Color.white); continue; }
                var display = new Display { route = route, color = route.DisplayColor, points = new Vector3[65], controls = new Vector3[route.PointCount] };
                for (var i = 0; i < display.controls.Length; i++) display.controls[i] = route.WorldPoint(i);
                try { route.SamplePolyline(display.points); }
                catch (InvalidOperationException) { display.points = null; }
                if (line != null) line.ApplyDisplay(display.points, display.color);
                displays.Add(display);
            }
        }
        private static void Draw(SceneView view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var oldTest = Handles.zTest;
            var oldColor = Handles.color;
            try
            {
                // 编辑辅助线始终可见，地形或草丛不会遮住它。
                Handles.zTest = CompareFunction.Always;
                foreach (var display in displays)
                {
                    var route = display.route;
                    if (route == null || SceneVisibilityManager.instance.IsHidden(route.gameObject)) continue;
                    // 未选中的路线也可直接点选，无需先在 Hierarchy 找到路线物体。
                    if (Selection.activeGameObject != route.gameObject)
                    using (new Handles.DrawingScope(display.color))
                        for (var i = 0; i < display.controls.Length; i++)
                        {
                            var point = display.controls[i];
                            var size = HandleUtility.GetHandleSize(point) * .065f;
                            if (Handles.Button(point, Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap))
                                StageRouteEditor.SelectPoint(route, i);
                        }
                    if (Event.current.type != EventType.Repaint) continue;
                    if (display.points != null)
                    {
                        Handles.color = display.color;
                        Handles.DrawAAPolyLine(3, display.points);
                    }
                    else
                    {
                        Handles.color = new Color(1, .65f, .1f);
                        for (var i = 1; i < display.controls.Length; i++) Handles.DrawDottedLine(display.controls[i - 1], display.controls[i], 4);
                        if (route.groundFailurePosition.HasValue)
                        {
                            var failed = route.groundFailurePosition.Value;
                            Handles.color = Color.red;
                            Handles.DrawWireDisc(failed, Vector3.up, HandleUtility.GetHandleSize(failed) * .1f);
                            Handles.Label(failed, route.name + "：地面/坡度/高度范围不符");
                        }
                    }
                }
            }
            finally { Handles.zTest = oldTest; Handles.color = oldColor; }
        }
    }
}
