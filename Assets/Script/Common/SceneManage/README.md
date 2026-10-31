# 场景与内容切换系统

## 启动结构

- `Entry` 是 Build Settings 中的第 0 个场景，只承担应用启动入口。
- `Entry` 中的 `[Entry Systems]` 挂载内容总控、叠加加载管理器和 XR 黑幕。XR Origin、控制器和模拟器由场景直接配置，管理脚本不会自动创建。直接运行某个内容场景调试时，系统只会建立备用的 `[WhaleMovie App Root]` 和切换组件。
- 其余内容场景全部使用 `LoadSceneMode.Additive` 叠加加载；切换成功后才卸载上一个内容场景，`Entry` 始终保留。
- 跨场景切换时，在黑幕完全遮住画面后暂停旧场景的活动根物体，再加载新场景，避免体积云等效果在加载期间继续渲染。保留 Entry、管理器和正在使用的 MainCamera；加载失败时恢复原先活动的根物体。
- 内容场景内带 `MainCamera` 标签或 `AudioListener` 的摄像机会被停用，统一使用常驻 XR 摄像机。

## 脚本职责

- `ContentFlowManager`：内容顺序、同场景内容切换、Timeline 启动和总流程入口。
- `SceneTransitionManager`：叠加加载、卸载和黑幕时序。
- `XRBlackoutSphere`：在 XR 摄像机内部生成球形黑幕并控制淡入淡出。
- `ContentCatalog`：定义 `ContentId` 和场景名；没有资源文件时使用项目当前五个内容的默认表。
- `SceneContentRegistry`：查找当前场景的 `ContentController`；旧场景未配置时自动建立兼容控制器。
- `ContentController`：管理一个内容根物体和它的 `PlayableDirector`，进入后从 0 播放，退出时停止并隐藏。
- `CueTrack` / `CueMarker`：Timeline 中的 Cue 轨道与 Cue 标记。
- `CueReceiver`：接收 Cue，并转发给内容根物体下实现 `ICueHandler` 的脚本。

## 在 Timeline 中添加 Cue

1. 在 Timeline 左侧点击加号，添加 `Cue Track`。
2. 在该轨道的标记区域右键添加 `Cue`。
3. 选中 Cue，在 Inspector 的 `Cue ID` 中填写名称。
4. 填写 `切换内容` 时，总控会按 `ContentId` 顺序进入下一个内容。

当前流程：`Entry` → `WhalePavilion` → `school` → `Ocean` → `Aquarium` → `jellyfish`。鲸馆、学校和 Ocean 的 Cue 使用 `切换内容`；Aquarium 中原有的 `BlueVortexPortal` 通往 jellyfish。切换时保留 Entry 和 XR 设备，卸载上一个内容场景。

其他脚本可实现 `ICueHandler` 接收自定义 Cue：

```csharp
using UnityEngine;
using WhaleMovie.SceneManage;

public sealed class MyCueLogic : MonoBehaviour, ICueHandler
{
    public void HandleCue(CueContext cue)
    {
        switch (cue.Name)
        {
            case "显示标题":
                // 在这里写该时间点的逻辑。
                break;
        }
    }
}
```

脚本需要放在对应 `ContentController` 的 `contentRoot` 下。

## 当前 Cue

- `Sc01.playable` 的 `切换内容`：鲸馆 → 学校。
- `Sc02.playable` 的 `切换内容`：学校 → Ocean。
- `Sc03.playable` 的 `切换内容`：Ocean → Aquarium。
- Aquarium → jellyfish 由场景中已有的 `BlueVortexPortal` 触发。jellyfish 是终点。

## 同一场景包含多个内容

1. 在场景里为每个内容根物体添加一个 `ContentController`，设置不同的 `ContentId` 和 `orderInScene`。
2. 通过 `Create > WhaleMovie > Scene Manage > Content Catalog` 创建 `ContentCatalog`。
3. 把资源保存为 `Assets/Resources/SceneManage/ContentCatalog.asset`。
4. 为每个内容添加一条记录；可以让多条记录使用同一个 `sceneName`，再用 `orderInScene` 指定先后顺序。

