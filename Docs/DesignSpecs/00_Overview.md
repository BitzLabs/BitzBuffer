# C# Buffer管理ライブラリ 要求仕様 - 概要

このドキュメントは、C# Buffer管理ライブラリの要求仕様の概要を示します。詳細な仕様は、関連する各ドキュメントファイルを参照してください。

## 1. ライブラリの目的とスコープ

*   **主要な目的:**
    *   画像処理、CAD/モデリング、機械学習用テンソル、FAプロトコル通信など、大量のデータを扱うアプリケーション向けの高性能なバッファ管理。
    *   マネージドメモリ、アンマネージドメモリ、およびGPUバッファ (OpenGL, Vulkanなど) を統一的なインターフェースで扱えるようにする（GPU対応は拡張性を重視し、別プロジェクト/DLLでの実装を想定）。
    *   メモリ効率の向上 (GC負荷軽減、LOH回避、メモリ断片化抑制、ゼロコピー操作の促進)。
    *   C#での実装方法の学習と処理の効率化の探求。
    *   Fluent Interfaceを考慮する。
*   **ターゲットフレームワーク:** .NET 6+ (一部機能は .NET 8+ も許容)
*   **主な機能:** バッファの確保、解放、プーリング、各種データ型への対応、各種メモリ種別への対応（拡張可能アーキテクチャ）、非連続メモリのサポート、所有権管理。

## 2. アーキテクチャ概要

このライブラリは、以下の主要なコンポーネントで構成されます。

*   **`BufferManager`**: アプリケーション全体で `IBufferProvider` を登録・管理し、利用者に提供します。Fluent APIによる設定が可能です。デフォルトプロバイダ（マネージド/アンマネージド）を内包します。
    *   詳細は [プロバイダと具象バッファ (`02_Providers_And_Buffers.md`)](02_Providers_And_Buffers.md) を参照してください。
*   **`IBufferProvider`**: 特定の技術領域（マネージド、アンマネージド、GPU等）のバッファに関する操作（プールからの貸し出し、直接生成）を提供します。プロバイダ固有の設定を持ちます。
    *   詳細は [プロバイダと具象バッファ (`02_Providers_And_Buffers.md`)](02_Providers_And_Buffers.md) を参照してください。
*   **`IBufferFactory<TBuffer, TOptions>`**: `IBufferProvider` の内部で使用され、実際にバッファオブジェクト (`IBuffer<T>` の実装) をインスタンス化します。
    *   詳細は [プロバイダと具象バッファ (`02_Providers_And_Buffers.md`)](02_Providers_And_Buffers.md) を参照してください。
*   **`IBufferPool<TBuffer>` / `IBufferPoolStrategy<TBuffer, TResource>`**: バッファのプーリング機構です。戦略はカスタマイズ可能です。`ArrayPool<T>` のコンセプトを参考に、独自実装を行います。
    *   詳細は [プーリング (`03_Pooling.md`)](03_Pooling.md) を参照してください。
*   **`IBufferLifecycleHooks<TBuffer>`**: プールされるバッファのライフサイクルイベント（取得時、返却時、クリア処理など）を処理します。
    *   詳細は [プーリング (`03_Pooling.md`)](03_Pooling.md) を参照してください。
*   **`IBuffer<T>` (および関連インターフェース)**: ライブラリの中心となるバッファインターフェースです。非連続メモリ、所有権管理、ライフサイクルを考慮して設計されています。
    *   詳細は [コアインターフェース (`01_Core_Interfaces.md`)](01_Core_Interfaces.md) を参照してください。

## 3. ドキュメント構成

この要求仕様書は、以下のファイルに分割されています。

*   **[`00_Overview.md`](00_Overview.md) (このファイル):** ライブラリ全体の目的、スコープ、アーキテクチャ概要、およびこのドキュメント構成を示します。
*   **[`01_Core_Interfaces.md`](01_Core_Interfaces.md):** `IBufferState`, `IOwnedResource`, `IReadOnlyBuffer<T>`, `IWritableBuffer<T>`, `IBuffer<T>` など、中核となるインターフェースの定義、設計思想、セマンティクスについて詳述します。
*   **[`02_Providers_And_Buffers.md`](02_Providers_And_Buffers.md):** `ManagedBuffer<T>`, `NativeBuffer<T>` などの具象バッファクラスの実装詳細と、`BufferManager`, `IBufferProvider` を介したバッファの取得・設定方法について記述します。
*   **[`03_Pooling.md`](03_Pooling.md):** バッファプーリング戦略、ライフサイクルフック、クリア処理ポリシーなど、メモリ効率化のための機構について詳述します。
*   **[`04_GPU_Support.md`](04_GPU_Support.md):** 拡張アーキテクチャを通じたGPUバッファサポートの実現方針について記述します。
*   **[`05_Error_Handling.md`](05_Error_Handling.md):** ライブラリ全体のエラーハンドリング戦略、使用する例外の種類（標準・カスタム）、`Try...` パターンの適用箇所について記述します。
*   **[`06_Future_Extensions.md`](06_Future_Extensions.md):** 初期実装のスコープ外となる将来的な拡張機能や検討事項のリストです。