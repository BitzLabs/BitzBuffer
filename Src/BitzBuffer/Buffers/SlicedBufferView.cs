using System.Buffers;

namespace BitzLabs.BitzBuffer // または BitzLabs.BitzBuffer.Views など
{
    // IReadOnlyBuffer<T> の読み取り専用スライスビューを表します。
    // このクラスは、元のバッファのデータをコピーせず、その一部範囲への参照を提供します（ゼロコピー）。
    // データ自体の所有権は持たず、このビューをDisposeしても元のバッファには影響しません。
    public sealed class SlicedBufferView<T> : IReadOnlyBuffer<T> where T : struct
    {
        // 参照元のバッファインスタンス。このビューがDisposeされても、この参照先は解放されません。
        private readonly IReadOnlyBuffer<T> _sourceBuffer;
        // 元のバッファ内での、このスライスが開始する位置を示すオフセット。
        private readonly long _offset;
        // このスライスの論理的な長さ（要素数）。
        private readonly long _length;
        // このスライスビューインスタンス自体が破棄されたかどうかを示すフラグ。
        private bool _isDisposed;

        // このビューが破棄されている場合に ObjectDisposedException をスローするヘルパーメソッド。
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName, "破棄されたスライスビューに対して操作を実行しようとしました。");
            }
        }

        // 指定されたバッファが破棄されている場合に ObjectDisposedException をスローする汎用ヘルパーメソッド。
        private static void ThrowIfBufferIsDisposed(IReadOnlyBuffer<T> bufferToCheck, string bufferParameterName)
        {
            // bufferToCheck 自体のnullチェックは呼び出し元で行うか、ここでも行うか設計次第。
            // ArgumentNullException.ThrowIfNull(bufferToCheck, bufferParameterName); // 必要であれば追加
            if (bufferToCheck.IsDisposed)
            {
                throw new ObjectDisposedException(bufferParameterName ?? bufferToCheck.GetType().FullName, $"指定されたバッファ '{bufferParameterName}' は既に破棄されています。");
            }
        }

        // 新しい SlicedBufferView<T> インスタンスを初期化します。
        // sourceBuffer: スライスの元となる IReadOnlyBuffer<T> インスタンス。
        // offset: 元のバッファ内でのスライスの開始オフセット。
        // length: スライスの長さ。
        public SlicedBufferView(IReadOnlyBuffer<T> sourceBuffer, long offset, long length)
        {
            // 引数の検証: sourceBufferがnullでないことを確認。
            ArgumentNullException.ThrowIfNull(sourceBuffer, nameof(sourceBuffer));

            // 引数の検証: offsetとlengthが負でないことを確認。
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "オフセットは負の値であってはなりません。");
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "長さは負の値であってはなりません。");
            }

            // 参照元のバッファ(引数)が破棄されていないか確認。
            ThrowIfBufferIsDisposed(sourceBuffer, nameof(sourceBuffer));

            // 引数の検証: offsetとlengthの合計が参照元バッファの長さを超えないことを確認。
            // sourceBuffer.Length を呼び出す前に、sourceBuffer.IsDisposed をチェック済み。
            if (offset + length > sourceBuffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "オフセットと長さの合計が、参照元バッファの長さを超えています。");
            }

            _sourceBuffer = sourceBuffer;
            _offset = offset;
            _length = length;
            _isDisposed = false;
        }

        // --- IBufferState の実装 ---

        // このビューはデータの所有権を持たないため、常に false を返します。
        public bool IsOwner => false;
        // このビューインスタンス自体が破棄されているかどうかを示します。
        public bool IsDisposed => _isDisposed;

        // --- IReadOnlyBuffer<T> の実装 ---

        // このスライスの論理的な長さ（要素数）を返します。
        public long Length
        {
            get
            {
                ThrowIfDisposed(); // 自身の破棄状態をチェック
                return _length;
            }
        }
        // このスライスが空（長さが0）かどうかを示します。
        public bool IsEmpty
        {
            get
            {
                ThrowIfDisposed(); // 自身の破棄状態をチェック
                return _length == 0;
            }
        }

        // このスライスが単一の連続したメモリセグメントとして表現できるかどうかを示します。
        public bool IsSingleSegment
        {
            get
            {
                ThrowIfDisposed();
                ThrowIfBufferIsDisposed(_sourceBuffer, nameof(_sourceBuffer)); // フィールドの _sourceBuffer をチェック
                return _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length).IsSingleSegment;
            }
        }

        // このスライスビューを破棄済みとしてマークします。
        public void Dispose()
        {
            _isDisposed = true;
        }

        // このスライスの内容を表す ReadOnlySequence<T> を返します。
        public ReadOnlySequence<T> AsReadOnlySequence()
        {
            ThrowIfDisposed();
            ThrowIfBufferIsDisposed(_sourceBuffer, nameof(_sourceBuffer));
            return _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length);
        }

        // このスライスが単一の連続したメモリ領域として表現できる場合、trueを返し、そのメモリ領域を memory パラメータに出力します。
        public bool TryGetSingleMemory(out ReadOnlyMemory<T> memory)
        {
            if (_isDisposed) // 自身の破棄状態を最初にチェック
            {
                memory = default;
                return false; // 例外ではなく false を返す
            }
            // 自身が破棄されていなければ、次に元バッファの破棄状態をチェック
            if (_sourceBuffer.IsDisposed) // IsDisposed プロパティは例外をスローしない想定
            {
                memory = default;
                return false; // 元バッファが破棄されていても false を返す
            }

            // 元のバッファと自身が破棄されていなければ、通常の処理を試みる
            var slicedSequence = _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length);
            if (slicedSequence.IsSingleSegment)
            {
                memory = slicedSequence.First;
                return true;
            }

            memory = default;
            return false;
        }

        // このスライスが単一の連続したスパンとして表現できる場合、trueを返し、そのスパンを span パラメータに出力します。
        public bool TryGetSingleSpan(out ReadOnlySpan<T> span)
        {
            if (_isDisposed) // 自身の破棄状態を最初にチェック
            {
                span = default;
                return false; // 例外ではなく false を返す
            }
            // 自身が破棄されていなければ、次に元バッファの破棄状態をチェック
            if (_sourceBuffer.IsDisposed)
            {
                span = default;
                return false; // 元バッファが破棄されていても false を返す
            }

            // 元のバッファと自身が破棄されていなければ、通常の処理を試みる
            var slicedSequence = _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length);
            if (slicedSequence.IsSingleSegment)
            {
                span = slicedSequence.FirstSpan;
                return true;
            }

            span = default;
            return false;
        }

        // このスライスビューからさらに指定された範囲の新しいスライスビューを作成します。
        public IReadOnlyBuffer<T> Slice(long start, long length)
        {
            ThrowIfDisposed(); // 自身の破棄状態をチェック
            // 元のバッファ (_sourceBuffer) の破棄状態は、新しい SlicedBufferView のコンストラクタ内でチェックされる。


            // 新しいスライスの範囲はこのビューの長さ (this.Length) を基準に検証。
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "新しいスライスの開始オフセットは負の値であってはなりません。");
            }
            // start が現在のビューの長さを超えていないか先にチェック
            if (start > this.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "新しいスライスの開始オフセットが、現在のスライスビューの長さを超えています。");
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "新しいスライスの長さは負の値であってはなりません。");
            }
            // このチェックは start が有効な範囲内にある前提で行う
            if (start + length > this.Length) 
            {
                throw new ArgumentOutOfRangeException(nameof(length), "新しいスライスのオフセットと長さの合計が、現在のスライスビューの長さを超えています。");
            }

            return new SlicedBufferView<T>(_sourceBuffer, _offset + start, length);
        }

        // このスライスビューから指定された開始位置以降の全ての範囲を表す新しいスライスビューを作成します。
        public IReadOnlyBuffer<T> Slice(long start)
        {
            ThrowIfDisposed();
            // 元のバッファ (_sourceBuffer) の破棄状態は、新しい SlicedBufferView のコンストラクタ内でチェックされる。

            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "新しいスライスの開始オフセットは負の値であってはなりません。");
            }
            // this.Length を使うことで自身のDisposeチェックが入る
            if (start > this.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "新しいスライスの開始オフセットが、現在のスライスビューの長さを超えています。");
            }

            return new SlicedBufferView<T>(_sourceBuffer, _offset + start, this.Length - start);
        }

        // public IEnumerable<BitzBufferSequenceSegment<T>> AsAttachableSegments()
        // {
        //     ThrowIfDisposed();
        //     ThrowIfBufferIsDisposed(_sourceBuffer, nameof(_sourceBuffer));
        //     throw new NotImplementedException("AsAttachableSegments for SlicedBufferView<T> is not yet implemented.");
        // }

        public override string ToString()
        {
            // _sourceBuffer の IsDisposed を安全にアクセスするために null 条件演算子を使用。
            // ただし、コンストラクタで null でないことが保証され、readonly なので、通常は null にならない。
            // Disposeされていなければ、this.Length で自身の長さを取得。
            string sourceDisposedStatus = _sourceBuffer?.IsDisposed.ToString() ?? "null_source";
            return $"SlicedBufferView<{typeof(T).Name}>[Offset={_offset}, Length={(_isDisposed ? _length.ToString() : this.Length.ToString())}, SourceDisposed={sourceDisposedStatus}, Disposed={_isDisposed}]";
        }
    }
}