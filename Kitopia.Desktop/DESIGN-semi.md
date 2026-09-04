---
version: 3.0.0
name: Kitopia Fluent Design System Guide
description: 针对 Kitopia 桌面与 Web 生态深度定制的 Fluent Design 2 (Windows 11 现代流体视觉体系) 设计规范与架构指南。核心哲学强调“自然分层 (Layering)、柔和亲和 (Soft & Rounded)、内容聚焦 (Content-First) 与现代通透”。全系统基于 Fluent 经典灰白双层景深模型（浅冷灰 Canvas 底色 + 纯白 12px 圆角悬浮卡片）、Kitopia/Windows 标志性鲜亮 Accent Blue (#0064FA)、6px 交互微圆角、9999px 状态胶囊徽章以及“分类眉题 (Kicker) + 粗体主标题”的信息层级架构。

colors:
  # ─── Brand & Accent (Kitopia / Windows 鲜亮蓝 #0064FA 系列) ───
  primary: "#0064FA"
  primary-hover: "#0052D9"
  primary-active: "#0041B2"
  primary-disabled: "#98CDFD"
  primary-light: "#E8F3FF"
  primary-light-hover: "#CBE4FE"
  primary-light-active: "#AFD4FD"
  
  secondary: "#0F6CBD"
  secondary-hover: "#115EA3"
  secondary-active: "#0C3B5E"
  secondary-light: "#F0F6FF"
  
  tertiary: "#6B7075"
  tertiary-hover: "#4B5563"
  tertiary-active: "#374151"
  tertiary-light: "#F3F4F6"
  
  # ─── Functional & Status Colors (语义与状态色) ───
  success: "#107C41"
  success-hover: "#0E703B"
  success-active: "#0C5F32"
  success-light: "#E6F7EC"
  success-border: "rgba(16, 124, 65, 0.20)"
  
  warning: "#B74700"
  warning-hover: "#9C3C00"
  warning-active: "#813200"
  warning-light: "#FFF4CE"
  warning-border: "rgba(183, 71, 0, 0.20)"
  
  danger: "#C42B1C"
  danger-hover: "#A82417"
  danger-active: "#8E1E13"
  danger-light: "#FDF3F2"
  danger-border: "rgba(196, 43, 28, 0.35)"
  
  info: "#0064FA"
  info-light: "#E8F3FF"
  info-border: "rgba(0, 100, 250, 0.20)"

  # ─── Badges & Tags (截图状态徽章专有色阶) ───
  badge-success-bg: "#E6F7EC"
  badge-success-text: "#107C41"
  badge-current-bg: "#E0EEFB"
  badge-current-text: "#0064FA"
  badge-neutral-bg: "#F0F2F5"
  badge-neutral-text: "#4B5563"

  # ─── Typography Neutrals (高可读文本色阶) ───
  text-0: "#111827"
  text-1: "#374151"
  text-2: "#6B7280"
  text-3: "#9CA3AF"
  text-disabled: "#D1D5DB"

  # ─── Surfaces & Layering (Fluent 分层架构) ───
  canvas: "#F5F6F8"
  bg-card: "#FFFFFF"
  bg-card-hover: "#FCFCFC"
  bg-popover: "#FFFFFF"
  bg-modal: "#FFFFFF"
  bg-subtle: "#F9FAFB"

  fill-0: "#F3F4F6"
  fill-1: "#E5E7EB"
  fill-2: "#D1D5DB"

  border: "rgba(0, 0, 0, 0.06)"
  border-control: "#D1D5DB"
  border-control-hover: "#9CA3AF"
  border-focus: "#0064FA"
  border-disabled: "#E5E7EB"
  overlay: "rgba(0, 0, 0, 0.40)"

  # ─── Dark Mode (Windows 11 Fluent Dark / Mica) ───
  dark-canvas: "#181818"
  dark-bg-card: "#242424"
  dark-bg-card-hover: "#2A2A2A"
  dark-bg-popover: "#2C2C2C"
  dark-bg-modal: "#323232"
  dark-border: "rgba(255, 255, 255, 0.08)"
  dark-border-control: "rgba(255, 255, 255, 0.16)"
  dark-text-0: "#F9FAFB"
  dark-text-1: "#E5E7EB"
  dark-text-2: "#9CA3AF"
  dark-text-3: "#6B7280"
  dark-primary: "#4CC2FF"
  dark-primary-light: "rgba(76, 194, 255, 0.15)"

typography:
  heading-hero:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 28px
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: -0.02em
  heading-1:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 24px
    fontWeight: 700
    lineHeight: 1.30
    letterSpacing: -0.01em
  heading-2:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 20px
    fontWeight: 600
    lineHeight: 1.35
    letterSpacing: -0.01em
  heading-3:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 18px
    fontWeight: 600
    lineHeight: 1.40
    letterSpacing: 0
  heading-4:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 16px
    fontWeight: 600
    lineHeight: 1.40
    letterSpacing: 0
  caption-kicker:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 12px
    fontWeight: 500
    lineHeight: 1.33
    letterSpacing: 0.02em
  body-lg:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 16px
    fontWeight: 400
    lineHeight: 1.50
    letterSpacing: 0
  body:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 14px
    fontWeight: 400
    lineHeight: 1.50
    letterSpacing: 0
  body-sm:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 12px
    fontWeight: 400
    lineHeight: 1.40
    letterSpacing: 0
  link:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 14px
    fontWeight: 500
    lineHeight: 1.43
    letterSpacing: 0
  code:
    fontFamily: '"Cascadia Code", "SFMono-Regular", Consolas, Menlo, Courier, monospace'
    fontSize: 13px
    fontWeight: 400
    lineHeight: 1.40
    letterSpacing: 0
  stat-number:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 18px
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: -0.01em
  stat-label:
    fontFamily: '"Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif'
    fontSize: 12px
    fontWeight: 400
    lineHeight: 1.33
    letterSpacing: 0

rounded:
  none: 0px
  xs: 2px
  sm: 4px
  md: 6px
  lg: 12px
  xl: 16px
  full: 9999px
  circle: 50%

spacing:
  super-tight: 2px
  extra-tight: 4px
  tight: 8px
  base-tight: 12px
  base: 16px
  base-loose: 20px
  loose: 24px
  extra-loose: 32px
  super-loose: 40px
  section: 48px
  section-lg: 64px

heights:
  control-sm: 28px
  control-default: 34px
  control-lg: 40px

components:
  # ─── Fluent Buttons ───
  button-accent:
    description: "Fluent Accent 强调主按钮（如“一键安装到 Kitopia”）— 实色鲜亮蓝底、纯白字、6px 现代微圆角。"
    backgroundColor: "{colors.primary}"
    textColor: "#FFFFFF"
    hoverBackgroundColor: "{colors.primary-hover}"
    activeBackgroundColor: "{colors.primary-active}"
    rounded: "{rounded.md}"
    height: "{heights.control-default}"
    padding: "0px {spacing.base}"
    typography: "{typography.link}"
    boxShadow: "0 1px 2px rgba(0, 0, 0, 0.08)"

  button-standard:
    description: "Fluent Standard 标准副按钮（如“下载 Windows”、“编辑版本详情”）— 纯白底色、细描边、深灰文字。"
    backgroundColor: "{colors.bg-card}"
    textColor: "{colors.text-0}"
    borderColor: "{colors.border-control}"
    hoverBackgroundColor: "{colors.fill-0}"
    activeBackgroundColor: "{colors.fill-1}"
    rounded: "{rounded.md}"
    height: "{heights.control-default}"
    padding: "0px {spacing.base}"
    typography: "{typography.link}"

  button-danger-outline:
    description: "Fluent 破坏性弱按钮（如“撤回此版本”）— 白底透明、危险红描边与文字。"
    backgroundColor: "{colors.bg-card}"
    textColor: "{colors.danger}"
    borderColor: "{colors.danger-border}"
    hoverBackgroundColor: "{colors.danger-light}"
    activeBackgroundColor: "rgba(196, 43, 28, 0.15)"
    rounded: "{rounded.md}"
    height: "{heights.control-default}"
    padding: "0px {spacing.base}"
    typography: "{typography.link}"

  button-subtle:
    description: "Fluent 幽灵/文字按钮 — 常态无背景与描边，悬浮带浅灰微填充。"
    backgroundColor: "transparent"
    textColor: "{colors.primary}"
    hoverBackgroundColor: "{colors.fill-0}"
    activeBackgroundColor: "{colors.fill-1}"
    rounded: "{rounded.md}"
    height: "{heights.control-default}"

  # ─── Fluent Form Controls ───
  text-input:
    backgroundColor: "{colors.bg-card}"
    textColor: "{colors.text-0}"
    borderColor: "{colors.border-control}"
    hoverBorderColor: "{colors.border-control-hover}"
    placeholderColor: "{colors.text-3}"
    rounded: "{rounded.md}"
    height: "{heights.control-default}"
    padding: "0px {spacing.base-tight}"

  text-input-focused:
    backgroundColor: "{colors.bg-card}"
    textColor: "{colors.text-0}"
    borderColor: "{colors.border-focus}"
    boxShadow: "0 0 0 2px rgba(0, 103, 192, 0.25)"
    rounded: "{rounded.md}"

  # ─── Fluent Badges & Tags (状态与标识) ───
  badge-success:
    description: "状态徽章 - 成功/公开（如“公开插件”、“已发布”）"
    backgroundColor: "{colors.badge-success-bg}"
    textColor: "{colors.badge-success-text}"
    rounded: "{rounded.sm}"
    padding: "3px {spacing.tight}"
    typography: "{typography.body-sm}"

  badge-current:
    description: "状态徽章 - 当前选中/活跃（如“当前版本”）"
    backgroundColor: "{colors.badge-current-bg}"
    textColor: "{colors.badge-current-text}"
    rounded: "{rounded.sm}"
    padding: "3px {spacing.tight}"
    typography: "{typography.body-sm}"

  badge-neutral:
    description: "环境/属性微标签（如“Windows”）"
    backgroundColor: "{colors.badge-neutral-bg}"
    textColor: "{colors.badge-neutral-text}"
    rounded: "{rounded.sm}"
    padding: "3px {spacing.tight}"
    typography: "{typography.body-sm}"

  # ─── Fluent Containers & Cards (卡片容器体系) ───
  card-fluent:
    description: "Fluent 基础业务卡片 — 纯白悬浮底色、12px 柔和圆角、极轻微边框与环境微阴影。"
    backgroundColor: "{colors.bg-card}"
    borderColor: "{colors.border}"
    rounded: "{rounded.lg}"
    boxShadow: "0 1px 3px rgba(0, 0, 0, 0.04), 0 2px 8px rgba(0, 0, 0, 0.02)"
    padding: "{spacing.loose}"

  plugin-hero-card:
    description: "插件主信息卡片 (Header Hero) — 包含 64px 插件首字母图章、主标题、作者栏、说明与右侧指标数。"
    backgroundColor: "{colors.bg-card}"
    borderColor: "{colors.border}"
    rounded: "{rounded.lg}"
    boxShadow: "0 1px 3px rgba(0, 0, 0, 0.04), 0 2px 8px rgba(0, 0, 0, 0.02)"
    padding: "{spacing.loose}"
    avatarBackground: "#D8E9FE"
    avatarTextColor: "{colors.primary}"
    avatarRounded: "{rounded.lg}"
    avatarSize: "64px"

  section-card:
    description: "内容段落卡片（如“详细介绍”、“全部版本与审核记录”）— 携带眉题 Kicker、粗体标题与独立分区。"
    backgroundColor: "{colors.bg-card}"
    borderColor: "{colors.border}"
    rounded: "{rounded.lg}"
    boxShadow: "0 1px 3px rgba(0, 0, 0, 0.04), 0 2px 8px rgba(0, 0, 0, 0.02)"
    padding: "{spacing.loose}"

  timeline-item:
    description: "版本时间线记录单元 — 包含 Accent 蓝色高亮节点、竖向连接线、版本号、状态徽章与操作按钮网格。"
    nodeColor: "{colors.primary}"
    nodeSize: "12px"
    lineColor: "{colors.fill-1}"
    lineWidth: "2px"
    headerTypography: "{typography.heading-4}"
    metaTypography: "{typography.body-sm}"

  modal-surface:
    backgroundColor: "{colors.bg-modal}"
    rounded: "{rounded.xl}"
    boxShadow: "0 8px 32px rgba(0, 0, 0, 0.12), 0 2px 8px rgba(0, 0, 0, 0.04)"
    padding: "{spacing.loose}"
---

## Overview

**Kitopia Fluent Design System** 深度融合了微软 **Fluent Design 2 (Windows 11)** 的现代桌面与 Web 交互美学，专为 Kitopia 插件生态、开发者工具与高品质桌面管理后台打造。

与追求冷峻平直、高密度挤压的传统后台（如 Semi / Ant Design）不同，Fluent 风格的核心在于：
1. **层次与叠放 (Natural Layering & Elevation)**：界面摒弃了“全白纯平靠黑线划分”的做法。系统底层使用宁静温润的浅冷灰底色（`canvas: #F5F6F8`），所有业务内容均承载于纯净如纸的纯白悬浮卡片（`bg-card: #FFFFFF`）上，并通过 12px 柔和圆角与漫反射微环境光，营造现代 Windows 11 式的高级沉浸感。
2. **现代几何与亲和圆角 (Soft & Friendly Geometry)**：
   - 基础交互控件（Button、Input）采用 **6px** 现代微圆角，手感精致利落；
   - 承载卡片（Card Container）采用 **12px** 友好大圆角；
   - 模态弹窗（Modal）采用 **16px**；
   - 状态指示使用 **9999px** 胶囊（Pill）或 **4px** 徽章（Badge）。
3. **视觉焦点与动线驱动 (Purposeful Hierarchy)**：
   - 标志性 **Kitopia Accent Blue (#0064FA)** 实色按钮专用于驱动主流程（如“一键安装到 Kitopia”）；
   - 次要操作一律收敛为标准描边按钮（Standard Button），保持克制与统一；
   - 危险操作（如“撤回此版本”）使用镂空红字浅描边，避免在常规页面过度刺眼，但在触碰时明确提示风险。
4. **结构化眉题排版 (Structured Kicker Typography)**：
   - 借鉴现代设计中的“Kicker 分类眉题 + 粗体主标题”排版范式（如 `插件介绍 -> 详细介绍`，`版本时间线 -> 全部版本与审核记录`），让用户的视线在海量数据中能快速定位上下文。

---

## Colors

### 1. Brand & Accent (品牌强调色)
- **Kitopia Accent Blue** (`{colors.primary}` — `#0064FA` / `#0067C0` 鲜亮蓝系列)：
  - 核心主强调色，高饱和度鲜亮蓝，用于实色主操作按钮（Accent Button）、时间线当前活动节点圆点、Tab 下划线、单选框选中态。
- **Hover & Active** (`{colors.primary-hover}` — `#0052D9`, `{colors.primary-active}` — `#0041B2`)：
  - 严格保持色彩明度递进，保证按压时的清晰视觉反馈。
- **Primary Light** (`{colors.primary-light}` — `#E8F3FF`)：
  - 用于高亮选中文本、轻量指示徽章、或图章图标的浅蓝氛围背景（`#D5EAFF`）。

### 2. Status & Badge Colors (语义与徽章色)
截图中的徽章采用了 Fluent 2 标志性的“淡雅底色 + 高对比深色文字”搭配，兼具美感与无障碍可读性：
- **Success (公开 / 已发布 / 正常)**：
  - 背景：`#E6F7EC`
  - 文本：`#107C41`
  - 描边：可无，或弱透明描边 `rgba(16, 124, 65, 0.20)`
- **Current (当前版本 / 活跃项)**：
  - 背景：`#E0EEFB`
  - 文本：`#0064FA`
- **Neutral (运行环境 / 属性标签，如 Windows)**：
  - 背景：`#F0F2F5`
  - 文本：`#4B5563`
- **Danger (撤回 / 删除 / 拦截)**：
  - 实色危险：`#C42B1C`
  - 弱描边镂空：描边 `rgba(196, 43, 28, 0.35)`，文字 `#C42B1C`，悬浮填充 `#FDF3F2`

### 3. Layering Surfaces (双层底色与层级)
- **Canvas (Layer 0)**：`#F5F6F8`（或桌面端 Mica 浅色），统领整个页面底座。
- **Card Surface (Layer 1)**：`#FFFFFF`，业务卡片独立浮于 Canvas 之上。
- **Control Fill (Layer 2)**：`#F3F4F6`，用于输入框内部辅助底色或悬浮态。
- **Border**：全系统采用 1px 细微描边 `rgba(0, 0, 0, 0.06)`，取代刺眼的深黑线条。

---

## Typography

### Font Stack (字体栈)
```css
font-family: "Segoe UI Variable", "Segoe UI", -apple-system, BlinkMacSystemFont, 
             "PingFang SC", "Hiragino Sans GB", "Microsoft YaHei", 
             "Helvetica Neue", Helvetica, Arial, sans-serif;
```
- **Segoe UI Variable / Segoe UI**：优先适配 Windows 11 原生 Fluent 排版引擎，字母圆润工整，字偶间距极其舒适。
- **PingFang SC / Microsoft YaHei**：中文字符平滑回退，保持笔画锐利。
- **数字排版**：统计数字（如“1.0.0”、“1”）采用加粗并开启 `font-variant-numeric: tabular-nums`。

### Hierarchy & Patterns

| Token | 字号 / 字重 | 行高 | 典型应用范式 |
|---|---|---|---|
| `{typography.heading-hero}` | 28px / 700 (Bold) | 35px | 插件详情主标题（如“Onnx OpenVino推理环境”） |
| `{typography.heading-2}` | 20px / 600 (Semi-Bold) | 27px | 卡片大分区标题（如“全部版本与审核记录”） |
| `{typography.heading-3}` | 18px / 600 | 25px | 中分区标题（如“详细介绍”） |
| `{typography.heading-4}` | 16px / 600 | 22px | 版本项标题（如“v1.0.0”） |
| `{typography.caption-kicker}` | 12px / 500 | 16px | **Fluent 经典眉题**（如标题上方的“插件介绍”、“版本时间线”） |
| `{typography.body}` | 14px / 400 | 21px | 标准正文、说明文字、按钮文字 |
| `{typography.body-sm}` | 12px / 400 | 17px | 辅助信息、时间戳（“提交于 2026年9月3日 14:59”）、Tag 文本 |
| `{typography.stat-number}` | 18px / 700 | 22px | 右上角核心指标值（如“1.0.0”、“1”） |
| `{typography.stat-label}` | 12px / 400 | 16px | 右上角核心指标标签（如“最新版本”、“下载量”） |

---

## Shapes & Radii

Fluent Design 2 建立了柔和而严谨的圆角递进体系：

| Token | 数值 | 适用组件与规范意图 |
|---|---|---|
| `{rounded.xs}` | 2px | 微型复选框焦点、进度条细指示线 |
| `{rounded.sm}` | 4px | 状态小徽标（`公开插件`、`Windows`）、时间戳外框 |
| `{rounded.md}` | 6px | **标准交互控件**：按钮 Button、输入框 Input、下拉菜单 Select、下拉面板 Dropdown |
| `{rounded.lg}` | 12px | **Fluent 标志性卡片圆角**：业务卡片 Card、插件大图章 Icon（64px 容器） |
| `{rounded.xl}` | 16px | 模态确认弹窗 Modal、大幅抽屉 Drawer 头部 |
| `{rounded.full}` | 9999px | 胶囊形 Pill 标签、气泡通知指示器 |
| `{rounded.circle}` | 50% | 用户头像 Avatar、时间线节点圆点 |

---

## Elevation & Depth (景深与阴影)

```css
/* Fluent Card Elevation (Layer 1) */
background: #FFFFFF;
border: 1px solid rgba(0, 0, 0, 0.06);
box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04), 0 2px 8px rgba(0, 0, 0, 0.02);

/* Fluent Modal / Popover Elevation (Layer 2) */
background: #FFFFFF;
border: 1px solid rgba(0, 0, 0, 0.08);
box-shadow: 0 8px 32px rgba(0, 0, 0, 0.12), 0 2px 8px rgba(0, 0, 0, 0.04);
```

- **浅色模式**：依靠 `#F5F6F8` 的底色衬托白色卡片，辅以 1px 极微弱边缘边框与 2~8px 的环境微晕散，完全不使用浑浊的黑色大阴影。
- **暗色模式 (Fluent Dark)**：底色为 `#181818`，卡片提升为 `#242424`，卡片外圈使用 `1px solid rgba(255, 255, 255, 0.08)` 描边，配合清晰的内层高光。

---

## Key Components & Layout Patterns (截图核心组件解析)

### 1. 插件大头部卡片 (`plugin-hero-card`)
- **左侧图章 (Icon Stamp)**：
  - 尺寸 64px × 64px，圆角 12px。
  - 填充柔和浅蓝背景（`#D8E9FE`），居中放置深色加粗字母或专属图标（如蓝色的字母 “O”）。
- **主体信息行**：
  - 第一行：主标题（24px/28px 粗体）紧随状态徽章（如绿底深绿字的“公开插件”），间距 12px。
  - 第二行：插件包唯一标识（`kitopiaonnxruntimeopenvino`），以 14px 弱化灰字展示。
  - 第三行：作者栏，包含 24px 圆形头像、粗体用户名与 `@handle`。
  - 第四行：插件一句话描述文本。
  - 第五行：适用平台 Tag（如浅灰圆角底的 “Windows”）。
- **右上角统计数据组 (Stats Block)**：
  - 右对齐排列，包含多列数据指标项（如“最新版本”、“下载量”）。
  - 上方为 12px 弱化标签，下方为 18px 粗体数值，字段整齐对齐。

### 2. 内容段落卡片 (`section-card`)
- **眉题与主标题组**：
  - 上层为 12px 弱化浅灰眉题（Caption Kicker，如“插件介绍”或“版本时间线”）。
  - 下层紧跟 18px~20px 粗体段落标题（如“详细介绍”或“全部版本与审核记录”）。
  - 右侧可自适应放置辅助状态（如“共 1 次提交”）。

### 3. 版本时间线与记录流 (`timeline-release-item`)
- **节点与轨道**：
  - 当前版本节点采用 12px 实心鲜亮蓝圆点（`#0064FA`）。
  - 竖向连接轨道使用 2px 宽度的柔和灰色直线（`#E5E7EB`）。
- **版本头部行**：
  - 版本号（如 `v1.0.0`，16px 粗体）。
  - 行内徽章组合：`已发布`（绿色徽标）与 `当前版本`（浅蓝徽标）。
  - 右侧并列显示平台适配标签（如 `Windows`）。
- **元数据与更新日志**：
  - 灰色时间戳（如“提交于 2026年9月3日 14:59”）。
  - 正文区域显示更新日志（如 `1.0.0`）。
- **操作按钮组 (Action Button Matrix)**：
  - **推进操作行**：
    - 次操作：“下载 Windows”（Standard 描边按钮）。
    - 主操作：“一键安装到 Kitopia”（Accent 实色蓝按钮，高对比度）。
  - **管理操作行**：
    - 常规管理：“编辑版本详情”（Standard 描边按钮）。
    - 危险管理：“撤回此版本”（Danger Outline 镂空红按钮，警告性操作）。

---

## Do's and Don'ts (指导守则)

### Do (必须遵循)
- **保持双层底色落差**：界面底色必须使用冷灰底色（`#F5F6F8`），业务卡片必须使用纯白（`#FFFFFF`），以此形成自然的 Windows 11 层叠景深。
- **遵守 6px 控件 / 12px 容器圆角**：常规按钮与输入框一律使用 6px 圆角，卡片容器统一使用 12px 圆角，保持界面温润柔和。
- **主次按钮明确区分**：一个操作区域内只保留一个 Accent 实色蓝按钮（如“一键安装到 Kitopia”），其余辅助操作使用 Standard 描边按钮。
- **规范使用分类眉题 (Kicker)**：在卡片标题上方添加 12px 次级分类小标签，建立清爽的视觉导读层次。
- **徽章采用浅底深字**：状态微标（Badge）使用高透明度淡彩底色搭配高饱和度文字，严禁使用刺眼的实色大红大绿。

### Don't (严谨禁止)
- **禁止使用 Semi 风格的 3px 微圆角与直角**：这会破坏 Fluent 的温和现代质感。
- **禁止让页面 Canvas 和卡片同为纯白又无明显分层**：失去底色反差会导致界面看起来惨白、缺乏结构。
- **禁止大面积滥用实色危险红色按钮**：非极端拦截弹窗场景下，危险操作（如“撤回此版本”）应采用 Outline 镂空红描边，悬浮时再提示危险填充，避免视觉过载。
- **禁止在中后台将所有普通输入框做成 9999px 全圆角药丸**：药丸形仅限用于状态 Badge 和筛选 Chip，标准表单输入必须收敛于 6px。
- **禁止破坏 1px 细微描边**：卡片边框透明度不应高于 8%，避免产生“黑色方框”式的沉重感。
