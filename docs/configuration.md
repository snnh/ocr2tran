# 配置说明

配置文件是 `appsettings.json`。开发默认文件位于 [src/Ocr2Tran/appsettings.json](../src/Ocr2Tran/appsettings.json)，发布后读取程序目录里的副本。

也可以从托盘菜单或控制面板点击“配置”打开图形化配置面板。点击“保存并应用”后会写回配置文件，并尝试立即应用 OCR、翻译、覆盖层、性能和热键设置。配置面板中的属性名和说明已本地化为中文；需要批量修改时仍可直接编辑 JSON。

## mode

- `startMinimizedToTray`：启动后只显示托盘。
- `autoIntervalMs`：自动 OCR/翻译循环间隔，最小限制为 `250` 毫秒。
- `showControlPanelOnStart`：启动时显示控制面板。

## hotkeys

默认值：

```json
{
  "singleOcr": "Ctrl+Alt+O",
  "singleOcrTranslate": "Ctrl+Alt+T",
  "regionOcr": "Ctrl+Alt+R",
  "regionOcrTranslate": "Ctrl+Alt+Y",
  "toggleAutoOcr": "Ctrl+Alt+A",
  "toggleAutoTranslate": "Ctrl+Alt+S",
  "toggleAutoRegionTranslate": "Ctrl+Alt+U",
  "clearOverlay": "Ctrl+Alt+C",
  "exit": "Ctrl+Alt+Q"
}
```

支持 `Ctrl`、`Alt`、`Shift`、`Win` 加普通按键。热键被其他程序占用时，程序会跳过该热键并继续运行；可以在图形化配置面板中更换冲突热键。

## ocr

`provider` 可选：

- `onnx`：内置 ONNX Runtime 后端。
- `paddleCli`：外部 PaddleOCR CLI 后端。
- 其他值：`noop` 调试 OCR。

`skipIfScreenshotUnchanged` 为 `true` 时，截图未变化会复用上一轮 OCR 结果。截图前程序会临时隐藏覆盖层，避免 OCR 识别到自己绘制的文字。

`postProcessing` 控制 OCR 后处理。后处理发生在翻译之前，因此被合并到同一 `TextRegion` 的文本会作为一次普通翻译请求发送给翻译接口。

- `enabled`：是否启用文本清洗。
- `removeControlCharacters`：删除不可见控制字符。
- `normalizeWhitespace`：把连续空白折叠为空格。
- `dropPunctuationOnly`：丢弃只包含标点、符号等噪声的区域。
- `dropShortIsolatedText`：丢弃没有邻近文本的短碎片，适合过滤界面控件里的零散误识别。
- `mergeNearbyTextRegions`：把同一行内距离较近的文本块合并后再翻译，减少逐词翻译和覆盖层碎片。
- `mergeNearbyLinesIntoBlocks`：把位置相邻且对齐的多行 OCR 文本合并为一个区域，同块文本会作为一次普通翻译请求发送给翻译接口。
- `dropOverlappingDuplicates`：丢弃位置高度重叠且文本相同的重复结果。
- `minMeaningfulCharacters`：保留区域所需的最少字母、数字或 CJK 等有效字符数。
- `minTextLength` / `minConfidence`：按文本长度和 OCR 置信度过滤。`minConfidence` 只作用于 OCR 后端提供了大于 `0` 置信度的结果，默认 `0.35`。
- `minRegionWidth` / `minRegionHeight` / `minRegionArea`：过滤过小文本框。
- `shortTextMaxLength`：短孤立文本判定长度，默认 `3`。
- `sameLineVerticalTolerancePx` / `sameLineMaxHorizontalGapPx`：同一行合并和邻近判定阈值。
- `sameBlockMaxVerticalGapPx`：相邻行合并为同一文本块的最大垂直间距。
- `duplicateOverlapRatio`：重复框重叠比例阈值。
- `maxRegions`：单轮最多保留区域数，`0` 表示不限制。
- `charactersToRemove`：无条件删除的字符列表，默认包含零宽字符和常见竖线噪声。

如果全屏 OCR 像 OBS、设置窗口这类复杂界面一样出现大量碎片，可以调高 `minConfidence`、`minRegionHeight`、`minRegionArea` 或调低 `maxRegions`。如果正文被误过滤，则反向调低这些值，或关闭 `dropShortIsolatedText`。

