# 与鲸同游项目开发架构与使用规范

**项目全称：**与鲸同游——虚拟现实沉浸式叙事软件  
**文档用途：**供后续维护、增加章节、设计交互和导入 FBX 模型时使用。  
**核对范围：**`G:/LollyProject/WhaleMovie` 中的 Unity 工程、项目脚本、场景配置、Timeline 和部分模型导入设置；核对日期为 2026 年 10 月 31 日。  
**阅读方式：**“当前实现”描述本次在仓库中看到的配置；“建议规范”是后续整理和新增功能时采用的做法，不能当作已完成的代码改造。本次为静态核对，未在 Meta Quest 3 上运行验收。

## 1. 项目概况与当前主线

工程使用 Unity **2022.3.53f1c1**、URP **14.0.11**、Timeline **1.7.6**、XR Interaction Toolkit **2.6.5**、XR Hands **1.8.1**，并包含 OpenXR **1.13.2** 与 Oculus XR Plugin **4.4.0**。主要目标设备是 Meta Quest 3。`Assets/XR/XRGeneralSettingsPerBuildTarget.asset` 中 Android 自动加载 OpenXR Loader；Standalone 的自动加载目前关闭。设备是否正常追踪双手，仍需用最终 APK 在头显上验证。

启动场景为 `Assets/Scenes/Entry.unity`。`ContentFlowManager` 建立常驻的应用根对象、XR 遮黑球和转场管理器，之后按内容顺序加载场景。默认目录给出的五段主线如下：

| 内容 ID | 场景文件 | 当前内容与主要触发 |
| --- | --- | --- |
| 10 `WhalePavilion` | `WhalePavilion.unity` | 鲸馆开场；`Sc01.playable` 的“切换内容”Cue 推进。 |
| 20 `School` | `school.unity` | 云海和握拳召唤滑翔伞；`Sc02.playable` 的 Cue 推进。 |
| 30 `Ocean` | `Ocean.unity` | 海洋叙事文字、船与鲸鱼运动；`Sc03.playable` 的 Cue 推进。 |
| 40 `Aquarium` | `Aquarium.unity` | 鲸鱼游动与蓝色旋涡传送门；当前同时存在 `Sc04.playable` 的切换 Cue 和传送门脚本。 |
| 50 `Jellyfish` | `jellyfish.unity` | 水母馆和掌心互动；`Sc05.playable` 未发现切换 Cue，默认在此结束。 |

`EditorBuildSettings.asset` 还启用了 `BoatMusium.unity`，但默认内容目录没有收录它。因此它会进入构建场景列表，却不是默认五段主线的一部分。是否保留该场景，应在确定发布内容后再决定。工程里也有 `test.unity`、示例和第三方资源，不能仅凭文件存在就认定它们属于正式体验。

**需要统一的一处现状：**海洋馆的 `Sc04.playable` 在约 57.88 秒设置了“切换内容”Cue；`BlueVortexPortalTrigger` 又会在头显进入旋涡中心时直接调用 `SwitchTo(Jellyfish)`。因此，代码层面有“时间到自动转场”和“进入传送门转场”两条路径。若产品设计要求必须穿过传送门，应移除或调整该 Cue；若两者都允许，应在用户手册、现场引导词和测试用例中说明。修改前先在 Quest 3 上核对实际运行结果。

## 2. 当前代码架构

### 2.1 场景与内容管理

| 层次 | 主要文件 | 目前职责 |
| --- | --- | --- |
| 内容标识与目录 | `Assets/Script/Common/SceneManage/ContentId.cs`、`ContentCatalog.cs` | 定义内容 ID、场景名、场景内次序以及是否进入顺序播放。目录不存在时回退到代码中的五段默认定义。 |
| 流程控制 | `ContentFlowManager.cs` | 创建和保持应用根对象，选择下一段或指定段落，协调加载、卸载、播放与失败恢复。默认按 `ContentId` 数值排序，结束模式为 `Stop`。 |
| 场景装配 | `SceneContentRegistry.cs`、`ContentController.cs` | 在目标场景中查找内容控制器；进入时复位并播放 Timeline，退出时停止。没有显式控制器时可动态创建回退控制器。 |
| 转场与定位 | `SceneTransitionManager.cs`、`XRBlackoutSphere.cs`、`SceneSpawnPoint.cs` | 遮黑、异步 Additive 加载、卸载旧内容、抑制重复主相机，并把 XR Origin 对齐到入口。 |
| 叙事提示 | `CueTrack.cs`、`CueMarker.cs`、`CueReceiver.cs`、`CueContext.cs`、`CueNames.cs` | Timeline 发出字符串 Cue；接收器将 Cue 分发给 `ICueHandler`，其中“切换内容”由 `ContentController` 调用 `NextContent()`。 |
| 空间触发 | `BlueVortexPortalTrigger.cs` | 在海洋馆检查头显位置或相关碰撞体，满足条件后直接切换至水母馆，并避免重复请求。 |

