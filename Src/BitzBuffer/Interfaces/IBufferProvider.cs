using System;
using System.Collections.Generic; // For IReadOnlyDictionary
using System.Diagnostics.CodeAnalysis; // For MaybeNullWhen

namespace BitzLabs.BitzBuffer // または BitzLabs.BitzBuffer.Interfaces
{
    // バッファの生成と管理を行うプロバイダのインターフェース。
    // プーリングやバッファの種類ごとに異なる実装が可能。
    public interface IBufferProvider : IDisposable
    {
        // 指定した最小長さ以上のバッファをレンタルします。
        // 実装によっては、プーリングされた既存のバッファを再利用することがあります。
        IBuffer<TItem> Rent<TItem>(int minimumLength = 0) where TItem : struct;

        // 指定した最小長さ以上のバッファのレンタルを試みます。
        // 成功時は true とバッファを返し、失敗時は false とバッファのデフォルト値を返します。
        bool TryRent<TItem>(int minimumLength, [MaybeNullWhen(false)] out IBuffer<TItem> buffer) where TItem : struct;

        // 指定した正確な長さの新しいバッファを生成します。
        // このメソッドは通常、プーリングを行わず、常に新しいバッファインスタンスを返します。
        IBuffer<TItem> CreateBuffer<TItem>(int exactLength) where TItem : struct;

        // 指定した正確な長さの新しいバッファ生成を試みます。
        // 成功時は true とバッファを返し、失敗時は false とバッファのデフォルト値を返します。
        bool TryCreateBuffer<TItem>(int exactLength, [MaybeNullWhen(false)] out IBuffer<TItem> buffer) where TItem : struct;

        // プーリング統計情報取得API（将来の拡張用。具体的な型は Diagnostics/ や Pooling/Statistics.cs などで定義予定）
        // このプロバイダが管理するプーリング機構全体の統計情報を取得します。(将来実装予定)
        // 戻り値: 全体のプーリング統計情報。プーリングを使用しない場合はデフォルト値または空の情報を返します。
        object GetPoolingOverallStatistics(); // 型は将来、具体的な統計情報クラス (例: OverallPoolStatistics) に変更予定

        // このプロバイダが管理するプーリング機構のバケットごとの統計情報を取得します。(将来実装予定)
        // 戻り値: バケットサイズ(または識別子)をキーとする統計情報の読み取り専用ディクショナリ。プーリングを使用しない場合は空のディクショナリを返します。
        IReadOnlyDictionary<string, object> GetPoolingBucketStatistics(); // キーの型と値の型は将来変更予定 (例: string, BucketStatistics)
    }
}