如果同一段文字被拆成多次翻译，保持 `mergeNearbyTextRegions` 与 `mergeNearbyLinesIntoBlocks` 开启，并适当调高 `sameBlockMaxVerticalGapPx`。如果不同段落、双列文本或表格内容被错误合并，则调低 `sameBlockMaxVerticalGapPx`，或关闭 `mergeNearbyLinesIntoBlocks`。

`imagePreprocessing` 控制送入 OCR 前的图像增强：

- `enabled`：是否启用预处理。
- `scale`：OCR 输入放大倍率，默认 `2`。小字识别差时可调高，CPU 占用高时可调低。
- `maxLongSide`：预处理后最长边上限，避免全屏截图过大。
- `grayscale`：转为灰度，减少彩色背景干扰。
- `contrast` / `brightness`：对比度和亮度调整。

预处理会保留原始屏幕坐标映射。OCR 在放大后的图像上运行，输出框会映射回真实屏幕区域。

### ONNX Runtime

```json
{
  "provider": "onnx",
  "onnx": {
    "modelRoot": "",
    "detectionModelPath": "",
    "recognitionModelPath": "",
    "classificationModelPath": "",
    "recCharDictPath": "",
    "detLimitSideLen": 2048,
    "detThreshold": 0.2,
    "boxThreshold": 0.35,
    "unclipRatio": 1.8,
    "minBoxSize": 6,
    "recImageHeight": 48,
    "recImageWidth": 480,
    "intraOpNumThreads": 2
  }
}
```

- `modelRoot`：用户导入的 ONNX 模型总目录。
- `detectionModelPath`：文本检测 ONNX 模型。
- `recognitionModelPath`：文字识别 ONNX 模型。
- `classificationModelPath`：方向分类 ONNX 模型，当前保留配置。
- `recCharDictPath`：识别字典。字典已用 `character` metadata 集成到 `rec.onnx` 时可留空，程序会按换行分隔读取字符表，并保留空白字符以避免类别索引错位。显式配置外置字典时，优先使用外置字典。
- `detLimitSideLen`：检测输入长边限制。小字漏识别时可调高，性能不足时调低。
- `detThreshold` / `boxThreshold`：检测阈值。漏识别时可降低，误识别或噪声多时可提高。
- `unclipRatio`：检测框外扩比例。
- `minBoxSize`：过滤过小文本框。
- `recImageHeight` / `recImageWidth`：识别输入尺寸 fallback。
- `intraOpNumThreads`：ONNX Runtime CPU 线程数。

### PaddleOCR CLI

```json
{
  "provider": "paddleCli",
  "paddle": {
    "executable": "paddleocr.exe",
    "argumentsTemplate": "--image_dir \"{image}\" --use_angle_cls {useAngleCls} --lang {lang} --type ocr {modelArgs}",
    "timeoutMs": 15000,
    "useImportedModels": false,
    "modelRoot": "",
    "detectionModelDir": "",
    "recognitionModelDir": "",
    "classificationModelDir": "",
    "recCharDictPath": "",
    "language": "ch",
    "useAngleCls": true
  }
}
```

- `executable`：`paddleocr.exe` 或兼容包装程序。
- `argumentsTemplate`：启动参数模板。
- `{image}`：临时截图 PNG。
- `{modelArgs}`：导入模型后自动展开的 `--det_model_dir`、`--rec_model_dir` 等参数。
- `timeoutMs`：OCR 子进程超时。
- `useImportedModels`：是否使用导入的 Paddle 推理模型目录。
- `detectionModelDir` / `recognitionModelDir` / `classificationModelDir`：PPOCR 模型目录。
- `recCharDictPath`：识别字典。

## translation

`provider` 可选：

- `noop`：返回原文，用于调试。
- `baidu`：百度翻译。
- `google`：Google Cloud Translation。
- `http` 或 `ai`：通用 HTTP/AI 翻译接口。

通用字段：

- `sourceLanguage`：源语言，默认 `auto`。
- `targetLanguage`：目标语言，默认 `zh`。
- `rps`：翻译请求速率限制。
- `timeoutMs`：单次翻译超时。

翻译层不会再把多个区域包装成带标记的批量请求。需要上下文时，应通过 `ocr.postProcessing.mergeNearbyLinesIntoBlocks` 先把同一块 OCR 文本合成一个区域，再由翻译后端正常翻译该块文本。这样对百度、Google、HTTP/AI 和插件翻译器都保持一致。