一次正常跨场景转场的实际顺序为：触发请求 → 渐暗 → 暂停旧场景根对象 → Additive 加载新场景 → 查找新 `ContentController` → 复位 Timeline → 卸载旧内容 → 应用 `SceneSpawnPoint` → 渐亮 → 播放 Timeline。加载失败时，流程会尝试恢复旧场景及旧 Timeline。直接打开某个内容场景用于调试时，代码允许该场景自身的 XR 相机继续工作；从 `Entry` 进入主线时，以常驻玩家相机为准。

`SceneSpawnPoint` 以出生点的世界位置和水平朝向定位 XR Origin，不直接写被追踪头显的 Transform。一个场景只能保留一个启用的出生点；若发现多个，代码会跳过定位并输出警告。这是新增场景时必须检查的条件。

### 2.2 手势与场景效果

学校场景使用 `FistGestureDetector` 读取 `XRHandTrackingEvents`，并用 `Assets/Script/Interact/HandShape/Fist Hand Shape.asset` 判断握拳。场景中的 UnityEvent 分别连接到 `FistGliderSpawner.OnFistStarted/OnFistEnded`。生成器从追踪到的手掌关节取位置，同一时刻只保留一个滑翔伞；松拳、换手或追踪丢失时清理对象。

水母馆使用 `PalmUpDetector` 比较手掌法向与世界上方向，场景配置的进入角为 50°、退出角为 60°，形成迟滞以减少抖动。它把开始和结束事件交给 `JellyfishPalmSpawner`；生成器可分别为左右手创建水母，并在翻掌或追踪丢失时移除。两个场景的逻辑都依赖 `XRHandTrackingEvents` 和 `XRHandSkeletonDriver`，因此模拟器通过不等于真机手部追踪通过。

`Assets/SceneStory` 是工程中的角色剧情与路线工具，包含 Runtime 和 Editor 程序集；它与项目的 `WhaleRouteFollower`、`WhaleTailWaterSplash` 等脚本一起驱动鲸鱼路线和效果。`Assets/Script/Art` 负责文字、物体、船只等场景表现。`Assets/Scripts/Rendering/CloudSeaVolume.cs` 为学校场景提供体积云参数，移动平台路径采用较低的步数设置。`Assets/Editor/AndroidApkBuild.cs` 是 APK 构建入口。

## 3. 建议的代码组织规范

### 3.1 目录、命名与职责

后续新增代码建议统一放在 `Assets/Script` 下，不再新增并列的 `Script`/`Scripts` 目录。现有目录可分批迁移，不宜一次性移动大量脚本及资源。建议的目标结构：

```text
Assets/Script/
  Core/                 # 内容 ID、目录、流程状态、转场、Cue
  XR/                   # XR Origin、出生点、设备输入适配
  Interaction/          # 手势或空间条件的检测器与事件契约
  Gameplay/             # 滑翔伞、水母等具体反馈和对象生命周期
  Presentation/         # 鲸鱼、文字、船、云海等场景表现
  Editor/               # 只在 Unity 编辑器运行的构建与制作工具
```

每个类承担一个清晰职责：**检测器只报告输入状态变化；玩法响应器决定反馈；内容控制器决定当前章节；转场管理器负责载入和淡入淡出。**不要让手势脚本直接加载场景，也不要让模型动画脚本顺手修改全局流程。公开的 `ContentId`、Cue 名称和资源路径属于序列化契约，修改时同步搜索场景、Prefab、Timeline 和文档。

