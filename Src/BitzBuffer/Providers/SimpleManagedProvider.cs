using System;
using BitzLabs.BitzBuffer.Managed; // ManagedBuffer<T> を使用するため

namespace BitzLabs.BitzBuffer.Providers
{
    // IBufferProvider のシンプルな実装。
    // マネージド配列 (T[]) を使用してバッファを都度生成する。プーリングは行わない。
    public class SimpleManagedProvider : IBufferProvider // クラスは非ジェネリックに変更
    {
        private bool _disposed = false;

        // バッファをレンタルする。
        // このプロバイダはプーリングを行わないため、実質的には CreateBuffer と同じ動作となる。
        public IBuffer<TItem> Rent<TItem>(int minimumLength = 0) where TItem : struct // メソッドをジェネリックに変更
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SimpleManagedProvider)); // nameof を修正
            }
            if (minimumLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumLength), "minimumLength は 0 以上である必要があります。");
            }
            return CreateBufferInternal<TItem>(minimumLength); // ジェネリック型引数を渡す
        }

        // バッファのレンタルを試みる。
        public bool TryRent<TItem>(int minimumLength, out IBuffer<TItem> buffer) where TItem : struct // メソッドをジェネリックに変更
        {
            buffer = default!;
            if (_disposed)
            {
                return false; // プロバイダ破棄済み
            }
            if (minimumLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumLength), "minimumLength は 0 以上である必要があります。");
            }

            try
            {
                buffer = Rent<TItem>(minimumLength); // ジェネリックメソッド呼び出し
                return true;
            }
            catch (OutOfMemoryException)
            {
                buffer = default!;
                return false;
            }
            // Rent<TItem>内でObjectDisposedExceptionがスローされる可能性もあるが、それは致命的なエラーなのでここではキャッチしない。
            // (既に入り口で_disposedチェックをしているため、通常ここには到達しないはず)
        }

        // 指定された正確な長さで新しいバッファを作成する。
        public IBuffer<TItem> CreateBuffer<TItem>(int exactLength) where TItem : struct // メソッドをジェネリックに変更
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SimpleManagedProvider)); // nameof を修正
            }
            if (exactLength < 0) 
            {
                throw new ArgumentOutOfRangeException(nameof(exactLength), "exactLength は 0 以上である必要があります。");
            }
            return CreateBufferInternal<TItem>(exactLength); // ジェネリック型引数を渡す
        }

        // 指定された正確な長さで新しいバッファの作成を試みる。
        public bool TryCreateBuffer<TItem>(int exactLength, out IBuffer<TItem> buffer) where TItem : struct // メソッドをジェネリックに変更
        {
            buffer = default!;
            if (_disposed)
            {
                return false; // プロバイダ破棄済み
            }
            if (exactLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exactLength), "exactLength は 0 以上である必要があります。");
            }

            try
            {
                buffer = CreateBufferInternal<TItem>(exactLength); // ジェネリック型引数を渡す
                return true;
            }
            catch (OutOfMemoryException)
            {
                buffer = default!;
                return false;
            }
        }

        // 内部的なバッファ作成処理
        private IBuffer<TItem> CreateBufferInternal<TItem>(int length) where TItem : struct // メソッドをジェネリックに変更
        {
            TItem[] array = new TItem[length];
            return new ManagedBuffer<TItem>(array, true);
        }

        // プーリング関連の統計API (スタブ実装)
        // public PooledBufferStatistics GetPoolingOverallStatistics()
        // {
        //     if (_disposed)
        //     {
        //         throw new ObjectDisposedException(nameof(SimpleManagedProvider));
        //     }
        //     // Issue #9 の指示通り、空またはダミーの統計情報を返す。
        //     // PooledBufferStatistics 型の定義が必要。
        //     return default; // または new PooledBufferStatistics();
        // }

        // public System.Collections.Generic.IReadOnlyDictionary<int, PooledBufferStatistics> GetPoolingBucketStatistics()
        // {
        //     if (_disposed)
        //     {
        //         throw new ObjectDisposedException(nameof(SimpleManagedProvider));
        //     }
        //     // Issue #9 の指示通り、空またはダミーの統計情報を返す。
        //     return new System.Collections.Generic.Dictionary<int, PooledBufferStatistics>();
        // }

        // IDisposable の実装
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // マネージドリソースの解放 (このプロバイダは特に保持していない)
                }
                // アンマネージドリソースの解放 (このプロバイダは特に保持していない)
                _disposed = true;
            }
        }

        // デストラクタ (アンマネージドリソースを持つ場合に備えて)
         ~SimpleManagedProvider()
        {
            Dispose(false);
        }
    }
}