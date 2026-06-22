# HandsUp

HandsUp 是一个以暂停菜单为入口的时空回退模组，包含单机回退功能与联机兼容逻辑。

当前公开版本：`v1.1.0`

## 项目结构

- `HandsUpCode/`：主要 C# 源码
- `HandsUp/`：Godot 资源与模组图标
- `tools/`：辅助脚本
- `HandsUp.json`：模组清单
- `HandsUp.csproj`：项目文件
- `project.godot`：Godot 工程文件

## 本地构建

1. 安装 MegaDot / Godot 4.5.1 Mono
2. 安装 .NET SDK
3. 复制 `Directory.Build.props.example` 为 `Directory.Build.props`
4. 按本机环境填写 `GodotPath`，必要时填写 `Sts2Path`
5. 构建项目：

```powershell
dotnet build HandsUp.sln
```

## 注意事项

- `Directory.Build.props` 是本机本地配置，不建议上传到仓库。
- `.godot/`、`bin/`、`obj/`、发布包和游戏安装目录文件不应进入源码仓库。
- 联机测试时，双方必须使用相同版本的 `HandsUp` 与 `BaseLib`。

## 反馈

如有问题，请抖音搜索 `IHanabiI`，找到作者后加群并录视频反馈。

本模组目前完全免费，不允许以任何形式盈利。
