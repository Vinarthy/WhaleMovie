using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditorInternal;
using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AIBuilder.EditorTools.CreatureStage
{
    public sealed class StageStoryWindow : EditorWindow
    {
        [SerializeField] private PlayableDirector director;
        [SerializeField] private StagePerformanceTrack selectedTrack;
        private StageActorDriver actorToAdd;
        private PlayableDirector storyToImport;
        private bool showActorTools;
        private int selected;
        private Vector2 scroll;
        private bool previewing, playing, repeatSegment, ripple = true;
        private double time, lastUpdate;
        private string message;
        private ReorderableList clipList;
        private Vector2 listScroll;
        private GUIStyle cardStyle, titleStyle, captionStyle, sectionStyle, rowTitleStyle, workspaceStyle, inspectorTitleStyle;
        private bool styleIsDark;
        private bool advancedSettings;
        private GUIStyle buttonStyle, primaryStyle, fieldStyle, labelStyle, badgeStyle, timeStyle;
        private readonly List<Texture2D> uiTextures = new List<Texture2D>();
        private Color Accent => EditorGUIUtility.isProSkin ? new Color(.28f, .76f, .78f) : new Color(.02f, .42f, .46f);
        private Color HoldColor => EditorGUIUtility.isProSkin ? new Color(.89f, .68f, .36f) : new Color(.58f, .34f, .08f);

        private void EnsureStyles()
        {
            if (cardStyle != null && styleIsDark == EditorGUIUtility.isProSkin) return;
            styleIsDark = EditorGUIUtility.isProSkin;
            foreach (var texture in uiTextures) if (texture != null) DestroyImmediate(texture);
            uiTextures.Clear();
            cardStyle = new GUIStyle { padding = new RectOffset(18, 18, 16, 16), border = new RectOffset(6, 6, 6, 6) };
            cardStyle.normal.background = Surface(styleIsDark ? new Color(.145f, .155f, .165f) : new Color(.89f, .90f, .91f), new Color(.38f, .40f, .42f, .5f));
            workspaceStyle = new GUIStyle { padding = new RectOffset(16, 16, 8, 16) };
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 26, fixedHeight = 36 };
            inspectorTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, wordWrap = false, clipping = TextClipping.Clip, fixedHeight = 30, alignment = TextAnchor.MiddleLeft };
            captionStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true };
            captionStyle.normal.textColor = styleIsDark ? new Color(.66f, .70f, .73f) : new Color(.34f, .38f, .41f);
            sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, fixedHeight = 28 };
            rowTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, clipping = TextClipping.Clip };
            labelStyle = new GUIStyle(EditorStyles.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            fieldStyle = new GUIStyle(EditorStyles.textField) { fontSize = 13, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 3, 3) };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(10, 10, 6, 6) };
            buttonStyle.normal.background = Surface(styleIsDark ? new Color(.24f, .26f, .28f) : new Color(.78f, .80f, .82f), new Color(.44f, .47f, .50f));
            buttonStyle.hover.background = Surface(styleIsDark ? new Color(.30f, .33f, .35f) : new Color(.86f, .88f, .90f), Accent);
            buttonStyle.active.background = buttonStyle.hover.background;
            primaryStyle = new GUIStyle(buttonStyle);
            primaryStyle.normal.background = Surface(styleIsDark ? new Color(.11f, .29f, .32f) : new Color(.62f, .84f, .85f), Accent);
            primaryStyle.hover.background = Surface(styleIsDark ? new Color(.15f, .38f, .41f) : new Color(.72f, .92f, .93f), Accent);
            primaryStyle.active.background = primaryStyle.hover.background;
            badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            timeStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
        }

        // 小尺寸九宫格背景，保留原生控件交互，不引入外部图片资源。
        private Texture2D Surface(Color fill, Color border)
        {
            var texture = new Texture2D(12, 12) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(Mathf.Clamp(x, 4, 7), Mathf.Clamp(y, 4, 7)));
                texture.SetPixel(x, y, distance > 4.6f ? Color.clear : distance > 3.5f ? border : fill);
            }
            texture.Apply(); uiTextures.Add(texture); return texture;
        }

        private void Section(string label)
        {
            GUILayout.Space(16);
            GUILayout.Label(label, sectionStyle);
            var line = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(.5f, .5f, .5f, .22f));
            GUILayout.Space(6);
        }

        private bool AccentButton(string label, params GUILayoutOption[] options)
        {
            return GUILayout.Button(label, primaryStyle, options);
        }

        [MenuItem("Tools/场景剧情/打开编排窗口")]
        public static void Open()
        {
            var window = GetWindow<StageStoryWindow>("场景剧情编排");
            window.minSize = new Vector2(430, 520);
            var inspected = TimelineEditor.inspectedDirector;
            if (inspected != null && inspected.playableAsset is TimelineAsset inspectedTimeline && inspectedTimeline.GetOutputTracks().Any(x => x is StagePerformanceTrack))
            { if (window.director != inspected) { window.StopPreview(); window.director = inspected; window.selectedTrack = null; } }
            if (window.director == null) window.director = FindObjectsOfType<PlayableDirector>().FirstOrDefault(d => d.playableAsset is TimelineAsset t && t.GetOutputTracks().Any(x => x is StagePerformanceTrack));
            window.SyncSelection();
        }

        public static void ShowDirector(PlayableDirector target)
        {
            var window = GetWindow<StageStoryWindow>("场景剧情编排");
            window.StopPreview(); window.director = target; window.selectedTrack = null; window.clipList = null; window.selected = 0; window.time = target != null ? target.time : 0;
            window.minSize = new Vector2(430, 520); window.Repaint();
        }

        public static void StopAllPreviews()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<StageStoryWindow>()) window.StopPreview();
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            Selection.selectionChanged += SyncSelection;
            SceneView.duringSceneGui += SceneGUI;
            Undo.undoRedoPerformed += StopPreview;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorSceneManager.sceneClosing += SceneClosing;
        }
        private void OnDisable()
        {
            StopPreview();
            foreach (var texture in uiTextures) if (texture != null) DestroyImmediate(texture);
            uiTextures.Clear(); cardStyle = null;
            EditorApplication.update -= Tick;
            Selection.selectionChanged -= SyncSelection;
            SceneView.duringSceneGui -= SceneGUI;
            Undo.undoRedoPerformed -= StopPreview;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            EditorSceneManager.sceneClosing -= SceneClosing;
        }
        private void PlayModeChanged(PlayModeStateChange state) { StopPreview(); }
        private void SceneClosing(UnityEngine.SceneManagement.Scene scene, bool removing) { StopPreview(); }

        private StagePerformanceTrack[] Tracks => director != null && director.playableAsset is TimelineAsset timeline
            ? timeline.GetOutputTracks().OfType<StagePerformanceTrack>().ToArray() : Array.Empty<StagePerformanceTrack>();
        private StagePerformanceTrack Track
        {
            get
            {
                var tracks = Tracks;
                if (selectedTrack == null || !tracks.Contains(selectedTrack)) selectedTrack = tracks.FirstOrDefault();
                return selectedTrack;
            }
        }
        public StagePerformanceTrack ActiveTrack => Track;
        public void SelectTrack(StagePerformanceTrack track)
        {
            if (!Tracks.Contains(track)) throw new ArgumentException("该角色轨道不属于当前剧情。");
            if (selectedTrack == track) return;
            StopPreview(); selectedTrack = track; selected = 0; clipList = null;
            scroll = Vector2.zero; listScroll = Vector2.zero; message = null;
            Repaint(); SceneView.RepaintAll();
        }
        private double StoryDuration => director != null && director.playableAsset is TimelineAsset timeline
            ? Math.Max(0, timeline.duration) : 0;
        private TimelineClip[] Clips => Track == null ? Array.Empty<TimelineClip>() : Track.GetClips().OrderBy(c => c.start).ToArray();

        private void SyncSelection()
        {
            // 场景选择优先于 Timeline 遗留的片段选择，避免刚切换又被旧轨道覆盖。
            if (Selection.activeGameObject != null)
            {
                SelectScenePoint(Selection.activeGameObject);
                return;
            }
            var native = TimelineEditor.selectedClip;
            var nativeTrack = Selection.activeObject as TrackAsset ?? native?.GetParentTrack();
            var inspected = TimelineEditor.inspectedDirector;
            if (nativeTrack != null && inspected != null && inspected != director &&
                inspected.playableAsset is TimelineAsset timeline && nativeTrack.timelineAsset == timeline &&
                timeline.GetOutputTracks().OfType<StagePerformanceTrack>().Any())
            {
                StopPreview(); director = inspected; selectedTrack = null; clipList = null;
                selected = 0; time = director.time; Repaint();
            }
            if (TimelineEditor.inspectedDirector == director)
            {
                var next = Tracks.FirstOrDefault(t => t == nativeTrack || t.animationTrack != null && t.animationTrack == nativeTrack);
                if (next == null && nativeTrack is AnimationTrack)
                    next = Tracks.FirstOrDefault(t => director.GetGenericBinding(t) is StageActorDriver actor && director.GetGenericBinding(nativeTrack) == actor.animator);
                if (next != null) SelectTrack(next);
            }
            var clips = Clips;
            var index = Array.FindIndex(clips, c => c == native || c.asset == Selection.activeObject);
            if (index >= 0) { selected = index; Repaint(); SceneView.RepaintAll(); }
        }

        public bool SelectScenePoint(GameObject point)
        {
            if (point == null || director == null || point.scene != director.gameObject.scene) return false;
            var matches = Tracks.SelectMany(t => t.GetClips()).Where(clip =>
            {
                var data = (StagePerformanceClip)clip.asset;
                if (data.mode == StagePerformanceMode.Hold)
                    return data.station.Resolve(director)?.gameObject == point;
                var route = data.route.Resolve(director);
                return route != null && (route.gameObject == point || route.startAnchor?.gameObject == point || route.endAnchor?.gameObject == point);
            }).ToArray();
            if (matches.Length == 0) return false;
            // 共用节点优先保留当前片段，其余按当前角色、时间顺序稳定选择。
            var current = Clips.ElementAtOrDefault(selected);
            var chosen = matches.FirstOrDefault(c => c == current)
                ?? matches.OrderBy(c => c.GetParentTrack() == Track ? 0 : 1).ThenBy(c => c.start).First();
            SelectTrack((StagePerformanceTrack)chosen.GetParentTrack());
            selected = Array.IndexOf(Clips, chosen);
            if (clipList != null) clipList.index = selected;
            // TimelineEditor.selectedClip 会替换 Unity Selection，必须保留场景物体以继续拖动点。
            Repaint(); SceneView.RepaintAll();
            return true;
        }

        public static void SelectSceneRoute(StageRoute route)
        {
            if (route == null) return;
            Selection.activeGameObject = route.gameObject;
            foreach (var window in Resources.FindObjectsOfTypeAll<StageStoryWindow>()) window.SelectScenePoint(route.gameObject);
        }

        private void Tick()
        {
            if (director != null && !previewing && !EditorApplication.isPlayingOrWillChangePlaymode &&
                AnimationMode.InAnimationMode() && TimelineEditor.inspectedDirector == director)
            {
                if (Math.Abs(time - director.time) > .000001) { time = director.time; Repaint(); }
            }
            if (!playing || director == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var now = EditorApplication.timeSinceStartup;
            time += Math.Min(0.1, now - lastUpdate);
            lastUpdate = now;
            var clips = Clips;
            var end = repeatSegment && clips.Length > 0 ? clips[Mathf.Clamp(selected, 0, clips.Length - 1)].end : StoryDuration;
            if (time >= end)
            {
                if (repeatSegment && clips.Length > 0) time = clips[Mathf.Clamp(selected, 0, clips.Length - 1)].start;
                else { time = end; playing = false; }
            }
            Evaluate();
            Repaint();
        }

        public void StopPreview()
        {
            playing = false;
            if (previewing && director != null) director.Stop();
            previewing = false;
            SceneView.RepaintAll();
        }

        private void Evaluate()
        {
            if (director == null) return;
            if (EditorApplication.isPlaying) { director.time = time; director.Evaluate(); return; }
            if (AnimationMode.InAnimationMode() && !previewing)
            { message = "请先关闭原生 Timeline 的 Preview，再用此窗口预览；也可直接在 Timeline 中拖动时间。"; playing = false; return; }
            previewing = true;
            director.time = time;
            director.Evaluate();
            SceneView.RepaintAll();
        }

        private void OnGUI()
        {
            EnsureStyles();
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 160;
            try { DrawWorkspace(); }
            finally { EditorGUIUtility.labelWidth = oldLabelWidth; }
        }

        private void DrawWorkspace()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height),
                EditorGUIUtility.isProSkin ? new Color(.16f, .17f, .18f) : new Color(.82f, .83f, .84f));
            GUILayout.BeginVertical(workspaceStyle);
            var wide = position.width >= 850;
            DrawHeader(wide);
            if (Track == null)
            {
                GUILayout.Space(12);
                GUILayout.BeginVertical(cardStyle);
                GUILayout.Label("开始编排一段剧情", sectionStyle);
                GUILayout.Label("指定带角色表演轨的剧情对象，或从基础示例开始。选中场景角色后，也可以为它创建剧情。", EditorStyles.wordWrappedLabel);
                GUILayout.Space(12);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (AccentButton("创建基础几何体示例", GUILayout.Height(32))) StageBasicExample.CreateExample();
                    using (new EditorGUI.DisabledScope(Selection.activeGameObject == null || EditorUtility.IsPersistent(Selection.activeGameObject)))
                        if (GUILayout.Button("为选中对象创建剧情", GUILayout.Height(28))) StageBasicExample.CreateForSelection();
                }
                GUILayout.EndVertical();
                GUILayout.EndVertical();
                return;
            }
            DrawTransport(wide);
            DrawActorToolbar();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("地面绑定 · 扫描 / 排除植被 / 重新绑定", GUILayout.Height(28)))
                    StageGroundBindingWindow.Open(director.gameObject.scene);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Warning);
            GUILayout.Space(8);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || Track != null && Track.lockedInHierarchy))
            {
                if (wide)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical(cardStyle, GUILayout.Width(Mathf.Clamp(position.width * .38f, 300, 540)), GUILayout.ExpandHeight(true));
                    DrawSequence(true);
                    GUILayout.EndVertical();
                    GUILayout.Space(8);
                    GUILayout.BeginVertical(cardStyle, GUILayout.ExpandHeight(true));
                    DrawInspectorHeader();
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawInspector();
                    EditorGUILayout.EndScrollView();
                    DrawSceneHint();
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
                else
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    GUILayout.BeginVertical(cardStyle);
                    DrawSequence(false);
                    GUILayout.EndVertical();
                    GUILayout.Space(8);
                    GUILayout.BeginVertical(cardStyle);
                    DrawInspectorHeader();
                    DrawInspector();
                    DrawSceneHint();
                    GUILayout.EndVertical();
                    EditorGUILayout.EndScrollView();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawHeader(bool wide)
        {
            var rect = GUILayoutUtility.GetRect(0, wide ? 72 : 124, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(rect.x + 12, rect.y + 10, 240, 36), "场景剧情", titleStyle);
            GUI.Label(new Rect(rect.x + 13, rect.y + 47, 220, 20), "SCENE STORY", captionStyle);
            float left = wide ? rect.x + rect.width * .43f : rect.x + 10;
            float y = wide ? rect.y + 23 : rect.y + 82;
            float available = rect.xMax - left;
            GUI.Label(new Rect(left, y, 66, 30), "场景对象", labelStyle);
            EditorGUI.BeginChangeCheck();
            var next = (PlayableDirector)EditorGUI.ObjectField(new Rect(left + 68, y, available - 226, 30), director, typeof(PlayableDirector), true);
            if (EditorGUI.EndChangeCheck()) { StopPreview(); director = next; selectedTrack = null; selected = 0; time = 0; clipList = null; message = null; }
            using (new EditorGUI.DisabledScope(Track == null))
                if (GUI.Button(new Rect(rect.xMax - 148, y, 116, 30), "打开 Timeline", buttonStyle))
                { StopPreview(); var timelineWindow = TimelineEditor.GetOrCreateWindow(); timelineWindow.SetTimeline(director); timelineWindow.Show(); timelineWindow.Focus(); }
            if (GUI.Button(new Rect(rect.xMax - 28, y, 28, 30), new GUIContent("?", "使用教程"), buttonStyle)) StageTutorial.Open();
        }

        private void DrawTransport(bool wide)
        {
            var rect = GUILayoutUtility.GetRect(0, wide ? 112 : 152, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none, cardStyle);
            var clips = Clips;
            double duration = StoryDuration;
            float buttonWidth = wide ? 124 : 116;
            var playRect = new Rect(rect.x + 16, rect.y + 20, buttonWidth, wide ? 60 : 42);
            var stopRect = new Rect(playRect.xMax + 8, playRect.y, wide ? 108 : 116, playRect.height);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || duration <= 0))
            {
                if (GUI.Button(playRect, playing ? "Ⅱ  暂停预览" : "▶  播放预览", primaryStyle))
                {
                    if (time >= duration) time = 0;
                    playing = !playing; lastUpdate = EditorApplication.timeSinceStartup; Evaluate();
                }
                if (GUI.Button(stopRect, new GUIContent("↻  恢复角色", "停止预览并恢复角色"), buttonStyle)) StopPreview();
            }
            float left = wide ? stopRect.xMax + 20 : rect.x + 16;
            float y = wide ? rect.y + 16 : rect.y + 70;
            float width = wide ? rect.xMax - left - 152 : rect.width - 32;
            EditorGUI.BeginChangeCheck();
            var typedTime = EditorGUI.DelayedTextField(new Rect(left, y, 82, 28), time.ToString("00.00", System.Globalization.CultureInfo.InvariantCulture), timeStyle);
            if (EditorGUI.EndChangeCheck() && double.TryParse(typedTime, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var requestedTime) && StageMotionPlan.IsFinite(requestedTime))
            { time = Math.Max(0, Math.Min(duration, requestedTime)); playing = false; Evaluate(); }
            GUI.Label(new Rect(left + 86, y + 3, width - 86, 24), $"/  {duration:00.00} 秒", labelStyle);
            DrawOverview(clips, duration, new Rect(left, y + 36, width, 12));
            EditorGUI.BeginChangeCheck();
            var nextTime = GUI.HorizontalSlider(new Rect(left, y + 65, width, 16), (float)time, 0, (float)duration);
            if (EditorGUI.EndChangeCheck()) { time = nextTime; playing = false; Evaluate(); }
            float optionsX = wide ? rect.xMax - 138 : stopRect.xMax + 12;
            float optionsY = rect.y + 24;
            repeatSegment = GUI.Toggle(new Rect(optionsX, optionsY, 132, 24), repeatSegment, "循环当前片段");
            ripple = GUI.Toggle(new Rect(optionsX, optionsY + 30, 132, 24), ripple, new GUIContent("顺延后续片段", "改时长后顺延后续片段"));
        }

        private void DrawSequence(bool independentScroll)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("剧情片段", inspectorTitleStyle);
            GUILayout.Label($"{Clips.Length:00}", badgeStyle, GUILayout.Width(34), GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            GUILayout.Label(new GUIContent("⋮", "拖动片段排序，自动首尾相接"), captionStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
            if (independentScroll) listScroll = EditorGUILayout.BeginScrollView(listScroll);
            DrawClipList(Clips);
            if (independentScroll) EditorGUILayout.EndScrollView();
            GUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            if (AccentButton("+  停留", GUILayout.Height(48))) AddClip(false);
            if (AccentButton("+  移动", GUILayout.Height(48))) AddClip(true);
            DrawDeleteButton(Clips);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspectorHeader()
        {
            var clips = Clips;
            if (clips.Length == 0) return;
            selected = Mathf.Clamp(selected, 0, clips.Length - 1);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("片段设置  /  " + clips[selected].displayName, clips[selected].displayName), inspectorTitleStyle, GUILayout.MinWidth(80));
            DrawClipActions(clips[selected]);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private void DrawInspector()
        {
            var clips = Clips;
            if (clips.Length > 0)
            {
                selected = Mathf.Clamp(selected, 0, clips.Length - 1);
                DrawClip(clips[selected]);
                if (advancedSettings) DrawGaitControls(clips[selected]);
            }
            else
            {
                GUILayout.Label("还没有剧情片段", sectionStyle);
                GUILayout.Label("添加停留表演或移动表演，开始设置动作与场景位置。", EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawSceneHint()
        {
            GUILayout.Space(10);
            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label("提示：在 Scene 视图中选中路线后，使用移动、旋转工具调整场景位置与方向。", captionStyle);
            GUILayout.EndVertical();
        }

        private void DrawOverview(TimelineClip[] clips, double duration, Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, .18f));
            if (duration <= 0) return;
            for (var i = 0; i < clips.Length; i++)
            {
                var item = clips[i];
                var block = new Rect(rect.x + (float)(item.start / duration) * rect.width, rect.y,
                    Mathf.Max(1, (float)(item.duration / duration) * rect.width - 2), rect.height);
                var color = ((StagePerformanceClip)item.asset).mode == StagePerformanceMode.Move ? Accent : HoldColor;
                EditorGUI.DrawRect(block, color);
                if (i == selected) EditorGUI.DrawRect(new Rect(block.x, block.yMax, block.width, 2), Color.white);
                GUI.Label(block, new GUIContent("", item.displayName + " · 点击选中片段"));
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && block.Contains(Event.current.mousePosition))
                {
                    selected = i; TimelineEditor.selectedClip = item;
                    Event.current.Use(); Repaint(); SceneView.RepaintAll();
                }
            }
            var x = rect.x + Mathf.Clamp01((float)(time / duration)) * (rect.width - 2);
            EditorGUI.DrawRect(new Rect(x, rect.y - 3, 2, rect.height + 6), EditorGUIUtility.isProSkin ? Color.white : Color.black);
            float previousLabelX = float.NegativeInfinity;
            foreach (var item in clips)
            {
                var labelX = rect.x + (float)(item.start / duration) * rect.width;
                if (labelX - previousLabelX < 48 || labelX > rect.xMax - 64) continue;
                GUI.Label(new Rect(labelX, rect.yMax + 1, 46, 16), item.start.ToString("0.0"), captionStyle);
                previousLabelX = labelX;
            }
            GUI.Label(new Rect(rect.xMax - 32, rect.yMax + 1, 38, 16), duration.ToString("0.0"), captionStyle);
        }

        private void DrawActorToolbar()
        {
            GUILayout.Space(8);
            GUILayout.BeginVertical(cardStyle);
            var tracks = Tracks;
            GUILayout.BeginHorizontal();
            GUILayout.Label("当前角色轨道", GUILayout.Width(90));
            var labels = tracks.Select((t, i) => $"{i + 1:00}  {t.name}  /  {(director.GetGenericBinding(t) is StageActorDriver actor ? actor.name : "未绑定角色")}").ToArray();
            var index = Array.IndexOf(tracks, Track);
            var next = EditorGUILayout.Popup(index, labels, GUILayout.Height(24));
            if (next >= 0 && next != index)
            {
                SelectTrack(tracks[next]);
                TimelineEditor.GetOrCreateWindow().SetTimeline(director);
                Selection.activeObject = tracks[next];
            }
            showActorTools = GUILayout.Toggle(showActorTools, "角色管理", buttonStyle, GUILayout.Width(88), GUILayout.Height(26));
            GUILayout.EndHorizontal();
            if (Track != null && director.GetGenericBinding(Track) is StageActorDriver colorActor)
            {
                var actorSettings = new SerializedObject(colorActor);
                actorSettings.Update();
                EditorGUILayout.PropertyField(actorSettings.FindProperty("routeColor"), new GUIContent("路径线颜色", "当前角色的所有移动路线共用此颜色；新增和合并路线自动跟随。"));
                if (actorSettings.ApplyModifiedProperties()) SceneView.RepaintAll();
            }
            if (Track != null && Track.lockedInHierarchy) EditorGUILayout.HelpBox("当前角色轨道已锁定，请在 Timeline 中解锁后编辑。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || Selection.activeGameObject == null))
                if (GUILayout.Button(new GUIContent("＋ 将选中物体加入新角色", "选中 Hierarchy 中的模型，自动配置角色并加入当前主 Timeline"), buttonStyle, GUILayout.Height(28)))
                {
                    var model = Selection.activeGameObject;
                    try
                    {
                        if (!StopEditingPreview()) throw new InvalidOperationException("请先停止运行和 Timeline 预览。");
                        SelectTrack(StageStoryMultiActor.AddSceneObject(director, model));
                        message = "已加入当前 Timeline，可添加停留或移动片段。";
                    }
                    catch (Exception error) { message = error.Message; }
                }
            if (showActorTools)
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                GUILayout.Space(8);
                actorToAdd = (StageActorDriver)EditorGUILayout.ObjectField("加入已有角色", actorToAdd, typeof(StageActorDriver), true);
                using (new EditorGUI.DisabledScope(actorToAdd == null))
                    if (GUILayout.Button("添加 / 选中角色轨道", buttonStyle, GUILayout.Height(26)))
                    {
                        try { StopAllPreviews(); SelectTrack(StageStoryMultiActor.AddActor(director, actorToAdd)); }
                        catch (Exception error) { message = error.Message; }
                    }
                storyToImport = (PlayableDirector)EditorGUILayout.ObjectField("已有角色剧情", storyToImport, typeof(PlayableDirector), true);
                EditorGUILayout.HelpBox("并入会复制角色片段与动画轨，保留原 Timeline 和场景路线；原剧情的自动播放会停用，由当前 Timeline 统一驱动。可用 Undo 撤销。", MessageType.None);
                using (new EditorGUI.DisabledScope(storyToImport == null || storyToImport == director))
                    if (GUILayout.Button("并入剧情并停用原播放", buttonStyle, GUILayout.Height(28)))
                    {
                        try { StopAllPreviews(); var imported = StageStoryMultiActor.Import(director, storyToImport); SelectTrack(imported[0]); message = $"已并入 {imported.Length} 个角色，由当前 Timeline 统一预览。"; }
                        catch (Exception error) { message = error.Message; }
                    }
                if (Track != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var linked = (AnimationTrack)EditorGUILayout.ObjectField("关联动画轨", Track.animationTrack, typeof(AnimationTrack), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (linked != null && linked.timelineAsset != director.playableAsset) message = "动画轨必须来自当前 Timeline。";
                        else if (linked != null && (!(director.GetGenericBinding(Track) is StageActorDriver boundActor) || director.GetGenericBinding(linked) != boundActor.animator))
                            message = "动画轨必须绑定当前角色的 Animator，不能关联其他角色的动画轨。";
                        else
                        {
                            StopPreview(); Undo.RecordObject(Track, "关联角色动画轨");
                            Track.animationTrack = linked; EditorUtility.SetDirty(Track); director.RebuildGraph();
                        }
                    }
                }
            }
            GUILayout.EndVertical();
        }

        private AnimationCurve DrawSpeedCurve(AnimationCurve curve)
        {
            var row = EditorGUILayout.GetControlRect(false, 64);
            var rect = EditorGUI.PrefixLabel(row, new GUIContent("移动速度曲线"), labelStyle);
            curve = EditorGUI.CurveField(rect, curve, Accent, new Rect(0, 0, 1, 2));
            // 只覆盖缩略图绘制；点击与曲线编辑仍由原生 CurveField 处理。
            if (Event.current.type != EventType.Repaint) return curve;
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(.12f, .14f, .15f) : new Color(.87f, .89f, .90f));
            var grid = new Color(.5f, .55f, .58f, .23f);
            for (int i = 0; i <= 12; i++) EditorGUI.DrawRect(new Rect(rect.x + i * (rect.width - 1) / 12, rect.y, 1, rect.height), grid);
            for (int i = 0; i <= 4; i++) EditorGUI.DrawRect(new Rect(rect.x, rect.y + i * (rect.height - 1) / 4, rect.width, 1), grid);
            var points = new Vector3[65];
            for (int i = 0; i < points.Length; i++)
            {
                float t = i / 64f;
                points[i] = new Vector3(rect.x + t * (rect.width - 1), rect.yMax - 1 - Mathf.Clamp01(curve.Evaluate(t) / 2) * (rect.height - 2));
            }
            Handles.BeginGUI();
            var oldColor = Handles.color;
            Handles.color = Accent; Handles.DrawAAPolyLine(2, points); Handles.color = oldColor;
            Handles.EndGUI();
            foreach (var key in curve.keys)
                if (key.time >= 0 && key.time <= 1)
                    EditorGUI.DrawRect(new Rect(rect.x + key.time * (rect.width - 4), rect.yMax - 3 - Mathf.Clamp01(key.value / 2) * (rect.height - 6), 4, 4), Accent);
            return curve;
        }

        private void DrawClipList(TimelineClip[] clips)
        {
            if (clipList == null || !clipList.list.Cast<TimelineClip>().SequenceEqual(clips))
            {
                clipList = new ReorderableList(new List<TimelineClip>(clips), typeof(TimelineClip), true, true, false, false);
                clipList.elementHeight = 44;
                clipList.showDefaultBackground = false;
                clipList.headerHeight = 0;
                clipList.footerHeight = 0;
                clipList.drawElementBackgroundCallback = (rect, index, active, focused) =>
                {
                    if (Event.current.type != EventType.Repaint) return;
                    var chosen = index == selected;
                    var tint = chosen ? Accent : Color.gray;
                    tint.a = chosen ? .19f : (index % 2 == 0 ? .055f : .025f);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2, rect.width, rect.height - 4), tint);
                    if (chosen) EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2, 3, rect.height - 4), Accent);
                };
                clipList.drawElementCallback = (rect, index, active, focused) =>
                {
                    var item = (TimelineClip)clipList.list[index];
                    var moving = ((StagePerformanceClip)item.asset).mode == StagePerformanceMode.Move;
                    EditorGUI.LabelField(new Rect(rect.x + 5, rect.y + 9, 32, 26), $"{index + 1:00}", timeStyle);
                    var nameRect = new Rect(rect.x + 48, rect.y + 10, rect.width - 195, 24);
                    EditorGUI.LabelField(nameRect, new GUIContent(item.displayName, item.displayName), rowTitleStyle);
                    var old = GUI.contentColor;
                    GUI.contentColor = moving ? Accent : HoldColor;
                    var badge = new Rect(rect.xMax - 140, rect.y + 11, 38, 22);
                    GUI.Box(badge, GUIContent.none, EditorStyles.helpBox);
                    EditorGUI.LabelField(badge, moving ? "移动" : "停留", badgeStyle);
                    GUI.contentColor = old;
                    EditorGUI.LabelField(new Rect(rect.xMax - 95, rect.y + 10, 94, 24), $"{item.start:0.0} – {item.end:0.0} 秒", captionStyle);
                };
                clipList.onSelectCallback = list =>
                {
                    if (list.index < 0 || list.index >= list.count) return;
                    selected = list.index;
                    TimelineEditor.selectedClip = (TimelineClip)list.list[list.index];
                    SceneView.RepaintAll();
                };
                clipList.onReorderCallbackWithDetails = (list, from, to) =>
                {
                    StopPreview();
                    var moved = (TimelineClip)list.list[to];
                    try { ReorderClip(Track, from, to); message = null; }
                    catch (InvalidOperationException error) { message = error.Message; clipList = null; Repaint(); return; }
                    selected = to;
                    time = moved.start;
                    TimelineEditor.selectedClip = moved;
                    director.RebuildGraph();
                    TimelineEditor.Refresh(RefreshReason.ContentsModified);
                    SceneView.RepaintAll();
                    Repaint();
                };
            }
            if (GUIUtility.hotControl == 0)
                clipList.index = clips.Length == 0 ? -1 : Mathf.Clamp(selected, 0, clips.Length - 1);
            clipList.DoLayoutList();
        }

        private void DrawDeleteButton(TimelineClip[] clips)
        {
            using (new EditorGUI.DisabledScope(clips.Length == 0))
            {
                if (GUILayout.Button(new GUIContent("删除片段", "删除选中片段，后续时间前移"), buttonStyle, GUILayout.Height(48)))
                {
                    StopPreview();
                    var index = Mathf.Clamp(selected, 0, clips.Length - 1);
                    DeleteClip(Track, index);
                    var remaining = Clips;
                    selected = Mathf.Clamp(index, 0, Math.Max(0, remaining.Length - 1));
                    TimelineEditor.selectedClip = remaining.Length == 0 ? null : remaining[selected];
                    time = remaining.Length == 0 ? 0 : remaining[selected].start;
                    director.time = time;
                    director.RebuildGraph();
                    clipList = null;
                    StageStoryHierarchy.Refresh(EditorPrefs.GetBool("AIBuilder.CreatureStage.ShowUnusedObjects", false));
                    TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
                    SceneView.RepaintAll();
                    Repaint();
                }
            }
        }

        public static void DeleteClip(StagePerformanceTrack track, int index)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            var ordered = track.GetClips().OrderBy(c => c.start).ToList();
            if (index < 0 || index >= ordered.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var start = ordered[0].start;
            var removed = ordered[index];
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(track, "删除剧情片段");
            track.DeleteClip(removed);
            ordered.RemoveAt(index);
            foreach (var item in ordered) { item.start = start; start += item.duration; }
            EditorUtility.SetDirty(track);
            Undo.FlushUndoRecordObjects();
        }

        public static void ReorderClip(StagePerformanceTrack track, int from, int to)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            var ordered = track.GetClips().OrderBy(c => c.start).ToList();
            if (from < 0 || to < 0 || from >= ordered.Count || to >= ordered.Count)
                throw new ArgumentOutOfRangeException(nameof(to));
            if (from == to) return;
            if (track.lockedInHierarchy || !StopEditingPreview())
                throw new InvalidOperationException("轨道已锁定或仍有动画预览，请退出预览并解锁后排序。");
            if (ordered.Any(c => !StageMotionPlan.IsFinite(c.duration) || c.duration <= 0))
                throw new InvalidOperationException("片段时长无效，无法排序。");
            var start = ordered[0].start;
            var moved = ordered[from];
            ordered.RemoveAt(from);
            ordered.Insert(to, moved);
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(track, "拖动剧情片段排序");
            foreach (var item in ordered) { item.start = start; start += item.duration; }
            EditorUtility.SetDirty(track);
            Undo.FlushUndoRecordObjects();
        }

        private void DrawClip(TimelineClip clip)
        {
            var data = (StagePerformanceClip)clip.asset;
            Section("基本信息");
            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField("片段名称", clip.displayName, fieldStyle, GUILayout.Height(28));
            GUILayout.Space(8);
            var oldWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 104;
            EditorGUILayout.BeginHorizontal();
            var start = EditorGUILayout.DoubleField("开始时间（秒）", clip.start, fieldStyle, GUILayout.Height(28));
            GUILayout.Space(14);
            var duration = EditorGUILayout.DoubleField("持续时间（秒）", clip.duration, fieldStyle, GUILayout.Height(28));
            EditorGUILayout.EndHorizontal();
            EditorGUIUtility.labelWidth = oldWidth;
            GUILayout.Space(8);
            var mode = (StagePerformanceMode)EditorGUILayout.Popup("表演类型", (int)data.mode, new[] { "停留表演", "沿路线移动" }, GUILayout.Height(26));
            Section("空间与动作");
            var nativeAnimation = Track.animationTrack != null;
            var animation = data.animation;
            var actor = director.GetGenericBinding(Track) as StageActorDriver;
            if (!nativeAnimation)
            {
                animation = (AnimationClip)EditorGUILayout.ObjectField("动作", animation, typeof(AnimationClip), false);
                if (actor != null && actor.profile != null && actor.profile.actions != null)
                {
                    var actions = actor.profile.actions.Where(a => a != null && a.clip != null).ToArray();
                    var choices = new[] { "从动作库选择…" }.Concat(actions.Select(a => a.displayName)).ToArray();
                    var chosen = EditorGUILayout.Popup("动作库", 0, choices);
                    if (chosen > 0) animation = actions[chosen - 1].clip;
                }
            }
            var point = data.station.Resolve(director);
            var route = data.route.Resolve(director);
            var followPrevious = data.followPrevious;
            if (mode == StagePerformanceMode.Hold)
            {
                followPrevious = EditorGUILayout.Toggle("自动跟随上一段终点", followPrevious);
                var following = followPrevious && StagePerformanceTrack.PlacementSource(clip) != clip;
                using (new EditorGUI.DisabledScope(following))
                    point = (Transform)EditorGUILayout.ObjectField(following ? "备用独立停留点" : "停留点", point, typeof(Transform), true);
                if (following) EditorGUILayout.HelpBox("连续停留向前跟随移动终点；前面没有移动时，以第一段停留点为基准。关闭自动跟随后可独立摆放。", MessageType.Info);
            }
            else
            {
                route = (StageRoute)EditorGUILayout.ObjectField("场景路线", route, typeof(StageRoute), true, GUILayout.Height(28));
                GUILayout.Space(10);
                if (route != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var snap = EditorGUILayout.Toggle("本片段贴合地形", data.UsesGround(route), GUILayout.Height(26));
                    if (EditorGUI.EndChangeCheck())
                    {
                        StopPreview(); Undo.RecordObject(data, "切换片段贴合地形");
                        data.groundMode = snap ? StageGroundMode.Ground : StageGroundMode.FreeSpace;
                        route.previewGroundMode = snap; route.Invalidate(); EditorUtility.SetDirty(data);
                        EditorSceneManager.MarkSceneDirty(route.gameObject.scene); SceneView.RepaintAll();
                    }
                    if (!data.UsesGround(route)) EditorGUILayout.HelpBox("当前为空中路线：直接使用节点 XYZ 坐标，可自由调整高度。", MessageType.Info);
                }
            }
            var loop = data.loop;
            var rate = data.playbackRate;
            if (!nativeAnimation)
            {
                loop = EditorGUILayout.Toggle("循环动作", loop);
                rate = EditorGUILayout.FloatField("动画倍率", rate);
            }
            Section("速度与衔接");
            var moveCurve = new AnimationCurve(data.movementSpeed == null ? AnimationCurve.Linear(0, 1, 1, 1).keys : data.movementSpeed.keys);
            var animCurve = new AnimationCurve(data.animationSpeed == null ? AnimationCurve.Linear(0, 1, 1, 1).keys : data.animationSpeed.keys);
            if (mode == StagePerformanceMode.Move)
            {
                if (route != null)
                {
                    try
                    {
                        var average = route.LengthForGround(data.UsesGround(route)) / duration;
                        var requested = EditorGUILayout.DoubleField("平均移动速度（米/秒）", average, fieldStyle, GUILayout.Height(28));
                        if (requested > 0 && StageMotionPlan.IsFinite(requested) && Math.Abs(requested - average) > 0.000001)
                            duration = route.LengthForGround(data.UsesGround(route)) / requested;
                    }
                    catch (InvalidOperationException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Warning); }
                }
                GUILayout.Space(8);
                moveCurve = DrawSpeedCurve(moveCurve);
                GUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("匀速", buttonStyle, GUILayout.Height(28))) { moveCurve = AnimationCurve.Linear(0, 1, 1, 1); GUI.changed = true; }
                if (GUILayout.Button("缓起缓停", buttonStyle, GUILayout.Height(28)))
                {
                    moveCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.2f, 1), new Keyframe(0.8f, 1), new Keyframe(1, 0));
                    GUI.changed = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (!nativeAnimation)
            using (new EditorGUI.DisabledScope(data.distanceSync && mode == StagePerformanceMode.Move))
                animCurve = EditorGUILayout.CurveField("动画速度曲线", animCurve, Color.cyan, new Rect(0, 0, 1, 2));
            GUILayout.Space(14);
            var changed = GUI.changed;
            advancedSettings = EditorGUILayout.Foldout(advancedSettings, "高级设置 · 朝向、衔接与步态", true);
            GUI.changed = changed;
            var transition = data.transitionDuration;
            var offset = data.facingOffset;
            var groundTransition = data.groundTransitionDuration;
            var facingTransition = data.facingTransitionDuration;
            var smoothFacing = data.smoothFacingTransition;
            var animatedFacing = data.animatedFacing;
            var facingCurve = data.facingCurve;
            var sync = data.distanceSync;
            var cycleDistance = data.cycleDistance;
            if (advancedSettings)
            {
                EditorGUILayout.HelpBox("速度曲线横轴 0–1 为本段时间，纵轴为相对速度；按曲线积分分配路程，负值按零处理。当前尚未实现地形脚锁及接触校准。", MessageType.None);
                if (mode == StagePerformanceMode.Move)
                    groundTransition = EditorGUILayout.FloatField("贴地过渡时间（秒）", groundTransition);
                facingTransition = EditorGUILayout.FloatField("朝向过渡时间（秒）", facingTransition);
                smoothFacing = EditorGUILayout.Toggle("平滑加减速转向", smoothFacing);
                EditorGUILayout.HelpBox("从上段末朝向过渡到本段朝向；0 为立即切换，最长为本段时长。贴地过渡从上段终点高度逐渐落到地面。", MessageType.None);
                if (!nativeAnimation) transition = EditorGUILayout.FloatField("动画混合时间（秒）", transition);
                animatedFacing = EditorGUILayout.Toggle("按时间变化朝向", animatedFacing);
                if (animatedFacing)
                {
                    facingCurve = EditorGUILayout.CurveField("朝向偏移曲线", facingCurve, Color.cyan, new Rect(0, -180, (float)duration, 360));
                    EditorGUILayout.HelpBox("横轴为本片段内秒数，纵轴为偏移角度。0 度沿路线前进方向；可添加关键点实现转向、保持、回正。", MessageType.None);
                }
                else offset = EditorGUILayout.FloatField("朝向偏移（度）", offset);
                if (GUILayout.Button("转向后回正 · 90° 预设"))
                {
                    facingCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe((float)duration * .25f, 90),
                        new Keyframe((float)duration * .5f, 90), new Keyframe((float)duration, 0));
                    animatedFacing = true;
                    GUI.changed = true;
                }
                if (!nativeAnimation)
                {
                    sync = EditorGUILayout.Toggle("按校准步幅同步", sync);
                    if (sync && mode == StagePerformanceMode.Move)
                        EditorGUILayout.HelpBox("同步模式由移动距离驱动动画，忽略动画倍率和动画速度曲线。独立调节动画速度请关闭同步。", MessageType.Info);
                    if (sync) cycleDistance = EditorGUILayout.FloatField("一个循环走多远（米）", cycleDistance);
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                if (!StageMotionPlan.IsFinite(start) || start < 0 || !StageMotionPlan.IsFinite(duration) || duration < 0.05 || !StageMotionPlan.IsFinite(rate) || rate <= 0)
                    message = "时间和倍率无效：时长至少 0.05 秒，开始不能为负。";
                else
                {
                    StopPreview();
                    Undo.RecordObjects(new UnityEngine.Object[] { Track, data, director }, "编辑剧情片段");
                    var delta = duration - clip.duration;
                    if (ripple && Math.Abs(delta) > 0.000001) foreach (var other in Clips) if (other != clip && other.start >= clip.end - 0.000001) other.start += delta;
                    clip.start = start; clip.duration = duration; clip.displayName = name;
                    data.followPrevious = followPrevious;
                    data.animation = animation; data.mode = mode; data.loop = loop; data.playbackRate = rate;
                    data.groundTransitionDuration = StageMotionPlan.IsFinite(groundTransition) ? Mathf.Max(0, groundTransition) : 0;
                    data.facingTransitionDuration = StageMotionPlan.IsFinite(facingTransition) ? Mathf.Max(0, facingTransition) : 0;
                    data.smoothFacingTransition = smoothFacing;
                    data.animatedFacing = animatedFacing; data.facingCurve = facingCurve;
                    data.facingOffset = offset; data.distanceSync = sync; data.cycleDistance = cycleDistance;
                    data.movementSpeed = moveCurve; data.animationSpeed = animCurve;
                    data.transitionDuration = StageMotionPlan.IsFinite(transition) ? Mathf.Max(0, transition) : 0;
                    Bind(data, route, point);
                    EditorUtility.SetDirty(data); EditorUtility.SetDirty(Track); EditorUtility.SetDirty(director);
                    EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
                    director.RebuildGraph();
                    message = null;
                    TimelineEditor.Refresh(RefreshReason.ContentsModified);
                }
            }
            if (advancedSettings && Track.animationTrack == null && GUILayout.Button("拆分动画到 Timeline 独立轨道", buttonStyle, GUILayout.Height(28)))
            {
                StopPreview();
                if (AnimationMode.InAnimationMode()) message = "请先关闭 Timeline Preview 再拆分。";
                else
                {
                    director.Stop(); StageAnimationTrackSetup.Create(director, Track); director.RebuildGraph();
                    TimelineEditor.GetOrCreateWindow().SetTimeline(director);
                    TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
                }
            }
        }

        private void DrawGaitControls(TimelineClip routeClip)
        {
            if (Track.animationTrack == null || ((StagePerformanceClip)routeClip.asset).mode != StagePerformanceMode.Move) return;
            Section("动画步态同步");
            foreach (var clip in Track.animationTrack.GetClips().Where(c => c.start < routeClip.end && c.end > routeClip.start).ToArray())
            {
                if (!(clip.asset is AnimationPlayableAsset animation) || animation.clip == null) continue;
                EditorGUILayout.LabelField($"{clip.start:F1}–{clip.end:F1} 秒  {clip.displayName}");
                var gait = animation as StageGaitAnimationAsset;
                EditorGUI.BeginChangeCheck();
                var sync = EditorGUILayout.Toggle("动画跟随移动距离", gait != null && gait.synchronizeDistance);
                var distance = gait != null ? gait.cycleDistance : 1f;
                if (sync) distance = EditorGUILayout.FloatField("一循环前进距离（米）", distance);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!StageMotionPlan.IsFinite(distance) || distance <= 0) { message = "循环距离必须大于零。"; continue; }
                    StopPreview();
                    if (gait == null) gait = StageGaitAnimationEditor.Enable(clip, director);
                    Undo.RecordObject(gait, "调整动画步态同步");
                    gait.synchronizeDistance = sync; gait.cycleDistance = distance;
                    EditorUtility.SetDirty(gait); director.RebuildGraph();
                    TimelineEditor.Refresh(RefreshReason.ContentsModified);
                }
                if (gait != null && gait.synchronizeDistance)
                {
                    try
                    {
                        var rate = (gait.SampleTime(Track, director, clip, routeClip.end) - gait.SampleTime(Track, director, clip, routeClip.start)) /
                            Math.Max(.001, Math.Min(clip.end, routeClip.end) - Math.Max(clip.start, routeClip.start));
                        EditorGUILayout.LabelField("本段平均步态倍率", rate.ToString("F2") + " 倍");
                        if (rate > 2 || rate < .5)
                            EditorGUILayout.HelpBox("步态倍率偏离原速较多。可调整移动时长或换用更合适的走 / 跑动画；请先校准循环距离。", MessageType.Warning);
                    }
                    catch (InvalidOperationException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Warning); }
                }
            }
            EditorGUILayout.HelpBox("仅对循环走跑动作开启。距离越大或时间越短，步频越快；移动曲线也会同步。该设置作用于整个动画片段，跨停留时步态暂停；吃东西、观察及拍翅请保持关闭。", MessageType.Info);
        }

        private void DrawClipActions(TimelineClip clip)
        {
            var data = (StagePerformanceClip)clip.asset;
            var route = data.route.Resolve(director);
            var point = data.station.Resolve(director);
            var source = StagePerformanceTrack.PlacementSource(clip);
            var sourceData = (StagePerformanceClip)source.asset;
            var sourceRoute = sourceData.route.Resolve(director);
            var locationTarget = sourceData.mode == StagePerformanceMode.Move && sourceRoute != null
                ? (source.start > clip.start
                    ? (sourceRoute.startAnchor != null ? sourceRoute.startAnchor : sourceRoute.transform)
                    : (sourceRoute.endAnchor != null ? sourceRoute.endAnchor : sourceRoute.transform))
                : sourceData.station.Resolve(director);
            if (GUILayout.Button(new GUIContent("定位路线", "选中场景位置并定位预览"), buttonStyle, GUILayout.Width(88), GUILayout.Height(30)))
            { time = clip.start; Evaluate(); Selection.activeGameObject = locationTarget != null ? locationTarget.gameObject : null; FrameTarget(locationTarget); }
            var duplicate = GUILayout.Button("复制片段", buttonStyle, GUILayout.Width(88), GUILayout.Height(30));
            if (duplicate)
            {
                StopPreview(); Undo.RecordObjects(new UnityEngine.Object[] { Track, director }, "复制剧情片段");
                var end = Clips.Max(c => c.end);
                var copy = Track.CreateClip<StagePerformanceClip>();
                Undo.RegisterCreatedObjectUndo(copy.asset, "复制剧情片段");
                EditorUtility.CopySerialized(data, copy.asset);
                var copied = (StagePerformanceClip)copy.asset;
                copied.route.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
                copied.station.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
                StageRoute routeCopy = null;
                Transform pointCopy = null;
                if (route != null && data.mode == StagePerformanceMode.Move)
                {
                    var obj = Instantiate(route.gameObject, route.transform.parent);
                    obj.name = route.name + "（副本）";
                    Undo.RegisterCreatedObjectUndo(obj, "复制路线");
                    routeCopy = obj.GetComponent<StageRoute>();
                    if (route.startAnchor != null) routeCopy.startAnchor = CopyPoint(route.startAnchor, obj.transform);
                    if (route.endAnchor != null) routeCopy.endAnchor = CopyPoint(route.endAnchor, obj.transform);
                }
                if (point != null && data.mode == StagePerformanceMode.Hold) pointCopy = CopyPoint(point, director.transform);
                Bind(copied, routeCopy, pointCopy);
                copy.start = end; copy.duration = clip.duration; copy.displayName = clip.displayName + "（副本）";
                EditorUtility.SetDirty(Track); EditorUtility.SetDirty(director);
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            }
        }

        private void Bind(StagePerformanceClip data, StageRoute route, Transform point)
        {
            if (PropertyName.IsNullOrEmpty(data.route.exposedName)) data.route.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
            if (PropertyName.IsNullOrEmpty(data.station.exposedName)) data.station.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
            director.SetReferenceValue(data.route.exposedName, route);
            director.SetReferenceValue(data.station.exposedName, point);
        }

        private static bool StopEditingPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            StopAllPreviews();
            // Timeline 1.7 未公开退出 Preview 的接口；调用窗口自身的状态开关，
            // 让它停止图并恢复角色，而非直接清除全局 AnimationMode。
            if (AnimationMode.InAnimationMode())
            {
                var nativeWindow = TimelineEditor.GetWindow();
                var state = nativeWindow?.GetType().GetProperty("state")?.GetValue(nativeWindow);
                var preview = state?.GetType().GetProperty("previewMode");
                if (preview != null && preview.CanWrite) preview.SetValue(state, false);
            }
            return !AnimationMode.InAnimationMode();
        }

        private void AddClip(bool moving)
        {
            if (Track == null || Track.lockedInHierarchy) { message = "请先选择可编辑的角色轨道。"; return; }
            if (!(director.GetGenericBinding(Track) is StageActorDriver)) { message = "当前角色轨道尚未绑定 StageActorDriver，请先在 Timeline 左侧绑定角色。"; return; }
            if (!StopEditingPreview()) { message = "请先停止运行或 Animation 窗口的预览后重试。"; return; }
            message = null;
            var oldClips = Clips;
            var insertionIndex = oldClips.Length == 0 ? 0 : Mathf.Clamp(selected, 0, oldClips.Length - 1) + 1;
            var preceding = oldClips.Take(insertionIndex).ToArray();
            Vector3? previousEnd = null;
            var previousFacing = Quaternion.identity;
            if (oldClips.Length > 0)
            {
                try
                {
                    StagePerformanceTrack.SamplePlacement(preceding.Last(), director, 1, out var end, out previousFacing);
                    previousEnd = end;
                }
                catch (InvalidOperationException error) { message = error.Message; }
            }
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { Track, director }, "插入剧情片段");
            var insertionTime = preceding.Length == 0 ? 0 : preceding.Last().end;
            // 插入始终为本角色留出完整时长；保留后续片段之间的时间间隔。
            var shift = 3 + (insertionIndex < oldClips.Length ? Math.Max(0, insertionTime - oldClips[insertionIndex].start) : 0);
            foreach (var later in oldClips.Skip(insertionIndex)) later.start += shift;
            var clip = Track.CreateClip<StagePerformanceClip>();
            Undo.RegisterCreatedObjectUndo(clip.asset, "新增剧情片段");
            clip.start = insertionTime;
            clip.duration = 3; clip.displayName = moving ? "移动到下一点" : "停留表演";
            var data = (StagePerformanceClip)clip.asset;
            data.mode = moving ? StagePerformanceMode.Move : StagePerformanceMode.Hold;
            var previousMove = preceding.Select(c => (StagePerformanceClip)c.asset).LastOrDefault(c => c.mode == StagePerformanceMode.Move);
            if (previousMove != null) data.groundMode = previousMove.groundMode;
            data.animation = oldClips.Select(c => (StagePerformanceClip)c.asset).FirstOrDefault(c => c.mode == data.mode)?.animation;
            var anchor = new GameObject("剧情点 " + (oldClips.Length + 1));
            Undo.RegisterCreatedObjectUndo(anchor, "新增剧情点");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(anchor, director.gameObject.scene);
            anchor.transform.SetParent(director.transform, false);
            var actor = director.GetGenericBinding(Track) as StageActorDriver;
            if (actor != null) anchor.transform.position = actor.transform.position;
            if (previousEnd.HasValue) anchor.transform.SetPositionAndRotation(previousEnd.Value, previousFacing);
            StageRoute route = null;
            if (moving)
            {
                var routeObject = new GameObject("路线 " + (oldClips.Length + 1));
                Undo.RegisterCreatedObjectUndo(routeObject, "新增路线");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(routeObject, director.gameObject.scene);
                routeObject.transform.SetParent(director.transform, false);
                route = routeObject.AddComponent<StageRoute>();
                route.startAnchor = anchor.transform;
                route.endAnchor = CopyPoint(anchor.transform, director.transform);
                route.endAnchor.name = "到达点 " + (oldClips.Length + 1);
                route.endAnchor.position += anchor.transform.forward * 2;
                var referenceRoute = preceding.Where(c => ((StagePerformanceClip)c.asset).mode == StagePerformanceMode.Move).Select(c => ((StagePerformanceClip)c.asset).route.Resolve(director)).LastOrDefault(r => r != null);
                if (referenceRoute != null)
                {
                    route.groundSurfaces = referenceRoute.groundSurfaces; route.snapToGround = referenceRoute.snapToGround; route.autoBindGround = referenceRoute.autoBindGround;
                    route.limitGroundProjection = referenceRoute.limitGroundProjection; route.groundStepUp = referenceRoute.groundStepUp;
                    route.groundDrop = referenceRoute.groundDrop; route.groundSlope = referenceRoute.groundSlope;
                }
                route.points = new[] { route.transform.InverseTransformPoint(anchor.transform.position), route.transform.InverseTransformPoint(route.endAnchor.position) };
            }
            Bind(data, route, anchor.transform);
            if (moving && route != null && route.autoBindGround && data.UsesGround(route))
            {
                var groundSettings = StageGroundRepair.GetSettings(director.gameObject.scene);
                if (groundSettings != null && groundSettings.scanMode == StageGroundScanMode.StaticMeshes && groundSettings.boundSurfaces != null)
                {
                    route.groundSurfaces = groundSettings.boundSurfaces.Where(c => c != null).ToArray();
                    route.limitGroundProjection = true; route.groundStepUp = groundSettings.maxStepUp;
                    route.groundDrop = groundSettings.maxDrop; route.groundSlope = groundSettings.maxSlope;
                }
                var repairResult = StageGroundRepair.Repair(director.gameObject.scene);
                message = route.groundSurfaces != null && route.groundSurfaces.Any(c => c != null) ? null : repairResult;
                try
                {
                    route.startAnchor.position = route.ProjectToGround(route.startAnchor.position, data.UsesGround(route));
                    route.endAnchor.position = route.ProjectToGround(route.endAnchor.position, data.UsesGround(route));
                }
                catch (InvalidOperationException error) { message = error.Message; }
            }
            EditorUtility.SetDirty(Track); EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            selected = Array.IndexOf(Clips, clip);
            clipList = null;
            director.RebuildGraph();
            TimelineEditor.selectedClip = clip;
            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
        }

        private static void FrameTarget(Transform target)
        {
            if (target != null && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(target.position, Vector3.one * 3), false);
        }

        private static Transform CopyPoint(Transform source, Transform parent)
        {
            var obj = new GameObject(source.name + "（副本）");
            Undo.RegisterCreatedObjectUndo(obj, "复制剧情点");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, parent.gameObject.scene);
            obj.transform.SetParent(parent, false);
            obj.transform.SetPositionAndRotation(source.position, source.rotation);
            return obj.transform;
        }

        private void SceneGUI(SceneView view)
        {
            var clips = Clips;
            if (clips.Length == 0 || EditorApplication.isPlaying) return;
            selected = Mathf.Clamp(selected, 0, clips.Length - 1);
            var data = (StagePerformanceClip)clips[selected].asset;
            if (data.mode == StagePerformanceMode.Move)
            {
                // 路线点由全局叠加层点选，选中后由 StageRouteEditor 绘制移动手柄。
                return;
            }
            else
            {
                if (StagePerformanceTrack.PlacementSource(clips[selected]) != clips[selected])
                {
                    try
                    {
                        StagePerformanceTrack.SamplePlacement(clips[selected], director, 1, out var end, out _);
                        Handles.Label(end + Vector3.up * 0.25f, clips[selected].displayName + "（自动跟随路线位置）");
                        Handles.DrawWireDisc(end, Vector3.up, HandleUtility.GetHandleSize(end) * 0.12f);
                    }
                    catch (InvalidOperationException) { }
                    return;
                }
                var point = data.station.Resolve(director);
                if (point == null) return;
                if (Selection.activeGameObject != null && Selection.activeGameObject != point.gameObject) return;
                Handles.Label(point.position + Vector3.up * 0.25f, clips[selected].displayName);
                if (Selection.activeGameObject == point.gameObject) return; // 已有原生 Transform 手柄。
                EditorGUI.BeginChangeCheck();
                var position = Handles.PositionHandle(point.position, point.rotation);
                var rotation = Handles.Disc(point.rotation, position, Vector3.up, HandleUtility.GetHandleSize(position) * 0.7f, false, 15);
                if (EditorGUI.EndChangeCheck())
                { StopPreview(); Undo.RecordObject(point, "编辑停留点"); point.SetPositionAndRotation(position, rotation); EditorUtility.SetDirty(point); }
            }
        }
    }

    public sealed class StagePreviewSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths) { StageStoryWindow.StopAllPreviews(); return paths; }
    }
}
