using AIBuilder.CreatureStage;
using UnityEditor;
using UnityEngine;

namespace AIBuilder.EditorTools.CreatureStage
{
    [CustomEditor(typeof(StageRoute))]
    public sealed class StageRouteEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("路线默认按 Timeline 当前顺序连接，保留到达点和中间控制点。自动连接的起点由前段终点控制；需要独立路线时关闭自动连接。", MessageType.Info);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("snapToGround"), new GUIContent("贴合地形"));
            DrawPropertiesExcluding(serializedObject, "m_Script", "snapToGround");
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("关闭贴合地形后，拖动节点的高度轴即可编辑空中路线；自动地面修复也不会修改该路线高度。", MessageType.None);
            EditorGUILayout.HelpBox("圆滑度越大，转弯弧度越明显。相邻移动片段首尾相接且端点重合时，可自动圆滑连接；恐龙朝向跟随曲线切线。", MessageType.None);
            var route = (StageRoute)target;
            try { var length = route.Length; EditorGUILayout.LabelField("路线长度", length.ToString("F2") + " 米"); }
            catch (System.InvalidOperationException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Warning); }
            if (GUILayout.Button("地面绑定 / 重新扫描")) StageGroundBindingWindow.Open(route.gameObject.scene);
            if (GUILayout.Button("设置地面名称匹配规则"))
            {
                var settings = StageGroundRepair.GetSettings(route.gameObject.scene);
                if (settings != null) Selection.activeGameObject = settings.gameObject;
            }
            if (route.PointCount >= 2 && GUILayout.Button("增加中间控制点"))
            {
                StageStoryWindow.StopAllPreviews();
                Undo.RecordObject(route, "增加路线控制点");
                var points = new System.Collections.Generic.List<Vector3>(route.points);
                var middle = route.PointCount / 2;
                points.Insert(middle, route.transform.InverseTransformPoint((route.WorldPoint(middle - 1) + route.WorldPoint(middle)) * 0.5f));
                route.points = points.ToArray();
                EditorUtility.SetDirty(route);
            }
            if (GUILayout.Button("打开剧情编排")) StageStoryWindow.Open();
        }

        private static StageRoute selectedRoute;
        private static int selectedPoint = -1;
        private bool hidTools;
        public static void SelectPoint(StageRoute route, int index)
        {
            selectedRoute = route; selectedPoint = index;
            StageStoryWindow.SelectSceneRoute(route);
        }
        private void OnSceneGUI()
        {
            if (Selection.activeGameObject != ((StageRoute)target).gameObject) return;
            if (!Tools.hidden) { Tools.hidden = true; hidTools = true; }
            DrawHandles((StageRoute)target);
        }
        private void OnDisable() { if (hidTools) Tools.hidden = false; }

        public static void DrawHandles(StageRoute route)
        {
            if (route == null) return;
            if (selectedRoute != route) { selectedRoute = route; selectedPoint = route.PointCount - 1; }
            selectedPoint = Mathf.Clamp(selectedPoint, 0, route.PointCount - 1);
            var routeColor = route.DisplayColor;
            for (var i = 0; i < route.PointCount; i++)
            {
                var old = route.WorldPoint(i);
                if (i == 0 && route.TryGetLinkedStart(out _))
                {
                    Handles.Label(old + Vector3.up * 0.12f, "出发点（自动接前段终点）");
                    continue;
                }
                Handles.Label(old + Vector3.up * 0.12f, i == 0 ? "出发点" : i == route.PointCount - 1 ? "到达点" : "路线点 " + i);
                var size = HandleUtility.GetHandleSize(old) * 0.065f;
                using (new Handles.DrawingScope(routeColor))
                    if (Handles.Button(old, Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap))
                    { SelectPoint(route, i); SceneView.RepaintAll(); }
                if (i != selectedPoint) continue;
                EditorGUI.BeginChangeCheck();
                var position = Handles.PositionHandle(old, Quaternion.identity);
                if (!EditorGUI.EndChangeCheck()) continue;
                StageStoryWindow.StopAllPreviews();
                try { position = route.ProjectToGround(position); }
                catch (System.InvalidOperationException) { /* 失去地面时仍允许移动控制点，恢复后再投射。 */ }
                var anchor = i == 0 ? route.startAnchor : i == route.PointCount - 1 ? route.endAnchor : null;
                if (anchor != null)
                {
                    Undo.RecordObject(anchor, "移动剧情点");
                    anchor.position = position;
                    EditorUtility.SetDirty(anchor);
                }
                else
                {
                    Undo.RecordObject(route, "编辑路线");
                    route.points[i] = route.transform.InverseTransformPoint(position);
                    EditorUtility.SetDirty(route);
                }
            }
        }
    }
}
