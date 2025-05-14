# BitzBuffer 設計仕様 - 概要

このドキュメントは、C# Buffer管理ライブラリ「BitzBuffer」の設計仕様の概要を示します。詳細な仕様は、関連する各ドキュメントファイルを参照してください。

## 1. ライブラリの目的とスコープ

*   **主要な目的:**
    *   画像処理、CAD/モデリング、機械学習用テンソル、FAプロトコル通信など、大量のデータを扱うアプリケーション向けの高性能なバッファ管理。
    *   マネージドメモリ、アンマネージドメモリ、およびGPUバッファ (OpenGL, Vulkanなど) を統一的なインターフェースで扱えるようにする（GPU対応は拡張性を重視し、別プロジェクト/DLLでの実装を想定）。
    *   メモリ効率の向上 (GC負荷軽減、LOH回避、メモリ断片化抑制、ゼロコピー操作の促進)。
    *   C#での実装方法の学習と処理の効率化の探求。
    *   Fluent Interfaceを考慮する。
*   **ターゲットフレームワーク:** .NET 6+ (一部機能は .NET 8+ も許容)
*   **主な機能:** バッファの確保、解放、プーリング、各種データ型への対応、各種メモリ種別への対応（拡張可能アーキテクチャ）、非連続メモリのサポート、所有権管理。
*   **関連プロジェクト:**
    *   **`BitzBuffer.Pipelines`**: 本ライブラリのバッファ管理機能を基盤とした、高レベルな非同期データ処理パイプライン機能の提供を目指すプロジェクト（またはモジュール）。`System.IO.Pipelines` に似た、より柔軟で高性能な代替手段の実現を検討します。（詳細は別途 `BitzBuffer.Pipelines` の設計仕様で定義）

## 2. アーキテクチャ概要

このライブラリは、以下の主要なコンポーネントで構成されます。

*   **`BufferManager`**: アプリケーション全体で `IBufferProvider` を登録・管理し、利用者に提供します。Fluent APIによる設定が可能です。デフォルトプロバイダ（マネージド/アンマネージド）を内包します。
    *   詳細は [プロバイダと実装クラス (`Docs/DesignSpecs/02_Providers_And_Buffers.md`)](Docs/DesignSpecs/02_Providers_And_Buffers.md) を参照してください。
*   **`IBufferProvider`**: 特定の技術領域（マネージド、アンマネージド、GPU等）のバッファに関する操作（プールからの貸し出し、直接生成）を提供します。プロバイダ固有の設定を持ちます。
    *   詳細は [プロバイダと実装クラス (`Docs/DesignSpecs/02_Providers_And_Buffers.md`)](Docs/DesignSpecs/02_Providers_And_Buffers.md) を参照してください。
*   **`IBufferFactory<TItem, TBuffer, TOptions>`**: `IBufferProvider` の内部で使用され、実際にバッファオブジェクト (`IBuffer<TItem>` の実装クラス) をインスタンス化します。基本的な方針は、`Create(TOptions options)` メソッドを持ち、`TOptions` で生成に必要な全情報を受け取ります。
    *   詳細は [プロバイダと実装クラス (`Docs/DesignSpecs/02_Providers_And_Buffers.md`)](Docs/DesignSpecs/02_Providers_And_Buffers.md) での `IBufferProvider` との連携部分を参照してください。
*   **`IBufferPool<TBuffer>` / `IBufferPoolStrategy<TBuffer, TResource, TItem>`**: バッファのプーリング機構です。戦略はカスタマイズ可能です。`ArrayPool<T>` のコンセプトを参考に、独自実装を行います。
    *   詳細は [プーリング (`Docs/DesignSpecs/03_Pooling.md`)](Docs/DesignSpecs/03_Pooling.md) を参照してください。
*   **`IBufferLifecycleHooks<TBuffer, TItem>`**: プールされるバッファのライフサイクルイベント（取得時、返却時、クリア処理など）を処理します。
    *   詳細は [プーリング (`Docs/DesignSpecs/03_Pooling.md`)](Docs/DesignSpecs/03_Pooling.md) を参照してください。
*   **`IBuffer<T>` (および関連インターフェース)**: ライブラリの中心となるバッファインターフェースです。非連続メモリ、所有権管理、ライフサイクルを考慮して設計されています。
    *   詳細は [コアインターフェース (`Docs/DesignSpecs/01_Core_Interfaces.md`)](Docs/DesignSpecs/01_Core_Interfaces.md) を参照してください。

## 3. ドキュメント構成

この設計仕様書は、以下のファイルに分割されています。(パスはリポジトリルートからの相対パスを想定)

*   **[`Docs/DesignSpecs/00_Overview.md`](Docs/DesignSpecs/00_Overview.md) (このファイル):** ライブラリ全体の目的、スコープ、アーキテクチャ概要、およびこのドキュメント構成を示します。
*   **[`Docs/DesignSpecs/01_Core_Interfaces.md`](Docs/DesignSpecs/01_Core_Interfaces.md):** `IBufferState`, `IOwnedResource`, `IReadOnlyBuffer<T>`, `IWritableBuffer<T>`, `IBuffer<T>` など、中核となるインターフェースの定義、設計思想、セマンティクスについて詳述します。
*   **[`Docs/DesignSpecs/02_Providers_And_Buffers.md`](Docs/DesignSpecs/02_Providers_And_Buffers.md):** `ManagedBuffer<T>`, `NativeBuffer<T>` などの実装クラスの実装詳細と、`BufferManager`, `IBufferProvider` を介したバッファの取得・設定方法について記述します。
*   **[`Docs/DesignSpecs/03_Pooling.md`](Docs/DesignSpecs/03_Pooling.md):** バッファプーリング戦略、ライフサイクルフック、クリア処理ポリシーなど、メモリ効率化のための機構について詳述します。
*   **[`Docs/DesignSpecs/04_GPU_Support.md`](Docs/DesignSpecs/04_GPU_Support.md):** 拡張アーキテクチャを通じたGPUバッファサポートの実現方針について記述します。
*   **[`Docs/DesignSpecs/05_Error_Handling.md`](Docs/DesignSpecs/05_Error_Handling.md):** ライブラリ全体のエラーハンドリング戦略、使用する例外の種類（標準・カスタム）、`Try...` パターンの適用箇所について記述します。
*   *(旧 `06_Future_Extensions.md` は内容を各章に振り分け、削除されました)*

## 4. 将来のライブラリ全体の拡張方針

BitzBuffer は、初期リリース後も継続的な改善と機能拡張を目指します。具体的な機能拡張は各コンポーネントの設計仕様書に記載されていますが、ライブラリ全体として以下のような方向性を検討しています。

*   **設定の柔軟性向上:** 設定ファイルからの構成読み込みサポートなど、より多様な環境での利用を容易にします。 (詳細は [`Docs/DesignSpecs/02_Providers_And_Buffers.md`](Docs/DesignSpecs/02_Providers_And_Buffers.md) の将来の拡張を参照)
*   **デバッグと診断機能の統合的強化:** イベントトレースの統合など、ライブラリ全体の動作状況を把握しやすくする機能を追加します。 (詳細は [`Docs/DesignSpecs/05_Error_Handling.md`](Docs/DesignSpecs/05_Error_Handling.md) の将来の拡張を参照)
*   **コミュニティとの連携:** 利用者からのフィードバックを積極的に取り入れ、より実践的なユースケースに対応できるよう進化していきます。