命名建议使用 `WhaleMovie.<Module>` 命名空间，类和公开成员用 PascalCase，私有字段统一选用一种风格（例如 `_fieldName`）。当前项目同时出现 `Assets/Script` 与 `Assets/Scripts`、`Prefeb` 拼写、`whaelblend` 拼写和中英文资源名；这些是现有路径，不应直接改名。计划迁移时，在 Unity 编辑器内移动并提交对应 `.meta`，逐一检查场景引用和 `Resources.Load` 字符串。

新增程序集定义时按依赖方向组织：`Core` 不引用具体玩法，`Interaction` 依赖 XR Hands，`Gameplay` 引用输入契约和场景模块，`Editor` 不进入运行时构建。现有 `Interact.asmdef` 和 `WhaleMovie.GamePlay.asmdef` 已存在，改动程序集名称或引用前先核对场景中的持久化 UnityEvent 目标。

### 3.2 流程状态与事件

流程修改建议统一经 `ContentFlowManager` 发起。一个事件只提出一次切换请求；调用方检查当前章节和 `IsTransitioning`，避免 Timeline Cue、传送门和其他触发器在同一帧竞争。需要保留两种转场方式时，应明确优先级和共同目标。新的 Cue 名称放进 `CueNames` 常量，Timeline Marker 的 `Cue ID` 与常量逐字一致；同一个 Cue 的处理者应明确，防止多个 `ICueHandler` 同时触发全局动作。

将“进入章节、播放、等待交互、请求退出、加载下一章、失败恢复”视为可观察的状态。每个状态应有单一负责人，重复进入或中途禁用组件时能安全清理。建议在转场完成事件或章节变更事件中记录内容 ID、场景名与结果；日志用于排查，不替代 UI。现有 `ReplayCurrentContent()` 是代码接口，并非面向普通用户的按钮，文档和交互设计不要默认用户可以随时回退。

## 4. 新增章节的操作规范

以下步骤适用于把一个新叙事段落加入默认主线。先决定它是独立场景、同场景内的新内容，还是仅在现有 Timeline 中增加片段；只有前两种需要新增 `ContentId` 或目录项。

1. **确定顺序和退出方式。**写清新段落的前一章、后一章、入口、结束条件，以及是否允许跳过或重复触发。当前主线按照 `ContentId` 数值排序，不是按 Build Settings 或目录列表的可见顺序排序。例如插在学校（20）与海洋（30）之间可选 25，但应由团队最终确认，避免 ID 冲突。
2. **创建场景与目录。**独立场景放在 `Assets/Scenes/`，在 `EditorBuildSettings.asset` 中启用，并保证 `Entry` 仍为索引 0。为新段落增加 `ContentId`，在 `ContentCatalog` 中设置一致的 ID、精确场景名、`includeInSequence`。当前工程未发现 `Assets/Resources/SceneManage/ContentCatalog.asset`，运行时使用 `CreateDefaultDefinitions()`；因此要么同步扩展该默认列表，要么创建目录资产并放到 `Resources/SceneManage/ContentCatalog`，不可只改枚举或 Build Settings。
3. **配置场景对象。**每个内容场景明确放置一个 `ContentController`，填写 ID、场景名、Timeline、CueReceiver 和内容根对象；加一个启用的 `SceneSpawnPoint` 并检查正前方。虽然代码有运行时回退控制器，正式章节应显式配置，便于定位绑定错误。
4. **制作叙事时间轴。**为 `PlayableDirector` 绑定音频、字幕、动画和角色表演。若要到时间自动进入下一章，添加 `Cue Track` 与 `CueMarker`，`Cue ID` 使用 `CueNames.SwitchContent` 的值“切换内容”。若章节必须由交互结束，则不要再放自动切换 Cue。检查 Cue Track 最终绑定到本章的 `CueReceiver`。
5. **检查显式目标。**搜寻 `SwitchTo(ContentId.XXX)` 等硬编码转场；例如当前海洋馆传送门明确指向 `Jellyfish`。插入新章后，明确是否修改该目标、保持分支，或改变触发区域。只依赖 `NextContent()` 的段落会受 ID 顺序影响。
6. **验证入口和失败恢复。**分别从 `Entry` 跑完整主线、直接打开新场景调试，检查唯一相机、出生点、Timeline 首帧、Cue、黑场、旧场景卸载、重复触发和失败后画面恢复。更新用户手册、场景清单及构建说明。

