<p align="center">
  <a href="README.zh-CN.md"><img alt="简体中文" src="https://img.shields.io/badge/%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87-README-ff6b6b?style=for-the-badge"></a>
  <a href="README.md"><img alt="English" src="https://img.shields.io/badge/English-README-2ea44f?style=for-the-badge"></a>
</p>

<h1 align="center">Audio Radar</h1>

<p align="center">
  一个面向无障碍、实验和 DeepSeek Harness 工作流的 Windows 端点环回音频方向雷达。
</p>

<p align="center">
  <img alt="Version" src="https://img.shields.io/badge/version-1.0.0-2ea44f">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8-512BD4">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-yellow">
</p>

![Audio Radar](docs/screenshot.png)

Audio Radar 通过 WASAPI loopback 读取 **Windows 输出端点最终混音**，从离散多声道能量估计方向，并绘制实时雷达。它**不注入游戏、不读取游戏内存、不抓取游戏进程**。

项目在 **DeepSeek Harness 44.7.0** 环境中构建和测试。Audio Radar 是独立的 Windows 应用，不是 DSH 插件，也不依赖 DSH API。

## 为什么做

听障——尤其是单侧耳聋——会丢失游戏里重要的方向线索。Audio Radar 是一个无障碍导向的实验：

- 读取和玩家相同的 Windows 混音；
- 用雷达、文字和可选悬浮窗显示方向；
- 保持外部实现、可审计；
- 避开对反作弊敏感的进程注入。

## 功能

- **自动读取默认输出端点**，不写死 HDMI 设备。
- **只使用端点环回**：WASAPI 共享模式，不 hook、不注入 DLL、不读内存。
- **通用声道布局**：2.0、5.1、7.1、5.1.2、7.1.2、5.1.4、7.1.4 和自定义 `WAVEFORMATEXTENSIBLE` mask。
- **方向估计**：声道频段能量 + 3D 单位向量；端点暴露 Top 声道时支持仰角。
- **自适应噪声底、瞬态增强、置信度、方向保持**。
- **半透明置顶悬浮雷达**：不透明度、尺寸、鼠标穿透、33ms 置顶重置。
- **声道电平表**：每声道 dBFS。
- **诊断工具**：端点探测、算法自测、采集测试、截图模式。

## 安装

### 方式一：GitHub Releases（推荐）

