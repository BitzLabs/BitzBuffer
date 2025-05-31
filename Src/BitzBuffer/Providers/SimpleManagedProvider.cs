using System;
using System.Collections.Generic; // For Dictionary
using BitzLabs.BitzBuffer.Managed;
// using BitzLabs.BitzBuffer.Diagnostics; // 将来、具体的な統計情報クラスを使用する場合

namespace BitzLabs.BitzBuffer.Providers.Managed // 名前空間を Providers.Managed に変更
{
    // IBufferProvider のシンプルな実装。
    // マネージド配列 (TItem[]) を使用してバッファを都度生成する。プーリングは行わない。
    public class SimpleManagedProvider : IBufferProvider
    {
        private bool _disposed = false;

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName); // GetType().FullName を使用
            }
        }

        // バッファをレンタルする。
        // このプロバイダはプーリングを行わないため、実質的には CreateBuffer と同じ動作となる。
        public IBuffer<TItem> Rent<TItem>(int minimumLength = 0) where TItem : struct
        {
            ThrowIfDisposed();
            if (minimumLength < 0)
            {
                // APIコントラクト違反の引数は、Try系でなくても例外をスローする。
                throw new ArgumentOutOfRangeException(nameof(minimumLength), "minimumLength は 0 以上である必要があります。");
            }
            return CreateBufferInternal<TItem>(minimumLength);
        }

        // バッファのレンタルを試みる。
        public bool TryRent<TItem>(int minimumLength, out IBuffer<TItem> buffer) where TItem : struct
        {
            buffer = default!; // default! で初期化
            if (_disposed)
            {
                return false; // プロバイダ破棄済み
            }
            if (minimumLength < 0)
            {
                // 不正な引数: プログラミングエラーとして扱い、例外をスローする。
                // Tryパターンは実行時のリソース確保失敗をfalseで通知するものであり、APIの誤用を隠蔽するものではない。
                throw new ArgumentOutOfRangeException(nameof(minimumLength), "minimumLength は 0 以上である必要があります。");
            }

            try
            {
                buffer = CreateBufferInternal<TItem>(minimumLength); // Rent<TItem>ではなく、直接Internalを呼ぶ
                return true;
            }
            catch (OutOfMemoryException)
            {
                // メモリ不足は実行時の要因による失敗なので false を返す。
                buffer = default!;
                return false;
            }
            // その他の予期せぬ例外はキャッチせず、そのままスローされる（バグの可能性）。
        }

        // 指定された正確な長さで新しいバッファを作成する。
        public IBuffer<TItem> CreateBuffer<TItem>(int exactLength) where TItem : struct
        {
            ThrowIfDisposed();
            if (exactLength < 0)
            {
                // APIコントラクト違反の引数は例外をスローする。
                throw new ArgumentOutOfRangeException(nameof(exactLength), "exactLength は 0 以上である必要があります。");
            }
            return CreateBufferInternal<TItem>(exactLength);
        }

        // 指定された正確な長さで新しいバッファの作成を試みる。
        public bool TryCreateBuffer<TItem>(int exactLength, out IBuffer<TItem> buffer) where TItem : struct
        {
            buffer = default!; // default! で初期化
            if (_disposed)
            {
                return false; // プロバイダ破棄済み
            }
            if (exactLength < 0)
            {
                // 不正な引数: プログラミングエラーとして扱い、例外をスローする。
                throw new ArgumentOutOfRangeException(nameof(exactLength), "exactLength は 0 以上である必要があります。");
            }

            try
            {
                buffer = CreateBufferInternal<TItem>(exactLength);
                return true;
            }
            catch (OutOfMemoryException)
            {
                // メモリ不足は実行時の要因による失敗なので false を返す。
                buffer = default!;
                return false;
            }
        }

        // 内部的なバッファ作成処理
        private IBuffer<TItem> CreateBufferInternal<TItem>(int length) where TItem : struct
        {
            // length が負でないことは呼び出し元でチェック済みと仮定する。
            // (このメソッドは private なので、呼び出し元でバリデーションされていればここでは不要)
            TItem[] array = new TItem[length];
            return new ManagedBuffer<TItem>(array, true); // takeOwnership: true
        }

        // プーリング関連の統計API (スタブ実装)
        public object GetPoolingOverallStatistics()
        {
            ThrowIfDisposed();
            // プーリングを行わないため、空またはデフォルトの情報を返す。
            // 具体的な型が決まるまでは object で対応。
            return new object(); // または return default!;
        }

        public IReadOnlyDictionary<string, object> GetPoolingBucketStatistics()
        {
            ThrowIfDisposed();
            // プーリングを行わないため、空のディクショナリを返す。
            return new Dictionary<string, object>();
        }

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
                    // マネージドリソースの解放 (このプロバイダは現時点では特に保持していない)
                }
                // アンマネージドリソースの解放 (このプロバイダは現時点では特に保持していない)
                _disposed = true;
            }
        }

        // デストラクタ (C#ではファイナライザと呼ばれる。アンマネージドリソースを持つ場合に重要)
        ~SimpleManagedProvider()
        {
            Dispose(false);
        }
    }
}