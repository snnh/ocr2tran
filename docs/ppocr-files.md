# PPOCR/ONNX 文件准备指南

`ocr2tran` 支持两种 OCR 模型导入方式：

- **推荐：ONNX 模型目录**  
  使用内置 ONNX Runtime，不需要 Python，不需要 `paddleocr.exe`。
- **兼容：PaddleOCR/PPOCR 推理模型目录**  
  适合已有 PaddleOCR 环境的用户，需要本机能运行 `paddleocr.exe`。

## 推荐方式：ONNX 模型

控制面板入口：**导入 ONNX 模型**。

最小目录，适用于 `rec.onnx` 已内置 `character` 字典的情况：

```text
ONNX-OCR/
  det.onnx
  rec.onnx
```

如果 `rec.onnx` 没有内置字典，则加入匹配的外置字典：

```text
ONNX-OCR/
  det.onnx
  rec.onnx
  ppocr_keys_v1.txt
```

方向分类模型可放在同一目录，当前只保存路径，暂不参与旋转判定：

```text
ONNX-OCR/
  det.onnx
  rec.onnx
  cls.onnx
```

导入器会递归扫描所选目录：

- 文件名或目录名包含 `det`：文本检测模型。
- 文件名或目录名包含 `rec`：文本识别模型。
- 文件名或目录名包含 `cls`、`angle`、`textline`：方向分类模型。
- `.txt` 文件名包含 `keys`、`dict`、`ppocr`：识别字典。

使用内置字典时，导入成功后配置会类似：

```json
{
  "ocr": {
    "provider": "onnx",
    "onnx": {
      "modelRoot": "D:\\models\\ONNX-OCR",
      "detectionModelPath": "D:\\models\\ONNX-OCR\\det.onnx",
      "recognitionModelPath": "D:\\models\\ONNX-OCR\\rec.onnx",
      "classificationModelPath": "D:\\models\\ONNX-OCR\\cls.onnx",
      "recCharDictPath": ""
    }
  }
}
```

当前 ONNX 后端使用检测模型 + 识别模型即可工作。方向分类模型路径会被保存，但旋转判定尚未接入。`recCharDictPath` 为空时，程序会尝试从 `rec.onnx` 的 metadata 中读取 `character` 字典。

如果目录里存在匹配的 `.txt` 字典文件，导入器会自动填入 `recCharDictPath`，程序会优先使用外置字典：

```json
{
  "ocr": {
    "provider": "onnx",
    "onnx": {
      "recognitionModelPath": "D:\\models\\ONNX-OCR\\rec.onnx",
      "recCharDictPath": "D:\\models\\ONNX-OCR\\ppocr_keys_v1.txt"
    }
  }
}
```

## 如何得到 ONNX 模型

PaddleOCR 官方发布的多数模型是 Paddle 推理格式。要使用内置 ONNX 后端，需要先把模型导出为 ONNX，或直接使用别人已经导出的 PP-OCR ONNX 模型。

常见做法：

1. 下载 Paddle 推理模型。
2. 使用 Paddle2ONNX 导出检测模型和识别模型。
3. 把导出的模型命名为 `det.onnx`、`rec.onnx`。
4. 准备与识别模型匹配的字典文件，或把字典写入 `rec.onnx` 的 `character` metadata。

导出工具：

- Paddle2ONNX：https://github.com/PaddlePaddle/Paddle2ONNX
- PaddleOCR 官方模型列表见本文末尾“官方来源”。

导出后建议统一整理为：

```text
ONNX-OCR/
  det.onnx
  rec.onnx
  ppocr_keys_v1.txt   # 可选
```

## 把字典写入 ONNX

推荐把识别字典写入识别模型的 metadata，减少发布时漏带字典文件的概率。程序支持读取 key 为 `character` 的 metadata，value 按换行分隔：

