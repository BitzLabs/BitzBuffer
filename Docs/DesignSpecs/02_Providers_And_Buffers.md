# BitzBuffer 設計仕様 - プロバイダと実装クラス

このドキュメントは、バッファ管理ライブラリ「BitzBuffer」における具体的なバッファの**実装クラス** (`ManagedBuffer<T>`, `NativeBuffer<T>` など) と、これらのバッファを管理・提供するコンポーネント (`BufferManager`, `IBufferProvider`) について詳述します。

コアインターフェースについては [`Docs/DesignSpecs/01_Core_Interfaces.md`](Docs/DesignSpecs/01_Core_Interfaces.md) を参照してください。

## 4. バッファの実装クラス

コアインターフェース (`IBuffer<T> where T : struct` など) を実装する具体的なクラスです。メモリの種類（マネージド、ネイティブ）と構造（連続、非連続）に応じて提供されます。各実装クラスは、デバッグ時に有用な情報を提供するために `ToString()` メソッドを適切にオーバーライドします。

### 4.1. マネージドバッファ

標準的な .NET のマネージド配列 (`T[]`) を利用するバッファの**実装クラス**です。`T` は `struct` である必要があります。

#### 4.1.1. `ManagedBuffer<T>` (連続・固定長) `where T : struct`

単一のマネージド配列 (`T[]`) をラップする、連続した固定長のバッファの**実装クラス**です。

*   **主な用途:** 事前にサイズが分かっている連続したメモリ領域が必要な場合。プーリングによる再利用に適しています。
*   **内部構造 (概念):**
    *   `T[] _array`: 内部で保持する配列。コンストラクタまたはプールから提供される。長さは不変。
    *   `int _length`: 現在の論理的な要素数。
    *   `bool _isOwner`, `bool _isDisposed`: `IBufferState` の状態。
    *   `IBufferPool<ManagedBuffer<T>>? _pool`: プールへの参照（もしプール管理下なら）。
*   **ライフサイクル:** `IOwnedResource` を実装。`Dispose()` でプール返却または解放。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`: 固定長。現在の空き容量を超える要求は例外。
    *   `Advance(count)`: `_length` を増加。
    *   `Write(ReadOnlySequence<T> source)`: コピー。
    *   `AttachSequence(sequence, attemptZeroCopy)`: 常にコピー (`AttachmentResult.Copied`)。
    *   `TryAttachZeroCopy(sequence)`: 常に `false`。
*   **`ToString()` (例):** `"ManagedBuffer<Byte>[Length=128, Capacity=1024, Owner=True, Disposed=False, Pooled]"`

#### 4.1.2. `SegmentedManagedBuffer<T>` (非連続・可変長) `where T : struct`

複数のマネージド配列セグメント (`T[]`) を論理的に連結して一つのバッファとして扱う**実装クラス**です。

*   **主な用途:** サイズが事前に分からないデータ、複数データソースの結合。
*   **内部構造 (概念):** `List<SegmentEntry>` (`SegmentEntry` は `ReadOnlyMemory<T> Memory`, `IDisposable? Owner`, `bool TookOwnership` を保持)。
*   **ライフサイクル:** `IOwnedResource` を実装。`Dispose()` で `DisposeAttachedResources()` を呼び出し、各セグメントの `Owner` を解放。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`: 最後のセグメントの空きを利用、不足時は新規セグメントを `new T[]` (ラップして `Owner` 設定) で確保。
    *   `Advance(count)`: `_totalLength` を増加。
    *   `Write(ReadOnlySequence<T> source)`: コピー。
    *   `AttachSequence(ReadOnlySequence<T> sequenceToAttach, bool attemptZeroCopy = true)`: `attemptZeroCopy` なら `TryAttachZeroCopy` を試行、失敗ならコピー。結果を `AttachmentResult` で返す。
    *   `TryAttachZeroCopy(ReadOnlySequence<T> sequenceToAttach)`: 条件（ライブラリ管理下の `IBuffer<T>` など）を満たせば所有権奪取して `true`、さもなくば `false`。
*   **`ToString()` (例):** `"SegmentedManagedBuffer<Int32>[Length=2048, Segments=2, Owner=True, Disposed=False]"`

### 4.2. ネイティブバッファ (`where T : unmanaged`)

