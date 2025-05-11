# C# Buffer管理ライブラリ 要求仕様 (暫定)

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

*   **`BufferManager`**: アプリケーション全体で `IBufferProvider` を登録・管理し、利用者に提供する。Fluent APIによる設定が可能。デフォルトプロバイダ（マネージド/アンマネージド）を内包。
*   **`IBufferProvider`**: 特定の技術領域（マネージド、アンマネージド、GPU等）のバッファに関する操作（プールからの貸し出し、直接生成）を提供。プロバイダ固有の設定を持つ。
*   **`IBufferFactory<TBuffer, TOptions>`**: `IBufferProvider` の内部で使用され、実際にバッファオブジェクト (`IBuffer<T>` の実装) をインスタンス化する。
*   **`IBufferPool<TBuffer>` / `IBufferPoolStrategy<TBuffer>`**: バッファのプーリング機構。戦略はカスタマイズ可能。
*   **`IBufferLifecycleHooks<TBuffer>`**: プールされるバッファのライフサイクルイベント（取得時、返却時、クリア処理など）を処理。

## 3. バッファ共通インターフェース

<!-- Rev: 3章全体を IOwnedResource, IBufferState 導入案で置き換え -->
アプリケーション内で多様なメモリリソースを統一的に扱うため、中心となるバッファインターフェース群を定義します。これらのインターフェースは、非連続メモリへの対応、効率的な読み書き、そして明確な所有権管理とライフサイクルを考慮して設計されています。

### 3.1. 設計思想と主要なユースケース

*   **非連続メモリのサポート:** `System.IO.Pipelines` のように、物理的に連続していない複数のメモリセグメントを単一の論理バッファとして扱えるようにします。
*   **効率的な読み書き:** `Span<T>`、`Memory<T>`、`ReadOnlySequence<T>` を活用し、型安全かつ高性能なデータアクセスを提供します。
*   **所有権管理とライフサイクル (`IOwnedResource`, `IBufferState`):**
    *   **`IBufferState`**: バッファが有効な所有権を持つか (`IsOwner`)、既に破棄されたか (`IsDisposed`) を示す状態プロパティを提供。
    *   **`IOwnedResource`**: `IBufferState` を拡張し、`IDisposable` を実装することでリソース解放の責務も明確化。
    *   **所有権の移譲:** `TryAttachSequence` メソッドにより、バッファの所有権を（ゼロコピーで）移譲可能。移譲元のバッファは `IsOwner = false` となり、解放責任は移譲先に移る。
    *   **`Dispose()` の挙動:**
        *   プール管理下のバッファ: プールへ返却。
        *   直接生成されたバッファ: リソースを直接解放。
        *   所有権を失ったバッファ (`IsOwner == false`, `IsDisposed == false`): `Dispose()` が呼ばれると `IsDisposed = true` となり、デバッグビルドでは警告ログが出力される。実質的なリソース解放は行わない。
*   **読み取り専用スライス:** `Slice` 操作は常に読み取り専用のバッファ (`IReadOnlyBuffer<T>`) を返します。

### 3.2. インターフェース階層

以下の主要なインターフェースを定義します。

*   **`IBufferState`**: バッファの所有権 (`IsOwner`) と破棄状態 (`IsDisposed`) を示すプロパティのみを持つ基本インターフェース。
*   **`IOwnedResource`**: `IBufferState` を継承し、`IDisposable` を実装することでリソース解放の責務を追加。
*   **`IReadOnlyBuffer<T>`**: `IOwnedResource` を継承し、読み取り専用アクセスを提供。
*   **`IWritableBuffer<T>`**: `IBufferState` を継承し、書き込み専用の操作を提供 (`IDisposable` は含まない)。
*   **`IBuffer<T>`**: `IReadOnlyBuffer<T>` (したがって `IOwnedResource` と `IBufferState` も) と `IWritableBuffer<T>` (したがって `IBufferState` も) の両方を継承し、読み書き可能な完全なバッファ表現。

