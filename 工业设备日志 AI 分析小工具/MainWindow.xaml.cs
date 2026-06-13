using Markdig;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 工业设备日志_AI_分析小工具
{
    /// <summary>
    /// 主窗口。提供工业设备日志导入、AI 分析、报告导出等一站式诊断功能。
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 状态与字段

        /// <summary>
        /// 底栏状态指示灯级别。
        /// </summary>
        private enum StatusLevel
        {
            Info,
            Success,
            Warning,
            Error
        }

        /// <summary>全局共享 HttpClient，避免频繁创建连接池。</summary>
        private static readonly HttpClient HttpClient = new();
        /// <summary>加密后文本的前缀标记，用于区分明文与密文。</summary>
        private const string EncryptedPrefix = "enc:";
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>运行时配置对象，从 appsettings.json 反序列化。</summary>
        private AppSettings _settings = new();
        /// <summary>已解析的日志条目列表。</summary>
        private List<LogEntry> _parsedLogs = new();
        /// <summary>最近一次 AI 分析的结果报告。</summary>
        private AnalysisReport? _latestReport;
        /// <summary>当前活跃的模型提供商名称。</summary>
        private string _activeProviderName = "当前模型";
        /// <summary>当前活跃的模型提供商配置。</summary>
        private ProviderConfig _activeProvider = new();
        /// <summary>配置文件所在目录（BaseDirectory + config）。</summary>
        private string _configDirectoryPath = string.Empty;
        /// <summary>appsettings.json 完整路径。</summary>
        private string _settingsFilePath = string.Empty;
        /// <summary>诊断提示词模板，从文本文件加载。</summary>
        private string _diagnosisPromptText = string.Empty;
        /// <summary>知识库内容，从 Markdown 文件加载。</summary>
        private string _knowledgeBaseText = string.Empty;
        /// <summary>对话框服务，用于显示提示/错误模态框。</summary>
        private readonly DialogService _dialogService;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化组件、对话框服务，然后依次加载配置、设置默认提供商、同步到 UI。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            _dialogService = new DialogService(this);
            LoadSettings();
            InitializeActiveProvider();
            LoadSelectedProviderSettingsToUi();
            InitializeRawLogTextBox();
        }

        #endregion

        #region UI 初始化

        /// <summary>
        /// 使 RawLogTextBox 完全不可聚焦（键盘+鼠标），保留滚轮能力但禁止光标进入。
        /// </summary>
        private void InitializeRawLogTextBox()
        {
            RawLogTextBox.PreviewGotKeyboardFocus += (_, e) => e.Handled = true;
            RawLogTextBox.PreviewMouseDown += (_, e) =>
            {
                // 鼠标中键滚轮仍可滚动，但不会获得焦点
                if (e.ChangedButton != System.Windows.Input.MouseButton.Middle)
                {
                    e.Handled = true;
                }
            };
        }

        #endregion

        #region 配置加载与保存

        /// <summary>
        /// 从 config/appsettings.json 读取配置，自动迁移明文 API Key 为加密存储。
        /// 若文件不存在，使用默认配置并以警告提示用户。
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                _configDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
                _settingsFilePath = Path.Combine(_configDirectoryPath, "appsettings.json");

                if (!File.Exists(_settingsFilePath))
                {
                    SetStatus("未找到 config/appsettings.json，已使用默认配置。", StatusLevel.Warning);
                    LoadPromptAndKnowledgeFiles();
                    return;
                }

                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (loaded is not null)
                {
                    _settings = loaded;
                }

                // 检测并加密未保护的 API Key
                EnsureApiKeysEncrypted();
                LoadPromptAndKnowledgeFiles();

                SetStatus("配置加载成功。", StatusLevel.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"配置加载失败：{ex.Message}", StatusLevel.Error);
            }
        }

        private void EnsureApiKeysEncrypted()
        {
            var changed = false;
            foreach (var provider in _settings.Providers.Values)
            {
                if (string.IsNullOrWhiteSpace(provider.ApiKey) || IsEncrypted(provider.ApiKey))
                {
                    continue;
                }

                provider.ApiKey = EncryptSecret(provider.ApiKey);
                changed = true;
            }

            if (changed)
            {
                SaveSettings();
            }
        }

        /// <summary>
        /// 将运行时配置写入 appsettings.json（保留中文字符，带缩进）。
        /// </summary>
        private void SaveSettings()
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions(_jsonOptions)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(_settingsFilePath, json, new UTF8Encoding(true));
        }

        private void LoadPromptAndKnowledgeFiles()
        {
            var promptPath = ResolveConfigPath(_settings.Prompting.DiagnosisPromptFile, "提示词/诊断提示词.txt");
            var knowledgePath = ResolveConfigPath(_settings.Prompting.KnowledgeBaseFile, "知识库/诊断知识库.md");

            _diagnosisPromptText = File.Exists(promptPath)
                ? File.ReadAllText(promptPath, Encoding.UTF8)
                : GetDefaultPrompt();

            _knowledgeBaseText = File.Exists(knowledgePath)
                ? File.ReadAllText(knowledgePath, Encoding.UTF8)
                : "无额外知识库内容。";
        }

        private string ResolveConfigPath(string relativeOrAbsolutePath, string fallbackRelativePath)
        {
            var path = string.IsNullOrWhiteSpace(relativeOrAbsolutePath) ? fallbackRelativePath : relativeOrAbsolutePath;
            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(_configDirectoryPath, path.Replace('/', Path.DirectorySeparatorChar));
        }

        #endregion

        #region UI 初始化与设置

        /// <summary>
        /// 从配置中选取第一个提供商作为当前活跃模型。
        /// 若配置为空，写入一组默认值（DeepSeek 并保存）。
        /// </summary>
        private void InitializeActiveProvider()
        {
            if (_settings.Providers.Count == 0)
            {
                _activeProviderName = "当前模型";
                _activeProvider = new ProviderConfig
                {
                    BaseUrl = "https://api.deepseek.com",
                    Model = "deepseek-v4-flash",
                    ApiKeyEnvVar = "DEEPSEEK_API_KEY"
                };

                _settings.Providers[_activeProviderName] = _activeProvider;
                SaveSettings();
                return;
            }

            var first = _settings.Providers.First();
            _activeProviderName = first.Key;
            _activeProvider = first.Value;
        }

        private void LoadSelectedProviderSettingsToUi()
        {
            ApiBaseUrlTextBox.Text = _activeProvider.BaseUrl;
            ModelNameTextBox.Text = _activeProvider.Model;
            ApiKeyPasswordBox.Password = string.Empty;
            ApiKeyHintTextBlock.Text = string.IsNullOrWhiteSpace(_activeProvider.ApiKey)
                ? "API Key：未保存，输入后点击“保存配置”"
                : "API Key：已保存，留空不修改";
        }

        private ProviderConfig BuildProviderFromUi(ProviderConfig source)
        {
            return new ProviderConfig
            {
                BaseUrl = NormalizeApiEndpoint(ApiBaseUrlTextBox.Text, source.BaseUrl),
                Model = string.IsNullOrWhiteSpace(ModelNameTextBox.Text) ? source.Model : ModelNameTextBox.Text.Trim(),
                ApiKeyEnvVar = source.ApiKeyEnvVar,
                ApiKey = source.ApiKey
            };
        }

        private static string NormalizeApiEndpoint(string? uiInput, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(uiInput) ? fallback : uiInput.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value;
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 保存模型配置：先校验输入，然后更新活跃提供商并持久化到文件。
        /// 若用户在密码框输入了新 API Key，则加密保存；留空则不修改。
        /// </summary>
        private void SaveModelSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateModelInputs(showFeedback: true))
            {
                SetStatus("配置校验失败，请根据提示修正输入。", StatusLevel.Warning);
                return;
            }

            _activeProvider.BaseUrl = NormalizeApiEndpoint(ApiBaseUrlTextBox.Text, _activeProvider.BaseUrl);
            _activeProvider.Model = string.IsNullOrWhiteSpace(ModelNameTextBox.Text) ? _activeProvider.Model : ModelNameTextBox.Text.Trim();

            var inputApiKey = ApiKeyPasswordBox.Password?.Trim();
            if (!string.IsNullOrWhiteSpace(inputApiKey))
            {
                _activeProvider.ApiKey = EncryptSecret(inputApiKey);
            }
            else if (string.IsNullOrWhiteSpace(_activeProvider.ApiKey))
            {
                ShowDialog("API Key 缺失", "API Key 留空且配置文件中不存在已保存的 API Key，请先输入后再保存。", StatusLevel.Warning);
                return;
            }

            _settings.Providers.Clear();
            _settings.Providers[_activeProviderName] = _activeProvider;

            SaveSettings();
            ApiKeyPasswordBox.Password = string.Empty;
            LoadSelectedProviderSettingsToUi();
            SetStatus("大模型配置已保存。", StatusLevel.Success);
        }

        /// <summary>
        /// 主分析入口：校验配置 → 解析日志 → 构建提示词 → 调用 AI API → 渲染报告。
        /// 异步执行，分析期间锁定 UI 控件防止重复操作。
        /// </summary>
        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            var runtimeProvider = BuildProviderFromUi(_activeProvider);

            if (!ValidateModelInputs(showFeedback: true))
            {
                SetStatus("配置校验失败，请根据提示修正输入。", StatusLevel.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(RawLogTextBox.Text))
            {
                ShowDialog("提示", "请先导入日志。", StatusLevel.Info);
                return;
            }

            var apiKey = GetProviderApiKey(runtimeProvider);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ShowDialog("API Key 缺失", $"未读取到可用 API Key。请配置环境变量 {runtimeProvider.ApiKeyEnvVar} 或在界面输入后保存。", StatusLevel.Warning);
                return;
            }

            SetBusy(true, "正在调用 AI 分析日志...");
            try
            {
                _parsedLogs = ParseLogLines(RawLogTextBox.Text);
                var maxLines = _settings.Prompting.MaxLogLinesForAnalysis > 0
                    ? _settings.Prompting.MaxLogLinesForAnalysis
                    : 200;
                ParseInfoTextBlock.Text = _parsedLogs.Count > maxLines
                    ? $"解析到 {_parsedLogs.Count} 条日志（取前 {maxLines} 条分析）"
                    : $"解析到 {_parsedLogs.Count} 条日志";

                var prompt = BuildPrompt(_parsedLogs);
                var analysisContent = await AnalyzeWithApiAsync(runtimeProvider, apiKey, prompt);
                _latestReport = ConvertToReport(analysisContent, _activeProviderName);
                RenderMarkdownToBrowser(RenderReport(_latestReport));
                SetStatus("分析完成。", StatusLevel.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"分析失败：{ex.Message}", StatusLevel.Error);
                ShowDialog("错误", ex.Message, StatusLevel.Error);
            }
            finally
            {
                SetBusy(false, "就绪");
            }
        }

        /// <summary>
        /// 打开文件选择对话框，读取日志内容并在后台线程中解析。
        /// </summary>
        private async void ImportLogButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "日志文件|*.log;*.txt;*.json|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SetBusy(true, "正在解析日志...");
            try
            {
                // 后台线程读取 + 解析，避免界面卡顿
                var rawText = await Task.Run(() => File.ReadAllText(dialog.FileName, Encoding.UTF8));
                RawLogTextBox.Text = rawText;
                var parsed = await Task.Run(() => ParseLogLines(rawText));
                _parsedLogs = parsed;
                ParseInfoTextBlock.Text = $"已导入：{Path.GetFileName(dialog.FileName)}，解析 {_parsedLogs.Count} 条";
                SetStatus("日志导入成功。", StatusLevel.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"日志导入失败：{ex.Message}", StatusLevel.Error);
                ShowDialog("导入错误", $"无法读取日志文件：{ex.Message}", StatusLevel.Error);
            }
            finally
            {
                SetBusy(false, _parsedLogs.Count > 0 ? "就绪" : "就绪");
            }
        }

        /// <summary>
        /// 将最近一次分析报告导出为 Markdown 文件，文件名含时间戳。
        /// </summary>
        private void ExportReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestReport is null)
            {
                ShowDialog("提示", "暂无可导出的分析报告，请先执行 AI 分析。", StatusLevel.Info);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Markdown 文件|*.md|文本文件|*.txt",
                FileName = $"分析报告_{DateTime.Now:yyyyMMdd_HHmmss}.md"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            File.WriteAllText(dialog.FileName, RenderReport(_latestReport), Encoding.UTF8);
            SetStatus($"报告已导出：{dialog.FileName}", StatusLevel.Success);
        }

        #endregion

        #region 日志解析与提示词构建

        /// <summary>
        /// 将原始文本按行解析为 LogEntry 列表。支持标准时间戳 + 级别前缀的格式，
        /// 无法匹配的行归为"未知"级别并保留原文。
        /// </summary>
        private List<LogEntry> ParseLogLines(string rawText)
        {
            var lines = rawText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            var result = new List<LogEntry>(lines.Length);
            // 匹配格式：[2025-06-12 10:30:00] [ERROR] 消息内容  或  2025-06-12T10:30:00 消息内容
            var regex = new Regex(@"^(?<time>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2})(\s+\[(?<level>\w+)\])?\s*(?<msg>.*)$", RegexOptions.Compiled);

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (!match.Success)
                {
                    result.Add(new LogEntry
                    {
                        Timestamp = null,
                        Level = "未知",
                        Message = line
                    });
                    continue;
                }

                DateTime? parsedTime = null;
                if (DateTime.TryParse(match.Groups["time"].Value, out var dt))
                {
                    parsedTime = dt;
                }

                result.Add(new LogEntry
                {
                    Timestamp = parsedTime,
                    Level = string.IsNullOrWhiteSpace(match.Groups["level"].Value) ? "信息级" : match.Groups["level"].Value,
                    Message = match.Groups["msg"].Value
                });
            }

            return result;
        }

        /// <summary>
        /// 组装发送给 AI 的完整提示词：诊断模板 + 知识库 + 截断后的日志条目。\n
        /// 日志条数受 MaxLogLinesForAnalysis 控制，超出则只取前 N 条。
        /// </summary>
        private string BuildPrompt(List<LogEntry> logs)
        {
            var maxLines = _settings.Prompting.MaxLogLinesForAnalysis > 0
                ? _settings.Prompting.MaxLogLinesForAnalysis
                : 200;
            var condensed = logs.Take(maxLines).Select((x, i) =>
                $"{i + 1}. [{x.Timestamp:yyyy-MM-dd HH:mm:ss}] [{x.Level}] {x.Message}");

            return $"""
{_diagnosisPromptText}

知识库参考：
{_knowledgeBaseText}

日志内容：
{string.Join(Environment.NewLine, condensed)}
""";
        }

        #endregion

        #region API 调用与响应转换

        /// <summary>
        /// 获取有效的 API Key，优先级：环境变量 > 加密存储的 Key > 明文 Key。
        /// </summary>
        private string GetProviderApiKey(ProviderConfig provider)
        {
            var envValue = Environment.GetEnvironmentVariable(provider.ApiKeyEnvVar ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue;
            }

            if (string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                return string.Empty;
            }

            return IsEncrypted(provider.ApiKey)
                ? DecryptSecret(provider.ApiKey)
                : provider.ApiKey;
        }

        private static bool IsEncrypted(string value)
            => value.StartsWith(EncryptedPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 使用 DPAPI 加密明文密钥，结果以 enc: 前缀标识。
        /// 加密基于当前 Windows 用户，仅同一用户可解密。
        /// </summary>
        private static string EncryptSecret(string plainText)
        {
            var input = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(input, null, DataProtectionScope.CurrentUser);
            return EncryptedPrefix + Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// 解密 enc: 前缀的密钥，使用 DPAPI（当前用户范围）。
        /// </summary>
        private static string DecryptSecret(string encryptedValue)
        {
            var base64 = encryptedValue[EncryptedPrefix.Length..];
            var encrypted = Convert.FromBase64String(base64);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        private static string GetDefaultPrompt()
        {
            return """
你是一名工业设备故障分析工程师。请根据日志给出结构化分析。

要求：
1) 输出 JSON，不要输出额外说明。
2) JSON 结构：
{
  "摘要": "...",
  "风险等级": "低|中|高|严重",
  "可能原因": ["..."],
  "建议措施": ["..."],
  "置信度": 0.0
}
3) 如果日志信息不足，请在“摘要”中明确说明信息不足，并给出建议采集项。
4) 所有输出内容必须使用中文（字段名、字段值、分析结论都使用中文）。
""";
        }

        /// <summary>
        /// 向 OpenAI 兼容接口发送 Chat Completion 请求，提取返回的文本内容。
        /// 请求地址由 BaseUrl 自动拼接（可接受完整地址或前缀）。
        /// </summary>
        private async Task<string> AnalyzeWithApiAsync(ProviderConfig provider, string apiKey, string prompt)
        {
            var requestUrl = ResolveAnalyzeApiUrl(provider.BaseUrl);
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new ChatCompletionsRequest
            {
                Model = provider.Model,
                Temperature = 0.2,
                Messages =
                [
                    new ChatMessage { Role = "system", Content = "你是严谨的工业设备日志分析助手。" },
                    new ChatMessage { Role = "user", Content = prompt }
                ]
            };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
            }

            var data = JsonSerializer.Deserialize<ChatCompletionsResponse>(body, _jsonOptions)
                       ?? throw new InvalidOperationException("未能解析 API 返回内容。");

            var content = data.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(content)
                ? throw new InvalidOperationException("模型返回为空。")
                : content;
        }

        /// <summary>
        /// 将用户输入的 API 地址规范化为可请求的 URL。
        /// 支持完整地址（含 /chat/completions）或简写前缀（自动补全 /v1/chat/completions）。
        /// </summary>
        private static string ResolveAnalyzeApiUrl(string baseUrl)
        {
            var value = (baseUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("API 地址不能为空。请先在“大模型设置”中配置。");
            }

            if (value.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var trimmed = value.TrimEnd('/');
            if (trimmed.EndsWith("/anthropic", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("当前工具使用 OpenAI 兼容的 chat.completions 请求格式，请将 API 地址设置为 https://api.deepseek.com。\n系统会在内部自动拼接为 /v1/chat/completions 调用地址。");
            }

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return $"{trimmed}/chat/completions";
            }

            return $"{trimmed}/v1/chat/completions";
        }

        /// <summary>
        /// 将 API 原始响应转换为 AnalysisReport 对象。
        /// 优先精确反序列化，失败则逐字段容错提取；
        /// 若两者都失败，将原始文本放入 Summary 作为回退。
        /// </summary>
        private AnalysisReport ConvertToReport(string analysisContent, string providerName)
        {
            if (TryParseAnalysisReport(analysisContent, out var report))
            {
                report.Provider = providerName;
                report.GeneratedAt = DateTime.Now;
                report.RiskLevel = NormalizeRiskLevel(report.RiskLevel);
                return report;
            }

            return new AnalysisReport
            {
                Provider = providerName,
                GeneratedAt = DateTime.Now,
                Summary = analysisContent,
                RiskLevel = "未知",
                PossibleCauses = new List<string> { "模型输出非 JSON，已作为纯文本保留。" },
                Recommendations = new List<string> { "请检查 prompt 或模型兼容性。" },
                Confidence = 0
            };
        }

        /// <summary>
        /// 将英文风险等级标准化为中文：low→低, medium→中, high→高, critical→严重。
        /// </summary>
        private static string NormalizeRiskLevel(string? riskLevel)
        {
            if (string.IsNullOrWhiteSpace(riskLevel))
            {
                return "未知";
            }

            return riskLevel.Trim().ToLowerInvariant() switch
            {
                "low" => "低",
                "medium" => "中",
                "high" => "高",
                "critical" => "严重",
                "unknown" => "未知",
                _ => riskLevel.Trim()
            };
        }

        private bool TryParseAnalysisReport(string content, out AnalysisReport report)
        {
            report = new AnalysisReport();

            try
            {
                var parsed = JsonSerializer.Deserialize<AnalysisReport>(content, _jsonOptions);
                if (parsed is not null && HasReportPayload(parsed))
                {
                    report = parsed;
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var summary = GetJsonString(root, "摘要", "summary");
                var riskLevel = GetJsonString(root, "风险等级", "riskLevel");
                var possibleCauses = GetJsonStringArray(root, "可能原因", "possibleCauses");
                var recommendations = GetJsonStringArray(root, "建议措施", "recommendations");
                var confidence = GetJsonDouble(root, "置信度", "confidence");

                report = new AnalysisReport
                {
                    Summary = summary,
                    RiskLevel = riskLevel,
                    PossibleCauses = possibleCauses,
                    Recommendations = recommendations,
                    Confidence = confidence
                };

                return HasReportPayload(report);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasReportPayload(AnalysisReport report)
        {
            return !string.IsNullOrWhiteSpace(report.Summary)
                   || report.PossibleCauses.Count > 0
                   || report.Recommendations.Count > 0;
        }

        private static string GetJsonString(JsonElement root, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!root.TryGetProperty(key, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? string.Empty;
                }

                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetRawText();
                }
            }

            return string.Empty;
        }

        private static List<string> GetJsonStringArray(JsonElement root, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                return value.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .ToList();
            }

            return new List<string>();
        }

        private static double GetJsonDouble(JsonElement root, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!root.TryGetProperty(key, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var stringNumber))
                {
                    return stringNumber;
                }
            }

            return 0;
        }

        #endregion

        #region 报告渲染与界面状态

        /// <summary>
        /// 将 AnalysisReport 对象渲染为 Markdown 字符串，供 WebBrowser 展示。
        /// </summary>
        private static string RenderReport(AnalysisReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 工业设备日志 AI 分析报告");
            builder.AppendLine();
            builder.AppendLine($"- 生成时间：{report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- 模型提供商：{report.Provider}");
            builder.AppendLine($"- 风险等级：{report.RiskLevel}");
            builder.AppendLine($"- 置信度：{report.Confidence:F2}");
            builder.AppendLine();
            builder.AppendLine("## 摘要");
            builder.AppendLine(report.Summary);
            builder.AppendLine();
            builder.AppendLine("## 可能原因");
            foreach (var item in report.PossibleCauses)
            {
                builder.AppendLine($"- {item}");
            }

            builder.AppendLine();
            builder.AppendLine("## 建议措施");
            foreach (var item in report.Recommendations)
            {
                builder.AppendLine($"- {item}");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 将 Markdown 字符串通过 Markdig 转为 HTML 后注入 WebBrowser 显示。
        /// 内置预定义的 CSS 样式（工业蓝主题）。
        /// </summary>
        private void RenderMarkdownToBrowser(string markdown)
        {
            var htmlBody = Markdown.ToHtml(markdown ?? string.Empty);
            var html = $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <style>
    * { box-sizing: border-box; }
    body {
      margin: 14px;
      font-family: "Segoe UI", "Microsoft YaHei", sans-serif;
      font-size: 14px;
      color: #1f2937;
      line-height: 1.7;
      background: transparent;
    }
    h1 {
      color: #0a1e3c;
      font-size: 20px;
      font-weight: 700;
      margin-top: 0.2em;
      margin-bottom: 0.6em;
      padding-bottom: 8px;
      border-bottom: 2px solid #dce5f0;
    }
    h2 {
      color: #0f2747;
      font-size: 17px;
      font-weight: 600;
      margin-top: 1.2em;
      margin-bottom: 0.5em;
      padding-bottom: 5px;
      border-bottom: 1px solid #e9eef4;
    }
    h3 {
      color: #1a3a5c;
      font-size: 15px;
      font-weight: 600;
      margin-top: 1em;
      margin-bottom: 0.4em;
    }
    p { margin: 0.5em 0; }
    ul, ol {
      padding-left: 1.4em;
      margin: 0.4em 0;
    }
    li { margin: 0.25em 0; }
    strong { color: #0f2747; font-weight: 600; }
    em { color: #374151; }
    code {
      background: #edf2f7;
      padding: 2px 6px;
      border-radius: 4px;
      font-family: "Cascadia Code", Consolas, "JetBrains Mono", monospace;
      font-size: 13px;
      color: #1a3a5c;
    }
    pre {
      background: #f3f7fc;
      border: 1px solid #dce5f0;
      border-radius: 8px;
      padding: 12px 14px;
      overflow-x: auto;
      margin: 0.8em 0;
    }
    pre code {
      background: none;
      padding: 0;
      border-radius: 0;
      font-size: 13px;
      color: #1f2937;
      line-height: 1.5;
    }
    blockquote {
      margin: 0.8em 0;
      padding: 0.6em 1em;
      border-left: 4px solid #60a5fa;
      background: #f0f7ff;
      color: #1e3a5f;
      border-radius: 0 6px 6px 0;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      margin: 0.8em 0;
      font-size: 13px;
    }
    th, td {
      border: 1px solid #dce5f0;
      padding: 8px 12px;
      text-align: left;
    }
    th {
      background: #eef2f7;
      color: #0f2747;
      font-weight: 600;
    }
    tr:nth-child(even) { background: #f8fafc; }
    tr:hover { background: #eff6ff; }
    hr {
      border: none;
      border-top: 1px solid #dce5f0;
      margin: 1.2em 0;
    }
  </style>
</head>
<body>
{{htmlBody}}
</body>
</html>
""";

            AnalysisMarkdownBrowser.NavigateToString(html);
        }

        /// <summary>
        /// 锁定/解锁 UI 控件，防止分析或导入期间的重复操作，同时更新状态文本。
        /// </summary>
        private void SetBusy(bool busy, string status)
        {
            ImportLogButton.IsEnabled = !busy;
            AnalyzeButton.IsEnabled = !busy;
            ExportReportButton.IsEnabled = !busy;
            SaveModelSettingsButton.IsEnabled = !busy;
            ApiBaseUrlTextBox.IsEnabled = !busy;
            ModelNameTextBox.IsEnabled = !busy;
            ApiKeyPasswordBox.IsEnabled = !busy;
            SetStatus(status, StatusLevel.Info);
        }

        /// <summary>
        /// 校验 API 地址与模型名输入框的内容，并用边框颜色反馈有效性。
        /// </summary>
        private bool ValidateModelInputs(bool showFeedback)
        {
            var messages = new List<string>();

            var apiUrl = ApiBaseUrlTextBox.Text?.Trim() ?? string.Empty;
            var modelName = ModelNameTextBox.Text?.Trim() ?? string.Empty;

            var apiUrlValid = Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)
                              && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            if (!apiUrlValid)
            {
                messages.Add("API地址格式不正确，应为 http/https 地址。");
            }

            var modelNameValid = !string.IsNullOrWhiteSpace(modelName);
            if (!modelNameValid)
            {
                messages.Add("模型名不能为空。");
            }

            SetControlValidationState(ApiBaseUrlTextBox, apiUrlValid);
            SetControlValidationState(ModelNameTextBox, modelNameValid);

            if (showFeedback)
            {
                ModelSettingsValidationTextBlock.Text = string.Join(" ", messages);
                ModelSettingsValidationTextBlock.Visibility = messages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }

            return messages.Count == 0;
        }

        private static void SetControlValidationState(Control control, bool valid)
        {
            control.BorderThickness = valid ? new Thickness(1) : new Thickness(2);
            control.BorderBrush = valid
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDDDDD"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"));
        }

        /// <summary>
        /// 更新底栏状态文本，并切换右侧指示灯颜色（蓝/绿/橙/红）。
        /// </summary>
        private void SetStatus(string message, StatusLevel level)
        {
            StatusTextBlock.Text = message;
            var inactiveLamp = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0BEC5"));
            InfoLamp.Fill = inactiveLamp;
            SuccessLamp.Fill = inactiveLamp;
            WarningLamp.Fill = inactiveLamp;
            ErrorLamp.Fill = inactiveLamp;

            switch (level)
            {
                case StatusLevel.Success:
                    StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"));
                    StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B5E20"));
                    SuccessLamp.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#43A047"));
                    break;
                case StatusLevel.Warning:
                    StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF8E1"));
                    StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"));
                    WarningLamp.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB8C00"));
                    break;
                case StatusLevel.Error:
                    StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDECEA"));
                    StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B71C1C"));
                    ErrorLamp.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"));
                    break;
                default:
                    StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F1FF"));
                    StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D47A1"));
                    InfoLamp.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E88E5"));
                    break;
            }
        }

        private void ShowDialog(string title, string message, StatusLevel level)
        {
            var dialogLevel = level switch
            {
                StatusLevel.Success => DialogLevel.Success,
                StatusLevel.Warning => DialogLevel.Warning,
                StatusLevel.Error => DialogLevel.Error,
                _ => DialogLevel.Info
            };
            _dialogService.Show(title, message, dialogLevel);
        }

        #endregion

        /// <summary>
        /// 实时校验 API 地址格式，更新 HelperText 提示。
        /// </summary>
        private void ApiBaseUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            string input = textBox.Text.Trim();

            // 验证是否为有效的 http/https 地址
            bool isValidUrl = !string.IsNullOrEmpty(input) &&
                              Uri.TryCreate(input, UriKind.Absolute, out Uri uriResult) &&
                              (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

            // 验证通过时清空HelperText，不显示提示
            if (isValidUrl)
            {
                materialDesign: HintAssist.SetHelperText(textBox, "API 地址");
                //textBox.BorderBrush = Brushes.Transparent; // 可选：恢复默认边框
            }
            else
            {
                materialDesign: HintAssist.SetHelperText(textBox, "请输入有效的 http/https 地址");
                // 可选：验证失败时显示红色边框
                //textBox.BorderBrush = Brushes.Red;
            }
        }

        /// <summary>
        /// 实时校验模型名是否为空，更新 HelperText 提示。
        /// </summary>
        private void ModelNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            string input = textBox.Text.Trim();

            // 验证模型名不能为空
            bool isValid = !string.IsNullOrEmpty(input);

            // 验证通过时清空HelperText，不显示提示
            if (isValid)
            {
                materialDesign: HintAssist.SetHelperText(textBox, "模型名");
                //textBox.BorderBrush = Brushes.Transparent;
            }
            else
            {
                materialDesign: HintAssist.SetHelperText(textBox, "模型名不能为空");
                //textBox.BorderBrush = Brushes.Red;
            }
        }
    }

    /// <summary>
    /// 应用配置根对象，对应 appsettings.json 顶层结构。
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>模型提供商字典，键为自定义名称，值为连接参数。</summary>
        [JsonPropertyName("模型提供商")]
        public Dictionary<string, ProviderConfig> Providers { get; set; } = new();

        /// <summary>提示词与知识库相关配置。</summary>
        [JsonPropertyName("提示配置")]
        public PromptingConfig Prompting { get; set; } = new();
    }

    /// <summary>
    /// 提示词与知识库文件路径配置，以及分析参数。
    /// </summary>
    public sealed class PromptingConfig
    {
        /// <summary>诊断提示词模板文件路径（相对或绝对）。</summary>
        [JsonPropertyName("诊断提示词文件")]
        public string DiagnosisPromptFile { get; set; } = "提示词/诊断提示词.txt";

        /// <summary>诊断知识库 Markdown 文件路径。</summary>
        [JsonPropertyName("诊断知识库文件")]
        public string KnowledgeBaseFile { get; set; } = "知识库/诊断知识库.md";

        /// <summary>单次分析提交给 AI 的最大日志条数，超出部分截断。</summary>
        [JsonPropertyName("每次分析最大日志条数")]
        public int MaxLogLinesForAnalysis { get; set; } = 200;
    }

    /// <summary>
    /// 单个模型提供商的连接参数（地址、模型名、环境变量、密钥）。
    /// </summary>
    public sealed class ProviderConfig
    {
        /// <summary>API 接口地址，如 https://api.deepseek.com</summary>
        [JsonPropertyName("接口地址")]
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>模型名称，如 deepseek-v4-flash</summary>
        [JsonPropertyName("模型名称")]
        public string Model { get; set; } = string.Empty;

        /// <summary>优先使用的环境变量键名，如 DEEPSEEK_API_KEY</summary>
        [JsonPropertyName("环境变量键名")]
        public string ApiKeyEnvVar { get; set; } = string.Empty;

        /// <summary>加密存储的接口密钥（以 enc: 前缀标识）。</summary>
        [JsonPropertyName("接口密钥")]
        public string ApiKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// 单条日志条目，解析后的结构化表示。
    /// </summary>
    public sealed class LogEntry
    {
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; } = "信息级";
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// AI 分析结果的结构化报告，包含摘要、风险等级、原因、建议。
    /// JSON 字段使用中文，与 AI 输出格式对齐。
    /// </summary>
    public sealed class AnalysisReport
    {
        [JsonPropertyName("摘要")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("风险等级")]
        public string RiskLevel { get; set; } = "未知";

        [JsonPropertyName("可能原因")]
        public List<string> PossibleCauses { get; set; } = new();

        [JsonPropertyName("建议措施")]
        public List<string> Recommendations { get; set; } = new();

        [JsonPropertyName("置信度")]
        public double Confidence { get; set; }

        /// <summary>模型提供商名称（运行时填充）。</summary>
        public string Provider { get; set; } = string.Empty;
        /// <summary>报告生成时间（运行时填充）。</summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// OpenAI 兼容的 Chat Completions 请求体。
    /// </summary>
    public sealed class ChatCompletionsRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = new();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.2;
    }

    /// <summary>
    /// Chat 消息，包含角色（system/user/assistant）和内容。
    /// </summary>
    public sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// OpenAI 兼容的 Chat Completions 响应体。
    /// </summary>
    public sealed class ChatCompletionsResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    /// <summary>
    /// 响应中的单个选择，包含模型返回的 message。
    /// </summary>
    public sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}