切到同场景的下一个内容时不会重新加载场景，只会退出旧内容、启用新内容并从头播放新内容的 Timeline。

## 编辑器 XR 模拟

请把需要的 XR Origin、控制器和 `XR Device Simulator` 预制件直接放入 `Entry`。XR 摄像机需要使用 `MainCamera` 标签，黑幕组件会自动绑定它。

## 场景出生点

构建列表中的 6 个内容场景已各自配置根物体 `SpawnPoint`，挂载 `SceneSpawnPoint` 并引用自身 Transform。后续直接移动、旋转该空物体即可调整进入该场景的位置和朝向，无需再次拖引用。Entry 作为启动场景不单独设置出生点。

| 场景 | 初始地面坐标 | 坐标来源 |
| --- | --- | --- |
| WhalePavilion | (0, 0, 0) | 场景切换改造前备份中的 XR 根位置 |
| school | (0, 6, 0) | 场景切换改造前备份中的 XR 根位置 |
| Ocean | (0, 6, 0) | 场景切换改造前备份中的 XR 根位置 |
| Aquarium | (0, 0, 0) | 当前场景 FootPoint 的位置 |
| jellyfish | (0, 0, -10) | 当前 Main Camera 的水平位置，地面高度暂设为 0 |
| BoatMusium | (0, 0, -10) | 当前 Main Camera 的水平位置，地面高度暂设为 0 |

BoatMusium 已配置出生点，但仍遵循原来的内容流程，不自动加入播放顺序。jellyfish、BoatMusium 的地面高度是初始值，可按实际场景调整。

给新增场景配置出生点：

1. 在每个内容场景创建空物体 `SpawnPoint`，放在希望玩家出生的**地面位置**，蓝色 Z 轴指向希望玩家面对的方向。不要放在玩家 XR 层级里。
2. 为这个空物体添加 `SceneSpawnPoint` 组件。`Spawn Point` 留空就使用本物体；也可以把另一个空物体拖入此字段作为位置引用。
3. `XR Origin` 通常留空，脚本会从常驻 MainCamera 的父级自动获取玩家。每个场景只保留一个启用的 `SceneSpawnPoint`。

已接入 `ContentFlowManager`：首次从 Entry 进入、跨场景跳转、同场景内容切换、直接运行内容场景调试时，都会在内容准备完毕、黑幕淡出之前应用出生点。未配置出生点的场景保持原来的位置；多个启用的出生点会报警并跳过定位。

- `Match Facing Direction`：默认开启，只校正水平朝向，保留头显俯仰和真实身高。
- `Align Headset Horizontally`：默认开启，补偿头显在游玩区域内的水平偏移，让玩家头显正下方对齐出生点；关闭则让 XR Origin 根节点的世界坐标直接等于空物体坐标。
- 运行时可在组件右上角菜单执行“将玩家移动到出生点（运行时）”进行检查。

出生点只定位一次，之后仍允许场景的 XR 移动脚本或 Timeline 继续移动玩家。鲸馆已有 `SC01XRRigMove`，会在出生后继续执行原来的移动动画。此脚本不会启用 Android XR，也不会修改 APK；头显使用新功能需重新打包安装。

## Quest 场景渲染

- school 的 `CloudBox / CloudSeaVolume` 在移动设备上默认使用 16 次视线采样、0 次额外光照步进，以高度近似体积光照，并复用形状噪声的细节通道。桌面端保留 48 次视线采样和 5 次光照步进。可在 Inspector 的 `Mobile / Quest Quality` 区域调整。
- 云的动画使用着色器时间，不再逐帧重复上传材质属性。运行时如有脚本修改云参数，修改后调用 `CloudSeaVolume.ApplyProperties()`。
- WhalePavilion 柱子模型已重新绑定 `Assets/Materials/WhalePavilion/Columns` 中的独立 URP 材质，修复了指向已删除模型的材质映射，以及柱子上的跨 FBX 材质引用。
