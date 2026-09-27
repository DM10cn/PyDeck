# ⚡ Performance and state consistency · 0.7.0

**English** · [简体中文](#简体中文)

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
