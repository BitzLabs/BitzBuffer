# C# Buffer管理ライブラリ 要求仕様 - プロバイダと実装クラス

このドキュメントは、バッファ管理ライブラリにおける具体的なバッファの**実装クラス** (`ManagedBuffer<T>`, `NativeBuffer<T>` など) と、これらのバッファを管理・提供するコンポーネント (`BufferManager`, `IBufferProvider`) について詳述します。

コアインターフェースについては [`01_Core_Interfaces.md`](01_Core_Interfaces.md) を参照してください。

## 4. バッファの実装クラス

コアインターフェース (`IBuffer<T>` など) を実装する具体的なクラスです。メモリの種類（マネージド、ネイティブ）と構造（連続、非連続）に応じて提供されます。

### 4.1. マネージドバッファ

標準的な .NET のマネージド配列 (`T[]`) を利用するバッファの**実装クラス**です。

#### 4.1.1. `ManagedBuffer<T>` (連続・固定長)

単一のマネージド配列 (`T[]`) をラップする、連続した固定長のバッファの**実装クラス**です。

*   **主な用途:** 事前にサイズが分かっている連続したメモリ領域が必要な場合。プーリングによる再利用に適しています。
*   **内部構造 (概念):**
    *   `T[] _array`: 内部で保持する配列。コンストラクタまたはプールから提供される。長さは不変。
    *   `int _length`: 現在の論理的な要素数。
    *   `bool _isOwner`, `bool _isDisposed`: `IBufferState` の状態。
    *   `IBufferPool<ManagedBuffer<T>>? _pool`: プールへの参照（もしプール管理下なら）。
*   **ライフサイクル:**
    *   `IOwnedResource` を実装します。
    *   `Dispose()`: プール管理下ならプールへ返却（内部配列も適切にプールへ返却）。そうでなければ参照を解放。`IsOwner=false`, `IsDisposed=true` に設定。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`: 内部配列 `_array` の現在の `_length` 以降の空き容量の範囲内で、`sizeHint` を満たす `Memory<T>` を返します。
        *   空き容量が不足する場合、または `sizeHint` が利用可能な空き容量を超える場合は、例外 (`InvalidOperationException` または `ArgumentOutOfRangeException`) をスローします。**配列の拡張は行いません。**
    *   `Advance(count)`: `_length` を増加させます。配列の物理長を超えないように検証します。
    *   `TryAttachSequence`: 基本的にサポートせず、常にコピーするか `false` を返します。

#### 4.1.2. `SegmentedManagedBuffer<T>` (非連続・可変長)

複数のマネージド配列セグメント (`T[]`) を論理的に連結して一つのバッファとして扱う**実装クラス**です。書き込み操作に応じて動的にセグメントが追加されることがあります。

*   **主な用途:** サイズが事前に分からないデータ、または複数のデータソースを結合する場合。ネットワークストリームの受信など。
*   **内部構造 (概念):**
    *   `List<SegmentEntry> _segments`: セグメント情報を保持するリスト。
        *   `SegmentEntry`: `ReadOnlyMemory<T> Memory`, `IDisposable? Owner`, `bool TookOwnership` を含む構造体/クラス。`Owner` はセグメントのメモリ解放（またはプール返却）の責任を持つオブジェクト。
    *   `long _totalLength`: 全セグメントの論理長の合計。
    *   `bool _isOwner`, `bool _isDisposed`: `IBufferState` の状態。
    *   `IBufferPool<SegmentedManagedBuffer<T>>? _pool`: プールへの参照。
*   **ライフサイクル:**
    *   `IOwnedResource` を実装します。
    *   `Dispose()`: プール管理下ならプールへ返却。そうでなければ参照を解放。`IsOwner=false`, `IsDisposed=true` に設定。
    *   **重要:** `Dispose` 時に、内部で保持している各 `SegmentEntry` のうち `TookOwnership == true` である `Owner.Dispose()` を呼び出し、アタッチされたリソースや自身で確保したセグメントを適切に解放（またはプールへ返却）します (`DisposeAttachedResources` メソッド)。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`:
        1.  最後のセグメントに十分な空き容量があれば、その部分の `Memory<T>` を返します。
        2.  空きがない/不足する場合、新しいセグメントを確保します。
            *   **確保戦略 (初期実装):** `new T[newSegmentSize]` で新しい配列を確保します (`newSegmentSize = Math.Max(sizeHint, DefaultSegmentSize)`)。
            *   確保した配列をラップする軽量な `ArrayOwnerWrapper<T> : IDisposable` を作成し、これを `Owner` として `SegmentEntry` をリストに追加します (`TookOwnership = true`)。
            *   将来的に、セグメントプールから `Rent` する拡張も可能です ([`06_Future_Extensions.md`](06_Future_Extensions.md) 参照)。
        3.  新しいセグメントから `Memory<T>` を返します。
    *   `Advance(count)`: 最後のセグメントの論理長と `_totalLength` を増加させます。
    *   `TryAttachSequence(sequence, takeOwnership)`:
        *   `sequence` の各セグメントをリストに追加します。
        *   `takeOwnership = true` の場合、セグメントがこのライブラリ管理下の `IBuffer<T>` から来ているなど、所有権を奪取できると判断できる場合に限り、元の `IBuffer<T>` の `IsOwner` を `false` にし、その `IBuffer<T>` インスタンスを `Owner` として保持 (`TookOwnership = true`)。
        *   所有権を奪取できない場合 (限定サポート外、または `takeOwnership = false` の場合) は、セグメントの内容を新しい配列にコピーし、その配列の `ArrayOwnerWrapper<T>` を `Owner` として保持 (`TookOwnership = false` または `true`)。
    *   `Prepend()`: 新しいセグメントをリストの先頭に挿入することで、比較的効率的に実装可能です。