### HTTP/AI 翻译

- `endpoint`：请求地址。
- `method`：HTTP 方法，通常是 `POST`。
- `apiKey`：HTTP/AI 翻译接口 API Key。非空时会作为 `Authorization: Bearer <apiKey>` 发送；如果 `headersJson` 中已配置 `Authorization`，则使用 `headersJson` 的值。
- `headersJson`：请求头 JSON 对象。配置文件中可直接写 JSON 对象；旧版转义字符串仍兼容。
- `bodyContentType`：请求体 `Content-Type`，默认 `application/json`。如果 `headersJson` 中配置了 `Content-Type`，会优先使用请求头里的值。
- `bodyTemplate`：请求体模板。配置文件中可直接写 JSON 对象；旧版转义字符串仍兼容。
- `prompt`：AI 翻译提示词。
- `responseFieldPath`：非流式 JSON 响应字段路径。
- `streamMode`：`none`、`sse` 或 `ndjson`，默认 `sse`。
- `streamDeltaFieldPath`：流式响应增量字段路径。未命中时会依次尝试 `responseFieldPath`、`response`、`text`、`content`。

模板变量：

- `{text}` / `{prompt}` / `{source}` / `{target}` / `{apiKey}`：按 JSON 字符串规则转义。
- `{textRaw}`、`{promptRaw}` 等 `Raw` 后缀：原文输出。
- `{textUrl}`、`{apiKeyUrl}` 等 `Url` 后缀：URL 编码输出。

OpenAI 兼容流式接口示例：

```json
{
  "apiKey": "",
  "headersJson": {
    "Content-Type": "application/json"
  },
  "bodyContentType": "application/json",
  "bodyTemplate": {
    "model": "local",
    "messages": [
      {
        "role": "system",
        "content": "{prompt}"
      },
      {
        "role": "user",
        "content": "{text}"
      }
    ],
    "stream": true
  }
}
```

表单接口示例：

```json
{
  "headersJson": {
    "Content-Type": "application/x-www-form-urlencoded"
  },
  "bodyContentType": "application/x-www-form-urlencoded",
  "bodyTemplate": "q={textUrl}&from={sourceUrl}&to={targetUrl}",
  "streamMode": "none",
  "responseFieldPath": "data.translation"
}
```

字段路径使用点号和数组下标，例如 `choices.0.message.content`。

## overlay

- `showOriginalWhenNoTranslation`：没有译文时显示 OCR 原文。
- `opacity`：覆盖文字背景不透明度，范围 `0` 到 `1`。`0` 为完全透明，`1` 为完全不透明，中间值会通过 Windows layered window 做真实半透明合成。
- `fontName` / `fontSize`：覆盖文字字体。
- `foreground` / `background`：文字和背景色，使用 HTML 颜色格式。

颜色示例：

```json
{
  "foreground": "#FFFFFF",
  "background": "#202020",
  "opacity": 0.65
}
```

`background` 只负责颜色，透明度由 `opacity` 控制。建议不要在 HTML 颜色里再写 alpha，避免调试时混淆。

## performance

- `cpuThreads`：默认 `2`。
- `cpuAffinityMask`：CPU 亲和性掩码，例如 `0x3` 表示 CPU 0 和 CPU 1。
- `memorySoftLimitMb`：内存软上限，超过后触发主动 GC。
- `enableTranslationCache`：缓存翻译结果。
- `skipTranslationWhenOcrUnchanged`：OCR 文本未变化时复用上一轮译文。

## plugins

- `enabled`：是否加载插件。
- `directory`：插件 DLL 目录。相对路径基于程序目录解析，默认 `plugins`。
- `requireManifest`：要求每个插件 DLL 配套同名 `.plugin.json` 清单，默认 `true`。
- `approvedNetworkHosts`：用户批准插件访问的域名列表，使用逗号、分号、空格或换行分隔，支持 `*.example.com`。

插件可实现 OCR 服务、翻译服务、图片处理或文本处理接口。OCR/翻译服务插件通过把 `ocr.provider` 或 `translation.provider` 设置为插件 `Name`、类型名或完整类型名启用。插件声明的网络域名必须出现在 `approvedNetworkHosts` 中才会加载。更多说明见 [插件开发](plugins.md)。
