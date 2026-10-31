# Quest 3 开机启动

## 已加入的功能

`WhaleMovieBoot.androidlib` 是 Unity 会自动纳入 Android 构建的原生插件。
开机完成后，`BootReceiver` 收到 `BOOT_COMPLETED`，根据当前 APK 的包名查找正常启动入口，并请求打开应用。
当前项目包名是 `com.DefaultCompany.WhaleMovie`；接收器运行时读取包名，后续更改包名无需改 Java 代码。

插件清单只追加权限和接收器，Unity 和 XR 插件继续生成 VR Activity、手部追踪等配置。
不需要将脚本挂到 GameObject，也不需要开启 Custom Main Manifest。

文件位置：

```text
Assets/Plugins/Android/
  WhaleMovieBoot.androidlib/
    AndroidManifest.xml
    build.gradle
    src/com/lollyproject/whalemovie/boot/BootReceiver.java
```

## 安装后怎么用

1. 回到 Unity，等待新插件导入。
2. 以 Android / ARM64 正常重新打包，并将新 APK 安装到 Quest 3。
3. 安装后在头显里手动完整打开一次，再退出。新安装且从未打开的应用通常处于 stopped 状态，不能直接依赖开机广播启动；在系统中“强行停止”后也需要再次手动打开。
4. 按下面的说明处理后台启动权限。
5. 完全关机再开机，观察应用是否打开。摘下头显、待机和唤醒都不是重新开机，插件不会在这些操作时重新启动应用。

已通过 Unity 2022.3.53f1c1 的 Android ARM64 构建，输出至 `G:\LollyProject\VRPackaged\1.apk`。
包内包含开机接收器和所需权限。2026-10-28 已通过 USB 覆盖安装到 Quest 3，保留原应用数据。
已将 `SYSTEM_ALERT_WINDOW` 授权设为 `allow`，并通过正常 VR Activity 入口打开应用一次（系统返回 `Status: ok`）。
尚未进行头显完全关机后的开机自启动确认。

## Quest 系统权限与限制

Android 10 及后续版本限制后台启动 Activity。接收到开机广播并不自动获得显示 VR 界面的资格。
插件声明了 `SYSTEM_ALERT_WINDOW` 特殊权限，以便在设备允许时使用 Android 文档中的后台启动例外；它不会自动弹出授权页面，也不会实际绘制悬浮窗。
普通 Manifest 声明不会自动授予该特殊权限。

如果头显提供“显示在其他应用上层”授权入口，可为本应用开启。
否则，在已开启开发者模式且授权 USB 调试的设备上，可以用电脑 ADB（或安卓手机 Bugjaeger 的 Shell）申请这项权限：

```text
appops set com.DefaultCompany.WhaleMovie SYSTEM_ALERT_WINDOW allow
```

电脑执行时加上 ADB 前缀：

```powershell
adb shell appops set com.DefaultCompany.WhaleMovie SYSTEM_ALERT_WINDOW allow
```

撤销授权：

```powershell
adb shell appops set com.DefaultCompany.WhaleMovie SYSTEM_ALERT_WINDOW default
```

如果更改了项目包名，应同步替换这些命令里的包名。重新安装后权限是否保留由设备系统决定。
该权限是可能适用的 Android 后台启动例外，不保证当前 Horizon OS 接受授权或允许启动 VR Activity。
系统初始化、边界确认等仍由 Quest 系统控制。此实现也不会限制 Home 键或把设备锁定成 Kiosk。

## 查看启动日志

```powershell
adb logcat -d -s WhaleMovieBoot ActivityTaskManager Unity
```

- `BOOT_RECEIVED`：系统确实把开机广播发给了接收器。
- `START_REQUESTED`：已调用启动入口；不等于成功显示 VR 画面。
- `NO_LAUNCHER` / `START_FAILED`：入口不存在或启动抛出了异常。
- `ActivityTaskManager` 中的后台启动拒绝日志：系统阻止了启动；重复调用启动、增加延时或不断重试不能授予权限。

## 暂停或移除

可在 Unity 中选中 `WhaleMovieBoot.androidlib`，在 Plugin Inspector 中取消 Android 平台，再重新打包。
或者只禁用安装在头显中的接收器（不用禁用整个应用）：

```powershell
adb shell pm disable com.DefaultCompany.WhaleMovie/com.lollyproject.whalemovie.boot.BootReceiver
```

重新启用：

```powershell
adb shell pm enable com.DefaultCompany.WhaleMovie/com.lollyproject.whalemovie.boot.BootReceiver
```

## 官方资料

- Unity Android Library 插件：https://docs.unity3d.com/2022.3/Documentation/Manual/android-library-project-import.html
- Android 后台启动限制：https://developer.android.com/guide/components/activities/secure-bal
- Meta 对 Quest 开机启动的说明：https://developers.meta.com/vr/support/investigations/2685347365156014/