1. 打开 [Releases](https://github.com/MicroHEROX/audio-radar/releases)。
2. 下载 `AudioRadar-1.0.0-win-x64.zip`。
3. 解压到自己的目录。
4. 运行 `AudioRadar.exe`。

Release 包是 Windows x64 自包含版本；ZIP 里只有运行所需文件，**不会把工程文档一起安装**。

### 方式二：npm

```bash
npm install -g audio-radar-dsh
audio-radar
```

npm 包只是启动器，首次运行会从对应 GitHub Release 下载运行时，并缓存到 `%LOCALAPPDATA%\AudioRadar`。npm 包不包含文档。

### 方式三：从源码构建

需要 Windows 10 2004+ x64 和 .NET 8 SDK。

```powershell
git clone https://github.com/MicroHEROX/audio-radar.git
cd audio-radar
dotnet run
```

## 快速使用

1. 启动程序。
2. 端点选择保持“系统默认（跟随 Windows）”，或手动选一个端点。
3. 点击“开始监听”。
4. 播放音频；雷达显示方向，底部显示声道电平。
5. 点击“悬浮雷达”打开半透明悬浮窗。
6. 游戏时保持“鼠标穿透”；要拖动时先取消，拖好再勾选。

要获得 360° 雷达，端点必须暴露离散多声道 bed（5.1/7.1 或更高），游戏也要输出到该 bed。立体声端点只能判断左右。

## 悬浮雷达

![Overlay](docs/overlay.png)

- 无边框、圆角、半透明。
- 置顶，每 33ms 重新置顶一次。
- 尝试未公开的 `SetWindowBand(SYSTEM_TOOLS)`；Windows 拒绝时显示 `TopMost fallback`，继续使用 33ms 置顶。
- 鼠标穿透使用 `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`。
- 不需要全局热键。

**独占全屏是外部悬浮窗的硬限制。** 如果游戏覆盖悬浮窗，请改成无边框/窗口化。RTSS 直接画进游戏 swapchain 的 OSD，也不受外部窗口控制。

## 支持的音频布局

| 布局 | 声道数 | 高度 | 说明 |
|---|---:|---|---|
| Stereo 2.0 | 2 | 否 | 只能左右；前后未知 |
| 5.1 | 6 | 否 | 前 + 后 |
| 7.1 | 8 | 否 | 前 + 侧 + 后 |
| 5.1.2 | 8 | 是 | 顶部前对 |
| 7.1.2 | 10 | 是 | 顶部前对 |
| 5.1.4 | 10 | 是 | 顶部前 + 顶部后 |
| 7.1.4 | 12 | 是 | 顶部前 + 顶部后 + 侧 |

程序从 `WAVEFORMATEXTENSIBLE.dwChannelMask` 读取 speaker mask，不假设固定声道顺序。

## Dolby / Atmos 的真实限制

Dolby Atmos for home theater 可能只向 WASAPI 暴露 **7.1 PCM bed**。高度对象通常封装在 Dolby MAT 元数据里，端点环回看不到独立的 PCM Top 声道。

因此：

- 7.1 bed 可以正常分析；
- 5.1.2/7.1.4 高度分析只在该端点真的暴露这些 PCM 声道时有效；
- 真正的 Atmos 对象级雷达需要 MAT/对象元数据访问，或游戏/音频引擎集成，超出本项目范围。

## 本项目做了什么

- 读取 Windows 端点混音；
- 估计一个主方向和置信度；
- 短时间方向保持，显示声道光斑；
- 主雷达 + 外部悬浮窗；
- 动态 channel mask 和多声道 PCM 采集；
- 可复现的诊断和自测。

## 本项目没做什么

- 不能把任意同时声源从混合端点里完全分开；
- 不能识别玩家、敌人或游戏对象；
- 不能给出以米为单位的距离；
- 不能解码 Dolby MAT 对象或 Atmos 高度元数据；
- 不注入游戏，也不绕过反作弊；
- 不保证独占全屏下悬浮窗可见；
- 不是医疗器械，也不能替代助听器/人工耳蜗。

## 路线：能走和不能走

### 能走

- **离散 5.1/7.1 端点环回**：直接分析声道，精度最好。
- **7.1.4 PCM 端点**：高度声道可见，可以做仰角雷达。
- **立体声降级**：只能左右，但可用。
- **外部悬浮窗**：不注入，无边框/窗口化游戏可用。
- **合成测试信号**：可复现验证布局和方向。
- **事件分类**：脚步/枪声/换弹可以通过更多 DSP/ML 区分。
- **多方向瞬时显示**：top-K 声道峰值可以显示，但还不是完整多目标跟踪。

### 不能走 / 不可靠

- **从一个混合端点分离任意同时声源**：同声道、同频段的两个源已经混合。
- **交叉时稳定保持多目标身份**：只有在空间/频段可分时才有机会，不能保证。
- **从 MAT 恢复 Atmos 高度对象**：端点环回看不到。
- **玩家身份/距离**：没有游戏状态或对象元数据。
- **不注入就覆盖独占全屏**：外部窗口无法保证。
- **多人竞技中保证反作弊安全**：外部可视化仍可能违反游戏规则。

## 隐私与安全

- 音频只在内存中处理，不录音、不上传。
- 雷达本身没有遥测、分析、账号或网络请求。
- 仓库中不包含 API key 或本机标识。
- 不修改游戏文件，不安装驱动。
- npm 安装会从 GitHub Release 下载运行时；源码构建使用 .NET SDK。

## 卸载

### Audio Radar（ZIP / 源码）

1. 关闭程序。
2. 删除解压目录。
3. 如果用过 npm 启动器，清缓存：

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\AudioRadar" -ErrorAction SilentlyContinue
```

### Audio Radar（npm）

```bash
npm uninstall -g audio-radar-dsh
```

然后删除 `%LOCALAPPDATA%\AudioRadar`（如果存在）。

### 可选的 Kimi Browser Extension

Audio Radar **不依赖** Kimi。如果你为了浏览器自动化安装过 Kimi Browser Extension，可单独卸载：

```powershell
& "$env:USERPROFILE\.kimi-webbridge\bin\kimi-webbridge.exe" uninstall
```

然后在 `chrome://extensions` 中移除 **Kimi** 扩展。

## 文档

- [安装与排错](docs/INSTALL.md)
- [架构](docs/ARCHITECTURE.md)
- [API 与 CLI](docs/API.md)
- [术语表](docs/GLOSSARY.md)
- [方案与取舍](docs/SOLUTIONS.md)
- [测试](docs/TESTING.md)
- [安全与隐私](docs/SECURITY.md)
- [发布流程](docs/RELEASE.md)

这些文档在 GitHub 仓库中；运行时 ZIP 和 npm 包故意不包含它们。

## 版本与兼容性

| 项目 | 值 |
|---|---|
| Audio Radar | 1.0.0 |
| 测试系统 | Windows 10.0.26300 x64 |
| 运行时 | .NET 8（源码）；Release 自包含 |
| DeepSeek Harness | 44.7.0（测试环境） |
| DSH API 依赖 | 无 |
| 许可证 | MIT |

## 致谢

- **DeepSeek** 和 **DeepSeek Harness**：本项目所处的环境和动机来源。
- **Microsoft Windows Core Audio / WASAPI**：端点枚举、loopback 和音频格式文档。
- **.NET 8 / Windows Forms**：运行时和 UI 技术栈。
- **Kimi Browser Extension / WebBridge**：发布自动化期间使用，不是运行时依赖。
- **VisualAudioOverlay**：早期探索无障碍音频雷达的开源项目，启发了问题定义；没有复制其代码。
- Windows 音频工具、无障碍和游戏音频研究社区。

## 许可证

MIT，见 [LICENSE](LICENSE)。
