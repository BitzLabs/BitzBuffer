using System;

namespace BitzLabs.BitzBuffer
{
    // バッファの生成と管理を行うプロバイダのインターフェース。
    // プーリングやバッファの種類ごとに異なる実装が可能。
    public interface IBufferProvider : IDisposable
    {
        // 指定した最小長さ以上のバッファをレンタルする。
        // プーリング実装では既存バッファを再利用する場合がある。
        IBuffer<TItem> Rent<TItem>(int minimumLength = 0) where TItem : struct;

        // 指定した最小長さ以上のバッファのレンタルを試みる。
        // 成功時は true、失敗時は false を返す。
        bool TryRent<TItem>(int minimumLength, out IBuffer<TItem> buffer) where TItem : struct;

        // 指定した正確な長さの新しいバッファを生成する。
        // プーリングは行わず、常に新規バッファを返す。
        IBuffer<TItem> CreateBuffer<TItem>(int exactLength) where TItem : struct;

        // 指定した正確な長さの新しいバッファ生成を試みる。
        // 成功時は true、失敗時は false を返す。
        bool TryCreateBuffer<TItem>(int exactLength, out IBuffer<TItem> buffer) where TItem : struct;

        // プーリング統計情報取得API（現時点ではスタブ、将来拡張用）
        // PooledBufferStatistics GetPoolingOverallStatistics();
        // System.Collections.Generic.IReadOnlyDictionary<int, PooledBufferStatistics> GetPoolingBucketStatistics();
    }
}