```python
from pathlib import Path
from typing import Union

import onnx
import onnxruntime as ort


def read_txt(path: Union[str, Path]) -> list[str]:
    with open(path, "r", encoding="utf-8") as f:
        return [line.rstrip("\n") for line in f]


dicts = read_txt("ppocrv5_dict.txt")
model_path = "rec.onnx"

model = onnx.load_model(model_path)
meta = model.metadata_props.add()
meta.key = "character"
meta.value = "\n".join(dicts)

new_model_path = Path(model_path).with_name("rec-with-dict.onnx")
onnx.save_model(model, new_model_path)

sess = ort.InferenceSession(str(new_model_path))
chars = sess.get_modelmeta().custom_metadata_map["character"].split("\n")
print(len(chars), chars[:5])
```

注意事项：

- 不要对字典执行 trim、排序、去重或删除空行。空字符串或空白字符可能是模型类别表的一部分。
- `rec.onnx` 的输出类别数通常和字典长度存在 `blank` 差异。程序会自动兼容常见 CTC blank 位置，但字典仍必须与识别模型匹配。
- 如果配置了外置 `recCharDictPath`，程序会优先使用外置字典，而不是 ONNX metadata。

## 兼容方式：PaddleOCR/PPOCR 模型

控制面板入口：**导入 PPOCR 模型**。

该模式仍通过 `paddleocr.exe` 执行推理。导入模型只是自动配置模型路径，不会替代 PaddleOCR 运行时。

推荐目录：

```text
PPOCR/
  paddleocr.exe                  # 可选；没有则使用 PATH 中的 paddleocr.exe
  ppocr_keys_v1.txt
  PP-OCRv4_mobile_det_infer/
    inference.pdmodel
    inference.pdiparams
    inference.yml
  PP-OCRv4_mobile_rec_infer/
    inference.pdmodel
    inference.pdiparams
    inference.yml
  PP-LCNet_x0_25_textline_ori_infer/
    inference.pdmodel
    inference.pdiparams
    inference.yml
```

导入器会识别：

- `inference.pdmodel` + `inference.pdiparams`
- 或 `model.pdmodel` + `model.pdiparams`
- 或 `inference.json`

目录名建议包含 `det`、`rec`、`cls` 等关键词，便于自动匹配。

## 安装 PaddleOCR 运行时

只在使用 **导入 PPOCR 模型** 时需要。

CPU 版示例：

```powershell
python -m pip install paddlepaddle==3.2.0 -i https://www.paddlepaddle.org.cn/packages/stable/cpu/
python -m pip install paddleocr
python -c "import paddle; print(paddle.__version__)"
paddleocr --help
```

如果 `paddleocr --help` 可以正常输出，说明运行时可用。若 `paddleocr.exe` 不在 PATH，可以把 Python 环境的 `Scripts` 目录加入 PATH，或把 `paddleocr.exe` 放进 `PPOCR/` 总目录。

## 下载 Paddle 推理模型

以下示例下载 PP-OCRv4 mobile 组合，适合 CPU 和常驻工具场景。

```powershell
New-Item -ItemType Directory -Force .\PPOCR | Out-Null

Invoke-WebRequest https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv4_mobile_det_infer.tar -OutFile .\PPOCR\PP-OCRv4_mobile_det_infer.tar
Invoke-WebRequest https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv4_mobile_rec_infer.tar -OutFile .\PPOCR\PP-OCRv4_mobile_rec_infer.tar
Invoke-WebRequest https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-LCNet_x0_25_textline_ori_infer.tar -OutFile .\PPOCR\PP-LCNet_x0_25_textline_ori_infer.tar
Invoke-WebRequest https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/release/2.7/ppocr/utils/ppocr_keys_v1.txt -OutFile .\PPOCR\ppocr_keys_v1.txt

tar -xf .\PPOCR\PP-OCRv4_mobile_det_infer.tar -C .\PPOCR
tar -xf .\PPOCR\PP-OCRv4_mobile_rec_infer.tar -C .\PPOCR
tar -xf .\PPOCR\PP-LCNet_x0_25_textline_ori_infer.tar -C .\PPOCR
```

