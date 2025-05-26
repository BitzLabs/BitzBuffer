using System;
using System.Buffers;

namespace BitzLabs.BitzBuffer
{
    // 読み取り専用の連続または非連続メモリ領域へのアクセスを提供します。
    // IOwnedResource を継承し、リソースの所有権とライフサイクル管理の責務を持ちます。
    public interface IReadOnlyBuffer<T> : IOwnedResource where T : struct
    {
        // バッファの現在の読み取り可能な長さをバイト単位で示します。
        // 非連続バッファの場合、全セグメントの合計長です。
        long Length { get; }

        // バッファが空かどうかを示します。Length == 0 と同等です。
        bool IsEmpty { get; }

        // バッファ全体を ReadOnlySequence<T> として取得します。
        // これにより、非連続メモリを効率的に扱うことができます。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        ReadOnlySequence<T> AsReadOnlySequence();

        // バッファの先頭から指定された長さのデータを新しい IReadOnlyBuffer<T> としてスライスします。
        // 元のバッファの所有権は保持され、スライスは元のバッファのビューとなります。
        // length が現在の Length を超える場合、ArgumentOutOfRangeException をスローします。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        IReadOnlyBuffer<T> Slice(long offset, long length);

        // バッファが単一の連続した ReadOnlyMemory<T> セグメントで表現できる場合に、そのセグメントを取得しようとします。
        // 成功した場合は true を返し、memory にセグメントが設定されます。
        // 失敗した場合 (非連続メモリの場合など) は false を返します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        bool TryGetSingleMemory(out ReadOnlyMemory<T> memory);

        // バッファが単一の連続した ReadOnlySpan<T> セグメントで表現できる場合に、そのセグメントを取得しようとします。
        // 成功した場合は true を返し、span にセグメントが設定されます。
        // 失敗した場合 (非連続メモリの場合など) は false を返します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        bool TryGetSingleSpan(out ReadOnlySpan<T> span);

        // 指定されたインデックスから始まるデータを、指定された Span<T> にコピーします。
        // コピーされるバイト数は、destination の長さと、sourceIndex からバッファの終端までの長さのうち、小さい方になります。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        // sourceIndex が負数または Length 以上の場合、ArgumentOutOfRangeException をスローします。
        void CopyTo(long sourceIndex, Span<T> destination);
    }
}