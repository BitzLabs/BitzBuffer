# BitzBuffer 設計仕様 - プーリング

このドキュメントは、バッファ管理ライブラリ「BitzBuffer」におけるバッファプーリング戦略、関連するライフサイクルフック、およびバッファのクリア処理ポリシーについて詳述します。プーリングは、メモリ割り当てのオーバーヘッドを削減し、GCプレッシャーを軽減するための重要な機能です。

## 6. プーリング戦略

本ライブラリは、`ArrayPool<T>` のコンセプトを参考にしつつ、より柔軟で多様なメモリリソース（マネージド、アンマネージド、将来的にGPU）に対応可能な独自のプーリング機構を実装します。

### 6.1. プーリングの基本コンポーネント

プーリング機構は、以下の主要なインターフェースと役割分担によって構成されます。

*   **`IPoolableBufferAllocator<TResource>`:**
    *   **役割:** 特定のメモリリソース (`TResource`) を実際に確保および解放する責務を持ちます。
    *   **API (例):**
        *   `TResource Allocate(int sizeInBytesOrElements, nuint alignment = 0)`
        *   `void Free(TResource resource)`
*   **`IBucket<TResource>`:**
    *   **役割:** 特定のサイズ（またはサイズ範囲）の `TResource` を管理する個々のプール（バケット）。
    *   **API (例):**
        *   `bool TryRent(out TResource resource)`
        *   `void Return(TResource resource)`
    *   各バケットは統計カウンターを内部に持ち、最大保持アイテム数などのポリシーを持ちます。
*   **`IBufferPoolStrategy<TBuffer, TResource, TItem>`:**
    *   **役割:** `TBuffer` (`IBuffer<TItem>` の実装クラス型) のプーリング全体の戦略を定義します。
    *   **`TBuffer`:** `IBuffer<TItem>` を実装するクラス。
    *   **`TResource`:** `TBuffer` が内部的に使用する基盤リソース型 (例: `TItem[]`, `SafeHandle`)。
    *   **`TItem`:** バッファ内の要素型 (`struct` 制約)。
    *   **主な挙動:**
        *   **`RentBuffer(minimumSizeInElements)`:** 要求サイズ以上の最小バケットから貸し出し。バケットが空なら新規アロケートを試みます。上限設定により失敗する可能性もあります。
        *   **`ReturnBuffer(TBuffer buffer)`:** バッファを適切なバケットに返却。バケットが最大保持数に達していればリソースを破棄します。
    *   **API (例):**
        *   `TBuffer RentBuffer(int minimumSizeInElements)`
        *   `void ReturnBuffer(TBuffer buffer)`
        *   `OverallPoolStatistics GetOverallStatistics()`
        *   `IReadOnlyDictionary<string, BucketStatistics> GetBucketStatistics()`
*   **`IBufferLifecycleHooks<TBuffer, TItem>`:**
    *   **役割:** `TBuffer` がプールのライフサイクルを通過する際のカスタムロジックをフックします。
    *   **インターフェース定義:**
        ```csharp
        public readonly struct BufferRentOptions
        {
            public bool? ClearBufferOnRent { get; }
            public int RequestedMinimumSize { get; }
            public BufferRentOptions(bool? clearBufferOnRent = null, int requestedMinimumSize = 0)
            {
                ClearBufferOnRent = clearBufferOnRent;
                RequestedMinimumSize = requestedMinimumSize;
            }
        }
        public readonly struct BufferReturnOptions
        {
            public bool? ClearBufferOnReturn { get; }
            public BufferReturnOptions(bool? clearBufferOnReturn = null)
            {
                ClearBufferOnReturn = clearBufferOnReturn;
            }
        }

        public interface IBufferLifecycleHooks<TBuffer, TItem>
            where TBuffer : class, IBuffer<TItem>
            where TItem : struct // IBuffer<T> の T の制約
        {
            TBuffer OnCreate<TResource>(TResource underlyingResource, int resourceSizeInElements, object? poolContext = null);
            void OnRent(TBuffer buffer, BufferRentOptions options);
            bool OnReturn(TBuffer buffer, BufferReturnOptions options);
            void OnDestroy(TBuffer buffer);
        }
        public interface IResettableBuffer
        {
            void ResetForReuse(int capacityHint);
        }
        ```
    *   **各フックの責務 (クリア処理との関連):**
        *   `OnCreate`: 新規リソースから `TBuffer` を生成・初期化 (内部で `IBufferFactory` を利用することが想定されます)。
        *   `OnRent`: 貸し出されるバッファの状態をリセット。プロバイダ設定や `BufferRentOptions.ClearBufferOnRent` に基づき、ここでバッファのクリア処理を実行。
        *   `OnReturn`: 返却されるバッファのクリーンアップ。プロバイダ設定や `BufferReturnOptions.ClearBufferOnReturn` に基づき、ここでバッファのクリア処理を実行。再利用可否も判断。
        *   `OnDestroy`: バッファ破棄前の最終処理。
