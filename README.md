# ProxyPlugin

为卫月插件提供系统代理、手动代理及静态资源镜像加速。输入 `/proxyplugin` 打开设置，`/proxyplugin diag` 打开网络诊断。

源码：[WayuSama/ProxyPlugin](https://github.com/WayuSama/ProxyPlugin)，基于 Loskh/ProxyPlugin，保留 AGPL-3.0-or-later 许可。

在卫月「设置 → 测试版 → 自定义插件仓库」加入与 Pulsar 相同的安装源：

```text
https://raw.githubusercontent.com/WayuSama/dalamud-plugins/refs/heads/main/pluginmaster.json
```

刷新插件列表，搜索 `ProxyPlugin` 安装。旧版用户可先卸载旧插件，再从此源安装，原有配置会保留。

当前版本 `0.3.0.1`（基于原版 `0.3.0.0`，修改序号 01）。修复 Harmony/MonoMod 在卫月隔离加载环境中的类型冲突，并在初始化失败时恢复代理、撤销部分补丁。

本地加载请使用 Release 中的完整 `ProxyPlugin.zip`，或构建产物的完整目录；不再需要额外的 `libs/0Harmony.dll`。

构建需要 .NET 10 SDK、Dalamud API 15，通过 `DALAMUD_HOME` 指定卫月依赖目录：

```powershell
dotnet build ProxyPlugin/ProxyPlugin.csproj -c Release
dotnet test ProxyPlugin.Core.Tests -c Release
./scripts/test-interception.ps1
```

发布时创建与项目版本一致的 `v四段版本号` 标签。只读工作流会构建、测试并生成 `ProxyPlugin.zip` 和兼容索引 `repo.json` 作为构建产物，再将它们上传至 GitHub Release。正式安装源独立维护在 [WayuSama/dalamud-plugins](https://github.com/WayuSama/dalamud-plugins)，将新版本的 `repo.json` 条目合入其 `pluginmaster.json`，保留其他插件。
