# OCR 和翻译接入

## OCR 后端选择

推荐优先使用 `onnx` 后端。它随应用内置 ONNX Runtime，不依赖 Python 或 `paddleocr.exe`。

仍保留 `paddleCli` 后端，用于已经有 PaddleOCR 环境或直接使用 Paddle 推理模型目录的场景。

## ONNX Runtime

控制面板入口：“导入 ONNX 模型”。

推荐目录结构：

```text
ONNX-OCR/
  det.onnx
  rec.onnx
  ppocr_keys_v1.txt   # 可选；rec.onnx 已内置 character metadata 时不需要
```

导入器会递归扫描目录：

- 文件名或目录名包含 `det`：检测模型
- 文件名或目录名包含 `rec`：识别模型
- 文件名或目录名包含 `cls`、`angle`、`textline`：方向分类模型
- `.txt` 文件名包含 `keys`、`dict`、`ppocr`：识别字典

导入成功后会自动设置：

```json
{
  "ocr": {
    "provider": "onnx"
  }
}
```

当前 ONNX 后端实现 DB 风格检测和 CTC 识别。检测框为轴对齐矩形；方向分类模型目前只保存路径，尚未参与推理。

### ONNX 内置字典

识别字典可以直接写入 `rec.onnx` 的 metadata。推荐 metadata key 使用 `character`，value 使用换行分隔字符表：

```python
from pathlib import Path
from typing import Union

import onnx


def read_txt(path: Union[str, Path]) -> list[str]:
    with open(path, "r", encoding="utf-8") as f:
        return [line.rstrip("\n") for line in f]


model = onnx.load_model("rec.onnx")
meta = model.metadata_props.add()
meta.key = "character"
meta.value = "\n".join(read_txt("ppocr_keys_v1.txt"))
onnx.save_model(model, "rec-with-dict.onnx")
```

程序读取内置字典时会保留空行、空格和制表符等字符项，以保证识别模型输出类别和字符表索引一致。外置 `recCharDictPath` 不为空时会优先使用外置字典。

如果检测框正常但识别文本大面积乱码，优先检查识别模型和字典是否来自同一个模型包，或 `character` metadata 是否被错误 trim、排序、去重。

## PaddleOCR CLI

控制面板入口：“导入 PPOCR 模型”。

该模式需要 `paddleocr.exe` 或兼容包装程序。导入目录中如果包含 `paddleocr.exe`，程序会自动使用它；否则使用 PATH 中的 `paddleocr.exe`。

执行形式：

```text
{ocr.paddle.executable} {ocr.paddle.argumentsTemplate}
```

默认模板：

```text
--image_dir "{image}" --use_angle_cls {useAngleCls} --lang {lang} --type ocr {modelArgs}
```

`{modelArgs}` 会展开为：

```text
--det_model_dir "..." --rec_model_dir "..." --cls_model_dir "..." --rec_char_dict_path "..."
```

如果使用兼容包装程序，推荐输出 JSON 数组：

```json
[
  {
    "text": "hello",
    "confidence": 0.98,
    "box": [10, 20, 120, 42]
  }
]
```

`box` 支持 `[x,y,w,h]` 或四点坐标数组。非 JSON 输出会按行显示，主要用于调试。

## 翻译后端

### noop

返回原文，用于验证 OCR 和覆盖层。

### 百度翻译

需要配置 `appId` 和 `secret`。签名使用百度翻译标准的 `appid + q + salt + secret` MD5。

### Google Cloud Translation

支持 `apiKey` 或 `bearerToken`。

### 通用 HTTP/AI

适配本地模型服务、OpenAI 兼容接口或其他 HTTP 翻译 API。

请求由这些字段组成：

- `endpoint` / `method`：请求地址和方法。
- `apiKey`：默认作为 `Authorization: Bearer <apiKey>` 发送；需要其他认证格式时，在 `headersJson` 中手动配置请求头。
- `headersJson`：请求头 JSON 对象，可在配置文件中直接写对象。
- `bodyContentType`：请求体类型，默认 `application/json`；`headersJson.Content-Type` 优先。
- `bodyTemplate`：完整请求体模板，可直接写 JSON 对象，也可写表单字符串。

模板变量：

- `{text}`：当前 OCR 区域文本。
- `{prompt}`：配置中的提示词。
- `{source}`：源语言。
- `{target}`：目标语言。
- `{apiKey}`：HTTP/AI 配置中的 API Key，适合需要把 key 放在请求体中的接口。

变量默认按 JSON 字符串规则转义。表单或纯文本请求可使用 `Raw` / `Url` 后缀，例如 `{textRaw}`、`{textUrl}`、`{apiKeyUrl}`。

普通响应通过 `responseFieldPath` 提取，例如：

```text
choices.0.message.content
```

流式响应默认使用 SSE：

- `streamMode = sse`：读取 `data:` 行，支持多行 SSE event，遇到 `[DONE]` 结束。
- `streamMode = ndjson`：逐行读取 JSON。
- `streamDeltaFieldPath`：每个增量片段的文本字段路径。未命中时会依次尝试 `responseFieldPath`、`response`、`text`、`content`。

OpenAI 兼容接口通常需要在 `bodyTemplate` 中设置 `"stream": true`，并保持 `streamMode` 为 `sse`。

## 插件

插件可替换 OCR 服务、翻译服务，也可扩展图片处理和 OCR 文本处理。插件 DLL 放入 `plugins` 目录后，通过 `ocr.provider` 或 `translation.provider` 选择服务插件；图片和文本处理插件会按 `Order` 顺序自动执行。插件必须用清单声明所需访问的网站，且域名经用户配置批准后才会加载。详见 [插件开发](plugins.md)。