アンマネージドメモリを利用するバッファの**実装クラス**です。`T` は `unmanaged` である必要があります。

#### 4.2.1. `NativeBuffer<T>` (連続・固定長) `where T : unmanaged`

単一の連続したアンマネージドメモリブロックをラップする、固定長のバッファの**実装クラス**です。

*   **主な用途:** ネイティブAPI連携、SIMD演算用。アライメントはプロバイダオプションで指定。
*   **内部構造 (概念):** `SafeHandle _nativeMemoryHandle`, `nuint _allocatedNBytes`, `MemoryManager<T>? _memoryManager` など。
*   **ライフサイクル:** `IOwnedResource` を実装。`Dispose()` で `_nativeMemoryHandle.Dispose()`。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`: 固定長。容量不足時は例外。
    *   `Advance(count)`: `_length` を増加。
    *   `Write(ReadOnlySequence<T> source)`: コピー。
    *   `AttachSequence(sequence, attemptZeroCopy)`: 常にコピー (`AttachmentResult.Copied`)。
    *   `TryAttachZeroCopy(sequence)`: 常に `false`。
*   **`ToString()` (例):** `"NativeBuffer<Single>[Length=512, Capacity=2048bytes, Alignment=32, Owner=True, Disposed=False]"`

#### 4.2.2. `SegmentedNativeBuffer<T>` (非連続・可変長) `where T : unmanaged`

複数のアンマネージドメモリブロックを論理的に連結して一つのバッファとして扱う**実装クラス**です。

*   **主な用途:** サイズが事前に分からないネイティブデータ。
*   **内部構造 (概念):** `List<NativeSegmentEntry>` (`NativeSegmentEntry` は `SafeHandle SegmentMemoryHandle`, `MemoryManager<T> SegmentMemoryManager`, `IDisposable Owner` などを保持)。
*   **ライフサイクル:** `IOwnedResource` を実装。`Dispose()` で `DisposeAttachedResources()`。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`: 最後のセグメントの空きを利用、不足時は新規ネイティブセグメントを確保（アライメントはプロバイダオプションに従う）。
    *   `Write(ReadOnlySequence<T> source)`: コピー。
    *   `AttachSequence(ReadOnlySequence<T> sequenceToAttach, bool attemptZeroCopy = true)`: `attemptZeroCopy` なら `TryAttachZeroCopy` を試行、失敗ならコピー。結果を `AttachmentResult` で返す。
    *   `TryAttachZeroCopy(ReadOnlySequence<T> sequenceToAttach)`: 条件を満たせば所有権奪取して `true`、さもなくば `false`。
*   **`ToString()` (例):** `"SegmentedNativeBuffer<Double>[Length=10000, Segments=5, Owner=True, Disposed=False]"`

### 4.3. スライス実装

#### 4.3.1. `SlicedBufferView<T>` `where T : struct`

`IBuffer<T>.Slice()` が返す `IReadOnlyBuffer<T>` の**実装クラス**です。

*   **役割:** 元の `IBuffer<T>` の一部に対する読み取り専用ビュー。ゼロコピー。
*   **内部構造 (概念):** `IBuffer<T> _sourceBuffer`, `long _offset`, `long _length` など。
*   **ライフサイクル:** `IOwnedResource` を実装 (`IsOwner` は `false`)。元のバッファが無効になると自身も無効。
*   **`ToString()` (例):** `"SlicedBufferView<Int32>[Length=50, Owner=False, Disposed=False, SourceType=ManagedBuffer<Int32>, Offset=100]"`

## 5. バッファの確保と設定

利用者は `BufferManager` から `IBufferProvider` を取得し、バッファを確保・設定します。

### 5.1. `BufferManager`

アプリケーション全体で `IBufferProvider` を管理。`IBufferManager` は `IDisposable` を実装。

*   **API (例):**
    *   `bool TryGetProvider(string providerName, out IBufferProvider? provider)`
    *   `IBufferProvider DefaultManagedProvider { get; }`
    *   `IBufferProvider DefaultNativeProvider { get; }`
    *   `void Dispose()`