### 4.2. ネイティブバッファ (`where T : unmanaged`)

アンマネージドメモリ (`NativeMemory` APIなど) を利用するバッファの**実装クラス**です。GCの影響を受けにくいメモリ領域が必要な場合や、ネイティブコードとの相互運用で使用されます。

#### 4.2.1. `NativeBuffer<T>` (連続・固定長)

単一の連続したアンマネージドメモリブロックをラップする、固定長のバッファの**実装クラス**です。

*   **主な用途:** ネイティブAPIとの間で固定サイズのデータをやり取りする場合、SIMD演算用の特定アライメントを持つバッファが必要な場合など。
*   **内部構造 (概念):**
    *   `SafeHandle _nativeMemoryHandle`: ネイティブメモリブロックを安全に管理する `SafeHandle` の派生クラス (`SafeNativeMemoryBlockHandle` など)。内部で `NativeMemory.Allocate` / `Free` (または `Aligned` 版) を呼び出す。
    *   `nuint _allocatedNBytes`: 確保されたバイト数。
    *   `int _length`: 現在の論理的な要素数。
    *   `MemoryManager<T>? _memoryManager`: `Memory<T>` を安全に提供するためのマネージャー。`_nativeMemoryHandle` と連携。
    *   `bool _isOwner`, `bool _isDisposed`, `IBufferPool<NativeBuffer<T>>? _pool`: 同上。
