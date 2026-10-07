using Inputs;
using UnityEngine;
namespace TapTap.Danmaku
{
    public static class BattleInputProjection
    {
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