若只是同一场景内的第二段内容，使用 `orderInScene` 和独立 `ContentController` 管理各自根对象及 Timeline；同时核对 `ContentId` 排序、切换时是否淡出、退出时对象是否停用。不要把第二段简单追加到某个旧控制器下而丢失独立生命周期。

## 5. 新增交互逻辑的规范

**事件契约先于视觉反馈。**在设计说明中先写触发场景、输入来源、成立条件、持续状态、结束条件、丢失追踪时的行为、是否可重复、生成对象的归属和清理方式。完成这些定义后，再分别实现检测器和反馈器。

| 要点 | 推荐做法 | 当前可参照实例 |
| --- | --- | --- |
| 输入检测 | 订阅 XR Hands 事件，发出“开始/结束”状态变化；不要每帧生成对象。 | `FistGestureDetector`、`PalmUpDetector` |
| 状态稳定 | 对角度或距离阈值设进入/退出差值，避免边界抖动。 | 掌心向上在水母馆为 50°/60°。 |
| 反馈对象 | 由独立响应器持有实例，重复回调不重复生成。 | `FistGliderSpawner` 同时仅持有一件；`JellyfishPalmSpawner` 按左右手维护。 |
| 追踪丢失 | 将输入状态转为结束，立即隐藏或销毁临时对象。 | 两个检测器及响应器均有清理路径。 |
| 场景离开 | 在 `OnDisable` 或章节退出时解除订阅、停用对象。 | 现有手势组件有停用处理。 |
| 参数调整 | 阈值、偏移、Prefab 由序列化字段配置，并记录实际场景值。 | 水母生成器的掌心偏移、旋转、缩放。 |

在 Unity 场景中连接 UnityEvent 时，核对左/右手 `Handedness` 参数传递及开始、结束方法各自的目标。更换类名、程序集名或方法签名后，重新检查 Inspector 中的持久化事件连接。对于新交互，先用编辑器验证事件序列，再在 Quest 3 上测试遮挡、追踪丢失、双手同时触发、快速进出阈值和章节切换。不要把编辑器的 XR Device Simulator 结果直接当作真机通过。

## 6. FBX 模型、Prefab 与动画使用规范

当前工程的主要模型使用 FBX，例如 `Assets/Model/whaelblend.fbx`、`Assets/Model/飞跃/滑翔伞动画.fbx`、鲸馆分件、学校建筑，以及 `Assets/Resources/Prefeb/jellyfish3_animation.fbx`。部分运行时效果使用独立 Prefab。另有 `Assets/Model/海洋/floating_island.glb`，因此“目前主要使用 FBX”比“全部使用 FBX”更准确。

### 6.1 从源文件到场景

1. 原始 FBX 放入按场景或对象归类的资源目录；同时保留材质、纹理及 `.meta`。通过 Unity 编辑器导入和移动资源，不在文件管理器里批量替换已被场景引用的 FBX 或删除 `.meta`。
2. 在 Model Importer 中检查单位/缩放、朝向、网格、法线、动画和材质映射。现有鲸鱼、滑翔伞和水母动画 FBX 的导入配置均启用了动画，`animationType` 为 Generic（序列化值 2），网格可读性为关闭。新资源按是否需要动画或 CPU 读网格决定配置，不必机械复制所有旧资源设置。
3. 为运行时对象制作 Prefab，把材质、动画控制器、碰撞体和项目脚本放在 Prefab 或场景装配层。场景尽量引用 Prefab，避免相同 FBX 在不同场景产生互不一致的手工设置。需要运行时通过 `Resources.Load` 读取的资源才放在 `Resources` 下；其他资源不必搬入该目录。
4. 绑定表演路线时，区分角色根节点、导入模型节点和 Animator。`StageActorDriver` 要求 ActorRoot 的世界缩放为 1，Animator 位于其子节点；`WhaleRouteFollower` 使用路线代理的位姿驱动模型并提供旋转偏移。不要直接改动被 Timeline 和脚本共同控制的同一 Transform。
5. 交互对象要检查连接点和局部坐标。滑翔伞生成器按渲染器节点名 `抓手_防滑握持段` 定位握持点；改 FBX 层级名称前须同步修改该字段。掌心水母由 Prefab 实例生成，脚本会清零实例局部位置、按场景倍率缩放，并重新居中可见模型。

