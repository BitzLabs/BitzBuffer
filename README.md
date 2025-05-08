# C# Buffer管理ライブラリ 要求仕様 (暫定)

## 1. ライブラリの目的とスコープ

*   **主要な目的:**
    *   画像処理、CAD/モデリング、機械学習用テンソルなど、大量のデータを扱うアプリケーション向けの高性能なバッファ管理。
    *   マネージドメモリ、アンマネージドメモリ、およびGPUバッファ (OpenGL, Vulkanなど) を統一的なインターフェースで扱えるようにする。
    *   メモリ効率の向上 (GC負荷軽減、LOH回避、メモリ断片化抑制)。
    *   C#での実装方法の学習と処理の効率化の探求。
*   **ターゲットフレームワーク:** .NET 6+ (一部機能は .NET 8+ も許容)
*   **主な機能:** バッファの確保、解放、プーリング、各種データ型への対応、各種メモリ種別への対応。

## 2. バッファ共通インターフェース (`IBuffer<T>`)

```csharp
public interface IBuffer<T> : IDisposable
{
    long LongLength { get; }      // バッファの総要素数
    bool IsContiguous { get; }  // 単一の連続したメモリブロックか
    int SegmentCount { get; }   // 構成セグメント数 (IsContiguous == true なら 1)

    // IsContiguous == true かつ LongLength <= int.MaxValue の場合に成功
    bool TryGetSpan(out Span<T> span);
    bool TryGetMemory(out Memory<T> memory);
    bool TryGetReadOnlySpan(out ReadOnlySpan<T> span);
    bool TryGetReadOnlyMemory(out ReadOnlyMemory<T> memory);

    // スライス操作 (オフセットは long, 長さは int)
    // 要求範囲が単一連続メモリとして表現できる場合。失敗時は例外をスローする。
    Span<T> Slice(long start, int length);
    Memory<T> SliceMemory(long start, int length);
    ReadOnlySpan<T> SliceReadOnly(long start, int length);
    ReadOnlyMemory<T> SliceReadOnlyMemory(long start, int length);

    // ReadOnlySequence<T> は常に取得可能
    ReadOnlySequence<T> AsReadOnlySequence();
    ReadOnlySequence<T> SliceReadOnlySequence(long start, long length);

    // 書き込み用
    IBufferWriter<T> GetWriter(long offset = 0);
}
```

*   **`GetPinnableReference()`**: `IBuffer<T>` からは削除。利用者は `TryGetSpan`/`Memory` で取得した `Span<T>`/`Memory<T>` (またはそのスライス) からピン留め参照を取得する。
*   **`Stream`系API**: 見送り。
*   **`StealPointer()`**: 見送り。
*   **`SpanSequence GetEnumerator()`**: `AsSpanSequence()` (または類似の `AsReadOnlySequence()`) が返すオブジェクトが `IEnumerable<Span<T>>` (または `IEnumerable<ReadOnlyMemory<T>>`) を実装することで対応。

## 3. GPUバッファ用インターフェース

### 3.1. `IGpuBuffer<T>`

```csharp
public enum GpuMapMode { Read, Write, ReadWrite }

public interface IGpuBuffer<T> : IBuffer<T>
{
    // GpuApiKind ApiKind { get; } // どのGPU APIのバッファか (Vulkan, OpenGLなど) - 検討
    // IntPtr NativeHandle { get; } // GPU API固有のハンドル - 検討 (型安全なラッパーも)

    bool IsMapped { get; }
    GpuMapMode CurrentMapMode { get; } // IsMapped == true の場合

    IGpuMappedBuffer<T> Map(GpuMapMode mode, long offset = 0, long length = -1); // マップ範囲指定可
    void Unmap(); // または IGpuMappedBuffer<T>.Dispose() で Unmap
}
```

### 3.2. `IGpuMappedBuffer<T>`

```csharp
public interface IGpuMappedBuffer<T> : IDisposable // Dispose で Unmap
{
    Span<T> Span { get; }
    Memory<T> Memory { get; }
    // unsafe T* Pointer { get; } // T が unmanaged の場合

    long Offset { get; } // バッファ全体の先頭からのオフセット
    long Length { get; } // マップされた領域の長さ
}
```

