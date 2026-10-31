using UnityEngine;

namespace AIBuilder.CreatureStage
{
    [ExecuteAlways, RequireComponent(typeof(LineRenderer), typeof(StageRoute))]
    public sealed class StageRouteLine : MonoBehaviour
    {
        private readonly Vector3[] positions = new Vector3[65];
        private StageRoute route;
        private LineRenderer line;
        private float nextRefresh;
        private void Update()
        {
            // 编辑模式由叠加层共享采样结果，运行时辅助线限频更新。
            if (!Application.isPlaying || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .2f;
            RefreshDisplay();
        }

        public void ApplyDisplay(Vector3[] samples, Color color)
        {
            if (line == null) line = GetComponent<LineRenderer>();
            if (samples == null) { line.positionCount = 0; return; }
            line.startColor = line.endColor = color;
            for (var i = 0; i < positions.Length; i++) positions[i] = samples[i] + Vector3.up * .03f;
            line.positionCount = positions.Length;
            line.SetPositions(positions);
        }

        public void RefreshDisplay()
        {
            if (route == null) route = GetComponent<StageRoute>();
            if (line == null) line = GetComponent<LineRenderer>();
            if (!route.IsUsedByStory())
            {
                if (line.positionCount != 0) line.positionCount = 0;
                return;
            }
            try
            {
                line.startColor = line.endColor = route.DisplayColor;
                route.SamplePolyline(positions);
                for (var i = 0; i < positions.Length; i++) positions[i].y += 0.03f;
                line.positionCount = positions.Length;
                line.SetPositions(positions);
            }
            catch (System.InvalidOperationException) { line.positionCount = 0; }
        }
    }
}
