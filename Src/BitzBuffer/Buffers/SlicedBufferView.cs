using System;
using System.Buffers;
using BitzLabs.BitzBuffer; // IReadOnlyBuffer<T> などがこの名前空間にあると想定

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

        // 参照元のバッファが破棄されている場合に ObjectDisposedException をスローするヘルパーメソッド。
        private void ThrowIfSourceDisposed()
        {
            if (_sourceBuffer.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(_sourceBuffer), "参照元のバッファが既に破棄されています。");
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

            // 参照元のバッファが破棄されていないか確認。
            ThrowIfSourceDisposed();

            // 引数の検証: offsetとlengthの合計が参照元バッファの長さを超えないことを確認。
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
        public long Length => _length;
        // このスライスが空（長さが0）かどうかを示します。
        public bool IsEmpty => _length == 0;

        // このスライスが単一の連続したメモリセグメントとして表現できるかどうかを示します。
        // 元のバッファの該当範囲を ReadOnlySequence<T>としてスライスし、
        // その結果が単一セグメントであるかで判断します。
        public bool IsSingleSegment
        {
            get
            {
                ThrowIfDisposed();
                ThrowIfSourceDisposed();
                return _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length).IsSingleSegment;
            }
        }

        // このスライスビューを破棄済みとしてマークします。
        // 元のバッファのリソース解放は行いません。これはビューの破棄であり、参照先データには影響しません。
        public void Dispose()
        {
            _isDisposed = true;
        }

        // このスライスの内容を表す ReadOnlySequence<T> を返します。
        // 元のバッファの ReadOnlySequence<T> を取得し、このスライスの範囲で切り出したものを返します。
        public ReadOnlySequence<T> AsReadOnlySequence()
        {
            ThrowIfDisposed();
            ThrowIfSourceDisposed();
            // 元バッファのシーケンスを取得し、このビューのオフセットと長さでスライスする。
            // ReadOnlySequence<T>.Slice はゼロコピーで効率的です。
            return _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length);
        }

        // このスライスが単一の連続したメモリ領域として表現できる場合、trueを返し、そのメモリ領域を memory パラメータに出力します。
        public bool TryGetSingleMemory(out ReadOnlyMemory<T> memory)
        {
            ThrowIfDisposed();
            ThrowIfSourceDisposed();

            // このビューの範囲を表すReadOnlySequence<T>を取得。
            var slicedSequence = _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length);
            if (slicedSequence.IsSingleSegment)
            {
                // スライスされたシーケンスが単一セグメントであれば、その最初の(唯一の)メモリブロックを返す。
                // ReadOnlyMemory<T>.Length は int なので、この時点で _length が int の範囲に収まっていることが期待される。
                memory = slicedSequence.First;
                return true;
            }

            memory = default;
            return false;
        }

        // このスライスが単一の連続したスパンとして表現できる場合、trueを返し、そのスパンを span パラメータに出力します。
        public bool TryGetSingleSpan(out ReadOnlySpan<T> span)
        {
            ThrowIfDisposed();
            ThrowIfSourceDisposed();

            // このビューの範囲を表すReadOnlySequence<T>を取得。
            var slicedSequence = _sourceBuffer.AsReadOnlySequence().Slice(_offset, _length);
            if (slicedSequence.IsSingleSegment)
            {
                // スライスされたシーケンスが単一セグメントであれば、その最初の(唯一の)スパンを返す。
                // ReadOnlySpan<T>.Length は int なので、この時点で _length が int の範囲に収まっていることが期待される。
                span = slicedSequence.FirstSpan;
                return true;
            }

            span = default;
            return false;
        }

        // このスライスビューからさらに指定された範囲の新しいスライスビューを作成します。
        // start: このスライスビュー内での新しいスライスの開始オフセット。
        // length: 新しいスライスの長さ。
        public IReadOnlyBuffer<T> Slice(long start, long length)
        {
            ThrowIfDisposed();
            // 元のバッファ (_sourceBuffer) の破棄状態は、新しい SlicedBufferView のコンストラクタ内でチェックされる。

            // 新しいスライスの範囲はこのビューの長さ (_length) を基準に検証。
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "新しいスライスの開始オフセットは負の値であってはなりません。");
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "新しいスライスの長さは負の値であってはなりません。");
            }
            if (start + length > _length) // _length はこのスライスビューの長さ
            {
                throw new ArgumentOutOfRangeException(nameof(length), "新しいスライスのオフセットと長さの合計が、現在のスライスビューの長さを超えています。");
            }

            // 新しいスライスビューを作成。オフセットは元の「ルート」バッファ基準で計算し直す。
            // _sourceBuffer は変わらず、このビューのオフセットに新しい開始位置を加算する。
            return new SlicedBufferView<T>(_sourceBuffer, _offset + start, length);
        }

        // このスライスビューから指定された開始位置以降の全ての範囲を表す新しいスライスビューを作成します。
        // start: このスライスビュー内での新しいスライスの開始オフセット。
        public IReadOnlyBuffer<T> Slice(long start)
        {
            ThrowIfDisposed();
            // 元のバッファ (_sourceBuffer) の破棄状態は、新しい SlicedBufferView のコンストラクタ内でチェックされる。

            // 新しいスライスの開始位置をこのビューの長さ (_length) を基準に検証。
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "新しいスライスの開始オフセットは負の値であってはなりません。");
            }
            if (start > _length) // _length はこのスライスビューの長さ
            {
                throw new ArgumentOutOfRangeException(nameof(start), "新しいスライスの開始オフセットが、現在のスライスビューの長さを超えています。");
            }

            // 新しいスライスビューを作成。オフセットは元の「ルート」バッファ基準で計算し直す。
            return new SlicedBufferView<T>(_sourceBuffer, _offset + start, _length - start);
        }

        // 設計書 (02_Providers_And_Buffers.md) の SlicedBufferView<T> セクションで AsAttachableSegments が言及されている場合、
        // その実装が必要になります。IReadOnlyBuffer<T> の一部として定義されていれば、ここに実装します。
        // public IEnumerable<BitzBufferSequenceSegment<T>> AsAttachableSegments()
        // {
        //     ThrowIfDisposed();
        //     ThrowIfSourceDisposed();
        //
        //     // 元のバッファの AsAttachableSegments() の結果を、このスライスの範囲 (_offset, _length) に合わせて
        //     // 適切にフィルタリングまたは調整して返す必要があります。
        //     // 各セグメントの所有者情報は元のバッファのものを参照しますが、このスライスビュー自体は所有権を持ちません。
        //     // この実装は BitzBufferSequenceSegment<T> (Issue #33) の定義と、
        //     // 元の IReadOnlyBuffer<T> の AsAttachableSegments の具体的な動作に依存します。
        //     throw new NotImplementedException("AsAttachableSegments for SlicedBufferView<T> is not yet implemented.");
        // }

        // Object.ToString() をオーバーライドして、デバッグに有用な情報を表示します。
        public override string ToString()
        {
            return $"SlicedBufferView<{typeof(T).Name}>[Offset={_offset}, Length={_length}, SourceDisposed={_sourceBuffer?.IsDisposed.ToString() ?? "null"}, Disposed={_isDisposed}]";
        }
    }
}