### 3.3. インターフェース定義

```csharp
using System;
using System.Buffers;

// バッファの所有権と破棄状態を示す基本的な状態インターフェース。
public interface IBufferState
{
    // このインスタンスが現在、基になるリソースに対する有効な所有権を持っているかどうかを示します。
    // 所有権が移譲されたり、リソースが破棄されたりすると false になります。
    bool IsOwner { get; }

    // このインスタンスが既に破棄 (Dispose) されているかどうかを示します。
    // true の場合、このオブジェクトは使用できません (特にリソース解放後)。
    bool IsDisposed { get; }
}

// IBufferState を拡張し、リソース解放の責務 (IDisposable) を追加したインターフェース。
public interface IOwnedResource : IBufferState, IDisposable
{
    // IsOwner, IsDisposed は IBufferState から継承。
    // Dispose は IDisposable から継承。
}

// 読み取り専用のバッファインターフェース。
// IOwnedResource (つまり IBufferState と IDisposable) を継承し、データへのアクセスとスライス機能を提供します。
public interface IReadOnlyBuffer<T> : IOwnedResource
{
    // バッファ内の要素の論理的な長さを取得します。
    long Length { get; }

    // バッファが空かどうか (Length == 0) を示します。
    bool IsEmpty { get; }

    // バッファが単一の連続したメモリセグメントで構成されているかどうかを示します。
    bool IsSingleSegment { get; }

    // バッファの内容を ReadOnlySequence<T> として取得します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることがあります。
    ReadOnlySequence<T> AsReadOnlySequence();

    // バッファが単一の連続したメモリセグメントで構成されている場合、そのセグメントの ReadOnlySpan<T> を取得します。
    // 成功した場合は true を返し、span に値が設定されます。それ以外の場合は false を返します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることが推奨されます (または常に false を返す)。
    bool TryGetSingleSpan(out ReadOnlySpan<T> span);

    // バッファが単一の連続したメモリセグメントで構成されている場合、そのセグメントの ReadOnlyMemory<T> を取得します。
    // 成功した場合は true を返し、memory に値が設定されます。それ以外の場合は false を返します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることが推奨されます (または常に false を返す)。
    bool TryGetSingleMemory(out ReadOnlyMemory<T> memory);

    // バッファの指定された範囲を表す新しい読み取り専用バッファ (スライス) を作成します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることがあります。
    IReadOnlyBuffer<T> Slice(long start, long length);

    // バッファの指定された開始位置から末尾までを表す新しい読み取り専用バッファ (スライス) を作成します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることがあります。
    IReadOnlyBuffer<T> Slice(long start);
}

// 書き込み専用のバッファインターフェース。
// IBufferState を継承し、データの書き込み、変更機能を提供します。IDisposable は含みません。
public interface IWritableBuffer<T> : IBufferState
{
    // IsOwner, IsDisposed は IBufferState から継承。
    // 書き込み操作の前には、これらのプロパティを確認し、
    // IsOwner が false または IsDisposed が true の場合は例外をスローすることが実装に期待されます。

    // バッファの末尾に書き込むためのメモリ領域を取得します。
    Memory<T> GetMemory(int sizeHint = 0);

    // GetMemory で取得した領域への書き込みが完了したことを通知し、バッファの論理的な長さを進めます。
    void Advance(int count);

    // 指定されたソースからバッファの末尾にデータを書き込みます。
    void Write(ReadOnlySpan<T> source);
    void Write(ReadOnlyMemory<T> source);
    void Write(T value);
    void Write(ReadOnlySequence<T> source); // データはコピーされます

    // バッファの先頭にデータを追加（プリペンド）します。(コストが高い可能性あり)
    void Prepend(ReadOnlySpan<T> source);
    void Prepend(ReadOnlyMemory<T> source);
    void Prepend(ReadOnlySequence<T> source); // データはコピーされます

    // 指定された ReadOnlySequence<T> を、このバッファの論理的な一部として末尾に追加（アタッチ）します。
    // takeOwnership = true の場合、元のバッファセグメントの所有権を引き継ぎます (ゼロコピー)。
    // 元のバッファは IsOwner が false になり、解放責任はこのバッファに移ります。
    // takeOwnership = false の場合、データはコピーされます。
    bool TryAttachSequence(ReadOnlySequence<T> sequenceToAttach, bool takeOwnership);

    // バッファの内容をクリアし、論理的な長さを0にリセットします。
    void Clear();

    // バッファの論理的な長さを指定された長さに切り詰めます。
    void Truncate(long length);
}

// バッファ管理ライブラリにおける主要なバッファインターフェース。
// IReadOnlyBuffer<T> (IOwnedResource と IBufferState を含む) と
// IWritableBuffer<T> (IBufferState を含む) の両方を継承します。
// これにより、読み書き可能な機能と完全なライフサイクル管理を提供します。
public interface IBuffer<T> : IReadOnlyBuffer<T>, IWritableBuffer<T>
{
    // IBufferState のメンバー (IsOwner, IsDisposed) は両方の親インターフェースから継承されるが、
    // 実装は単一の underlying state を持つ。
    // IDisposable は IReadOnlyBuffer<T> (経由で IOwnedResource) から継承。

    // 実装クラスは、IsOwner および IsDisposed の状態に基づいて、
    // 読み書きメソッドが呼び出された際に適切に例外をスローする必要があります。

    // 将来的な拡張ポイント。
    // 例:
    // BufferType BufferType { get; } // マネージド、アンマネージドなどを示す enum
    // string? AssociatedProviderName { get; } // このバッファを生成したプロバイダ名
}
```
<!-- Rev End: 3章全体置き換え -->

