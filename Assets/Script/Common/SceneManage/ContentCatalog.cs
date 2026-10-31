using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhaleMovie.SceneManage
{
    [Serializable]
    public sealed class ContentDefinition
    {
        public ContentId id;
        public string sceneName;
        [Min(0)] public int orderInScene;
        public bool includeInSequence = true;

        public ContentDefinition()
        {
        }

        public ContentDefinition(ContentId id, string sceneName, int orderInScene = 0)
        {
            this.id = id;
            this.sceneName = sceneName;
            this.orderInScene = orderInScene;
            includeInSequence = true;
        }
    }

    [CreateAssetMenu(fileName = "ContentCatalog", menuName = "WhaleMovie/Scene Manage/Content Catalog")]
    public sealed class ContentCatalog : ScriptableObject
    {
        [SerializeField] private List<ContentDefinition> contents = new List<ContentDefinition>();

        public IReadOnlyList<ContentDefinition> Contents => contents;

        public static List<ContentDefinition> CreateDefaultDefinitions()
        {
            return new List<ContentDefinition>
            {
                new ContentDefinition(ContentId.WhalePavilion, "WhalePavilion"),
                new ContentDefinition(ContentId.School, "school"),
                new ContentDefinition(ContentId.Ocean, "Ocean"),
                new ContentDefinition(ContentId.Aquarium, "Aquarium"),
                new ContentDefinition(ContentId.Jellyfish, "jellyfish"),
            };
        }

        public static bool TryGetDefaultId(string sceneName, out ContentId id)
        {
            List<ContentDefinition> defaults = CreateDefaultDefinitions();
            for (int i = 0; i < defaults.Count; i++)
            {
                if (string.Equals(defaults[i].sceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    id = defaults[i].id;
                    return true;
                }
            }

            id = ContentId.None;
            return false;
        }
    }
}
