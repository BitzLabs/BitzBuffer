using System;
using System.Buffers;

namespace BitzLabs.BitzBuffer
{
    // 読み取り専用の連続または非連続メモリ領域へのアクセスを提供します。
    // IOwnedResource を継承し、リソースの所有権とライフサイクル管理の責務を持ちます。
    public interface IReadOnlyBuffer<TItem> : IOwnedResource where TItem : struct
    {
        // バッファの現在の読み取り可能な長さを要素単位で示します。
        // 非連続バッファの場合、全セグメントの合計長です。
        long Length { get; }

        // バッファが空かどうかを示します。Length == 0 と同等です。
        bool IsEmpty { get; }

        // バッファが単一の連続したメモリセグメントで構成されているかどうかを示します。
        // true の場合、TryGetSingleMemory や TryGetSingleSpan が成功する可能性が高いことを示唆します。
        bool IsSingleSegment { get; }

        // バッファ全体を ReadOnlySequence<TItem> として取得します。
        // これにより、非連続メモリを効率的に扱うことができます。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローする場合があります。
        ReadOnlySequence<TItem> AsReadOnlySequence();

        // バッファの先頭から指定された長さのデータを新しい IReadOnlyBuffer<TItem> としてスライスします。
        // 元のバッファの所有権は保持され、スライスは元のバッファのビューとなります。
        // length が現在の Length を超える場合、ArgumentOutOfRangeException をスローします。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        IReadOnlyBuffer<TItem> Slice(long start, long length);

        // バッファの指定された開始位置から終端までのデータを新しい IReadOnlyBuffer<TItem> としてスライスします。
        // 元のバッファの所有権は保持され、スライスは元のバッファのビューとなります。
        // start が負数または Length を超える場合、ArgumentOutOfRangeException をスローします。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        IReadOnlyBuffer<TItem> Slice(long start);


        // バッファが単一の連続した ReadOnlyMemory<TItem> セグメントで表現できる場合に、そのセグメントを取得しようとします。
        // 成功した場合は true を返し、memory にセグメントが設定されます。
        // 失敗した場合 (非連続メモリの場合など) は false を返します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローする場合があります。
        bool TryGetSingleMemory(out ReadOnlyMemory<TItem> memory);

        // バッファが単一の連続した ReadOnlySpan<TItem> セグメントで表現できる場合に、そのセグメントを取得しようとします。
        // 成功した場合は true を返し、span にセグメントが設定されます。
        // 失敗した場合 (非連続メモリの場合など) は false を返します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローする場合があります。
        bool TryGetSingleSpan(out ReadOnlySpan<TItem> span);
    }
}