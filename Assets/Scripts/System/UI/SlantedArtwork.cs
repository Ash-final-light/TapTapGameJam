using UnityEngine;

namespace TapTap.Systems.UI
{
    /// <summary>Restricts a supplied UV region to the slanted card, excluding adjacent cards.</summary>
    public sealed class SlantedArtwork : UnityEngine.UI.RawImage
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            const float slope = .10f;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, new Vector2(uvRect.xMin, uvRect.yMin));
            mesh.AddVert(new Vector3(rect.xMin + rect.width * slope, rect.yMax), color,
                new Vector2(uvRect.xMin + uvRect.width * slope, uvRect.yMax));
            mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, new Vector2(uvRect.xMax, uvRect.yMax));
            mesh.AddVert(new Vector3(rect.xMax - rect.width * slope, rect.yMin), color,
                new Vector2(uvRect.xMax - uvRect.width * slope, uvRect.yMin));
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(2, 3, 0);
        }
    }
}