*   **ピン留め:** `IBuffer<T>` インターフェースには含めず、利用者が`Span<T>`/`Memory<T>`から`fixed`で対応するか、`NativeBuffer`を使用。

## 4. デフォルトプロバイダと具象バッファクラス

### 4.1. `ManagedBuffer<T>` (マネージド配列ベース)
*   `new T[]` で確保された配列、または複数の配列セグメントをラップ (`IBuffer<T>` の `IsSingleSegment` プロパティで識別)。
*   ファイナライザは不要。
*   `Dispose()` は、3.1章で定義された所有権とプーリングのルールに従う。プール管理下ならプールへ返却、そうでなければ配列参照をnull化（またはセグメント管理を開放）。 `IsOwner`, `IsDisposed` プロパティを適切に更新。
*   書き込みメソッド (`GetMemory`, `Advance`, `Write`など) は `IWritableBuffer<T>` インターフェースに従い、必要に応じて内部配列の拡張やセグメントの追加を行う。 `IsOwner`/`IsDisposed` の状態に基づき例外をスローする。 <!-- Rev: 例外スローを明記 -->

### 4.2. `NativeBuffer<T>` (アンマネージドメモリベース, `where T : unmanaged`)
*   `NativeMemory.Allocate` で確保されたメモリ、または複数のネイティブメモリセグメントをラップ。
*   リソースリークを防ぐため、`SafeHandle` の派生クラスを利用してアンマネージドメモリをラップすることを推奨。これにより、`NativeBuffer<T>` 自体のファイナライザは原則不要となる（`SafeHandle`が担当）。
*   `Dispose()` は、3.1章で定義された所有権とプーリングのルールに従う。プール管理下ならプールへ返却、そうでなければ `SafeHandle.Dispose()` を呼び出して直接解放。`IsOwner`, `IsDisposed` プロパティを適切に更新。
*   `Memory<T>` 提供には `MemoryManager<T>` を使用し、`SafeHandle` と連携させることで、`Memory<T>` のライフサイクルとネイティブリソースの安全な管理を実現。
*   クリア処理やアライメント指定は、バッファ生成時のオプションで固定。
*   書き込みメソッドは `IWritableBuffer<T>` インターフェースに従い、`IsOwner`/`IsDisposed` の状態に基づき例外をスローする。 <!-- Rev: 例外スローを追記 -->

