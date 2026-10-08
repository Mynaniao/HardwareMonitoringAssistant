# 第三方组件与再分发说明

本仓库**只包含本项目的源代码**，不含任何第三方二进制文件。使用时需要另外准备下列程序。

## 1. HWiNFO（必需，用于读取温度）

- 官网：https://www.hwinfo.com/ ｜ 作者：Martin Malik / REALiX s.r.o.
- 用途：本程序通过 HWiNFO 的**共享内存**（`Global\HWiNFO_SENS_SM2`）读取 CPU / GPU / 硬盘温度。
- ⚠️ **请不要把 `HWiNFO64.exe` 提交到本仓库、也不要随本项目的压缩包一起分发。**
  HWiNFO 是**免费但闭源**的软件，有独立的最终用户许可协议（EULA），个人可免费使用，
  但**再分发需要另行获得授权**。协议原文：https://www.hwinfo.com/files/license.pdf
- 正确做法：让用户自行到官网下载便携版（Portable），把 `HWiNFO64.exe` 放进程序目录的 `HWiNFO\` 子目录。
  本仓库只提供一份**配置模板** `HWiNFO64.INI`（把 HWiNFO 设为静默 + 共享内存模式）。
- 免费版的「Shared Memory Support」有时长限制（社区反馈约 12 小时），长时间挂机后温度会停止更新；
  HWiNFO Pro 可解除该限制。

## 2. PresentMon（可选，用于帧率）

- 仓库：https://github.com/GameTechDev/PresentMon ｜ 作者：Intel
- 许可：**MIT**（可自由再分发，但需保留其版权与许可声明）
- 它读取 ETW 事件来统计帧率；本程序以 `--output_stdout --no_csv` 方式解析它的输出。
- 若你把它一起打包分发，请在压缩包里附上 PresentMon 的 LICENSE 文件。

## 3. 本项目的定位

- 本项目是**独立的第三方工具**，不是 HWiNFO、Intel、NVIDIA 或微软的官方产品，与它们无隶属关系。
- 因使用本程序产生的任何后果由使用者自行承担；请自行确认对 HWiNFO 的使用符合其许可条款。
