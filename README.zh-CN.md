# Compact Save Deck — 竖排存档卡组

**用可展开的卡组整理 Celeste 存档，支持排序与搜索。**

[English](README.md) · [更新记录](docs/更新记录.md) · [反馈问题](https://github.com/Tsukishima-Koichi/celeste-compact-save-deck/issues)

Compact Save Deck 是一个 Everest Mod。它保留 Celeste 原有的竖排存档布局：未归组存档继续显示原生卡片，归组存档收成卡堆，展开后仍是竖排存档列表，并带有平滑的展开、收起和页面过渡动画。

**当前版本：0.1.7，实验性版本。** 已在 Windows、Celeste 1.4.0.0 FNA、Everest 6696-ultra-de194-stable 环境下测试；其他系统和 Everest 版本尚未验证。

## 功能

- 创建、重命名和解散卡组，批量选择成员、移动存档归属。
- 调整未归组存档、整个卡组以及组内存档的竖排顺序。
- 搜索全部存档与卡组，包括折叠卡组里的存档。
- 搜索列表显示存档名、最后所在的地图集 / 地图和槽号，方便区分多个 Madeline 存档。
- 沿用游戏原有的继续、新建、重命名、辅助模式和删除流程。
- 管理菜单使用原生字体、高亮及过渡动画。
- 提供英文与简体中文界面。

分类和排序不会修改存档文件名或槽号。解散卡组不会删除其中的存档。

## 安装

需要先安装 [Everest](https://everestapi.github.io/)。Mod 清单要求 **Everest 1.6696.0 或更新版本**，已测试的具体版本见上文。

1. 如果已发布安装包，从 [Releases](https://github.com/Tsukishima-Koichi/celeste-compact-save-deck/releases) 下载 `CompactSaveDeck.zip`；也可以按下方说明自行构建。
2. 退出 Celeste，将 ZIP 放入游戏的 `Mods` 文件夹，无需解压。
3. 启动游戏，选择 **CLIMB** 进入存档页。

ZIP 和解压后的 `Mods/CompactSaveDeck/` 目录只保留一种，避免重复安装。GitHub 自动生成的 **Source code** 压缩包是源码，不是可直接安装的 Mod。

可以通过 Everest 的 Mod 管理界面禁用，或将已安装包的名称写入 `Mods/blacklist.txt`。更改启用状态后重启游戏。

## 操作

| 操作 | 默认按键 |
| --- | --- |
| 管理卡组 | **Tab** / 手柄左扳机 |
| 搜索存档与卡组 | **R**，固定键盘按键 |
| 展开卡组 | 右方向键 / 手柄十字键右 |
| 收起卡组 | 左方向键 / 手柄十字键左 |
| 浏览、确认、返回 | 沿用游戏现有菜单绑定 |

管理、展开和收起按键可以在 **Mod 选项 → CompactSaveDeck → 键盘/手柄配置** 中修改。左下角的放大镜和卡片堆叠图标分别提示搜索与管理操作。

### 整理存档

选中存档或卡组，按 **Tab** 打开管理菜单，可以选择成员创建卡组、调整归属、重命名或解散。

选择“调整上下位置”后，上下移动条目，确认保存，返回取消。组内存档在本组内排序；未归组存档经过展开的卡组时，会越过整个卡组，不会自动归组。

对卡组按确认可以展开或收起；选中具体存档后，进入游戏原生的存档操作界面。管理菜单使用返回键关闭或返回上一层。

### 搜索

在存档页按 **R**，顶部弹出输入框，同时显示全部存档与卡组列表。

输入存档名、地图名、卡组名或槽号（例如 `#9`），按 **Enter** 高亮并定位最佳匹配项。搜索不会自动进入存档；如果结果位于折叠卡组内，会展开其所在卡组。

匹配依次考虑完整名称、前缀、包含、多关键词和缩写；匹配度相同则保留自定义排序。**Esc / 返回**退出搜索，退格用于编辑文字。

## 数据与兼容性

卡组配置保存在实际存档目录中的 `compact-save-deck.json`，上一版备份为 `compact-save-deck.json.bak`。如果设置了 `EVEREST_SAVEPATH`，也会使用对应的存档目录。需要保留分类时，请把这两个文件与存档一起备份。

分类和排序不会重写原生存档。原生操作仍按游戏本来的方式执行，包括删除存档。

当前边界：

- 暂不支持鼠标拖放或鼠标操作管理菜单。
- 外部用不同存档覆盖同一槽号时，会继承该槽号的分类。
- 仍使用原生存档加载器，大量真实存档的首次加载性能尚未完整验证。
- 与其他替换存档菜单的 Mod 同时使用时可能冲突。
- 日志可能出现 `IdlePosition` Hook 的内联警告。目前测试环境未发现相关故障，但不能保证所有运行环境都兼容。

## 从源码构建

需要：

- `global.json` 指定的 **.NET SDK 10.0.201**，或兼容的更新 .NET 10 feature band。
- 本地 Everest Core 游戏安装，用于引用游戏程序集。
- PowerShell，用于打包和部署脚本。

项目目标框架是 **net8.0**，不会将游戏程序集复制进发布包。

在仓库根目录执行，将示例路径换成自己的游戏目录：

```powershell
dotnet build CompactSaveDeck.csproj -c Release "-p:CelestePath=C:\Games\Celeste"
dotnet run --project Tests/CompactSaveDeck.Tests.csproj -c Release
.\scripts\Build.ps1 -CelestePath "C:\Games\Celeste"
```

安装包输出到 `dist/CompactSaveDeck.zip`。如需部署解压形式的开发版本，退出游戏后执行：

```powershell
.\scripts\Deploy.ps1 -CelestePath "C:\Games\Celeste"
```

部署脚本会检查游戏进程，运行中拒绝覆盖文件。使用开发部署前，请移除另外安装的同名 ZIP。

## 验证与反馈

当前测试通过 **978 项断言**，覆盖分类排序、配置恢复、搜索排名及动画状态；Release 编译无警告。这些检查不能代替游戏内验收与跨 Mod 兼容性测试。

反馈问题时，请提供 Mod 版本、Celeste / Everest 版本、复现步骤和相关 `log.txt` 记录。若是菜单冲突，也请列出其他修改存档界面的 Mod。

仓库包含 Mod 源码和开发记录，不包含游戏素材、游戏程序集、私人测试存档或生成的构建产物。

## 许可

本项目的原创贡献采用 **CC BY-NC 4.0（署名—非商业性使用）**许可：允许为**非商业目的**修改与分享。分发原版或修改版时，须署名 **Tsukishima-Koichi**、提供许可链接、保留适用声明，并说明是否作出修改。

完整条款见 [LICENSE](LICENSE)，署名及适用范围见 [NOTICE](NOTICE)。Celeste、Everest 及第三方内容仍遵循各自的权利与许可。本项目属于非商业许可下的源码公开项目，不属于 OSI 定义的开源许可项目。
