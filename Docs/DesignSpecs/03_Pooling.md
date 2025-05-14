# BitzBuffer 設計仕様 - プーリング

このドキュメントは、バッファ管理ライブラリ「BitzBuffer」におけるバッファプーリング戦略、関連するライフサイクルフック、およびバッファのクリア処理ポリシーについて詳述します。プーリングは、メモリ割り当てのオーバーヘッドを削減し、GCプレッシャーを軽減するための重要な機能です。

## 6. プーリング戦略

本ライブラリは、`ArrayPool<T>` のコンセプトを参考にしつつ、より柔軟で多様なメモリリソース（マネージド、アンマネージド、将来的にGPU）に対応可能な独自のプーリング機構を実装します。

### 6.1. プーリングの基本コンポーネント

プーリング機構は、以下の主要なインターフェースと役割分担によって構成されます。

*   **`IPoolableBufferAllocator<TResource>`:**
    *   **役割:** 特定のメモリリソース (`TResource`) を実際に確保および解放する責務を持ちます。`TResource` は、マネージド配列 (`T[]`)、ネイティブメモリをラップする `SafeHandle`、GPUリソースハンドルなど、プール対象の基盤となるリソース型を表します。
    *   **API (例):**
        *   `TResource Allocate(int sizeInBytesOrElements, nuint alignment = 0)`: 指定されたサイズ（およびオプションでアライメント）でリソースを確保します。
        *   `void Free(TResource resource)`: 確保されたリソースを解放します。
    *   **実装例:**
        *   `ManagedArrayAllocator<TElement> : IPoolableBufferAllocator<TElement[]>`: `new TElement[]` で確保、解放はGC任せ。
        *   `NativeMemoryAllocator : IPoolableBufferAllocator<SafeNativeMemoryBlockHandle>`: `NativeMemory.Allocate(Aligned)` で確保し `SafeHandle` でラップ、`SafeHandle.Dispose()` で解放。
*   **`IBucket<TResource>`:**
    *   **役割:** 特定のサイズ（またはサイズ範囲）の `TResource` を管理する個々のプール（バケット）。
    *   **内部構造 (例):** `ConcurrentQueue<TResource>` や `ConcurrentStack<TResource>` を使用して、スレッドセーフなリソースの貸し出しと返却を実現。
    *   **API (例):**
        *   `bool TryRent(out TResource resource)`: バケットからリソースを貸し出します。
        *   `void Return(TResource resource)`: リソースをバケットに返却します。返却時にバケットの最大保持数を超過する場合は、`IPoolableBufferAllocator<TResource>.Free` を呼び出してリソースを破棄することもあります。
    *   各バケットは、対応するサイズ、最大保持アイテム数などのポリシーを持ちます。
*   **`IBufferPoolStrategy<TBuffer, TResource>`:**
    *   **役割:** `TBuffer` (`IBuffer<TItem>` の実装クラス型) のプーリング全体の戦略を定義します。複数の `IBucket<TResource>` を管理し、要求されたサイズに応じて適切なバケットを選択するロジック、およびバケットが空の場合に `IPoolableBufferAllocator<TResource>` を使って新しいリソースを確保するロジックを持ちます。
    *   **`TBuffer` と `TResource` の関係:** `TBuffer` は `TResource` を内部的にラップして使用します（例: `ManagedBuffer<TItem>` は `TItem[]` をラップ）。
    *   **API (例):**
        *   `TBuffer RentBuffer(int minimumSizeInElements)`: `minimumSizeInElements` を満たす `TBuffer` をプールから取得または新規作成して返します。
        *   `void ReturnBuffer(TBuffer buffer)`: `TBuffer` をプールに返却します。
        *   `PoolStatistics GetOverallStatistics()`: このプーリング戦略全体の統計情報を取得します。
        *   `IReadOnlyDictionary<string, PoolStatistics> GetBucketStatistics()`: 管理下にある各バケットごとの統計情報を取得します。
*   **`IBufferLifecycleHooks<TBuffer>`:**
    *   **役割:** `TBuffer` がプールから取得されたり、プールに返却されたりする際の、状態リセットやクリア処理などのカスタムロジックをフックするインターフェース。プーリング戦略とは独立して機能し、バッファの再利用性を高めます。
    *   **インターフェース定義:**
        ```csharp
        public readonly struct BufferRentOptions { /* ... (詳細は後述または別記) ... */ public bool? ClearBufferOnRent { get; } public int RequestedMinimumSize { get; } }
        public readonly struct BufferReturnOptions { /* ... (詳細は後述または別記) ... */ public bool? ClearBufferOnReturn { get; } }

        public interface IBufferLifecycleHooks<TBuffer> where TBuffer : class, IBuffer // IBuffer<TItem> の実装クラス
        {
            TBuffer OnCreate<TResource>(TResource underlyingResource, int resourceSizeInElements, object? poolContext = null);
            void OnRent(TBuffer buffer, BufferRentOptions options);
            bool OnReturn(TBuffer buffer, BufferReturnOptions options); // trueなら再利用, falseなら破棄
            void OnDestroy(TBuffer buffer);
        }

        public interface IResettableBuffer // IBuffer の実装クラスがこれを実装することを推奨
        {
            void ResetForReuse(int capacityHint); // 論理長を0に、IsOwner=true, IsDisposed=falseなど
        }
        ```
    *   **各フックの責務:**
        *   `OnCreate`: `IPoolableBufferAllocator` で確保されたリソースをラップする `TBuffer` インスタンスを生成・初期化 ( `IBufferFactory` と連携)。
        *   `OnRent`: 貸し出されるバッファの状態をリセット（論理長0、`IsOwner=true`, `IsDisposed=false`など、`IResettableBuffer.ResetForReuse` の呼び出しを推奨）、オプションに応じてクリア。
        *   `OnReturn`: 返却されるバッファのクリア（オプション）、再利用可能かの判断。`SegmentedBuffer` の場合はアタッチされたリソースの整理も考慮。
        *   `OnDestroy`: バッファがプールから完全に破棄される直前の最終処理（通常は `buffer.Dispose()` に委ねられる）。
