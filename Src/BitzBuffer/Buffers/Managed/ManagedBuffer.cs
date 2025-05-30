using System.Buffers;
using System.Runtime.CompilerServices;

namespace BitzLabs.BitzBuffer.Managed
{
    public class ManagedBuffer<T> : IBuffer<T> where T : struct
    {
        // 内部で保持するマネージド配列。Dispose時にnullになる可能性があるためnull許容型。
        private T[]? _array;
        // 現在バッファに書き込まれている論理的な要素数。
        private int _length;
        // このインスタンスが基になる配列リソースの所有権を持つかどうか。
        private bool _isOwner;
        // このバッファインスタンスが破棄済みかどうか。
        private bool _isDisposed;
        // プーリング実装時に、自身が属するプールへの参照を保持するために使用 (M2で実装)。
        // private readonly IBufferPool<ManagedBuffer<T>>? _pool;

        // 指定された配列をラップし、所有権の有無を指定するコンストラクタ。
        // ライブラリ内部でのみ呼び出されることを想定 (internal)。
        internal ManagedBuffer(T[] array, bool takeOwnership)
        {
            ArgumentNullException.ThrowIfNull(array, nameof(array));
            _array = array;
            _isOwner = takeOwnership;
            _length = 0; // 初期状態では書き込み済みデータはないものとする (方針D1)。
            _isDisposed = false;
        }

        // プールからレンタルされる場合のコンストラクタの例 (M2で詳細を検討・実装)。
        // internal ManagedBuffer(T[] array, IBufferPool<ManagedBuffer<T>> pool)
        // {
        //     _array = array;
        //     _isOwner = true; // プールが配列を所有するが、レンタル中は利用者がバッファインスタンスの一時的な所有者となる。
        //     _length = 0;
        //     _isDisposed = false;
        //     _pool = pool;
        // }

