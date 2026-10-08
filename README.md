# 硬件监控浮窗（Hardware Monitor）

> 🖥️ 一个常驻桌面的**半透明悬浮窗**，实时显示 CPU / 内存 / 显卡 / 硬盘 / 游戏帧率
>
> 免安装 · 单文件 exe · 原生 WinForms · 仅占用 < 1% CPU

<!-- 截图：把 PNG 放到 docs/screenshot.png 后，删掉下面这行的注释标记
![截图](docs/screenshot.png)
-->

## 功能

| 行 | 显示内容 |
|---|---|
| **CPU** | 使用率 % + 温度 °C |
| **RAM** | 使用率 % + 已用 / 总容量（GB） |
| **GPU** | 使用率 % + 温度 °C + 显存已用 / 总量（GB） |
| **DISK** | 总容量占用 % + 实时读写速度 + 硬盘温度 |
| **FPS** | 当前前台程序的帧率（由 PresentMon 采集，全屏游戏才有效） |

**交互**

- 左键拖动移动位置，靠近屏幕边缘自动吸附
- 右键菜单：**锁定 / 解锁**（锁定后鼠标穿透，打游戏不挡操作）
- 右键 → 退出

## 环境要求

| 要求 | 说明 |
|---|---|
| Windows 10 64 位 / Windows 11 | 需要 .NET Framework 4.8 —— **Win10 1903+ 与 Win11 已内置**，不用另外安装 ✓ |
| 管理员权限 | 读取传感器与 ETW 帧率需要，**每次启动会弹一次 UAC**（见下方「已知限制」） |
| HWiNFO（需自行下载） | 温度数据来自 HWiNFO 的共享内存。**本仓库不附带 HWiNFO**（第三方软件不允许再分发），请到 [hwinfo.com](https://www.hwinfo.com/download/) 下载便携版（Portable），解压后把 `HWiNFO64.exe` 放进程序目录的 `HWiNFO\` 子目录 |
| PresentMon（可选） | 帧率显示需要它。它是 Intel 的开源工具（MIT 许可），可自行从 [PresentMon 仓库](https://github.com/GameTechDev/PresentMon) 获取，放到程序目录下；**Releases 里的便携包已附带** |
| NVIDIA 驱动 | 只有显存容量读数依赖 `nvidia-smi`，所以**目前显存行仅支持 NVIDIA 显卡**（见 Roadmap） |

## 使用

```
硬件监控便携版\
├── 硬件监控.exe        ← 双击运行
├── PresentMon.exe      （可选，帧率）
└── HWiNFO\
    ├── HWiNFO64.exe    （自行下载放入）
    └── HWiNFO64.INI    （仓库提供，已预设共享内存模式）
```

1. 把 `HWiNFO64.INI` 放进 `HWiNFO\` 目录（仓库的 `assets/` 或便携包里带）
2. 双击 `硬件监控.exe`，允许 UAC
3. 程序会自动拉起 HWiNFO64 与 PresentMon，浮窗出现在右上角，拖动即可移动

**开机自启（可选）**：把 `硬件监控.exe` 的快捷方式放进 `Win+R` → `shell:startup`。
⚠️ 因为需要管理员权限，自启时**会弹一次 UAC**；想完全免提示，可用「任务计划程序」创建一个"使用最高权限运行"的登录时任务。

## 构建

**不需要安装 .NET SDK** —— 用 Windows 自带的 C# 编译器即可：

```bat
build.cmd
```

或直接：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物：`dist\HardwareMonitor.exe`（想用中文名，自己改名成 `硬件监控.exe` 即可）
用 Visual Studio 打开 `src\HardwareMonitor.csproj` 也可以编译（目标框架 `net48`）。
CI 见 [`.github/workflows/build.yml`](.github/workflows/build.yml)（每次 push 自动编译并上传产物）。

## 数据来源

| 数据 | 来源 |
|---|---|
| CPU / GPU / 硬盘**温度** | HWiNFO64 共享内存（`Global\HWiNFO_SENS_SM2`） |
| CPU / 内存 / 硬盘**使用率**、读写速度 | Windows 性能计数器（PDH） |
| 显存已用 / 总量 | `nvidia-smi` |
| 帧率 | PresentMon（ETW 事件跟踪） |

## 已知限制

> 这一节是**故意写清楚**的：当前版本是在一台 Intel 机器上开发调校的，下面这些情况会出现数据不准或显示 `NaN`。

1. **传感器按名字匹配，目前只覆盖 Intel 平台**：CPU 温度读的是 HWiNFO 里标签为 `CPU Package`、且分组名含 `Enhanced` 的传感器；CPU 占用读的是分组名含 `Core Ultra` 的 `Total CPU Usage`。
   → 在 **AMD** 或其它 Intel 平台上，CPU 这两行会显示 `NaN%` / `NaN°C`（其它行正常）。
2. **显存只支持 NVIDIA**（走 `nvidia-smi`）→ AMD / Intel 显卡显存行空白。
3. **启动时会强制结束已有的 HWiNFO64 进程再重新拉起** → 如果你自己开着 HWiNFO 做别的事，会被它关掉。
4. **退出程序不会关闭 HWiNFO**（下次启动时会重新拉起，所以不会堆积）。
5. **必须管理员权限**：每次启动弹 UAC；开机自启也会弹（见上文的任务计划程序方案）。
6. **未签名**：首次运行可能有 SmartScreen「未知发布者」提示，选"仍要运行"即可。
7. **HWiNFO 免费版的共享内存有时限**（社区反馈约 12 小时）→ 长时间挂机后温度可能停止更新；重启程序可恢复。购买 HWiNFO Pro 可解除该限制。
8. **FPS 只在全屏独占游戏 / DirectX / Vulkan 程序里有数**，桌面与窗口化应用显示 `--`。

## Roadmap

- [ ] **多平台传感器适配**：把"按名字精确匹配"改成"多候选 + 模糊匹配 + 单位校验"，覆盖 AMD（`CPU (Tctl/Tdie)`）与各代 Intel
- [ ] **多厂牌显卡**：显存/温度不再依赖 `nvidia-smi`，AMD / Intel 平台也能读
- [ ] **不强杀 HWiNFO**：检测到已在运行且共享内存可用时直接复用
- [ ] **退出时清理**自己拉起的 HWiNFO
- [ ] 可选：不依赖 HWiNFO 的传感器后端
- [ ] 托盘图标 + 免 UAC 的自启方式
- [ ] 主题 / 字号 / 显示项自定义

## 许可

- 本项目代码按 **MIT** 许可发布，见 [LICENSE](LICENSE)
- 第三方程序（HWiNFO、PresentMon）的说明与再分发注意见 [NOTICE.md](NOTICE.md)
- 本项目**不是 HWiNFO / Intel / NVIDIA 官方产品**，与它们没有隶属关系

## 致谢

- [HWiNFO](https://www.hwinfo.com/)（REALiX s.r.o.）—— 提供传感器共享内存
- [PresentMon](https://github.com/GameTechDev/PresentMon)（Intel）—— 提供帧率采集