*   **プロバイダオプション (`*ProviderOptionsBuilder`):**
    *   プーリング戦略の種類（デフォルト、カスタム）、バケットのサイズ構成、各バケットの最大保持数、アロケータの指定、ライフサイクルフックの実装、デフォルトのクリアポリシーなどを設定するために使用されます。（詳細は [`Docs/DesignSpecs/02_Providers_And_Buffers.md`](Docs/DesignSpecs/02_Providers_And_Buffers.md) を参照）

### 6.2. バッファのクリア処理ポリシー

バッファ内の以前のデータが漏洩することを防ぐため、またはバッファを初期状態にリセットするためにクリア処理が必要となる場合があります。しかし、クリア処理はパフォーマンスコストを伴うため、柔軟なポリシー設定が求められます。

*   **クリアタイミングの選択肢:**
    *   **返却時クリア (OnReturn):** プールに返却される際にクリア。プール内は常にクリーン。
    *   **レンタル時クリア (OnRent):** プールから貸し出される際にクリア。実際に使用される直前にクリア。
    *   **クリアしない (NoClear):** 自動的なクリアは行わず、パフォーマンスを優先。利用者が `IBuffer<T>.Clear()` を呼び出すか、データの性質上クリアが不要な場合。
*   **設定方法:**
    *   プロバイダの設定オプション (`*ProviderOptionsBuilder`) で、デフォルトのクリアポリシー (`BufferClearOption` enum: `NoClear`, `ClearOnReturn`, `ClearOnRent`) を指定します。
    *   `IBufferLifecycleHooks` の `OnReturn` および `OnRent` フックメソッド内で、この設定と渡されるオプション（もしあれば）に基づいて実際のクリア処理を実行します。
*   **デフォルトポリシー:** 特に指定がない場合は `BufferClearOption.NoClear`（クリアしない）をデフォルトとし、パフォーマンスを優先します。セキュリティ要件が高い場合や、常に初期化されたバッファが必要な場合は、利用者が明示的にクリアポリシーを設定するか、`IBuffer<T>.Clear()` を呼び出します。
*   **クリアAPI:**
    *   マネージド配列: `Array.Clear()` または `Span<T>.Clear()`。
    *   ネイティブメモリ: `NativeMemory.Clear()` または `new Span<byte>(ptr, size).Clear()`。

### 6.3. デフォルトのプーリング戦略

ライブラリは、マネージドメモリ (`T[]`) およびネイティブメモリ (`SafeHandle`) 向けの汎用的なデフォルト `IBufferPoolStrategy` 実装を提供します。これらは以下の特徴を持ちます。

*   **サイズ別バケット:** 一般的なサイズ（例: 4KB, 8KB, 16KB, ..., 1MBなど、プロバイダオプションで設定可能）ごとにリソースを管理するバケットを使用します。`RentBuffer(size)` の際には、`size` 以上の最も小さい適切なバケットが選択されます。
*   **スレッドセーフ:** `ConcurrentQueue<TResource>` などを利用して、スレッドセーフな貸し出しと返却を保証します。
*   **最大保持数の制限:** 各バケットやプール全体で保持するアイテム数に上限を設定し、メモリ使用量が過大になるのを防ぎます（オプションで設定可能）。上限を超えた場合は、返却されたリソースを破棄します。

### 6.4. プーリング統計情報API (検討中)

プーリング機構の動作状況を把握し、チューニングに役立てるための統計情報APIを提供します。

*   **`PoolStatistics` 構造体/クラス:** 以下の情報を含むことを検討します。
    *   対象リソースサイズ、現在の空きアイテム数、貸し出し中アイテム数、最大キャパシティ。
    *   Rentリクエスト総数、Rentヒット数、新規アロケーション数。
    *   Return総数、Return時破棄数、プールからのアイテム破棄数。
*   **取得API:** `IBufferPoolStrategy` に `GetOverallStatistics()` および `GetBucketStatistics()` メソッドを追加し、`IBufferProvider` を介してこれらの情報を取得できるようにします。
    *   詳細は「プーリング統計情報APIの内容」に関する議論で具体化します。

### 6.5. 将来の拡張 (プーリング関連)

*   **セグメント単位のプーリングサポート:**
    *   `SegmentedManagedBuffer<T>` (および `SegmentedNativeBuffer<T>`) が新しいメモリセグメントを必要とする際に、専用のセグメントプールからレンタルする機能を実装します。
*   **アイドルアイテムの定期的な破棄 (Scavenging):**
    *   プール内で一定期間利用されなかったバッファインスタンスを自動的に検出し、破棄するメカニズムを導入します。破棄の閾値は設定可能にします。
*   **NUMA (Non-Uniform Memory Access) 対応プーリング:**
    *   NUMAアーキテクチャにおいて、特定のCPUコアに近いメモリノードからバッファを確保するような、NUMAアウェアなプーリング戦略やアロケータオプションを提供します。
*   **動的プールサイズ調整の高度化:**
    *   アプリケーションの負荷状況やGCの状況に応じて、プールのバケットサイズや最大保持数を動的に調整する、より洗練されたアルゴリズムを検討します。
*   **プーリング統計情報APIのさらなる拡充:**
    *   基本的な統計情報に加え、より詳細なパフォーマンスメトリクス（例: 平均待機時間、フラグメンテーション情報など）の収集と提供を検討します。