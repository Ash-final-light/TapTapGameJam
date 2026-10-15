using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TapTap.Systems.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AudioWaveformGraphic : UnityEngine.UI.MaskableGraphic, IPointerClickHandler
    {
        public float[] Peaks;
        public double Duration, WindowStart, WindowEnd, Bpm = 120, FirstBeat, Position;
        public Action<double> Clicked;
        protected override void OnEnable()
        {
            // RequireComponent does not repair instances created before a script reload.
            // Restore the dependency before Graphic registers with the Canvas/raycaster.
            if (!GetComponent<CanvasRenderer>()) gameObject.AddComponent<CanvasRenderer>();
            base.OnEnable();
        }
        public void Refresh() => SetVerticesDirty();
        public void OnPointerClick(PointerEventData data)
        {
            if (WindowEnd <= WindowStart || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, data.position, data.pressEventCamera, out var point)) return;
            double t = Mathf.InverseLerp(rectTransform.rect.xMin, rectTransform.rect.xMax, point.x);
            Clicked?.Invoke(Math.Max(0, Math.Min(Duration - .000001, WindowStart + t * (WindowEnd - WindowStart))));
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear(); var r = rectTransform.rect;
            if (Peaks == null || Peaks.Length == 0 || Duration <= 0 || WindowEnd <= WindowStart) return;
            int columns = Math.Min(800, Mathf.CeilToInt(r.width));
            for (int i = 0; i < columns; i++)
            {
                double t0 = WindowStart + (WindowEnd - WindowStart) * i / columns;
                double t1 = WindowStart + (WindowEnd - WindowStart) * (i + 1) / columns;
                int start = Mathf.Clamp((int)(t0 / Duration * Peaks.Length), 0, Peaks.Length - 1);
                int end = Mathf.Clamp((int)Math.Ceiling(t1 / Duration * Peaks.Length), start + 1, Peaks.Length);
                float peak = .006f; for (int j = start; j < end; j++) peak = Mathf.Max(peak, Peaks[j]);
                float x = r.xMin + r.width * i / columns, height = peak * r.height * .42f;
                Quad(mesh, x, r.center.y - height, x + r.width / columns, r.center.y + height, color);
            }
            if (Bpm >= 30 && Bpm <= 300)
            {
                long start = Math.Max(0, (long)Math.Ceiling((WindowStart - FirstBeat) * Bpm / 60));
                for (int i = 0; i < 1000; i++)
                {
                    double t = FirstBeat + (start + i) * 60 / Bpm; if (t > WindowEnd) break;
                    Line(mesh, t, new Color(1, .76f, .32f, .35f), 1);
                }
            }
            Line(mesh, FirstBeat, new Color(1, .75f, .22f), 3);
            Line(mesh, Position, Color.white, 2);
        }
        private void Line(UnityEngine.UI.VertexHelper mesh, double seconds, Color tint, float width)
        {
            if (seconds < WindowStart || seconds > WindowEnd) return;
            var r = rectTransform.rect; float x = r.xMin + (float)((seconds - WindowStart) / (WindowEnd - WindowStart)) * r.width;
            Quad(mesh, x - width / 2, r.yMin, x + width / 2, r.yMax, tint);
        }
        private static void Quad(UnityEngine.UI.VertexHelper mesh, float x0, float y0, float x1, float y1, Color tint)
        {
            int index = mesh.currentVertCount; mesh.AddVert(new Vector3(x0, y0), tint, Vector2.zero); mesh.AddVert(new Vector3(x0, y1), tint, Vector2.zero);
            mesh.AddVert(new Vector3(x1, y1), tint, Vector2.zero); mesh.AddVert(new Vector3(x1, y0), tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