*   **設定:** `IBufferManagerOptions` (実装は `BufferManagerOptions`) を介して行う。DIあり/なし両対応。
    ```csharp
    public interface IBufferManagerOptions
    {
        IBufferManagerOptions AddSharedPool(string poolName, Action<SharedPoolConfigurator> configurePool);
        IBufferManagerOptions ConfigureDefaultManagedSharedPool(Action<ManagedSharedPoolConfigurator> configurePool);
        IBufferManagerOptions ConfigureDefaultNativeSharedPool(Action<NativeSharedPoolConfigurator> configurePool);
        IBufferManagerOptions AddManagedProvider(string providerName, Action<IManagedProviderOptionsBuilder> configureProvider);
        IBufferManagerOptions AddNativeProvider(string providerName, Action<INativeProviderOptionsBuilder> configureProvider);
        IBufferManagerOptions ConfigureDefaultManagedProvider(Action<IManagedProviderOptionsBuilder> configureProvider);
        IBufferManagerOptions ConfigureDefaultNativeProvider(Action<INativeProviderOptionsBuilder> configureProvider);
    }
    public interface IProviderOptionsBuilderBase
    {
        IProviderOptionsBuilderBase UseSharedPool(string sharedPoolName);
        IProviderOptionsBuilderBase UseDefaultSharedPool();
        IProviderOptionsBuilderBase ConfigureDedicatedPooling(Action<DedicatedPoolConfigurator> configurePool);
        IProviderOptionsBuilderBase UseNoPooling();
        IProviderOptionsBuilderBase SetLifecycleHooks<TItem, TBuffer>(IBufferLifecycleHooks<TBuffer, TItem> hooks) // シグネチャ修正
            where TBuffer : class, IBuffer<TItem>
            where TItem : struct;
    }
    public interface IManagedProviderOptionsBuilder : IProviderOptionsBuilderBase { }
    public interface INativeProviderOptionsBuilder : IProviderOptionsBuilderBase
    {
        INativeProviderOptionsBuilder SetDefaultAlignment(nuint alignment);
    }
    ```

### 5.2. `IBufferProvider`

特定のメモリ種別のバッファを生成・管理。`IDisposable` を実装。

*   **役割:** `CreateBuffer`, `Rent`。プーリング戦略、ライフサイクルフック、バッファファクトリと連携。
    *   **`IBufferFactory<TItem, TBuffer, TOptions>` の利用:** `IBufferProvider` は、`TBuffer Create(TOptions options)` を持つ `IBufferFactory` を内部的に利用して `IBuffer<TItem>` の実装クラスインスタンスを生成。
*   **API (主要メソッド):**
    *   `IBuffer<T> Rent<T>(int minimumLength = 0) where T : struct`
    *   `bool TryRent<T>(int minimumLength, [MaybeNullWhen(false)] out IBuffer<T> buffer) where T : struct`
    *   `IBuffer<T> CreateBuffer<T>(int exactLength) where T : struct`
    *   `bool TryCreateBuffer<T>(int exactLength, [MaybeNullWhen(false)] out IBuffer<T> buffer) where T : struct`
    *   `OverallPoolStatistics GetPoolingOverallStatistics()`
    *   `IReadOnlyDictionary<string, BucketStatistics> GetPoolingBucketStatistics()`
    *   `void Dispose()`
*   **プロバイダ固有オプション:** 高度な設定（アライメント個別指定など）はプロバイダオプションでのデフォルト設定を基本とし、個別指定APIは設けない。特殊ケースは拡張メソッド等で対応。

### 5.3. 将来の拡張 (プロバイダとバッファ実装関連)

*   **`BufferManager` とプロバイダ機能の拡張:** プロバイダ間フォールバック、設定ファイルからの構成読み込み、プロバイダ固有APIへの安全なアクセス方法。
*   **特定プラットフォーム向け最適化/機能:** ピン留めマネージドプロバイダ、高性能メモリアロケータ活用プロバイダなど。
*   **非同期な確保/解放API:** `RentAsync<T>()`, `CreateBufferAsync<T>()`, `DisposeAsync()`。
*   **特定用途向けバッファ実装の追加:** リングバッファ、ギャップバッファなど。
*   **`ToString()` のさらなる充実:** デバッグビルド時の内容プレビューなど。
*   **メモリリーク検出支援:** ファイナライザ警告強化、追跡機能など。