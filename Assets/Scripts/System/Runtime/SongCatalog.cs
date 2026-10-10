using System;
using UnityEngine;
namespace TapTap.Systems
{
    [CreateAssetMenu(menuName = "Spotlight/Song Catalog")]
    public sealed class SongCatalog : ScriptableObject
    {
        public SongDefinition[] Songs = Array.Empty<SongDefinition>();
        public SongDefinition Find(string id) => Array.Find(Songs, s => s && s.Id == id);
    }
}
