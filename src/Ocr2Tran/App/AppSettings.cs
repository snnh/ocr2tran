using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Ocr2Tran.App;

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class AppSettings
{
    [DisplayName("运行模式")]
    [Description("启动行为和自动 OCR/翻译循环设置。")]
    public ModeSettings Mode { get; set; } = new();

    [DisplayName("快捷键")]
    [Description("全局快捷键配置。")]
    public HotkeySettings Hotkeys { get; set; } = new();

    [DisplayName("OCR")]
    [Description("OCR 后端、模型和文本后处理设置。")]
    public OcrSettings Ocr { get; set; } = new();

    [DisplayName("翻译")]
    [Description("翻译服务、语言和 HTTP/AI 接入设置。")]
    public TranslationSettings Translation { get; set; } = new();

    [DisplayName("覆盖层")]
    [Description("原位显示文字的样式设置。")]
    public OverlaySettings Overlay { get; set; } = new();

    [DisplayName("性能")]
    [Description("CPU、内存和缓存相关设置。")]
    public PerformanceSettings Performance { get; set; } = new();

    [DisplayName("插件")]
    [Description("用户插件加载设置。")]
    public PluginSettings Plugins { get; set; } = new();
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class ModeSettings
{
    [DisplayName("启动后隐藏到托盘")]
    [Description("启用后，程序启动时默认只显示托盘图标。")]
    public bool StartMinimizedToTray { get; set; } = true;

    [DisplayName("启动时显示控制面板")]
    [Description("启用后，程序启动时自动打开控制面板。")]
    public bool ShowControlPanelOnStart { get; set; }

    [DisplayName("自动循环间隔毫秒")]
    [Description("自动 OCR/翻译循环的间隔，最小按 250 毫秒处理。")]
    public int AutoIntervalMs { get; set; } = 10000;

    public override string ToString() => "运行模式设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class HotkeySettings
{
    [DisplayName("单次 OCR")]
    [Description("触发一次全屏 OCR。")]
    public string SingleOcr { get; set; } = "Ctrl+Alt+O";

    [DisplayName("单次 OCR 并翻译")]
    [Description("触发一次全屏 OCR 和翻译。")]
    public string SingleOcrTranslate { get; set; } = "Ctrl+Alt+T";

    [DisplayName("框选 OCR")]
    [Description("框选区域后 OCR。")]
    public string RegionOcr { get; set; } = "Ctrl+Alt+R";

    [DisplayName("框选 OCR 并翻译")]
    [Description("框选区域后 OCR 和翻译。")]
    public string RegionOcrTranslate { get; set; } = "Ctrl+Alt+Y";

    [DisplayName("启动/暂停自动 OCR")]
    [Description("切换全屏自动 OCR 循环。")]
    public string ToggleAutoOcr { get; set; } = "Ctrl+Alt+A";

    [DisplayName("启动/暂停自动翻译")]
    [Description("切换全屏自动 OCR 和翻译循环。")]
    public string ToggleAutoTranslate { get; set; } = "Ctrl+Alt+S";

    [DisplayName("启动/暂停框选自动翻译")]
    [Description("框选固定区域后切换自动 OCR 和翻译循环。")]
    public string ToggleAutoRegionTranslate { get; set; } = "Ctrl+Alt+U";

    [DisplayName("退出")]
    [Description("彻底关闭程序。")]
    public string Exit { get; set; } = "Ctrl+Alt+Q";

    public override string ToString() => "快捷键设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class OcrSettings
{
    [DisplayName("OCR 后端")]
    [Description("可选 onnx、paddleCli 或 noop。")]
    public string Provider { get; set; } = "noop";

    [DisplayName("截图未变化时跳过")]
    [Description("启用后，相同截图会复用上一轮 OCR 结果。")]
    public bool SkipIfScreenshotUnchanged { get; set; } = true;

    [DisplayName("文本后处理")]
    [Description("OCR 文本清洗、过滤、合并和去重设置。")]
    public OcrPostProcessingSettings PostProcessing { get; set; } = new();

    [DisplayName("图像预处理")]
    [Description("送入 OCR 前的放大、灰度和对比度增强设置。")]
    public OcrImagePreprocessingSettings ImagePreprocessing { get; set; } = new();

    [DisplayName("PaddleOCR")]
    [Description("外部 PaddleOCR CLI 后端设置。")]
    public PaddleSettings Paddle { get; set; } = new();

    [DisplayName("ONNX OCR")]
    [Description("内置 ONNX Runtime OCR 后端设置。")]
    public OnnxOcrSettings Onnx { get; set; } = new();

    public override string ToString() => "OCR 设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class OcrImagePreprocessingSettings
{
    [DisplayName("启用图像预处理")]
    [Description("启用后，截图会先进行放大和画质增强再送入 OCR。")]
    public bool Enabled { get; set; } = true;

    [DisplayName("放大倍率")]
    [Description("OCR 输入图像放大倍率。小字识别差时可调高，性能不足时调低。")]
    public double Scale { get; set; } = 2;

    [DisplayName("最大长边")]
    [Description("预处理后图像最长边上限，避免全屏 OCR 占用过多 CPU 和内存。")]
    public int MaxLongSide { get; set; } = 3200;

    [DisplayName("转为灰度")]
    [Description("把 OCR 输入转为灰度图，通常能减少彩色背景干扰。")]
    public bool Grayscale { get; set; } = true;

    [DisplayName("对比度")]
    [Description("OCR 输入对比度倍率。1 表示不调整。")]
    public double Contrast { get; set; } = 1.35;

    [DisplayName("亮度")]
    [Description("OCR 输入亮度偏移，范围建议 -0.5 到 0.5。")]
    public double Brightness { get; set; }

    public override string ToString() => "图像预处理设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class OcrPostProcessingSettings
{
    [DisplayName("启用后处理")]
    [Description("关闭后，OCR 结果会原样进入翻译和覆盖层。")]
    public bool Enabled { get; set; } = true;

    [DisplayName("删除控制字符")]
    [Description("删除不可见控制字符，保留换行、回车和制表符。")]
    public bool RemoveControlCharacters { get; set; } = true;

    [DisplayName("规范化空白")]
    [Description("把连续空白折叠成一个空格。")]
    public bool NormalizeWhitespace { get; set; } = true;

    [DisplayName("丢弃纯标点区域")]
    [Description("丢弃只有标点或符号、缺少有效字符的 OCR 区域。")]
    public bool DropPunctuationOnly { get; set; } = true;

    [DisplayName("丢弃短孤立文本")]
    [Description("过滤没有邻近文本的短碎片，适合减少复杂界面误识别。")]
    public bool DropShortIsolatedText { get; set; } = true;

    [DisplayName("合并邻近文本块")]
    [Description("把同一行距离较近的文本块合并后再显示或翻译。")]
    public bool MergeNearbyTextRegions { get; set; } = true;

    [DisplayName("丢弃重叠重复框")]
    [Description("丢弃位置高度重叠且文本相同的重复 OCR 结果。")]
    public bool DropOverlappingDuplicates { get; set; } = true;

    [DisplayName("最少有效字符数")]
    [Description("文本中至少需要多少个字母、数字或 CJK 字符。")]
    public int MinMeaningfulCharacters { get; set; } = 1;

    [DisplayName("最短文本长度")]
    [Description("短于该长度的 OCR 文本会被过滤。")]
    public int MinTextLength { get; set; } = 1;

    [DisplayName("最低置信度")]
    [Description("过滤低置信度结果。仅作用于后端提供了大于 0 置信度的区域。")]
    public double MinConfidence { get; set; } = 0.5;

    [DisplayName("最小区域宽度")]
    [Description("OCR 文本框宽度小于该值时过滤。")]
    public int MinRegionWidth { get; set; } = 12;

    [DisplayName("最小区域高度")]
    [Description("OCR 文本框高度小于该值时过滤。")]
    public int MinRegionHeight { get; set; } = 8;

    [DisplayName("最小区域面积")]
    [Description("OCR 文本框面积小于该值时过滤。")]
    public int MinRegionArea { get; set; } = 96;

    [DisplayName("短文本最大长度")]
    [Description("短孤立文本过滤使用的长度阈值。")]
    public int ShortTextMaxLength { get; set; } = 3;

    [DisplayName("同行垂直容差")]
    [Description("判断两个文本框是否在同一行的垂直容差，单位像素。")]
    public int SameLineVerticalTolerancePx { get; set; } = 10;

    [DisplayName("同行最大水平间距")]
    [Description("同一行内文本块可合并的最大水平间距，单位像素。")]
    public int SameLineMaxHorizontalGapPx { get; set; } = 28;

    [DisplayName("重复框重叠比例")]
    [Description("文本相同且重叠比例超过该值时视为重复框。")]
    public double DuplicateOverlapRatio { get; set; } = 0.82;

    [DisplayName("最多保留区域数")]
    [Description("单轮最多保留多少个 OCR 区域；0 表示不限制。")]
    public int MaxRegions { get; set; } = 80;

    [DisplayName("强制删除字符")]
    [Description("无条件从 OCR 文本中删除的字符列表。")]
    public string CharactersToRemove { get; set; } = "\u200B\u200C\u200D\uFEFF¦|";

    public override string ToString() => "文本后处理设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class PaddleSettings
{
    [DisplayName("可执行文件")]
    [Description("paddleocr.exe 或兼容包装程序路径。")]
    public string Executable { get; set; } = "paddleocr.exe";

    [DisplayName("参数模板")]
    [Description("启动 PaddleOCR 的命令行参数模板。")]
    public string ArgumentsTemplate { get; set; } = "--image_dir \"{image}\" --use_angle_cls true --lang ch --type ocr";

    [DisplayName("超时毫秒")]
    [Description("PaddleOCR 子进程超时时间。")]
    public int TimeoutMs { get; set; } = 15000;

    [DisplayName("使用导入模型")]
    [Description("启用后会把导入的模型目录展开到命令行参数。")]
    public bool UseImportedModels { get; set; }

    [DisplayName("模型根目录")]
    [Description("导入的 PaddleOCR 模型总目录。")]
    public string ModelRoot { get; set; } = "";

    [DisplayName("检测模型目录")]
    [Description("PaddleOCR det 推理模型目录。")]
    public string DetectionModelDir { get; set; } = "";

    [DisplayName("识别模型目录")]
    [Description("PaddleOCR rec 推理模型目录。")]
    public string RecognitionModelDir { get; set; } = "";

    [DisplayName("方向分类模型目录")]
    [Description("PaddleOCR cls 推理模型目录，可为空。")]
    public string ClassificationModelDir { get; set; } = "";

    [DisplayName("识别字典路径")]
    [Description("PaddleOCR rec 字典文件路径。")]
    public string RecCharDictPath { get; set; } = "";

    [DisplayName("识别语言")]
    [Description("传给 PaddleOCR 的 lang 参数。")]
    public string Language { get; set; } = "ch";

    [DisplayName("启用方向分类")]
    [Description("传给 PaddleOCR 的 use_angle_cls 参数。")]
    public bool UseAngleCls { get; set; } = true;

    public override string ToString() => "PaddleOCR 设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class OnnxOcrSettings
{
    [DisplayName("模型根目录")]
    [Description("导入的 ONNX OCR 模型总目录。")]
    public string ModelRoot { get; set; } = "";

    [DisplayName("检测模型路径")]
    [Description("文本检测 det.onnx 路径。")]
    public string DetectionModelPath { get; set; } = "";

    [DisplayName("识别模型路径")]
    [Description("文字识别 rec.onnx 路径。")]
    public string RecognitionModelPath { get; set; } = "";

    [DisplayName("方向分类模型路径")]
    [Description("方向分类模型路径，当前保留配置。")]
    public string ClassificationModelPath { get; set; } = "";

    [DisplayName("识别字典路径")]
    [Description("OCR 识别字典文件路径。字典已集成到 rec.onnx 时可留空。")]
    public string RecCharDictPath { get; set; } = "";

    [DisplayName("检测长边限制")]
    [Description("检测模型输入长边限制。")]
    public int DetLimitSideLen { get; set; } = 1536;

    [DisplayName("检测阈值")]
    [Description("DB 检测像素阈值。")]
    public float DetThreshold { get; set; } = 0.25f;

    [DisplayName("文本框阈值")]
    [Description("检测框平均分阈值。")]
    public float BoxThreshold { get; set; } = 0.45f;

    [DisplayName("文本框外扩比例")]
    [Description("检测框外扩比例。")]
    public float UnclipRatio { get; set; } = 1.8f;

    [DisplayName("最小文本框尺寸")]
    [Description("过滤过小检测框。")]
    public int MinBoxSize { get; set; } = 6;

    [DisplayName("识别输入高度")]
    [Description("识别模型输入高度 fallback。")]
    public int RecImageHeight { get; set; } = 48;

    [DisplayName("识别输入宽度")]
    [Description("识别模型输入宽度 fallback。")]
    public int RecImageWidth { get; set; } = 480;

    [DisplayName("ONNX CPU 线程数")]
    [Description("ONNX Runtime IntraOp 线程数。")]
    public int IntraOpNumThreads { get; set; } = 2;

    public override string ToString() => "ONNX OCR 设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class TranslationSettings
{
    [DisplayName("翻译后端")]
    [Description("可选 noop、baidu、google、http 或 ai。")]
    public string Provider { get; set; } = "noop";

    [DisplayName("源语言")]
    [Description("源语言代码，auto 表示自动检测。")]
    public string SourceLanguage { get; set; } = "auto";

    [DisplayName("目标语言")]
    [Description("目标语言代码，例如 zh、en、ja。")]
    public string TargetLanguage { get; set; } = "zh";

    [DisplayName("每秒请求数")]
    [Description("翻译请求速率限制；0 表示不限制。")]
    public double Rps { get; set; } = 2;

    [DisplayName("超时毫秒")]
    [Description("单次翻译请求超时时间。")]
    public int TimeoutMs { get; set; } = 15000;

    [DisplayName("百度翻译")]
    [Description("百度翻译接口设置。")]
    public BaiduSettings Baidu { get; set; } = new();

    [DisplayName("Google 翻译")]
    [Description("Google Cloud Translation 设置。")]
    public GoogleSettings Google { get; set; } = new();

    [DisplayName("HTTP/AI 翻译")]
    [Description("通用 HTTP 或 AI 翻译接口设置。")]
    public HttpTranslatorSettings Http { get; set; } = new();

    public override string ToString() => "翻译设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class BaiduSettings
{
    [DisplayName("接口地址")]
    [Description("百度翻译 API endpoint。")]
    public string Endpoint { get; set; } = "https://fanyi-api.baidu.com/api/trans/vip/translate";

    [DisplayName("App ID")]
    [Description("百度翻译 App ID。")]
    public string AppId { get; set; } = "";

    [DisplayName("密钥")]
    [Description("百度翻译 Secret。")]
    public string Secret { get; set; } = "";

    public override string ToString() => "百度翻译设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class GoogleSettings
{
    [DisplayName("接口地址")]
    [Description("Google Cloud Translation endpoint。")]
    public string Endpoint { get; set; } = "https://translation.googleapis.com/language/translate/v2";

    [DisplayName("API Key")]
    [Description("Google Cloud Translation API Key。")]
    public string ApiKey { get; set; } = "";

    [DisplayName("Bearer Token")]
    [Description("Google Cloud 访问令牌，可替代 API Key。")]
    public string BearerToken { get; set; } = "";

    public override string ToString() => "Google 翻译设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class HttpTranslatorSettings
{
    [DisplayName("接口地址")]
    [Description("HTTP/AI 翻译接口 endpoint。")]
    public string Endpoint { get; set; } = "http://localhost:8000/v1/chat/completions";

    [DisplayName("HTTP 方法")]
    [Description("请求方法，通常为 POST。")]
    public string Method { get; set; } = "POST";

    [DisplayName("API Key")]
    [Description("HTTP/AI 翻译接口 API Key。非空时会作为 Bearer Token 发送。")]
    [PasswordPropertyText(true)]
    public string ApiKey { get; set; } = "";

    [DisplayName("请求头 JSON")]
    [Description("请求头 JSON 对象，例如 {\"Content-Type\":\"application/json\"}。配置文件中也可直接写 JSON 对象。")]
    [JsonConverter(typeof(JsonTextConverter))]
    public string HeadersJson { get; set; } = "{\"Content-Type\":\"application/json\"}";

    [DisplayName("请求体 Content-Type")]
    [Description("请求体 Content-Type，例如 application/json 或 application/x-www-form-urlencoded。headersJson 中的 Content-Type 优先。")]
    public string BodyContentType { get; set; } = "application/json";

    [DisplayName("请求体模板")]
    [Description("请求体模板，支持 {text}、{prompt}、{source}、{target}、{apiKey}，可加 Raw 或 Url 后缀。配置文件中也可直接写 JSON 对象。")]
    [JsonConverter(typeof(JsonTextConverter))]
    public string BodyTemplate { get; set; } = "{\"text\":\"{text}\"}";

    [DisplayName("提示词")]
    [Description("AI 翻译提示词，可在请求体模板里用 {prompt} 引用。")]
    public string Prompt { get; set; } = "将 OCR 提取的游戏内文本翻译为中文。在没有官方译名的情况下，地名和人名不要翻译。只返回翻译结果，不要任何多余的文本。";

    [DisplayName("响应字段路径")]
    [Description("普通 JSON 响应中读取译文的字段路径。")]
    public string ResponseFieldPath { get; set; } = "choices.0.message.content";

    [DisplayName("流式模式")]
    [Description("可选 none、sse 或 ndjson。默认 sse。")]
    public string StreamMode { get; set; } = "sse";

    [DisplayName("流式增量字段路径")]
    [Description("SSE/NDJSON 流式响应中读取增量文本的字段路径。未命中时会尝试常见字段。")]
    public string StreamDeltaFieldPath { get; set; } = "choices.0.delta.content";

    public override string ToString() => "HTTP/AI 翻译设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class OverlaySettings
{
    [DisplayName("无译文时显示原文")]
    [Description("没有译文时，在覆盖层显示 OCR 原文。")]
    public bool ShowOriginalWhenNoTranslation { get; set; } = true;

    [DisplayName("背景不透明度")]
    [Description("覆盖文字背景不透明度，范围 0 到 1。")]
    public double Opacity { get; set; } = 0.5;

    [DisplayName("字体名称")]
    [Description("覆盖层文字字体。")]
    public string FontName { get; set; } = "Microsoft YaHei UI";

    [DisplayName("字体大小")]
    [Description("覆盖层文字字号。")]
    public float FontSize { get; set; } = 10;

    [DisplayName("文字颜色")]
    [Description("覆盖层文字颜色，使用 HTML 颜色格式。")]
    public string Foreground { get; set; } = "#FFFFFF";

    [DisplayName("背景颜色")]
    [Description("覆盖层背景颜色，使用 HTML 颜色格式。")]
    public string Background { get; set; } = "#202020";

    public override string ToString() => "覆盖层设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class PerformanceSettings
{
    [DisplayName("CPU 线程数")]
    [Description("OCR/翻译相关线程和底层库线程数建议值。")]
    public int CpuThreads { get; set; } = 2;

    [DisplayName("CPU 亲和性掩码")]
    [Description("进程 CPU 亲和性掩码，例如 0x3 表示 CPU 0 和 1；留空则不设置。")]
    public string CpuAffinityMask { get; set; } = "";

    [DisplayName("内存软上限 MB")]
    [Description("超过该内存软上限时尝试主动 GC；不是硬限制。")]
    public int MemorySoftLimitMb { get; set; } = 800;

    [DisplayName("启用翻译缓存")]
    [Description("相同文本再次出现时复用译文。")]
    public bool EnableTranslationCache { get; set; } = true;

    [DisplayName("OCR 未变化时跳过翻译")]
    [Description("OCR 文本未变化时复用上一轮译文。")]
    public bool SkipTranslationWhenOcrUnchanged { get; set; } = true;

    public override string ToString() => "性能设置";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class PluginSettings
{
    [DisplayName("启用插件")]
    [Description("启用后，程序会从插件目录加载用户插件 DLL。")]
    public bool Enabled { get; set; } = false;

    [DisplayName("插件目录")]
    [Description("插件 DLL 目录。相对路径会基于程序目录解析。")]
    public string Directory { get; set; } = "plugins";

    [DisplayName("要求插件清单")]
    [Description("启用后，插件 DLL 必须有同名 .plugin.json 清单才会加载。")]
    public bool RequireManifest { get; set; } = true;

    [DisplayName("已批准网络域名")]
    [Description("允许插件访问的域名，使用逗号、分号、空格或换行分隔，支持 *.example.com。")]
    public string ApprovedNetworkHosts { get; set; } = "";

    public override string ToString() => "插件设置";
}
