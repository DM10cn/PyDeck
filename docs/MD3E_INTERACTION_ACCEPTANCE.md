# MD3E 展开与涟漪检查

## 参考与实现

本轮参考用户提供的两张 Kazumi 设置截图，并通过本机 Computer Use 打开外观设置、展开深色模式菜单、观察行内扩散反馈后关闭菜单，没有修改其偏好。

源码参考固定为 [Kazumi 35ae2f6](https://github.com/Predidit/Kazumi/tree/35ae2f62e76f33d85873ef1ddfcf2babe07e1138)：

- `lib/bean/widget/split_list_row.dart`：首尾大圆角、内部 4 圆角、4 间距；按下时调整形状；尊重关闭动画的偏好。
- `lib/bean/widget/tonal_card.dart`：24 圆角、`surfaceContainerLow`、边界裁切。
- `lib/bean/settings/settings_list.dart`：`InkWell` 保留整行反馈，标题与说明使用不同文字角色。
- `lib/pages/settings/settings_page.dart`：选中导航使用 secondary-container 配色及圆角裁切。

这些是设计参考。PyDeck 继续使用自己的 WinUI 控件与共享业务服务；参考源码只保存在被 Git 忽略的 `artifacts/reference-kazumi-source`，不参与构建或发布。

`MaterialRipple` 在自定义 Material 按钮和导航模板内的独立宿主绘制 Composition 圆形，不改变布局尺寸、命令派发、指针捕获或事件 Handled。圆心采用指针在宿主中的位置，键盘 Space/Enter 使用中心；等半径扩展到最远角，按实际表面四角半径裁切。每个控件最多保留三波；松开淡出，取消加速退出，禁用、卸载、换模板或关闭动画时清理。Fluent 继续使用其原生反馈。

展开优化缓存不变的筛选/推荐/分组投影，单次刷新建立安装状态查找表；新增 release 不再扫描全部保留尾段；锚点从可见索引范围寻找；未变化组头不重复配置资源；目录局部关闭条目批量转场，并将原生 ItemsStackPanel 的预取缓存从默认四屏降为一屏。涟漪在首次交互时才创建 Composition 图层；用于等宽测量、从未挂入视觉树的临时按钮不登记输入监听。原生 Expander、稳定组头对象和收起后焦点恢复保留。逐条集合通知与数组搬移仍存在，不把整个更新过程宣称为线性复杂度。

## 复验

```powershell
./scripts/Build.ps1 -Checks -Publish
./scripts/Smoke-Test.ps1 -ExpansionOnly -BuildDirectory '<build>'
./scripts/Smoke-Test.ps1 -RippleOnly -BuildDirectory '<build>'
./scripts/Smoke-Test.ps1 -PerformanceOnly -BuildDirectory '<build>'
```

展开前后必须使用同一份 `ExpansionPerformanceChecks.cs`，相同机器、窗口、DPI、文字缩放、动画偏好。基线是本轮修改前冻结的工作区源码（含此前已完成的布局修改），不是旧 Git HEAD。每组 25/250/1000 条，目录总量为其两倍；普通展开与后组已展开两个场景分别进行 2 次预热、6 次测量。P95 使用 nearest-rank，六个样本时即最大值。

同步耗时测原生 UIA 调用，visible-ready 测布局中目标组行实际就绪；不等于 GPU 呈现帧延迟或刷新率。焦点断言与动作间稳定等待不计入耗时。推荐卡不计入组内行。

涟漪自动专项检查真实加载控件、Composition 结构、输入点注入、资源清理、32 次突发触发、八次样式重配及原生 UIA Click 次数。注入不能代替真实鼠标/键盘路由，Composition 基值不能代替逐帧动画像素。实机输入与自动结果分开记录。

## 本轮结果

最终构建：`artifacts/dev-win-x64-20261003-140919`。编译零警告、零错误；本轮核心检查 118 项通过，见 `artifacts/expand-ripple-build.log`；最终发布见 `artifacts/expand-ripple-lazy-build.log`。

| 验证 | 结果与证据 |
|---|---|
| 修改前展开基线 | `artifacts/smoke-20261003-140108/result.json`，通过 |
| 最终展开专项 | `artifacts/smoke-20261003-141025/result.json`，通过 |
| 涟漪结构与生命周期 | `artifacts/smoke-20261003-141004/result.json`，通过；禁用清理在生产中修复后复测 |
| 滚动、搜索、回收、焦点、Activity 与构建交互回归 | `artifacts/smoke-20261003-142938/result.json`，通过 |
| 前后比较 | `artifacts/expansion-comparison.json`；相同夹具、窗口、125% DPI、100% 文字缩放、动画开启 |

以下为 visible-ready 中位数，单位 ms。每个单元格为“基线 → 最终”。

| 每组条目数 | 普通展开 | 后组已展开时展开前组 | 后组已展开时收起前组 |
|---|---|---|---|
| 25 | 29.50 → 21.39 | 185.90 → 37.29 | 31.69 → 48.54 |
| 250 | 30.52 → 21.37 | 176.62 → 44.48 | 158.90 → 50.76 |
| 1000 | 35.59 → 18.96 | 157.03 → 44.45 | 134.19 → 50.94 |

所有展开场景的中位数改善。减少预取后，25 条后组场景的收起中位数增加约 17 ms；不能据此宣称全部操作都更快。1,000 条后组场景中，实际已实现 runtime 卡片最多 9 条；空回收容器另计，不用容器总数冒充活动卡片数。

第一次基线测试因夹具错误读取 ListViewItem.Content 而失败；修正为生产模板宿主的 Tag 后，对前后源码使用完全相同的夹具重测。首轮带涟漪的优化版本曾在重负载场景回退，后经按需创建图层与缩减预取修复；该轮 `smoke-20261003-140308` 仅为诊断，不作为最终结果。失败与中间数据保留。

Computer Use 曾将显式开发 launcher 路径匹配为 `C:/APP/Pydeck/PyDeck.Launcher.exe`，未获得预期窗口，此次不计入验证。随后按完整文件路径启动，Win32 进程及 Computer Use 返回的窗口路径都确认是最终 `140919/PyDeck.exe`。启动和目录界面可读取；真实鼠标/键盘涟漪检查尚未完成，窗口恢复时的黑帧也不作为动画证据。未宣称真实触控、连续动效帧、系统高对比切换或安装器验收通过。

最终 SHA-256：

```text
PyDeck.dll          FB34810FBDBEFE07C67B9BCD818087DD03F1D59CEE2DB5C0904F7BBDA52EEEF7
PyDeck.Launcher.exe 99390AC2BFC2E5E8E3AB799AA69E9E0F165A7DA73D7B00A7EDB50C7800E6D7DE
```
