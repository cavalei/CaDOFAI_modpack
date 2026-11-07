# PROGRESS — StrictJudge

> 本模块的独立迭代记录。整套的发版记录见上级目录的 `../PROGRESS.md`。
> 按 AGENTS.md 第 11 节：每个阶段完成追加，新的写在最上面。

---

## 阶段 1：源码入库 + 编译通过（2026-11-08）

### 是什么

Overlayer 的一个模块，把每次击打的时间偏移与「严判」窗口比较，预测这一局会不会死、死在哪一轨。

```
继承         OverlayerModule
依赖         Overlayer.ModuleAPI / Tag.Core / O5Kit / Newtonsoft.Json / 0Harmony
运行时        用 Harmony patch 抓击打，用 scrPlayer.Die / scrController.OnLandOnPortal 结算
```

### 规模

```
Module.cs        1,122 行   54 KB
ReportServer.cs    198 行    8 KB
StrictJudge.csproj            netstandard2.1
产物              Overlayer.Module.StrictJudge.dll   44 KB
```

### 输出文件

```
严判记录.json          每次击打的记录
严判检查点.json        检查点
严判关卡.json          按关卡汇总
严判失败记录.xlsx      报表（自带 Xlsx 写入器，无第三方依赖）
严判失败记录.html      网页版，落在桌面
```

### 游戏内标签

```
SJ_Status    严判状态（会死 / 空）
```

### 报告服务

```
ReportServer.cs   TcpListener，只读
默认状态          关闭
绑定地址          需在配置里显式指定（原设计为热点 IP）
```

### 完成了什么

```
✅ 源码从桌面备份搬进仓库（StrictJudge/）
✅ 新写 StrictJudge.csproj，改为引用我们 UMM 版 Overlayer.dll
✅ 编译通过，0 error，产物 44 KB（与旧版同尺寸）
```

### 验证结果

```
✅ 编译通过
✅ 产物尺寸与旧版一致（44 KB → 说明代码未变）
❌ 游戏内未实测：标签是否注册、报表是否生成、TCP 服务是否可开
```

### 遗留问题

```
① 未在游戏里跑过：需确认 SJ_Status 标签出现在 Overlayer 标签列表里
② 未验证报表：跑一首失败曲目，看 json/xlsx/html 是否落到 OutDir 与桌面
③ ReportServer 默认关闭，绑定地址配置项没在本次核实
④ 依赖本移植版 Overlayer 的模块 API，官方 MelonLoader 版加载器不认
```

### 下一步

```
1. 装到 Mods\OverlayerUMM\ 起游戏，确认模块被 ModuleService 发现
2. 跑一首必死曲目，检查 5 个输出文件
3. 需要远程看报表时再开 ReportServer，注意只绑内网地址
```
