# 🧪 虚拟机分步验收

[English](README.md) · **简体中文**

每次把提供的脚本复制到**测试虚拟机内部**运行，回传本步骤日志，确认结果后再进行下一步。PyDeck 在宿主机编译，虚拟机只需为后续运行测试准备运行依赖

## 1️⃣ 检查环境

把 `01-InspectEnvironment.ps1` 复制到虚拟机内可写的文件夹，用将来运行 PyDeck 的同一用户打开 **64 位 Windows PowerShell**，进入该文件夹执行，无需管理员权限

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\01-InspectEnvironment.ps1
```

这里的执行策略参数只影响当前进程，不修改系统或用户策略。如果组织策略仍然拦截，回传错误即可，不要修改安全设置

如果旧脚本在第 8 行提示 `Join-Path ... Path ... empty string`，替换为修正版，或直接在命令末尾指定输出目录

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\01-InspectEnvironment.ps1 -OutputDirectory .\PyDeck-Diagnostics
```

📦 完成后会显示 **SEND BACK**，后面就是需要回传的 ZIP 路径，默认保存在脚本旁的 `PyDeck-Diagnostics` 目录。ZIP 只包含三个文件

- `summary.txt`：本步结论和下一步说明
- `checks.log`：每项检查的 PASS / WARN / FAIL / ERROR 结果
- `result.json`：结构化版本信息和检查结果

如果 ZIP 生成失败，直接回传所显示目录中的这三个文件。每次运行都会建立新目录。`FAIL` 代表依赖不满足，`ERROR` 代表该项未能完成，其余项目仍继续检查。**脚本运行结束不代表兼容性验收通过**

🔎 本步骤检查 Windows、实际生效的 x64 .NET 位置、当前用户的 Windows App Runtime 注册、VC++ 文件、PIM 是否可找到，以及已有 PyDeck 安装登记。不启动 Python 或 PyDeck，不下载或安装软件，不修改虚拟机设置，也不读取应用设置。用户目录路径会脱敏，安装位置和软件包版本仍属于诊断信息，发送前可以自行查看。生成的报告不会进入 Git 源码

⚠️ **0.7.1-fix 要求 Windows 11 x64**。Windows 10 LTSC 出现系统条件不满足是预期结果，其他依赖齐全也不代表已经支持 Windows 10

## 🔄 后续步骤

当前只提供第 01 步。读取来宾日志后，再按实际环境准备下一步对应的脚本和测试包

| 步骤 | 操作与反馈 |
| --- | --- |
| 02 · 依赖 | 核对安装程序与版本，用户执行安装，再检测并生成报告 |
| 03 · 安装 | 测试选定的 MSI/MSIX，记录安装日志、退出码和包身份 |
| 04 · 启动 | 使用隔离的冒烟测试配置，收集应用结果和错误 |
| 05 · 升级与卸载 | 明确测试安装归属，检查文件、快捷方式和用户数据保留 |
| 06 · 界面实测 | 最后用 Computer Use 验证布局、交互、材质回退和真实操作流程 |

MSI 与 MSIX 分开验收。仓库现有升级脚本是开发者测试工具，会清理它创建的测试安装，不应直接照搬进虚拟机。后续脚本必须说明修改对象，不能把还原检查点、卸载无关安装或强制关机当作默认清理操作
