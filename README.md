# CaDOFAI — ADOFAI 的 Overlayer UMM 移植版 + StrictJudge 严判模块

把 [Overlayer](https://github.com/modlist-org/Overlayer) 从 MelonLoader 移植到 **UnityModManager（UMM）**，
运行在 ADOFAI 3.4.0（Unity 6 / Mono）上；并附带一个自研模块 **StrictJudge**，
用于预测曲目在「严判」规则下会不会失误、失误在哪一轨，并生成报表。

> **本项目是修改版，不是上游官方发布。**
> 上游 Overlayer 与 Overlayer.Module.ADOFAI 均为 **GPL-3.0**，本仓库按其要求公开全部源码与改动说明。
> 详见 [LICENSE](LICENSE) 与 [NOTICE](NOTICE.md)。

---

## 内容

| 目录 | 说明 | 来源 |
|---|---|---|
| `Overlayer/` | Overlayer 本体（UMM 版） | 上游 5.9.3 + 我们的移植补丁 |
| `Overlayer.Module.ADOFAI/` | ADOFAI 模块 | 上游 1.4.6 + 补丁 |
| `StrictJudge/` | 严判预测模块（自研） | 本项目原创 |
| `libs/` | 第三方依赖（不随仓库提供，见下） | — |

---

## 移植改动（相对上游）

1. **新增 `Overlayer/Overlayer/UmmLoader.cs`**
   实现 `IOverlayerHost` / `IOverlayerLogger`，向 UMM 注册 `Load(ModEntry)`；
   延迟 3 秒创建引导对象并调用 `MainCore.Initialize`；`OnUpdate` 驱动 `MainCore.Tick()`；
   `SceneManager.sceneLoaded` 时补做 `EnsureInitialized` / `EnsureRootAlive` 与布局刷新；
   Harmony 实例用 `new Harmony(modEntry.Info.Id)`；卸载时 `UnpatchAll`。

2. **新增 `Overlayer/Overlayer/MelonShim.cs`**
   提供空 `MelonLoader` 命名空间与 `MelonLoader.Utils.MelonEnvironment`
   （`GameRootDirectory` / `UserDataDirectory`），让上游代码在无 MelonLoader 的环境下编译。

3. **新增 `Overlayer/Overlayer/Overlayer.umm.csproj`**
   `netstandard2.1` + `UMM` 常量 + `ImplicitUsings` + `AllowUnsafeBlocks`；
   引用游戏 `Managed` 下除框架本体外的程序集、UMM 的 `UnityModManager.dll` / `0Harmony.dll`、
   `libs` 下的第三方 DLL；`DebugType=none` 以避免把本机路径写进二进制。

4. **新增 `Overlayer.Module.ADOFAI/ADOFAI/Overlayer.Module.ADOFAI.umm.csproj`**
   与 3 同理，改为引用本项目编译出的 `Overlayer.dll`。

5. **`UmmLoader` 补充两处行为（对齐旧移植版）**
   - `LoadModuleAssemblies`：扫描模组目录下 `Overlayer.Module.*.dll` 并 `Assembly.LoadFrom`
   - `OverlayerUmmBoot.ApplySceneGate`：反射读 `scrController.instance`，为空（主界面）时隐藏所有画布，
     进入关卡后恢复；每 0.25 秒判定一次，切场景强制重判
   - `OverlayerFilePath` 返回 `<游戏根>\UserData`（与 MelonLoader 版一致，Runtime 会再拼 `Overlayer`）

6. **新增 `StrictJudge/`**（原创模块，GPL-3.0 同样适用）

> 上游 5.9.3 / 1.4.6 已自带的部分（`GameAccess` 读 `LevelData`、`Song.cs` 走 `GameAccess.LevelSong`）
> 无需再改，本项目直接沿用。

---

## 构建

前置：.NET SDK 9、游戏本体（提供托管程序集）。

```bat
:: 1. 准备第三方 DLL（不随仓库分发）
::    从已安装的 Overlayer 模组目录复制到 libs\：
::    O5Kit.1.0.dll  ClearScript.Core.dll  ClearScript.V8.dll
::    FuzzySharp.dll  Esprima.dll  NativeFileDialog.Extended.dll

:: 2. 编译本体
cd Overlayer\Overlayer
dotnet build Overlayer.umm.csproj -c Release

:: 3. 编译 ADOFAI 模块
cd ..\..\Overlayer.Module.ADOFAI\ADOFAI
dotnet build Overlayer.Module.ADOFAI.umm.csproj -c Release

:: 4. 编译 StrictJudge
cd ..\..\StrictJudge
dotnet build StrictJudge.csproj -c Release
```

游戏路径默认 `E:\SteamLibrary\steamapps\common\A Dance of Fire and Ice`，
可用 `-p:GamePath=你的路径` 覆盖。

---

## 安装

1. 游戏装好 UMM（`winhttp.dll` + `doorstop_config.ini`）
2. 把 `Overlayer.dll`、`Overlayer.Module.ADOFAI.dll`、`Overlayer.Module.StrictJudge.dll`
   与第三方 DLL 一起放进 `Mods\OverlayerUMM\`
3. 同目录放 `info.json`（UMM 的模组描述）
4. 启动游戏，日志出现 `[Overlayer] Delayed boot done (UMM)` 即成功

---

## StrictJudge 是什么

- 用 Harmony patch `scrMisc.GetHitMarginInSec` 记录每次击打的时间偏移
- 与「严判」窗口比较，预测本局是否会死、死在哪一轨
- 通过 `TcpListener` 提供只读报表服务（默认关闭，需在配置里指定绑定地址）
- 产出 `严判记录.json` / `严判检查点.json` / `严判失败记录.xlsx` / `严判失败记录.html`

**注意**：StrictJudge 依赖本移植版的模块 API，官方 MelonLoader 版加载器不识别，因此必须与本仓库一起使用。

---

## 已知限制

- 只针对 **ADOFAI 3.4.0 / Unity 6 / Mono** 验证
- UMM 版没有 MelonLoader 的 `OnSceneWasInitialized` 等回调，改由 `SceneManager.sceneLoaded` + 逐帧兜底
- 上游若更新，本补丁需要重新对齐

---

## 许可

本项目整体按 **GPL-3.0** 发布，与上游一致。修改部分已在上文列出。
`StrictJudge/` 为本项目原创，同样以 GPL-3.0 提供。

第三方组件许可见 [NOTICE.md](NOTICE.md)。