更高准确率但更重的 server 模型：

```powershell
Invoke-WebRequest https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv4_server_det_infer.tar -OutFile .\PPOCR\PP-OCRv4_server_det_infer.tar
Invoke-WebRequest https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv4_server_rec_infer.tar -OutFile .\PPOCR\PP-OCRv4_server_rec_infer.tar
```

PP-OCRv5 示例：

```powershell
Invoke-WebRequest https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_det_infer.tar -OutFile .\PPOCR\PP-OCRv5_mobile_det_infer.tar
Invoke-WebRequest https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_rec_infer.tar -OutFile .\PPOCR\PP-OCRv5_mobile_rec_infer.tar
```

## 字典文件

识别模型必须和字典匹配。字典不匹配会导致乱码、缺字或识别结果为空。典型现象是文本框位置正确，但识别文本变成无意义字符。

常见选择：

- 中文/中英：`ppocr_keys_v1.txt`
- 英文：`en_dict.txt`
- 日文、韩文、其他语言：使用对应模型包或官方文档提供的字典

中文字典获取：

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/release/2.7/ppocr/utils/ppocr_keys_v1.txt -OutFile .\ppocr_keys_v1.txt
```

## 导入后检查

ONNX 模型：

```json
{
  "provider": "onnx",
  "onnx": {
    "detectionModelPath": "D:\\models\\ONNX-OCR\\det.onnx",
    "recognitionModelPath": "D:\\models\\ONNX-OCR\\rec.onnx",
    "recCharDictPath": "D:\\models\\ONNX-OCR\\ppocr_keys_v1.txt"
  }
}
```

Paddle 模型：

```json
{
  "provider": "paddleCli",
  "paddle": {
    "useImportedModels": true,
    "detectionModelDir": "D:\\models\\PPOCR\\PP-OCRv4_mobile_det_infer",
    "recognitionModelDir": "D:\\models\\PPOCR\\PP-OCRv4_mobile_rec_infer",
    "recCharDictPath": "D:\\models\\PPOCR\\ppocr_keys_v1.txt"
  }
}
```

然后点击“测试 OCR”或按 `Ctrl+Alt+O`。

## 常见问题

### 我只想导入模型，不想安装 Python/PaddleOCR

使用 ONNX 路线。准备 `det.onnx`、`rec.onnx` 和字典，然后点“导入 ONNX 模型”。

### 导入 PPOCR 模型后仍提示找不到 paddleocr.exe

Paddle 模型路线需要 PaddleOCR 运行时。安装 PaddleOCR，或把 `paddleocr.exe` 放进导入目录。

### 导入器找不到模型

检查文件名或目录名是否包含 `det`、`rec`、`cls` 等关键词。ONNX 模型建议直接命名为 `det.onnx` 和 `rec.onnx`。

### 识别结果乱码或缺字

检查识别模型和字典是否匹配。中文模型优先使用模型包自带字典；如果使用 ONNX 内置字典，确认 metadata key 是 `character`，并且读取后字符数量和原始字典一致。

## 官方来源

- PaddleOCR 安装文档：https://www.paddleocr.ai/v3.3.0/en/version3.x/installation.html
- PaddleOCR 文本检测模型列表：https://www.paddleocr.ai/latest/en/version3.x/module_usage/text_detection.html
- PaddleOCR 文本识别模型列表：https://www.paddleocr.ai/latest/en/version3.x/module_usage/text_recognition.html
- PaddleOCR 文本行方向分类模型列表：https://www.paddleocr.ai/latest/en/version3.x/module_usage/textline_orientation_classification.html
- PaddleOCR GitHub 字典文件：https://github.com/PaddlePaddle/PaddleOCR/tree/main/ppocr/utils
- Paddle2ONNX：https://github.com/PaddlePaddle/Paddle2ONNX