        // バッファが破棄済みの場合に ObjectDisposedException をスローするヘルパーメソッド。
        // パフォーマンスクリティカルな箇所でのインライン化を期待。
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName, "破棄されたバッファに対して操作を実行しようとしました。");
            }
        }

        // 書き込み操作の前に、このインスタンスが所有権を持っていない場合に InvalidOperationException をスローするヘルパーメソッド。
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfNotOwnerForWrite()
        {
            if (!_isOwner)
            {
                throw new InvalidOperationException("所有権のないバッファに対して書き込み操作を実行することはできません。");
            }
        }

        // --- IBufferState ---
        public bool IsOwner => _isOwner;
        public bool IsDisposed => _isDisposed;

        // --- IReadOnlyBuffer<T> ---
        public long Length
        {
            get
            {
                ThrowIfDisposed();
                return _length;
            }
        }
        public bool IsEmpty
        {
            get
            {
                ThrowIfDisposed();
                return _length == 0;
            }
        }
        public bool IsSingleSegment
        {
            get
            {
                ThrowIfDisposed();
                return true;
            }
        } 

        public ReadOnlySequence<T> AsReadOnlySequence()
        {
            ThrowIfDisposed(); // この呼び出し後、_isDisposed は false。
                               // _isDisposed が false の場合、_array は null ではないことが期待される。

            return new ReadOnlySequence<T>(_array!, 0, _length);
        }

        public bool TryGetSingleSpan(out ReadOnlySpan<T> span)
        {
            ThrowIfDisposed(); // この呼び出し後、_isDisposed は false。
                               // _isDisposed が false の場合、_array は null ではないことが期待される。

            if (_length > 0)
            {
                span = new ReadOnlySpan<T>(_array!, 0, _length);
                return true;
            }
            span = ReadOnlySpan<T>.Empty;
            return false;
        }

        public bool TryGetSingleMemory(out ReadOnlyMemory<T> memory)
        {
            ThrowIfDisposed(); // この呼び出し後、_isDisposed は false。
                               // _isDisposed が false の場合、_array は null ではないことが期待される。

            if (_length > 0)
            {
                memory = new ReadOnlyMemory<T>(_array!, 0, _length);
                return true;
            }
            memory = ReadOnlyMemory<T>.Empty;
            return false;
        }

        public IReadOnlyBuffer<T> Slice(long start, long length)
        {
            ThrowIfDisposed();
            // スライス範囲は現在の論理長(_length)を基準とする。
            if (start < 0 || start > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), $"引数 start ({start}) が不正です。0以上かつ現在の長さ ({_length}) 以下である必要があります。");
            }
            if (length < 0 || start + length > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(length), $"引数 length ({length}) が不正です。要求されたスライス範囲 [{start}-{start + length - 1}] は現在の長さ ({_length}) の範囲外です。");
            }
            // SlicedBufferView<T> を返す（ゼロコピー）
            return new SlicedBufferView<T>(this, start, length);
        }

        public IReadOnlyBuffer<T> Slice(long start)
        {
            ThrowIfDisposed();
            if (start < 0 || start > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), $"引数 start ({start}) が不正です。0以上かつ現在の長さ ({_length}) 以下である必要があります。");
            }
            // 残りの部分全てをスライスする。
            return Slice(start, _length - start);
        }

        // --- IWritableBuffer<T> ---
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            int capacity = _array!.Length;
            int remainingCapacity = capacity - _length;

            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint), "引数 sizeHint は0以上である必要があります。");
            }

            // GetMemory が返すメモリサイズの決定ポリシー:
            // - 方針C1 (現在の実装):     sizeHint が 0 の場合、バッファの残りの全容量を返そうとします。
            //                           これは、利用可能な領域を最大限に提供するシンプルなアプローチです。
            // - 方針C2 (将来の検討事項): sizeHint が 0 の場合や非常に大きな場合に、
            //                           パフォーマンスやAPIの予測可能性の観点から、
            //                           デフォルトのチャンクサイズ上限（例: 4096バイトやプロバイダオプションで設定可能な値）を
            //                           設けることも考えられます。以下の TODO コメント参照。
            int effectiveSizeHint;
            if (sizeHint == 0)
            {
                effectiveSizeHint = Math.Max(0, remainingCapacity); // 方針C1: sizeHintが0なら残りの全容量;
            }
            else
            {
                effectiveSizeHint = sizeHint;
            }

            // TODO (方針C2): 将来的な検討事項として、sizeHintが0の場合や非常に大きな場合に
            // デフォルトのチャンクサイズ上限（例: 4096）を設けることも考えられる。
            // 例: if (sizeHint == 0 && remainingCapacity > 0) {
            //         effectiveSizeHint = Math.Min(remainingCapacity, DefaultChunkSize);
            //     } else if (sizeHint == 0) {
            //         effectiveSizeHint = 0;
            //     } else {
            //         effectiveSizeHint = sizeHint;
            //     }
            // DefaultChunkSize はプロバイダオプション等で設定。
            int actualSize = Math.Min(remainingCapacity, effectiveSizeHint);
            return _array!.AsMemory(_length, actualSize);
        }

        public void Advance(int count)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "引数 count は0以上である必要があります。");
            }
            if (_length + count > _array!.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"指定された要素数 ({count}) だけ進めると、バッファの物理的な容量 ({_array!.Length}) を超えます。現在の長さ: {_length}。");
            }

            _length += count;
        }

        public void Write(ReadOnlySpan<T> source)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            if (source.Length > _array!.Length - _length)
            {
                throw new ArgumentException($"書き込むソースデータ (長さ: {source.Length}) が、バッファの残り容量 (空き: {_array!.Length - _length}) を超えています。", nameof(source));
            }

            Memory<T> destination = _array!.AsMemory(_length, source.Length);
            source.CopyTo(destination.Span);
            _length += source.Length;
        }

        public void Write(ReadOnlyMemory<T> source)
        {
            // ReadOnlyMemory<T> から ReadOnlySpan<T> を取得して共通のWriteメソッドを呼び出す。
            Write(source.Span);
        }

        public void Write(T value)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            // 単一要素を書き込むスペースがあるか確認。
            if (_array!.Length - _length < 1)
            {
                throw new ArgumentException("単一の値を書き込むための十分な空き容量がバッファにありません。", nameof(value));
            }

            _array![_length] = value;
            _length += 1;
        }

        public void Write(ReadOnlySequence<T> source)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            long sourceLength = source.Length;
            if (sourceLength > _array!.Length - _length)
            {
                throw new ArgumentException($"書き込むソースシーケンス (長さ: {sourceLength}) が、バッファの残り容量 (空き: {_array!.Length - _length}) を超えています。", nameof(source));
            }

            if (source.IsSingleSegment)
            {
                // 単一セグメントの場合は、そのSpanを直接書き込む方が効率的。
                Write(source.FirstSpan);
            }
            else
            {
                // 複数セグメントの場合は、各セグメントを順に書き込む。
                foreach (ReadOnlyMemory<T> segment in source)
                {
                    // このWrite呼び出しが内部で_lengthを更新する。
                    Write(segment.Span);
                }
            }
        }

        public AttachmentResult AttachSequence(ReadOnlySequence<T> sequenceToAttach, bool attemptZeroCopy = true)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            // ManagedBuffer<T> は連続した単一配列を持つため、外部シーケンスのアタッチは常に内容のコピーとなる。
            Write(sequenceToAttach);
            return AttachmentResult.AttachedAsCopy;
        }

        // 設計書(02_Providers_And_Buffers.md 4.1.1.)で言及されている IReadOnlyBuffer<T> を引数に取る AttachSequence オーバーロード。
        // IWritableBuffer<T> インターフェースに追加する場合に実装する。
        // public AttachmentResult AttachSequence(IReadOnlyBuffer<T> sourceBitzBuffer, bool attemptZeroCopy = true)
        // {
        //     ThrowIfDisposed();           // この呼び出しにより、_isDisposed が false であることが保証される。
        //     ThrowIfNotOwnerForWrite();   // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
        //                                  // _isDisposed が false の場合、_array は null ではないことが期待される。
        //     Write(sourceBitzBuffer.AsReadOnlySequence());
        //     return AttachmentResult.AttachedAsCopy;
        // }

        // IWritableBuffer<T> インターフェースで定義される可能性のある TryAttachZeroCopy(ReadOnlySequence<T>)。
        // ManagedBuffer<T> は外部シーケンスのセグメントをゼロコピーでリンクしないため、常に false を返す。
        public bool TryAttachZeroCopy(ReadOnlySequence<T> sequenceToAttach)
        {
            // ゼロコピー試行が失敗した場合に自動的にコピーを行うかなどは設計判断。現状は常にfalse。
            return false;
        }

        // 設計書で想定されている IEnumerable<BitzBufferSequenceSegment<T>> を引数に取る TryAttachZeroCopy。
        // IWritableBuffer<T> インターフェースに追加する場合に実装する。
        // public bool TryAttachZeroCopy(IEnumerable<BitzBufferSequenceSegment<T>> segmentsToAttach)
        // {
        //     // ManagedBuffer<T> は外部の複数セグメントをゼロコピーで取り込むことはサポートしない。
        //     return false;
        // }

        public void Prepend(ReadOnlySpan<T> source)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            // ManagedBuffer<T> は固定長の配列ベースであり、先頭への効率的な挿入は困難。
            // (既存データを全て後方にシフトする必要があるため、パフォーマンスが悪い)。
            // M1のスコープでは実装しない。将来的にサポートする場合、詳細な設計が必要。
            throw new NotImplementedException("Prepend 操作は ManagedBuffer<T> では効率的にサポートされていないか、まだ実装されていません。");
        }

        public void Prepend(ReadOnlyMemory<T> source)
        {
            // ReadOnlySpan<T> 版の Prepend を呼び出す。
            Prepend(source.Span);
        }

        public void Prepend(ReadOnlySequence<T> source)
        {
            // ReadOnlySpan<T> 版の Prepend が実装された場合に、このメソッドも実装する。
            // ReadOnlySequence<T> を一時的なSpanにまとめるか、逆順に処理する必要がある。
            throw new NotImplementedException("ReadOnlySequence<T> からの Prepend 操作はまだ実装されていません。");
        }

        public void Clear()
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            _length = 0; // 論理的な長さを0にリセット。
                         // TODO (プーリング - M2): プーリング時のクリアポリシー (返却時/レンタル時/クリアしない) を考慮し、
                         // 必要に応じて Array.Clear(_array!, 0, _array!.Length) を実行する。
                         // _array! の使用は、この時点で _array が非nullであることが期待されるため。
                         // 現状は論理長のリセットのみ。
        }

        public void Truncate(long length)
        {
            ThrowIfDisposed();          // この呼び出しにより、_isDisposed が false であることが保証される。
            ThrowIfNotOwnerForWrite();  // この呼び出しにより、_isOwner が true であることが保証される (書き込み操作の場合)。
                                        // _isDisposed が false の場合、_array は null ではないことが期待される。

            // length は 0以上、かつ現在の論理長 _length 以下である必要がある。
            // _length は int 型なので、この時点で length が int.MaxValue を超えることはない。
            if (length < 0 || length > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(length), $"要求された切り詰め後の長さ ({length}) が不正です。0以上かつ現在の長さ ({_length}) 以下である必要があります。");
            }

            // 上記のバリデーションにより、length は int の範囲内に収まることが保証されているため、
            // ここでの (int)length キャストは安全。
            _length = (int)length;
            // TODO (設計ポリシー): Truncate時に切り捨てられた部分のデータをクリアするかどうか検討。
            // セキュリティやメモリ管理のポリシーによる。
        }

        // --- IDisposable ---
        public void Dispose()
        {
            if (_isDisposed)
            {
                return; // 既に破棄済みなら何もしない。
            }

            // TODO (プーリング - M2): プール管理下の場合は、プールへ返却するロジックをここに実装する。
            // その際、ライフサイクルフック (OnReturn) の呼び出しや、ResetForReuse の実行も考慮する。
            // if (_pool != null && _isOwner) // _isOwner の意味合いはプール文脈で再検討が必要。
            // {
            //    // プールへの返却処理。
            //    // _pool.Return(this);
            // }
            // else 
            if (_isOwner && _array is not null) // 所有権があり、かつプール管理外で、配列がまだ存在する場合 (方針E)。
            {
                // 所有権があり、プール管理外の場合、配列への参照をnullに設定します。
                // これにより、GCによるメモリ解放が促されます。
                // 注意: この操作は配列の「内容」をクリアしません。
                // 配列に機密データが含まれていた場合、メモリ上にそのデータが残存する可能性があります。
                // 必要に応じて、利用者はDispose前に Array.Clear() を呼び出すか、
                // 将来提供されるクリアオプション付きのClearメソッドを使用してください。
                // プーリング使用時は、プール返却時のクリアポリシーが適用されます (M2で実装予定)。
                _array = null;
        }

            _isDisposed = true;
            _isOwner = false; // Dispose後は、このインスタンスはリソースの所有権を失う。
        }

        // --- IReadOnlyBuffer<T> の AsAttachableSegments (設計書 02_Providers_And_Buffers.md 4.1.1. より) ---
        // このメソッドを実装するには、BitzBufferSequenceSegment<T> (Issue #33) の定義が必要。
        // public IEnumerable<BitzBufferSequenceSegment<T>> AsAttachableSegments()
        // {
        //     ThrowIfDisposed();
        //     // この時点で _isDisposed は false。_array は null ではないという契約。
        //
        //     // ManagedBuffer<T> は単一セグメントで構成される。
        //     // SegmentSpecificOwner は、この ManagedBuffer<T> インスタンス自身か、
        //     // または内部配列を管理する IDisposable オブジェクト (例: プールからレンタルした場合の配列ラッパー)。
        //     // SourceBuffer は `this` (この ManagedBuffer<T> インスタンス)。
        //     // IsOwnershipTransferred は初期状態では false。
        //     // IsEligibleForOwnershipTransfer は、このバッファが所有権を持ち、かつ破棄されていないなどの条件で true。
        //
        //     // yield return new BitzBufferSequenceSegment<T>(
        //     //     new ReadOnlyMemory<T>(_array!, 0, _length),
        //     //     segmentSpecificOwner: this, // または適切な所有者オブジェクト
        //     //     sourceBuffer: this,
        //     //     isOwnershipTransferred: false,
        //     //     isEligibleForOwnershipTransfer: _isOwner && !_isDisposed 
        //     // );
        //     throw new NotImplementedException("AsAttachableSegments の実装には BitzBufferSequenceSegment<T> (Issue #33) が必要です。");
        // }

        // --- Object overrides ---
        public override string ToString()
        {
            // Dispose後に _array が null になる可能性があるため、null条件演算子で安全にアクセス。
            return $"ManagedBuffer<{typeof(T).Name}>[Length={_length}, Capacity={_array?.Length ?? 0}, Owner={_isOwner}, Disposed={_isDisposed}]";
            // TODO (プーリング - M2): プールによって管理されている場合、プールIDや "[Pooled]" といった識別子を ToString() の結果に追加する。
        }

        // --- IResettableBuffer (設計書 03_Pooling.md より) - M2のプーリング実装時に必要 ---
        // プールされたバッファが再利用のためにリセット可能であることを示すインターフェースの実装。
        // public void ResetForReuse(int capacityHint)
        // {
        //     // ThrowIfDisposed(); // プール側で呼び出す前に、このバッファが破棄されていないことを確認するべき。
        //     if (!_isDisposed) // プールは破棄済みのバッファをリセットしようとしない想定。
        //     {
        //         _length = 0;
        //         _isOwner = true; // プールから再レンタルされる際、一時的に所有権が回復する。
        //         _isDisposed = false;
        //         // capacityHint は、ManagedBuffer<T> の場合、内部配列のサイズ変更には使用しない (固定長のため)。
        //         // ただし、クリアポリシーによっては、このタイミングで配列の内容をクリアする必要があるかもしれない。
        //     }
        //     else
        //     {
        //         // 破棄済みのバッファをリセットしようとした場合の処理 (ログ記録や例外スローなど)。
        //         // 通常はプール側のロジックでこのような呼び出しは避けるべき。
        //     }
        // }
    }
}