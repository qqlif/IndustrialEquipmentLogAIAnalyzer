# IndustrialEquipmentLogAIAnalyzer

基于 `.NET 8 + WPF` 的工业设备日志 AI 分析小工具。项目定位为**作品集 Demo**：强调可读性、可配置性与完整业务闭环（导入日志 → AI 分析 → 导出报告）。

---

## 1. 项目简介

本项目面向一线操作员、运维工程师、维修工程师，提供日志快速诊断能力：

- 解析工业设备运行日志（时间、级别、消息）
- 将日志与预置诊断提示词、知识库组合为分析输入
- 调用大模型 API 输出结构化故障分析
- 生成并导出可读的诊断报告（Markdown）

---

## 2. 使用场景

- 设备巡检时对异常日志进行快速初判
- 值班/运维交接时生成可追溯诊断记录
- 维修前快速归纳可能原因与建议措施
- 作为工业日志 AI 诊断方向的演示样例

---

## 3. 技术栈

- 平台：`.NET 8`、`WPF`
- 语言：`C# 12`
- UI：`MaterialDesignThemes`、`MaterialDesignColors`
- 配置：`System.Text.Json`（中文字段 + UTF-8 BOM）
- 网络：`HttpClient`
- 密钥安全：`DPAPI (ProtectedData, CurrentUser)`

---

## 4. 环境依赖

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022/2026（含 WPF 开发组件）
- 可用的大模型 API（示例：DeepSeek 兼容接口）

---

## 5. 目录说明

- `工业设备日志 AI 分析小工具/MainWindow.xaml`：主界面布局
- `工业设备日志 AI 分析小工具/MainWindow.xaml.cs`：核心业务流程
- `工业设备日志 AI 分析小工具/DialogService.cs`：统一风格弹窗服务
- `工业设备日志 AI 分析小工具/config/appsettings.json`：中文配置文件
- `工业设备日志 AI 分析小工具/config/提示词/诊断提示词.txt`：诊断提示词
- `工业设备日志 AI 分析小工具/config/知识库/诊断知识库.md`：诊断知识库

---

## 6. 如何运行

1. 使用 Visual Studio 打开解决方案（`*.slnx`）。
2. 确认 `config/appsettings.json` 中 API 地址、模型名配置正确。
3. 配置 API Key（推荐环境变量方式）。
4. 启动项目（F5 / Ctrl+F5）。

---

## 7. 如何配置 API Key

支持两种方式，优先读取环境变量：

### 方式 A（推荐）：环境变量

在 `appsettings.json` 中设置：

- `环境变量键名`（例如：`DEEPSEEK_API_KEY`）

再在系统中配置对应环境变量值。

### 方式 B：界面输入保存

在界面“`大模型设置`”输入 API Key 并点击“保存配置”：

- 首次明文输入后会自动加密并写入配置（`enc:...`）
- 下次启动不回显密钥（输入框保持空）
- 留空保存时，如果配置中也无已保存密钥，会弹窗提示

---

## 8. 功能演示流程

1. 点击“`导入日志`”，选择 `.log/.txt/.json` 文件
2. 检查状态栏“已导入 + 解析条数”
3. 点击“`AI 故障分析`”
4. 查看右侧“AI 分析报告”
5. 点击“`导出报告`”，保存为 `.md/.txt`
