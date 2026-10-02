# 魔力宝贝窗口排列器（Moli Preview）

Windows 小工具，用于把当前打开的 `Reincarnation.exe` 客户端快速排列到显示器工作区中。

## 下载

**[⬇ 下载编译版 MoliWindowTiler.exe](https://github.com/royluo1984/moli-preview/releases/latest/download/MoliWindowTiler.exe)**

[查看 Release 版本和校验值](https://github.com/royluo1984/moli-preview/releases)

## 环境要求

### 运行已编译版本

- Windows 7 SP1 或更高版本，建议使用 Windows 10/11。
- 已安装 .NET Framework 4.8 Runtime。程序的项目目标为 .NET Framework 4.0，4.8 Runtime 可以兼容运行；Windows 10/11 通常已经内置兼容组件。
- 至少连接一个显示器，并使用桌面窗口会话运行程序。
- 需要排列的游戏客户端应为窗口模式，并且进程名称为 `Reincarnation.exe`。

发布版不需要安装 .NET SDK、Visual Studio 或其他第三方库。若启动时提示缺少 .NET Framework，请从微软官方下载并安装：

- [.NET Framework 4.8 Runtime（微软官方下载）](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- [.NET Framework 4.8 Developer Pack（从源码编译时使用）](https://dotnet.microsoft.com/download/dotnet-framework/net48)

安装完成后重新启动 `MoliWindowTiler.exe`。建议把程序放在用户有读写权限的目录，例如 `D:\Tools\moli-preview`，这样程序可以保存窗口位置、设置和错误日志。

### 从源码编译

- Windows 系统及 .NET Framework 4.x 的 C# 编译器。`build_window_tiler.cmd` 会自动查找 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，找不到时再查找 32 位编译器目录。
- Git（仅在使用 `git clone` 获取源码时需要）：[Git for Windows 官方下载](https://git-scm.com/download/win)。
- 若系统没有可用的 .NET Framework 编译器，可安装上面的 [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48)。

源码构建不依赖 NuGet 包或其他第三方库。

## 安装与首次运行

### 直接安装发布版

1. 打开 [Releases](https://github.com/royluo1984/moli-preview/releases) 页面，下载最新的 `MoliWindowTiler.exe`。
2. 将 exe 保存到单独的文件夹中；首次使用前确认已安装上面的 .NET Framework 4.8 Runtime。
3. 先启动一个或多个窗口模式的游戏客户端，再双击 `MoliWindowTiler.exe`。
4. 点击“刷新窗口”，确认客户端列表出现后勾选要排列的账号。
5. 选择排列方式、对齐方式、间距和目标屏幕，然后点击“一键排列”。

程序首次运行后，会在 exe 所在目录生成以下本地文件：

- `moli-settings.json`：排列方式、间距、目标屏幕等设置。
- `character-positions.json`：按人物名称保存的窗口位置。
- `WindowTiler-error.log`：运行异常记录。

### 从 GitHub 源码安装

在 PowerShell 或命令提示符中执行：

```powershell
git clone https://github.com/royluo1984/moli-preview.git
cd moli-preview
.\build_window_tiler.cmd
.\run_window_tiler.cmd
```

编译成功后，程序位于 `WindowTiler\bin\Release\MoliWindowTiler.exe`。也可以直接运行该 exe；`run_window_tiler.cmd` 在找不到编译结果时会先调用构建脚本。

### 升级

下载新版本 exe 后，替换旧的 `MoliWindowTiler.exe` 即可。若希望保留上次的排列位置和界面设置，请保留同一目录下的 `moli-settings.json` 与 `character-positions.json`。

## 功能

- 自动识别当前打开的魔力宝贝客户端。
- 按每个客户端当前的原始外框尺寸计算布局；支持 `800×600` / `640×480` 客户端混合排列，排列过程只移动位置，不向客户端发送尺寸或分辨率变更。
- 支持智能排列、一行横排、一列竖排、两行、三行，以及 `3,3`、`2,2,2` 这类自定义行组合。
- 支持左上、上中、右上、左中、居中、右中、左下、下中、右下九种对齐方式。
- 支持设置客户端之间的间距；默认 `0` 像素，屏幕空间足够时会严格保留该间距。
- 通过右上角半透明切换浮层，点击人物名即可激活对应客户端。
- 浮层中的客户端按钮支持拖动交换；交换后会立即按新的顺序重新排列窗口，并保存该顺序。
- 提供“关闭所有客户端”按钮，确认后向当前发现的全部客户端发送正常关闭请求。
- 从线程描述或窗口标题读取人物名；登录后线程名/标题发生变化时，会用人物后缀、线程号和进程启动标识继续匹配同一个客户端，并把窗口位置保存到本机的 `character-positions.json`。
- 排列后允许窗口重叠，同时保证每个窗口外框完整位于屏幕工作区内。
- 智能排列在 6 个客户端时默认使用 `3,3` 两行布局，并记住上次使用的排列方式、行组合、对齐、间距、边距、目标屏幕和切换浮层设置。
- 窗口排列偏好保存在本机的 `moli-settings.json`；已勾选客户端按人物名称记录，重启软件后自动恢复。

## 使用

1. 启动一个或多个游戏客户端。
2. 双击 `run_window_tiler.cmd`。
3. 点击“刷新窗口”，确认需要排列的客户端已勾选。
4. 选择排列方式；6 开时可选择“两行均匀”，或选择“自定义组合”填写 `3,3`。
5. 选择对齐方式，并设置窗口间距（默认 0 像素）。
6. 点击“一键排列”。

右上角浮层可以通过“显示点击切换浮层”复选框关闭。点击浮层中的人物名会恢复并激活对应客户端；按住某个客户端按钮拖到另一个按钮上，会交换两者在排列中的位置。交换后的顺序保存在 `moli-settings.json`，下次启动后仍会沿用。

## 编译

项目使用 Windows 自带的 .NET Framework C# 编译器，不依赖第三方库：

```text
build_window_tiler.cmd
```

编译输出为 `WindowTiler\bin\Release\MoliWindowTiler.exe`。

## 项目结构

- `WindowTiler/`：C# 源码和项目文件。
- `build_window_tiler.cmd`：使用 .NET Framework 编译源码。
- `run_window_tiler.cmd`：编译（如需要）并启动程序。

## 许可证

MIT License，详见 [LICENSE](LICENSE)。