当前 `FistGliderSpawner` 的回退加载字符串是 `Prefeb/滑翔伞动画`，`JellyfishPalmSpawner` 是 `Prefeb/Jellyfish3 Animated 03`。`Prefeb` 是工程里的实际目录名；即使拼写不规范，也不能只改文件夹名而不改代码和引用。建议在未来单独迁移时统一为 `Prefabs`，并通过 Unity 编辑器核对 GUID、场景绑定及运行时加载。

### 6.2 材质、碰撞与 Quest 3 性能

材质应在 URP 下检查着色器和贴图通道，不以编辑器场景视图显示正常作为头显验收结论。只给确实需要碰撞、抓取或触发的对象配置碰撞体；传送门需要触发区域和头显位置判定，纯视觉鲸鱼与水母不应凭 FBX 导入设置自动生成大量碰撞体。对重复出现或占画面较大的模型，先查看网格、纹理、动画和 Draw Call 的实际开销，再决定 LOD、纹理压缩、动画简化或材质合并，不在未测量时设统一数值上限。

学校场景的 `CloudSeaVolume` 在移动平台分支使用简化采样，默认移动步数 16、光照步数 0；新增体积效果应以 Quest 3 上的帧时间和舒适度测试为依据。模型、粒子、透明材质和多光源会共同影响 XR 双眼渲染开销，需在真实设备上检查，而不是只在 PC 编辑器里观察。

## 7. 构建、验证与文档维护

当前 Android 构建脚本 `WhaleMovie.BuildTools.AndroidApkBuild.Build` 读取 Build Settings 中所有启用场景，要求 `-apkOutput` 指向 `.apk`，并使用 Android 目标与 LZ4 压缩。执行前先确认 `Entry` 为首场景，核对实际要发布的其他场景；构建脚本不会自动替团队判定哪些场景应进入主线。`ProjectSettings/ProjectSettings.asset` 中仍可见 `productName: WhaleMovie` 与默认包名 `com.DefaultCompany.WhaleMovie`，正式发布命名、包名、签名与版本号需要在发布流程中确认，本文件不代填。

建议每次改动至少覆盖以下核对点：

- **流程：**从 `Entry` 连续运行五段；新章插入后核对目录、ID、Build Settings、直接场景调试和默认终点。
- **转场：**Cue 到点、传送门进入、同一帧重复请求、场景加载失败、黑场恢复、旧相机停用、出生点唯一。
- **交互：**左右手、双手同时、松拳或翻掌、追踪丢失、场景离开后的对象清理。
- **资源：**FBX 材质与动画、Prefab 引用、`Resources.Load` 路径、场景中的缺失脚本和持久化 UnityEvent。
- **设备：**在 Meta Quest 3 上检查手部追踪、画面可读性、转场舒适度和帧时间；保留关键日志或截图供回归比较。
- **文档：**任何章节顺序、退出条件、手势或模型连接点的变化，都同步修改本开发规范和用户手册。

## 8. 建议的整理顺序

1. **先固定行为。**决定海洋馆到底使用“传送门必经”还是“时间到也可离开”；在真机上记录当前行为，随后统一 Cue、触发器和用户说明。
2. **再固定目录。**为主线创建显式的 `ContentCatalog.asset`，将 Build Settings 与目录分工写清，并检查 `BoatMusium` 的发布用途。
3. **再整理代码边界。**优先处理新功能的模块划分和事件契约；旧脚本按场景逐步迁移，移动时保留 `.meta` 并检查序列化引用。
4. **最后整理模型与性能。**建立 FBX、Prefab、材质、动画的资源对应表；改名和移动采用单独变更。每个高开销效果都在 Quest 3 上验证。

这份文档不替代 Unity 场景、Inspector 和最终安装包的实测。它把当前项目的可核对事实与后续团队应采用的规范放在同一处，供每次新增章节或交互时逐项更新。
