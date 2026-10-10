using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
namespace TapTap.Systems.UI
{
    public sealed class SongListItemView : MonoBehaviour, IMoveHandler
    {
        public UnityEngine.UI.Button Button;
        public TMP_Text Label;
        public RectTransform Rect;
        public LoopSongList Owner;
        public void OnMove(AxisEventData e)
        {
            if (e.moveDir != MoveDirection.Up && e.moveDir != MoveDirection.Down) return;
            Owner.Move(e.moveDir == MoveDirection.Up ? -1 : 1); e.Use();
            EventSystem.current?.SetSelectedGameObject(Owner.Rows[Owner.Rows.Length / 2].gameObject);
        }
    }
}