### 3.3. オプション: `INonCoherentMappedBuffer` (非コヒーレントメモリ用)

```csharp
public interface INonCoherentMappedBuffer
{
    void Flush(long offset, long size);
    void Invalidate(long offset, long size);
}
```
*   `IGpuMappedBuffer<T>` の具象クラスが必要に応じてこのインターフェースを実装する。

*   **`UploadAsync` / `DownloadAsync`**: `IGpuBuffer<T>` からは見送り。高レベルなデータ転送は別コンポーネント (例: `System.IO.Pipelines`連携、専用ユーティリティ) で検討。

## 4. バッファの確保と設定

*   **ファクトリ形式 (`BufferManager`)**:
    *   `BufferManager.RentManaged<T>(int minBufferSize, BufferOptions options = null)`
    *   `BufferManager.RentUnmanaged<T>(int minBufferSize, UnmanagedBufferOptions options = null)`
    *   `BufferManager.RentGpu<T>(GpuBufferOptions gpuOptions)`
*   **設定方法**: C# Logger風の `Configure(Action<Options> options)` パターンを採用。
    *   `BufferManager` 全体の設定。
    *   個別のバッファ確保時のオプション設定。
*   **GPUバッファ確保オプション (`GpuBufferOptions`)**:
    *   ターゲットAPI (Vulkan, OpenGLなど)。
    *   メモリの種類 (DeviceLocal, HostVisibleなど)。
    *   用途フラグ (VertexBuffer, IndexBufferなど)。
    *   アライメント要件。

## 5. プーリング戦略

*   **必須機能**: `ArrayPool<T>` や `MemoryPool<T>` のような効率的な再利用。
*   **管理単位**:
    *   スレッドローカルプール (高速確保/解放)。
    *   共通バックグラウンドプール (スレッド間共有、全体最適化)。
    *   階層型プーリングを検討。
*   **解放指針**:
    *   マネージド: GC状態 (フルGC通知、`Gen2GcCallback`) や時間ベース。
    *   アンマネージド/GPU: 時間ベース、メモリプレッシャー、手動トリガー。
*   **GPUリソースのプーリング**: 状態管理 (クリア済みか、用途など) の複雑性を考慮。

## 6. その他の要件・検討事項

*   **Scatter/Gather I/O**: `ReadOnlySequence<T>` を通じてサポート。
*   **スレッドセーフティ**: プール操作はスレッドセーフであること。取得したバッファ自体の操作は利用者の責任範囲も明確化。
*   **エラーハンドリング**:
    *   `Slice`失敗時: 例外スロー (範囲外など)。
    *   `Map`失敗時: 例外スロー (リソース不足、不正モードなど)。
    *   プール枯渇時の挙動定義 (例外、新規確保、待機など)。
*   **パフォーマンス目標**:
    *   GC負荷軽減 (特にLOH回避)。
    *   確保/解放速度。
    *   メモリ断片化抑制。
*   **ドキュメント**: APIリファレンス、使用例、設計思想。
*   **テスト**: ユニットテスト、結合テスト (特に外部API連携)、パフォーマンステスト。

## 7. 今後の検討ポイント

*   `BufferManager` の具体的なAPI設計 (特に `RentGpu` と `GpuBufferOptions` の詳細)。
*   プーリングアルゴリズムの具体的な実装方法。
    *   サイズ別バケット構成。
    *   スレッドローカルプールと共通プールの連携ロジック。
*   `IGpuBuffer<T>.NativeHandle` や `ApiKind` の具体的な型と公開方法。
*   各GPU API (Vulkan, OpenGL) の具象クラス設計。
*   詳細なエラーコードや例外クラスの設計。
*   デバッグ支援機能 (プール統計情報など)。

---
```

**補足:**

*   上記は現時点でのまとめであり、今後詳細を詰めていく中で変更・追加があり得ます。
*   `// 検討` と書かれている部分は、まだ具体的な仕様が固まっていない、あるいは複数の選択肢がある箇所です。
*   コードブロックはC#としていますが、Markdown内なのでシンタックスハイライトが効くようにしています。
