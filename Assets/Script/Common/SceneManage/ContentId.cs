using UnityEngine;

namespace WhaleMovie.SceneManage
{
    public enum ContentId
    {
        None = 0,

        [InspectorName("鲸馆")]
        WhalePavilion = 10,
        [InspectorName("学校")]
        School = 20,
        [InspectorName("海洋主场景")]
        Ocean = 30,
        [InspectorName("海洋馆")]
        Aquarium = 40,
        [InspectorName("水母馆")]
        Jellyfish = 50,
    }

    public enum SequenceEndMode
    {
        Stop,
        Loop,
    }
}
