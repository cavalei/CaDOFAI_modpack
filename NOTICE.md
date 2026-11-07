# 第三方组件与许可

本项目分发/编译时用到以下第三方组件。**这些 DLL 不随仓库提供**，需自行从游戏或官方渠道获取。

| 组件 | 许可 | 来源 | 用途 |
|---|---|---|---|
| Overlayer | GPL-3.0 | modlist-org/Overlayer | 上游本体 |
| Overlayer.Module.ADOFAI | GPL-3.0 | modlist-org/Overlayer.Module.ADOFAI | 上游模块 |
| UnityModManager | MIT | UnityModManager 官方 | 模组加载器 |
| Harmony (0Harmony) | MIT | Andreas Pardeike | 运行时补丁 |
| O5Kit | 见上游声明 | Overlayer 生态 | UI / 输入工具库 |
| ClearScript (V8) | MIT | Microsoft | JS 表达式引擎 |
| Newtonsoft.Json | MIT | James Newton-King | JSON 序列化 |
| FuzzySharp | MIT | Božo Stojković | 模糊匹配 |
| Esprima | BSD-2-Clause | Sébastien P. | JS 语法解析 |
| NativeFileDialog.Extended | zlib | Nathan T. | 文件对话框 |
| Microsoft.Bcl.AsyncInterfaces | MIT | Microsoft | ClearScript 的编译期依赖 |

## 说明

- 本仓库的**源码**按 GPL-3.0 发布；上述第三方组件的**二进制**按其各自许可分发。
- `Overlayer/` 与 `Overlayer.Module.ADOFAI/` 保留上游版权声明与许可文本（见各自目录下的 `LICENCE.md`）。
- 若你是第三方组件的作者并认为此处署名不当，请开 issue 更正。
