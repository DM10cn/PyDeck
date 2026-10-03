# PyDeck 0.7.2

## English

- ✨ **A clearer workspace** — Refined Material layouts improve list widths, scrollbar placement and installation-status alignment. Tonal grouping and button ripples are more consistent, with keyboard focus indicators retained while ordinary control outlines are reduced.
- ⚙️ **Organized settings** — Dedicated categories make the settings workspace easier to navigate, and an About page groups app information. Related settings are available in both interface styles, alongside refinements to window buttons and sidebar connection controls.
- ⚡ **Faster wallpaper colors** — Handwritten AVX2 and SSSE3 kernels accelerate pixel conversion, while AVX2 speeds up color quantization. Removing unused sorting reduces additional work; unsupported processors retain compatible fallbacks without changing transparency handling or seed ordering.
- 📦 **Native installation** — Setup brings folder selection, shortcuts, initial interface style, runtime preparation and MSI progress into one flow. Completion and log access stay in the same window for easier installation and maintenance.
- 🛠️ **Smoother maintenance** — Repair keeps the registered folder locked, cancellation leaves controls in a consistent state, and failed uninstall can be retried. Uninstall uses the cached MSI directly and preserves Python, environments, shared runtimes and preferences.

## 简体中文

- ✨ **界面细节**：调整列表宽度、滚动条与状态对齐，优化色块分组和按钮涟漪，保留键盘焦点提示。
- ⚙️ **分类设置**：新增分类工作区与关于页，两种风格同步提供，调整窗口按钮及连接状态。
- ⚡ **取色加速**：汇编优化像素转换和颜色量化，删除无用排序，保留兼容回退与原有配色规则。
- 📦 **原生安装**：选择目录、快捷方式和首次风格，准备依赖并显示安装进度。
- 🛠️ **维护改进**：完善修复、取消及卸载重试，直接使用缓存安装包，保留解释器、环境与偏好。
