using System;
using UnityEngine;

namespace TapTap.Systems
{
    [Serializable] public sealed class LevelEntry { public string Id, ScenePath, DisplayName; }
    [CreateAssetMenu(menuName = "Spotlight/Level Entries")]
    public sealed class LevelEntryCatalog : ScriptableObject
    {
        public string FirstEntryId = "tutorial";
        public LevelEntry[] Entries = Array.Empty<LevelEntry>();
        public LevelEntry Find(string id) => Array.Find(Entries, e => e != null && e.Id == id);
    }
}
