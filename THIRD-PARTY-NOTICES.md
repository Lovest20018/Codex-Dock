# Third-party notices

## Superba (catalog data, translations and design reference)

Source: https://github.com/krill-lab/superba

Reviewed revision: `f78d77a2849aceb8dffc1f27a83869ffceab7e90` (2026-10-27).

Superba is licensed under MIT OR Apache-2.0. This project initially imported configuration metadata, curated grouping and Chinese translations from its Codex 0.164.0 catalog under the MIT option. The data is now independently maintained in `config/config-schema.json`; no upstream checkout or generator is required. Key-preserving editing, backup/rollback and PAT routing were independently implemented with its design as a reference. No Superba Rust crates or brand assets are linked or distributed. The MIT notice remains in `third_party/Superba-LICENSE.txt`.

## Tomlyn 0.19.0

Copyright Alexandre Mutel. BSD-2-Clause.

Source: https://github.com/xoofx/Tomlyn/tree/0.19.0

The full notice is in `third_party/Tomlyn-LICENSE.txt`.

## .NET 8 and System.Security.Cryptography.ProtectedData 8.0.0

Copyright .NET Foundation and contributors / Microsoft. MIT.

Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

The Windows self-contained release includes runtime license and third-party notices under `third_party/`.

## Inno Setup simplified Chinese messages

`third_party/ChineseSimplified.isl` is adapted from the user-contributed Chinese Simplified language file from the Inno Setup repository, fixed at tag `is-6_5_4`:
https://github.com/jrsoftware/issrc/blob/is-6_5_4/Files/Languages/Unofficial/ChineseSimplified.isl

The file retains its translator attribution and source comments. The unmodified upstream file SHA-256 is `fc1dbf303559cfefd47cf59eec7f1ad1c5fc6266ef8fa64569c13bc754adfeb4`. Local changes remove four obsolete message names and translate the additional Inno Setup 6.7 messages.

