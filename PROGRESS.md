# PROGRESS — CaDOFAI

> 按 AGENTS.md 第 11 节：每个阶段完成后追加，新的写在最上面。

---

## 阶段 1：源码重建 + 首发 v2.0（2026-11-08）

### 完成了什么

**源码重建**

```
clone 上游 modlist-org/Overlayer @5.9.3、Overlayer.Module.ADOFAI @1.4.6
新增 Overlayer/Overlayer/UmmLoader.cs           UMM 入口（IOverlayerHost/Logger，3 秒延迟启动，场景钩子）
新增 Overlayer/Overlayer/MelonShim.cs           MelonEnvironment 垫片
新增 Overlayer/Overlayer/Overlayer.umm.csproj    netstandard2.1 + UMM
新增 Overlayer.Module.ADOFAI/ADOFAI/Overlayer.Module.ADOFAI.umm.csproj
新增 StrictJudge/                               自研模块源码（Module.cs + ReportServer.cs）
补齐 README.md / NOTICE.md / LICENSE(GPL-3.0) / .gitignore
```

**编译**

```
Overlayer.dll                      2,075 KB   0 error
Overlayer.Module.ADOFAI.dll          116 KB   0 error
Overlayer.Module.StrictJudge.dll      44 KB   0 error
三个产物均确认不含 C:\Users / AppData 路径
```

**发布**

```
仓库     https://github.com/cavalei/CaDOFAI_modpack  公开 · GPL-3.0
提交     f68a691，465 个文件（libs/、bin/obj 已 gitignore）
Release  v2.0，四个资产：CaDOFAI_v2.0.exe、三个 DLL
```

### 验证结果

```
✅ 三个程序集编译通过
✅ 二进制无个人路径（DebugType=none）
✅ .git/config 无 token 残留（push 用临时 Basic 头）
✅ GitHub 识别许可为 GPL-3.0
❌ 游戏内未实测
```

### 踩过的坑（下次直接照抄结论）

```
① Managed\*.dll 全量引用会顶掉 netstandard 引用程序集
   → 必须 Exclude: mscorlib/netstandard/System*/Microsoft.CSharp/Mono.*/I18N*
② ClearScript 7.5.1 依赖 Microsoft.Bcl.AsyncInterfaces 9.0，游戏只有 8.0
   → 加 PackageReference 9.0 + PrivateAssets=all + ExcludeAssets=runtime
③ 上游要 ImplicitUsings=enable + AllowUnsafeBlocks=true
   → 不加会报 1500+ CS0246，从 21266 错误降到 0 的关键
④ Push 不能用 "Authorization: bearer"，要用 Basic base64(x-access-token:TOKEN)
   并加 -c credential.helper= 关掉凭据管理器
```

### 遗留问题

```
① 游戏内未验证：日志应出现 [Overlayer] Delayed boot done (UMM) 与 [ModuleService] Module found
② ADOFAI 模块 116 KB，旧版 286 KB，功能可能有差异，需实际跑曲目确认
③ 本机 Mods\OverlayerUMM\ 里仍是旧 DLL，未替换成新编译的
④ 仓库未配 user.name/email（仅本地仓库临时设了 cavalei）
```

### 下一步

```
1. 把新编的三个 DLL 覆盖到 Mods\OverlayerUMM\，起游戏看日志
2. 跑一首曲子确认标签正常、StrictJudge 报表能生成
3. 如有功能缺失，对比上游 1.4.6 与旧版的差异
4. 稳定后再考虑补 CI（workflow 打包）
```