## 5. バッファの確保と設定

*   **`BufferManager` から `IBufferProvider` を取得して使用:**
    *   `BufferManager.TryGetProvider(string providerName, out IBufferProvider? provider)`
    *   `BufferManager.TryGetProvider<TBuffer>(out IBufferProvider? provider)`
    *   `BufferManager.TryGetDefaultProvider(out IBufferProvider? provider)`
*   **`IBufferProvider` のAPI:**
    *   `Rent<T>(int minimumLength, string? configurationName = null, object? providerSpecificOptions = null) where T : struct`
        *   返却値は `IBuffer<T>` となる。
    *   `CreateBuffer<T>(int minimumLength, string? configurationName = null, object? providerSpecificOptions = null) where T : struct`
        *   返却値は `IBuffer<T>` となる。
*   **設定方法 (Fluent Interface):**
    *   `BufferManagerExtensions.Add[ProviderName]Provider(Action<ProviderOptionsBuilder> configure)`
    *   各プロバイダは対応する `ProviderOptionsBuilder` を提供。
*   **オプションの事前登録:** プロバイダ生成時に主要オプションを指定。`Rent`/`CreateBuffer`時は設定名や最小限の情報でアクセス。
*   **デフォルトプロバイダ:** `BufferManager` はデフォルトで `ManagedMemoryProvider` と `NativeMemoryProvider` を標準的な設定で登録する。

## 6. プーリング戦略

*   **カスタマイズ可能:** `IBufferPoolStrategy<TBuffer>` インターフェースを定義し、ユーザーが独自の戦略を実装・登録可能。 (ここでの `TBuffer` は `IBuffer<T>` の実装型を指す)
*   **`IBufferPoolStrategyProvider`:** 適切な戦略インスタンスを提供する。`BufferManager` が保持し、プロバイダが利用。
*   **デフォルト戦略:** ライブラリが汎用的なデフォルト戦略を提供（例: サイズ別バケット管理、LRU方式など）。

## 7. 将来の拡張と検討事項 (初期実装では保留)

### 7.1. 高度な所有権管理とパイプライン機能
*   参照カウントベースのより高度な所有権管理 (`IMemoryOwner<T>` との連携強化)。
*   現在の `TryAttachSequence` を超える、より複雑なバッファ合成・分割操作。
*   `System.IO.Pipelines` とのよりシームレスな連携、または `IBuffer<T>` を直接 `PipeReader`/`PipeWriter` としてアダプトする機能。

### 7.2. `IBuffer<T>` インターフェースの拡張
*   `IWritableBuffer<T>.GetMemory()` の高度化（特定セグメントへの書き込み指定など）。
*   `TrySlice` パターン（スライスが不可能な場合に例外ではなく `bool` で成否を返す）。

### 7.3. プーリング戦略の高度化と多様化
*   GPUリソース特化戦略、NUMA対応、統計情報API拡充、動的プールサイズ調整の高度化。

### 7.4. `BufferManager` とプロバイダ機能の拡張 (および関連API)
*   プロバイダ間フォールバック強化
*   設定ファイルからの構成読み込み
*   プロバイダ固有APIへの安全なアクセス方法
*   **`IBuffer<T>` / `IWritableBuffer<T>` と `System.IO.Stream` との高度な連携機能:**
    *   `IBuffer<T>` の内容を効率的に `Stream` へ書き出すメソッド (`CopyToAsync` など)。
    *   `Stream` から `IWritableBuffer<T>` へ効率的にデータを読み込むメソッド (`ReadFromAsync` など)。
    *   `IBuffer<T>` を `Stream` としてラップするアダプタクラスの提供。

