using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
namespace TapTap.Systems.UI
{
    public sealed class LoopSongList : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public SongListItemView[] Rows;
        public UnityEngine.UI.Button Previous, Next;
        public int SelectedIndex { get; private set; }
        public event Action<int> SelectionChanged;
        private SongDefinition[] songs = Array.Empty<SongDefinition>();
        private float animatedOffset, dragPixels;
        public void Bind(SongDefinition[] catalog)
        {
            songs = catalog ?? Array.Empty<SongDefinition>(); SelectedIndex = songs.Length == 0 ? 0 : Mathf.Clamp(SelectedIndex, 0, songs.Length - 1);
            Previous.onClick.RemoveAllListeners(); Next.onClick.RemoveAllListeners();
            Previous.onClick.AddListener(() => Move(-1)); Next.onClick.AddListener(() => Move(1));
            for (int i = 0; i < Rows.Length; i++)
            { int relative = i - Rows.Length / 2; Rows[i].Owner = this; Rows[i].Button.onClick.RemoveAllListeners(); Rows[i].Button.onClick.AddListener(() => Move(relative)); }
            Draw();
        }
        public void Move(int amount)
        {
            if (songs.Length == 0) return;
            SelectedIndex = ((SelectedIndex + amount) % songs.Length + songs.Length) % songs.Length;
            animatedOffset += Mathf.Clamp(amount, -3, 3) * 105; SelectionChanged?.Invoke(SelectedIndex); Draw();
        }
        public void OnScroll(PointerEventData e) { if (Mathf.Abs(e.scrollDelta.y) > .01f) Move(e.scrollDelta.y > 0 ? -1 : 1); }
        public void OnBeginDrag(PointerEventData e) { dragPixels = 0; }
        public void OnDrag(PointerEventData e)
        {
            float scale = GetComponentInParent<Canvas>().scaleFactor;
            dragPixels += e.delta.y / Mathf.Max(.01f, scale);
            while (Mathf.Abs(dragPixels) >= 105) { int direction = dragPixels > 0 ? 1 : -1; Move(direction); dragPixels -= direction * 105; }
        }
        public void OnEndDrag(PointerEventData e) { if (Mathf.Abs(dragPixels) > 45) Move(dragPixels > 0 ? 1 : -1); dragPixels = 0; }
        private void Update() { animatedOffset = Mathf.Lerp(animatedOffset, 0, 1 - Mathf.Exp(-16 * Time.unscaledDeltaTime)); Draw(); }
        private void Draw()
        {
            Previous.interactable = Next.interactable = songs.Length > 1;
            for (int i = 0; i < Rows.Length; i++)
            {
                var row = Rows[i]; row.gameObject.SetActive(songs.Length > 0); if (songs.Length == 0) continue;
                int relative = i - Rows.Length / 2;
                int index = ((SelectedIndex + relative) % songs.Length + songs.Length) % songs.Length;
                var song = songs[index]; row.Label.text = song ? song.Title : "未配置歌曲"; row.Button.interactable = song;
                float y = -relative * 105 + animatedOffset;
                row.Rect.anchoredPosition = new Vector2(Mathf.Min(145, y * y / 1800), y);
                row.Rect.localScale = Vector3.one * (relative == 0 ? 1 : .88f);
                row.Button.GetComponent<UnityEngine.UI.Image>().color = relative == 0 ? new Color(.04f, .62f, .82f) : new Color(.28f, .35f, .44f);
            }
        }
    }
}
