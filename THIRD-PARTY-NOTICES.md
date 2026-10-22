# Third-Party Notices

VoiceCtrl is licensed under the MIT License (see `LICENSE`).
It bundles, or downloads on first use, the following third-party components,
which carry their own license terms.

## Parakeet-TDT-0.6B-v2 (offline speech recognition model)

- **License:** [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/)
- **Original model:** [NVIDIA Parakeet-TDT-0.6B-v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2)
- **Distributed files:** the INT8 ONNX conversion at
  [csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8](https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8)
  (format conversion only; no change to model weights or behavior)

Not bundled with this repository or its releases. Downloaded once, on first
use of Offline mode, directly from Hugging Face to
`%LocalAppData%\VoiceCtrl\models\`.

## Parakeet-TDT-0.6B-v3 (offline speech recognition, European language pack)

- **License:** [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/)
- **Original model:** [NVIDIA Parakeet-TDT-0.6B-v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3)
- **Distributed files:** the INT8 ONNX conversion at
  [csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8](https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8)
  (format conversion only; no change to model weights or behavior)

Not bundled. Downloaded only if the user picks the European pack in Settings, directly from
Hugging Face to `%LocalAppData%\VoiceCtrl\models\`.

## Whisper large-v3-turbo (offline speech recognition, All languages pack)

- **License:** [MIT](https://github.com/openai/whisper/blob/main/LICENSE), Copyright (c) 2022 OpenAI
- **Original model:** [openai/whisper-large-v3-turbo](https://huggingface.co/openai/whisper-large-v3-turbo)
- **Distributed files:** the INT8 ONNX conversion at
  [csukuangfj/sherpa-onnx-whisper-turbo](https://huggingface.co/csukuangfj/sherpa-onnx-whisper-turbo)
  (format conversion and quantisation only)

Not bundled. Downloaded only if the user picks the All languages pack in Settings, directly from
Hugging Face to `%LocalAppData%\VoiceCtrl\models\`.

## sherpa-onnx (offline inference runtime)

- **License:** [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- **Project:** [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
- **Distributed via:** the `org.k2fsa.sherpa.onnx` and
  `org.k2fsa.sherpa.onnx.runtime.win-x64` NuGet packages, unmodified

A copy of the Apache License 2.0 is available at the link above. The runtime package includes
[ONNX Runtime](https://github.com/microsoft/onnxruntime) (MIT, Copyright (c) Microsoft Corporation).

## Qwen3 language models (Smart cleanup)

- **License:** [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- **Original models:** [Qwen3-1.7B](https://huggingface.co/Qwen/Qwen3-1.7B) and
  [Qwen3-4B-Instruct-2507](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507) by the Qwen team, Alibaba Cloud
- **Distributed files:** the Q4_K_M GGUF quantisations at
  [unsloth/Qwen3-1.7B-GGUF](https://huggingface.co/unsloth/Qwen3-1.7B-GGUF) and
  [unsloth/Qwen3-4B-Instruct-2507-GGUF](https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF)
  (format conversion and quantisation only), each pinned to an exact revision and checked against a SHA-256

Not bundled with this repository or its releases. One of them is downloaded when
Smart cleanup is set up, directly from Hugging Face to `%LocalAppData%\VoiceCtrl\models\`.

## llama.cpp (runs the Smart cleanup language model)

- **License:** [MIT](https://github.com/ggml-org/llama.cpp/blob/master/LICENSE)
- **Project:** [ggml-org/llama.cpp](https://github.com/ggml-org/llama.cpp), release b11429 (Windows CPU build)
- **Distributed files:** the unmodified `llama-server` release archive, downloaded together with the
  language model and checked against a SHA-256. It runs as a separate process, listening on this PC only.

## Sora, Public Sans and Newsreader (interface typefaces)

- **License:** [SIL Open Font License 1.1](https://openfontlicense.org)
- **Sora:** Copyright 2019 The Sora Project Authors ([sora-xor/sora-font](https://github.com/sora-xor/sora-font))
- **Public Sans:** Copyright 2015 The Public Sans Project Authors ([uswds/public-sans](https://github.com/uswds/public-sans))
- **Newsreader:** Copyright 2020 The Newsreader Project Authors ([productiontype/Newsreader](https://github.com/productiontype/Newsreader))
- **Distributed files:** the variable-weight Latin and Latin Extended WOFF2
  subsets published by [Fontsource](https://fontsource.org) (format and subset
  conversion only), bundled with the app in `ui/`.

The full license text for each face ships next to the font files, in
`ui/src/design/fonts/*-OFL.txt`.

## React and React DOM (user interface library)

- **License:** [MIT](https://github.com/facebook/react/blob/main/LICENSE)
- **Project:** [facebook/react](https://github.com/facebook/react), Copyright (c) Meta Platforms, Inc. and affiliates
- **Distributed via:** the `react` and `react-dom` npm packages, bundled into
  the app's interface pages, unmodified

## Microsoft Edge WebView2 SDK (window host for the interface pages)

- **License:** BSD-3-Clause-style, Copyright (C) Microsoft Corporation. The full text ships in the
  `Microsoft.Web.WebView2` NuGet package (`LICENSE.txt`) and is reproduced below.
- **Project:** [MicrosoftEdge/WebView2Feedback](https://github.com/MicrosoftEdge/WebView2Feedback)
- **Distributed via:** the `Microsoft.Web.WebView2` NuGet package; its managed and loader DLLs
  ship next to the app, unmodified. The WebView2 Runtime itself is part of Windows or installed
  by Microsoft's Evergreen bootstrapper, not redistributed by VoiceCtrl.

```
Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors 
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Velopack (installer and updates)

- **License:** [MIT](https://github.com/velopack/velopack/blob/develop/LICENSE), Copyright (c) Velopack Ltd.
- **Project:** [velopack/velopack](https://github.com/velopack/velopack)
- **Distributed via:** the `Velopack` NuGet package inside the app, and the `vpk` tool that builds
  `VoiceCtrl-Setup.exe` and the update packages, unmodified

## NAudio and NAudio.Lame (microphone capture, audio encoding)

- **License:** [MIT](https://github.com/naudio/NAudio/blob/master/license.txt), Copyright (c) Mark Heath
  and contributors; [MIT](https://github.com/Corey-M/NAudio.Lame/blob/master/LICENSE.txt), Copyright (c)
  Corey Murtagh
- **Distributed via:** the `NAudio` and `NAudio.Lame` NuGet packages, unmodified
- NAudio.Lame includes the [LAME](https://lame.sourceforge.io) MP3 encoder library (`libmp3lame`),
  licensed under the [GNU LGPL 2.0](https://www.gnu.org/licenses/old-licenses/lgpl-2.0.html). Its source
  is available from the LAME project. It is used only to compress a recording before it is sent to
  Gemini Online.

## Bouncy Castle (update signature check)

- **License:** [MIT](https://www.bouncycastle.org/about/license/), Copyright (c) 2000-2026 The Legion of the
  Bouncy Castle Inc.
- **Distributed via:** the `BouncyCastle.Cryptography` NuGet package, unmodified

## .NET runtime

- **License:** [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), Copyright (c) .NET Foundation
  and Contributors
- **Distributed via:** the self-contained .NET 8 runtime and the
  `System.Security.Cryptography.ProtectedData` package, published inside `VoiceCtrl.exe`, unmodified

## Built-in vocabulary (names, places, brands and terms; ordinary-word list)

VoiceCtrl ships two compressed word lists inside the app (`terms.txt.gz`, `everyday-words.txt.gz`). They
are built from the sources below by `tools/vocabulary/build_vocabulary.py`; only names and words are
kept, never the sources' other data. The lists are used on the user's PC to spell names the speech
model misheard and are never sent anywhere.

- **Wikidata** (labels of notable people's names, companies, brands, software, places, works and other
  items): [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/), no attribution required.
  [wikidata.org](https://www.wikidata.org)
- **Hand-written lists** (about 11,000 names and terms in `tools/vocabulary/curated/`, written for
  VoiceCtrl): released under CC0 1.0 with the rest of the data files.
- **GeoNames** (names of cities of 5,000 people or more, and countries): licensed under
  [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/). Source:
  [geonames.org](https://www.geonames.org). Changes: only the place names were extracted and
  de-duplicated.
- **OurAirports** (airport names): public domain. [ourairports.com](https://ourairports.com/data/)
- **U.S. Census Bureau, Frequently Occurring Surnames from the 2010 Census:** a work of the U.S.
  government. Names were re-capitalised.
- **SCOWL** (the list of ordinary English words, size 70, US spelling, via the Hunspell `en_US-large`
  dictionary 2026.02.25; suffix and prefix rules were expanded into words):

  ```
  Copyright 2000-2026 by Kevin Atkinson

  Permission to use, copy, modify, distribute, and sell any part of SCOWLv2, or
  word lists created from it, is hereby granted without fee, provided that the
  above copyright notice appears in all copies and that both the above
  copyright notice and this notice appear in supporting documentation.  Kevin
  Atkinson makes no representations about the suitability of this database for
  any purpose.  It is provided "as is" without express or implied warranty.
  ```

  SCOWL is derived from many sources, most in the public domain, and from the Corpus of Contemporary
  American English; the primary source of words is 12dicts and ENABLE2K, both in the public domain,
  credit to Alan Beale. The affix rules come from Geoff Kuenning's Ispell (BSD license).

## Launch video (README and release page only, not part of the app)

- **Music:** "Happy Beats & Business Moves Vol. 10" by Sascha Ende,
  [ende.app](https://ende.app/en/song/12875-happy-beats-business-moves-vol-10), licensed under
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Trimmed and faded for the video.
- **Sound effects:** [Kenney](https://kenney.nl) (CC0) and Keyboard Soundpack #1 by unicae_games (CC0).