### 7.5. 特定プラットフォーム向け最適化/機能
*   ピン留め機能付きマネージドプロバイダ、プラットフォーム固有高性能メモリAPI活用プロバイダ、ハードウェアアクセラレーション連携。

### 7.6. デバッグと診断機能の強化
*   二重解放/解放後アクセス検知強化（`IsOwner`/`IsDisposed` と例外、デバッグログで対応）。
*   メモリリーク検出支援（`SafeHandle` の利用、ファイナライザからの警告など）。
*   `ToString()` 充実（バッファの種類、長さ、セグメント数、所有権状態などを表示）。

### 7.7. 非同期操作のサポート
*   非同期な確保/解放（特にGPUリソースなど、確保に時間がかかる場合）。
*   `IAsyncBufferReader` / `IAsyncBufferWriter` のような、非同期I/Oを前提としたバッファアクセスインターフェースの検討。

## 8. GPUサポートの実現（拡張アーキテクチャ経由）

本ライブラリのコアは特定のGPU APIに依存しませんが、拡張性の高い設計により、外部ライブラリ（DLL）を通じて各種GPU API（Vulkan, OpenGL, DirectXなど）のバッファを統一的に管理することを目指します。

### 8.1. GPUプロバイダ (`IBufferProvider`の実装)
*   各GPU APIに対応する専用の `IBufferProvider` を拡張ライブラリとして提供します。
*   これらのプロバイダは、それぞれのGPU API固有のバッファ確保、解放、マッピング、メモリタイプ管理などのロジックをカプセル化します。
*   プロバイダは、`BufferManager` に登録され、利用者からは他のプロバイダと同様に `Rent<T>` や `CreateBuffer<T>` で利用できます。

### 8.2. GPUバッファ固有のインターフェース (`IBuffer<T>`の拡張または専用インターフェース)
*   拡張ライブラリは、`IBuffer<T>` を実装しつつ、GPUバッファ固有の操作を公開するための追加インターフェースを定義できます。
*   利用者は、`IBufferProvider` から取得した `IBuffer<T>` をこれらの固有インターフェースにキャストするか、プロバイダ固有のAPI経由で取得して、GPU特有の機能を利用します。
*   GPUバッファも `IBufferState` (したがって `IsOwner`, `IsDisposed`) を実装し、同様の所有権管理ルールに従うことが期待されます。 <!-- Rev: IBufferState を参照 -->

### 8.3. GPUバッファのオプションと設定
*   各GPUプロバイダは、専用のオプションクラスと、それを設定するためのFluent Builderを提供します。
*   これにより、バッファの用途、メモリプロパティなどのGPU固有パラメータを指定できます。

### 8.4. GPUリソースのプーリングとライフサイクル管理
*   GPUバッファのプーリングは、`IBufferPoolStrategy<TBuffer>` と `IBufferLifecycleHooks<TBuffer>` のカスタマイズを通じて実現します。
*   ライフサイクルフックでは、GPUバッファの再利用時の状態リセット、破棄時のAPI固有の解放処理などを行います。GPUリソースの解放は `SafeHandle` のような機構を通じて行うことが推奨されます。
*   プーリング戦略では、GPUメモリの割り当て単位や、デバイスごとのプール分割などを考慮できます。

### 8.5. データ転送
*   CPUメモリとGPUメモリ間のデータ転送は、GPUプロバイダが提供するユーティリティメソッドや、利用者がGPU APIコマンドを直接発行することで行います。
*   ライブラリのコアは直接的な転送コマンド発行機能を持たず、バッファリソースの提供に注力しますが、`TryAttachSequence` のようなゼロコピーの概念は、ステージングバッファを介した効率的な転送シナリオでも応用できる可能性があります。

### 8.6. 同期
*   GPU操作の同期は、GPUプロバイダを利用する側の責任範囲となります。ライブラリは同期オブジェクト自体は管理しませんが、設計上、外部の同期機構と連携しやすいように考慮します。
