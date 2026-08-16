# Third-Party Notices

VoiceCtrl is licensed under the MIT License (see `LICENSE`).
Offline mode downloads and runs the following third-party components, which
carry their own license terms.

## Parakeet-TDT-0.6B-v2 (offline speech recognition model)

- **License:** [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/)
- **Original model:** [NVIDIA Parakeet-TDT-0.6B-v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2)
- **Distributed files:** the INT8 ONNX conversion at
  [csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8](https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8)
  (format conversion only; no change to model weights or behavior)

Not bundled with this repository or its releases. Downloaded once, on first
use of Offline mode, directly from Hugging Face to
`%LocalAppData%\VoiceCtrl\models\`.

## sherpa-onnx (offline inference runtime)

- **License:** [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- **Project:** [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
- **Distributed via:** the `org.k2fsa.sherpa.onnx` and
  `org.k2fsa.sherpa.onnx.runtime.win-x64` NuGet packages, unmodified

A copy of the Apache License 2.0 is available at the link above.
