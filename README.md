# 魔力宝贝窗口排列器（Moli Preview）

Windows 小工具，用于把当前打开的 `Reincarnation.exe` 客户端快速排列到显示器工作区中。

## 下载

**[⬇ 下载编译版 MoliWindowTiler.exe](https://github.com/royluo1984/moli-preview/releases/latest/download/MoliWindowTiler.exe)**

[查看 Release 版本和校验值](https://github.com/royluo1984/moli-preview/releases)

## 功能

- 自动识别当前打开的魔力宝贝客户端。
- 按每个客户端当前的原始外框尺寸计算布局；支持 `800×600` / `640×480` 客户端混合排列，排列过程只移动位置，不向客户端发送尺寸或分辨率变更。
- 支持智能排列、一行横排、一列竖排、两行、三行，以及 `3,3`、`2,2,2` 这类自定义行组合。
- 支持左上、上中、右上、左中、居中、右中、左下、下中、右下九种对齐方式。
- 支持设置客户端之间的间距；默认 `0` 像素，屏幕空间足够时会严格保留该间距。
- 通过右上角半透明切换浮层，点击人物名即可激活对应客户端。
- 从线程描述或窗口标题读取人物名，并把每个人物的窗口位置保存到本机的 `character-positions.json`。
- 排列后允许窗口重叠，同时保证每个窗口外框完整位于屏幕工作区内。

## 使用

1. 启动一个或多个游戏客户端。
2. 双击 `run_window_tiler.cmd`。
3. 点击“刷新窗口”，确认需要排列的客户端已勾选。
4. 选择排列方式；6 开时可选择“两行均匀”，或选择“自定义组合”填写 `3,3`。
5. 选择对齐方式，并设置窗口间距（默认 0 像素）。
6. 点击“一键排列”。

右上角浮层可以通过“显示点击切换浮层”复选框关闭。点击浮层中的人物名会恢复并激活对应客户端。

## 编译

项目使用 Windows 自带的 .NET Framework C# 编译器，不依赖第三方库：

```text
build_window_tiler.cmd
```

编译输出为 `WindowTiler\\bin\\Release\\MoliWindowTiler.exe`。

## 项目结构

- `WindowTiler/`：C# 源码和项目文件。
- `build_window_tiler.cmd`：使用 .NET Framework 编译源码。
- `run_window_tiler.cmd`：编译（如需要）并启动程序。

## 许可证

MIT License，详见 [LICENSE](LICENSE)。