*   **ライフサイクル:**
    *   `IOwnedResource` を実装します。
    *   `Dispose()`: プール管理下ならプールへ返却。そうでなければ `_nativeMemoryHandle.Dispose()` を呼び出してネイティブメモリを解放。`_memoryManager` も適切に破棄。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`: 確保済みのネイティブメモリの空き容量内で `Memory<T>` (通常は `_memoryManager.Memory` のスライス) を返します。**メモリの拡張は行いません。** 容量不足時は例外。
    *   `Advance(count)`: `_length` を増加させます。
    *   `TryAttachSequence`: 基本的にサポートせず、コピー。

#### 4.2.2. `SegmentedNativeBuffer<T>` (非連続・可変長)

複数のアンマネージドメモリブロックを論理的に連結して一つのバッファとして扱う**実装クラス**です。

*   **主な用途:** サイズが事前に分からないネイティブデータを扱う場合、複数のネイティブバッファを結合する場合。
*   **内部構造 (概念):**
    *   `List<NativeSegmentEntry> _segments`: ネイティブセグメント情報を保持。
        *   `NativeSegmentEntry`: `SafeHandle SegmentMemoryHandle`, `nuint AllocatedNBytes`, `MemoryManager<T> SegmentMemoryManager`, `IDisposable Owner`, `bool TookOwnership` を含む。
    *   `long _totalLength`, `bool _isOwner`, `bool _isDisposed`, `IBufferPool<SegmentedNativeBuffer<T>>? _pool`: 同上。
*   **ライフサイクル:**
    *   `IOwnedResource` を実装します。
    *   `Dispose()`: プール返却または直接解放。`DisposeAttachedResources` で各 `NativeSegmentEntry` の `Owner.Dispose()` (実質 `SafeHandle.Dispose()` など) を呼び出す。
*   **書き込み (`IWritableBuffer<T>` 実装):**
    *   `GetMemory(sizeHint)`:
        1.  最後のセグメントに空きがあれば利用。
        2.  不足時は新しいネイティブセグメントを確保。
            *   **確保戦略:** `IPoolableBufferAllocator<SafeHandle>` を介して、`NativeMemory.Allocate` (または `Aligned`) で新しいメモリブロックを確保し、`SafeHandle` でラップして返す。
            *   将来的に、ネイティブメモリのセグメントプールからのレンタルも可能です ([`06_Future_Extensions.md`](06_Future_Extensions.md) 参照)。
        3.  新しい `SafeHandle` から `NativeSegmentEntry` を作成しリストに追加。
    *   `TryAttachSequence(sequence, takeOwnership)`:
        *   `sequence` がネイティブメモリ由来の場合に所有権奪取を試みる（限定サポート）。
        *   それ以外は新しいネイティブセグメントにコピー。

### 4.3. スライス実装

#### 4.3.1. `SlicedBufferView<T>`

`IBuffer<T>.Slice()` が返す `IReadOnlyBuffer<T>` の**実装クラス**です。

*   **役割:** 元の `IBuffer<T>` の一部に対する読み取り専用ビューを提供します。ゼロコピーです。
*   **内部構造 (概念):**
    *   `IBuffer<T> _sourceBuffer`: 元のバッファへの参照。
    *   `long _offset`, `long _length`: スライスの範囲。
    *   `bool _isDisposed`: スライスインスタンス自身の破棄状態。
*   **ライフサイクル:**
    *   `IOwnedResource` を実装しますが、`IsOwner` は常に `false` です。
    *   `IsDisposed` は、自身が `Dispose` されたか、`_sourceBuffer` が無効になった場合に `true` となります。
    *   `Dispose()` は `_isDisposed = true` に設定するだけで、リソース解放は行いません。
*   **データアクセス:** 全ての読み取り操作は `_sourceBuffer` に対して `Slice(_offset, _length)` を適用した結果を返します。アクセス前に `_sourceBuffer` の有効性をチェックし、無効なら `ObjectDisposedException` をスローします。

## 5. バッファの確保と設定

ライブラリの利用者は、`BufferManager` を介して `IBufferProvider` を取得し、それを通じてバッファの確保 (`Rent` または `CreateBuffer`) や設定を行います。

### 5.1. `BufferManager`

アプリケーション全体で `IBufferProvider` インスタンスを管理するシングルトンまたはDIコンテナ管理のサービスです。

*   **役割:**
    *   各種 `IBufferProvider` (マネージド用、ネイティブ用、将来的にGPU用など) を登録・管理。
    *   利用者に名前や特性に基づいて `IBufferProvider` を提供。
    *   デフォルトプロバイダの提供。
*   **API (例):**
    *   `TryGetProvider(string providerName, out IBufferProvider? provider)`: 名前でプロバイダを取得。
    *   `TryGetProvider<TBuffer>(out IBufferProvider? provider)`: (検討中) バッファタイプでプロバイダを取得。
    *   `TryGetDefaultProvider(BufferType type, out IBufferProvider? provider)`: 指定タイプ（Managed/Native）のデフォルトプロバイダを取得。
    *   `DefaultManagedProvider { get; }`, `DefaultNativeProvider { get; }`: デフォルトプロバイダへの直接アクセス。
*   **設定 (Fluent Interface):**
    *   `IServiceCollection.AddBufferManager(Action<BufferManagerOptions> configure)` (DIを使用する場合)
    *   `BufferManagerOptions` でデフォルトプロバイダの設定やカスタムプロバイダの登録を行う。
    *   `options.AddManagedProvider(string name, Action<ManagedProviderOptionsBuilder> configure)`
    *   `options.AddNativeProvider(string name, Action<NativeProviderOptionsBuilder> configure)`
    *   `options.AddProvider(string name, IBufferProvider instance)`

### 5.2. `IBufferProvider`

特定のメモリ種別（マネージド、ネイティブなど）のバッファを生成・管理する責務を持ちます。

*   **役割:**
    *   設定に基づき、対応する `IBuffer<T>` インスタンスを生成 (`CreateBuffer`)。
    *   設定に基づき、対応する `IBuffer<T>` インスタンスをプールから貸し出し (`Rent`)。
    *   内部でプーリング戦略 (`IBufferPoolStrategy`) やライフサイクルフック (`IBufferLifecycleHooks`) と連携。
*   **API (主要メソッド):**
    *   `IBuffer<T> Rent<T>(int minimumLength = 0)`: プールから `IBuffer<T>` を借ります。`minimumLength` を満たすバッファが選択されます。プールが枯渇している場合は `PoolExhaustedException` をスローすることがあります。
    *   `bool TryRent<T>(int minimumLength, [MaybeNullWhen(false)] out IBuffer<T> buffer)`: `Rent` の `Try...` パターン。失敗時に `false` を返します。
    *   `IBuffer<T> CreateBuffer<T>(int exactLength)`: 指定された正確な長さで新しい `IBuffer<T>` を生成します（プールは使用しません）。
    *   `bool TryCreateBuffer<T>(int exactLength, [MaybeNullWhen(false)] out IBuffer<T> buffer)`: `CreateBuffer` の `Try...` パターン。メモリ確保失敗時などに `false` を返します。
    *   *注: `Rent` の `minimumLength=0` は、プールが適切なデフォルトサイズのバッファを返すことを示します。`CreateBuffer` は通常、正確なサイズ指定を要求します。*
*   **`ProviderOptionsBuilder`:**
    *   各プロバイダの**実装クラス**（例: `ManagedMemoryProvider`, `NativeMemoryProvider`）は、自身の設定を行うための `*OptionsBuilder` クラスを提供します。
    *   例: `ManagedProviderOptionsBuilder`
        *   `.UsePoolingStrategy(IpoolingStrategy)`: プーリング戦略を指定。
        *   `.ConfigurePooling(Action<PoolingOptions> configure)`: プールのバケットサイズ、最大保持数などを設定。
        *   `.SetLifecycleHooks(IBufferLifecycleHooks)`: ライフサイクルフックを指定。
        *   `.SetDefaultClearOption(BufferClearOption)`: クリアポリシーを設定。
    *   例: `NativeProviderOptionsBuilder`
        *   上記に加え、`.SetDefaultAlignment(int alignment)` などネイティブ固有のオプション。
*   **プロバイダ固有オプション:**
    *   `Rent`/`CreateBuffer` に `object? providerSpecificOptions` を渡す機能は、型安全性の問題から推奨されません。
    *   特定のユースケースで高度な設定が必要な場合は、設定名を導入するか、プロバイダ固有の拡張メソッド (`RentWithAlignment<T>(this INativeBufferProvider provider, ...)` など) を定義することを検討します。
