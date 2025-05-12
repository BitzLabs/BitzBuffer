# C# Buffer管理ライブラリ 要求仕様 - プーリング

このドキュメントは、バッファ管理ライブラリにおけるバッファプーリング戦略、関連するライフサイクルフック、およびバッファのクリア処理ポリシーについて詳述します。プーリングは、メモリ割り当てのオーバーヘッドを削減し、GCプレッシャーを軽減するための重要な機能です。

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
    *   **役割:** `TBuffer` (`IBuffer<T>` の実装クラス型) のプーリング全体の戦略を定義します。複数の `IBucket<TResource>` を管理し、要求されたサイズに応じて適切なバケットを選択するロジック、およびバケットが空の場合に `IPoolableBufferAllocator<TResource>` を使って新しいリソースを確保するロジックを持ちます。
    *   **`TBuffer` と `TResource` の関係:** `TBuffer` は `TResource` を内部的にラップして使用します（例: `ManagedBuffer<T>` は `T[]` をラップ）。
    *   **API (例):**
        *   `TBuffer RentBuffer(int minimumSizeInElements)`: `minimumSizeInElements` を満たす `TBuffer` をプールから取得または新規作成して返します。
        *   `void ReturnBuffer(TBuffer buffer)`: `TBuffer` をプールに返却します。
        *   (オプション) `void ClearPools()`: 全てのバケットを空にします。
        *   (オプション) `PoolStatistics GetStatistics()`: プールの統計情報を取得。
*   **`IBufferLifecycleHooks<TBuffer>`:**
    *   **役割:** `TBuffer` がプールから取得されたり、プールに返却されたりする際の、状態リセットやクリア処理などのカスタムロジックをフックするインターフェース。プーリング戦略とは独立して機能し、バッファの再利用性を高めます。
    *   **API (フックポイント例):**
        *   `void OnRent(TBuffer buffer, BufferRentOptions options)`: バッファがプールから貸し出された直後に呼び出されます。バッファの論理長のリセット、オプションに応じたクリア処理などを行います。
        *   `void OnReturn(TBuffer buffer, BufferReturnOptions options)`: バッファがプールに返却される直前に呼び出されます。オプションに応じたクリア処理、アタッチされたリソースの解放（`DisposeAttachedResources` の呼び出し推奨）などを行います。
        *   `TBuffer OnCreateForPool(TResource resource, /*...*/)`: プールが新しい `TResource` から `TBuffer` を生成する際に呼び出されます。`TBuffer` の初期化を行います。
        *   `void OnClear(TBuffer buffer)`: 利用者が明示的に `IBuffer.Clear()` を呼び出した際、またはプーリング戦略がクリアを指示した際に、実際のクリア処理を行うために呼び出されることがあります。
*   **プロバイダオプション (`*ProviderOptionsBuilder`):**
    *   プーリング戦略の種類（デフォルト、カスタム）、バケットのサイズ構成、各バケットの最大保持数、アロケータの指定、ライフサイクルフックの実装、デフォルトのクリアポリシーなどを設定するために使用されます。

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