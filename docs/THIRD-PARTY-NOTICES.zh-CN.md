# 📚 第三方声明

[English](../THIRD-PARTY-NOTICES.md) · **简体中文** · [🏠 首页](../README.zh-CN.md)

## 🐍 Python 名称与图形

PyDeck 是 Python Install Manager 的独立配套项目，与 Python Software Foundation 不存在隶属或背书关系

Python 及 Python 标志是 Python Software Foundation 的商标或注册商标。应用图标包含基于 Python 标志的图形元素，项目的 MIT 协议不授予第三方商标权利，也不代表获得背书。复用或修改图形时请参考 [PSF 商标政策](https://www.python.org/psf/trademarks/)

## 🪟 平台与依赖

版本卡使用 [PSF Python 标志 SVG](https://www.python.org/static/community_logos/python-logo-generic.svg) 中未修改的双蛇路径，仅裁去文字和阴影。嵌入式、自由线程、测试套件 SVG 角标为 PyDeck 绘制，EAP 角标独立表示预发布版本，不代表与 JetBrains 有关联。

PyDeck 与 Microsoft 不存在隶属或背书关系，Windows、.NET、WinUI 等产品名称归各自权利人所有

项目通过 NuGet 还原 Microsoft .NET / Windows App SDK 组件，依赖清单见项目文件及 `packages.lock.json`，各依赖保留自己的许可证和声明。源码仓库不内置还原后的包与运行时二进制文件

原生依赖启动器、独立检查工具与 MSI 文件夹浏览操作使用 Microsoft C++ 编译，静态链接发行版基础库，对应组件仍遵循 Microsoft 的适用条款。这些原生工具无需另外安装 Visual C++ Redistributable，完整 MSI / 非打包 WinUI 应用则需要；完整运行依赖统一见[安装指南](INSTALL.zh-CN.md)

Python Install Manager、Python 发行包和离线包是独立软件，适用各自许可证，PyDeck 的 MIT 协议不替代这些条款。分发编译产物时，请检查实际包含组件的许可证和声明要求

📦 发行安装包包含 `ThirdPartyNotices` 目录，保留所还原运行组件、Windows SDK 和 .NET 主机工具链提供的许可证与声明，原始法律文本不作翻译
