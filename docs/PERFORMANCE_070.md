# ⚡ Performance and state consistency · 0.7.0–0.7.1-fix

**English** · [简体中文](#简体中文)

## ⚡ Follow-up optimization (included in 0.7.1-fix)

Catalog and installed-inventory search reuse one immutable `RuntimeCatalogSnapshot` for the current source list. Version keys, searchable text, architecture and release flags are prepared once; repeated queries scan the sorted projection. Replacing the source list invalidates it. This is display data only: install, repair and removal still perform their existing fresh identity and filesystem checks.

Version, environment and package search wait for a 160 ms typing pause; Enter applies immediately. Explicit filters still apply immediately. Navigation, page rebuild and window close cancel pending searches. No timer keeps running while idle.

Build presets and component switches update existing controls and command text in place. PGO/Release and pip/SSL constraints stay synchronized with a reentrancy guard. These changes preserve the page, scroll position and controls, and avoid rereading build history for each switch. Activity retention stores the measured line length instead of formatting a second string when evicting it, while retaining the 2,000-line / 1 MiB limits.

🧪 Historical development verification: a local Release build passed **111 core checks**. An offscreen fixture suite passed search coalescing, navigation cancellation, replaced-catalog invalidation, package/environment search, background output/progress and build options in all four languages and both designs. **These checks were not rerun for the 0.7.1-fix release packages.** The suite uses isolated preferences, performs no discovery/network/installation and does not activate its window:

```powershell
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Smoke-Test.ps1 -PerformanceOnly
```

📏 One historical development measurement, 20,000 synthetic entries and 8 successive searches: previous queries **310.0 ms / 56,875,240 managed bytes**, indexed queries **4.5 ms / 4,501,672 bytes**. Initial projection cost was **53.7 ms / 9,619,744 bytes**. Timing depends on machine load, and allocations are cumulative managed allocations, not process working set. This does not measure real scrolling, startup, downloads, or application-wide speed. Full visual acceptance and installer lifecycle testing are separate from this fixture suite.

## 🔄 Background updates

Activity and operation progress share the `CoalescedAction` scheduling rule: each signal has at most one pending dispatcher callback. Producers update their data first; the callback reads current state. Notifications arriving during rendering schedule a follow-up. Rejected dispatch and failed callbacks cannot leave the signal permanently busy. Finishing one operation cannot restore its stale progress after the user selects another operation.

Process output is redacted using the operation's captured proxy secret before entering the thread-safe, bounded activity log. It no longer schedules one UI callback or reads credential storage for every line. The log retains the existing 2,000-line / 1 MiB limits, caches immutable snapshots until changed, and updates the activity view in batches. Hidden activity is retained without rebuilding the current page; clear and copy operate on the authoritative log immediately.

## 🧮 Catalog and control lifetime

Catalog sorting, minor-series grouping and version comparisons use one version-key definition. Sorting parses each key once per item instead of reparsing two strings for every comparison. Recommendation selection uses one pass, retaining stable-release, distribution and architecture rules. Keys are not kept in an unbounded global cache.

Control availability uses a conditional weak table. A predicate capturing a control no longer makes a nominally weak registration retain an abandoned visual tree. Navigation still clears the page's registrations; unrelated operations retain their independent resource ownership.

## 💾 Environment registry

Remembering an environment reads/parses one registry snapshot under its file lock. Refresh probes its initial list, then commits the results in one atomic write. Before committing, it verifies that inspected records still match the originals. Concurrent removal or modification causes an explicit retry message instead of resurrecting or overwriting an entry. New records are preserved, along with existing base-runtime associations.

This is an optimistic registry check, not a transaction covering external Python processes or filesystem changes. Installation identity, source trust, file-link checks and cleanup revalidation remain authoritative at execution time.

## 🧪 Verification

Core checks cover concurrent notification floods, reentrant/rejected dispatch, failed callbacks, bounded immutable log snapshots, historical version-order equivalence and concurrent registry edits. A synthetic 20,000-entry sort compares time and thread allocation with the previous comparator; the test prints measurements without imposing machine-dependent timing thresholds. These figures are not an application-wide speed claim.

WinUI smoke checks exercise 3,000 background output/progress updates, the final progress value, retention and severity, navigation, clearing and operation completion. The expanded full GUI suite has a configurable timeout (`Smoke-Test.ps1 -TimeoutSeconds`, default 300), separate from product operation timeouts.

## 简体中文

### ⚡ 后续优化（纳入 0.7.1-fix）

目录与已安装列表搜索复用当前数据源的不可变 `RuntimeCatalogSnapshot`，版本键、搜索文字、架构及发行标记只准备一次，后续在已排序结果上筛选。刷新替换数据源后重建索引；索引仅供显示，安装、修复和删除仍执行原有的最新身份与文件检查

版本、虚拟环境和包搜索在输入停顿 160 ms 后更新，按 Enter 立即应用，明确选择筛选器仍立即生效。离开页面、重建页面或关闭窗口都会取消待执行的搜索，空闲时没有持续运行的搜索定时器

构建预设和组件开关原位同步控件与命令预览，保留 PGO／Release、pip／SSL 联动并避免事件递归。切换组件不再重建页面、重读历史，滚动位置与控件保持不变。日志保存每条记录的计量长度，淘汰旧记录时不再重复格式化，仍保留 2,000 行／1 MiB 上限

🧪 历史开发验证：此前本地 Release 构建通过 **111 项核心检查**，离屏测试通过搜索请求合并、导航取消、目录替换失效、包与环境搜索、后台日志／进度，以及四种语言、两种风格下的构建选项检查。**0.7.1-fix 本次发行包未重新运行这些检查。** 上方 `-PerformanceOnly` 使用独立设置，不连接 PIM、不联网、不安装软件，也不激活窗口

📏 此前开发期间的一次本机模拟测量，2 万条目录连续搜索 8 次：原查询 **310.0 ms／56,875,240 字节托管分配**，索引查询 **4.5 ms／4,501,672 字节**；首次建立索引另需 **53.7 ms／9,619,744 字节**。耗时受机器负载影响，分配量为累计托管分配，不是进程占用内存。该测量不代表实际滚动、启动、下载或整款应用提速，完整视觉验收与安装器生命周期测试仍属独立检查

### 🔄 后台刷新

日志和进度统一使用 `CoalescedAction` 合并通知，每类通知最多保留一个待处理的界面回调。先更新数据，再由回调读取当前状态；刷新期间的新通知另行调度，调度失败或回调异常不会卡住后续更新

进程输出使用操作开始时捕获的代理密码进行脱敏，再写入线程安全的有界日志，不再逐行读取凭据或逐行调度界面。保留原有 2,000 行 / 1 MiB 上限；快照在数据改变前复用，活动页批量刷新。未打开活动页时保留记录，清空与复制立即作用于真实日志

### 🧮 目录与控件

版本比较、目录排序和次版本分组共用版本键。排序时每项只解析一次，推荐项改为单次遍历，保留稳定版本、发行包类型和架构规则，不建立无限增长的全局缓存

控件可用性使用条件弱引用表，避免判断函数的闭包反过来保留已移除的控件树。页面切换清理注册，无关任务仍分别持有资源

### 💾 环境登记

登记操作在文件锁内只读取、解析一次快照。刷新先检查所有环境，再一次性提交；提交前比较原记录，遇到其他实例移除或修改时提示重新刷新，不复活或覆盖旧条目。期间新增的环境与已有运行时关联均保留

这不是覆盖外部进程和文件变化的事务。执行前的身份检查、来源信任、链接检查和删除复查仍读取最新状态

### 🧪 验证范围

核心回归覆盖并发通知、重入与调度失败、异常恢复、日志上限与快照、历史版本顺序，以及登记表并发修改。2 万条版本排序输出旧实现与新实现的耗时和分配量，不使用依赖机器速度的硬性阈值，也不将其当成整款应用提速比例

WinUI 验收覆盖 3,000 条后台输出与进度通知、最终进度、日志上限和级别、导航、清空及完成后的旧通知。完整界面检查的等待时限可用 `Smoke-Test.ps1 -TimeoutSeconds` 调整，默认 300 秒，与应用操作时限无关