*   **プロバイダオプション (`*ProviderOptionsBuilder`):**
    *   プーリング戦略、バケット設定、アロケータ、ライフサイクルフックの実装、デフォルトのクリアポリシーなどを設定します。（詳細は [`Docs/BitzBuffer/02_Providers_And_Buffers.md`](Docs/BitzBuffer/02_Providers_And_Buffers.md) を参照）

### 6.2. バッファのクリア処理ポリシー

（内容は前回提示版と同様）

### 6.3. デフォルトのプーリング戦略

（内容は前回提示版と同様）

### 6.4. プーリング統計情報API

プーリング機構の動作状況を把握し、チューニングに役立てるための統計情報APIを提供します。

*   **`BucketStatistics` 構造体:** 個々のバケットの状態とパフォーマンスを示します。
    ```csharp
    public readonly struct BucketStatistics
    {
        public string Identifier { get; }
        public int ResourceSize { get; }
        public int CurrentFreeCount { get; }
        public int CurrentRentedCount { get; }
        public int MaxCapacity { get; }
        public long TotalRentRequests { get; }
        public long TotalRentHits { get; }
        public long TotalMissesAndAllocations { get; }
        public long TotalReturns { get; }
        public long TotalReturnsDiscardedOnLimit { get; }
        public long TotalItemsExplicitlyFreed { get; }

        public BucketStatistics(string identifier, int resourceSize, int currentFreeCount, int currentRentedCount, int maxCapacity,
                                 long totalRentRequests, long totalRentHits, long totalMissesAndAllocations,
                                 long totalReturns, long totalReturnsDiscardedOnLimit, long totalItemsExplicitlyFreed)
        { /* プロパティ初期化 */ }
        public override string ToString() { /* 見やすい形式で表示 */ return "..."; }
    }
    ```
*   **`OverallPoolStatistics` 構造体:** プーリング戦略全体の集計情報。
    ```csharp
    public readonly struct OverallPoolStatistics
    {
        public string StrategyName { get; }
        public int TotalCurrentFreeCount { get; }
        public int TotalCurrentRentedCount { get; }
        public int TotalItemsManaged => TotalCurrentFreeCount + TotalCurrentRentedCount;
        public long GrandTotalRentRequests { get; }
        // ... (BucketStatistics の各 Total/GrandTotal 版) ...

        public OverallPoolStatistics(string strategyName, IEnumerable<BucketStatistics> bucketStats) { /* 集計ロジック */ }
        public override string ToString() { /* 見やすい形式で表示 */ return "..."; }
    }
    ```
*   **取得API:**
    *   **`IBufferPoolStrategy<TBuffer, TResource, TItem>` に追加:**
        ```csharp
        OverallPoolStatistics GetOverallStatistics();
        IReadOnlyDictionary<string, BucketStatistics> GetBucketStatistics();
        ```
    *   **`IBufferProvider` に追加 (プーリング戦略経由で提供):**
        ```csharp
        OverallPoolStatistics GetPoolingOverallStatistics();
        IReadOnlyDictionary<string, BucketStatistics> GetPoolingBucketStatistics();
        ```
*   **実装考慮:** 統計カウンターはスレッドセーフに更新。統計取得はスナップショット。

### 6.5. 将来の拡張 (プーリング関連)

*   **セグメント単位のプーリングサポート**
*   **アイドルアイテムの定期的な破棄 (Scavenging)**
*   **NUMA (Non-Uniform Memory Access) 対応プーリング**
*   **動的プールサイズ調整の高度化**
*   **プーリング統計情報APIのさらなる拡充** (より詳細なパフォーマンスメトリクスなど)