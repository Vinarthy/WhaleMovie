using UnityEngine;
using UnityEngine.InputSystem;

namespace WhaleMovie.SceneManage.Temp
{
    [DisallowMultipleComponent]
    public sealed class SpaceSkipContent : MonoBehaviour
    {
        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame)
                return;

            ContentFlowManager flow = ContentFlowManager.Instance;
            if (flow == null)
            {
                Debug.LogWarning("按下空格，但没有找到 ContentFlowManager。", this);
                return;
            }

            Debug.Log("[SceneManage Temp] 按下空格，切换到下一个内容。", this);
            flow.NextContent();
        }
    }
}
