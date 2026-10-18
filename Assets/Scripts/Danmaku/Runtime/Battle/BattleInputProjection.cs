using Inputs;
using UnityEngine;
namespace TapTap.Danmaku
{
    public static class BattleInputProjection
    {
        // Use the same local plane as aiming and rendering, including camera offset and field scale.
        public static bool TryGetVisibleBounds(Camera camera, Transform field, out Rect bounds)
        {
            bounds = default;
            if (!camera || !field || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return false;
            var plane = new Plane(field.forward, field.position);
            if (!ProjectCorner(0, 0, out var bottomLeft) || !ProjectCorner(1, 0, out var bottomRight) ||
                !ProjectCorner(0, 1, out var topLeft) || !ProjectCorner(1, 1, out var topRight)) return false;
            // A rotated/skewed viewport is not an axis-aligned movement rectangle.
            // Fall back to the authored field rather than permit movement outside the image.
            const float tolerance = 0.001f;
            if (Mathf.Abs(bottomLeft.x - topLeft.x) > tolerance || Mathf.Abs(bottomRight.x - topRight.x) > tolerance ||
                Mathf.Abs(bottomLeft.y - bottomRight.y) > tolerance || Mathf.Abs(topLeft.y - topRight.y) > tolerance) return false;
            bounds = Rect.MinMaxRect(Mathf.Min(bottomLeft.x, bottomRight.x), Mathf.Min(bottomLeft.y, topLeft.y),
                Mathf.Max(bottomLeft.x, bottomRight.x), Mathf.Max(bottomLeft.y, topLeft.y));
            return bounds.width > 0 && bounds.height > 0;

            bool ProjectCorner(float x, float y, out Vector3 local)
            {
                local = default;
                var ray = camera.ViewportPointToRay(new Vector3(x, y, 0));
                if (!plane.Raycast(ray, out float distance)) return false;
                local = field.InverseTransformPoint(ray.GetPoint(distance));
                return !float.IsNaN(local.x) && !float.IsInfinity(local.x) && !float.IsNaN(local.y) && !float.IsInfinity(local.y);
            }
        }

        public static BattleInput Project(InputSnapshot input, Vector2 screenPoint, Camera camera, Transform field)
        {
            if (!camera || !field) return new BattleInput(input, default, false);
            var ray = camera.ScreenPointToRay(screenPoint);
            var plane = new Plane(field.forward, field.position);
            if (!plane.Raycast(ray, out float distance)) return new BattleInput(input, default, false);
            Vector3 local = field.InverseTransformPoint(ray.GetPoint(distance));
            return new BattleInput(input, DanmakuSimulation.ToFP(new Vector2(local.x, local.y)), true);
        }
    }
}
