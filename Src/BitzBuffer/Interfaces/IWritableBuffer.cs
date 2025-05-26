using System;

namespace BitzLabs.BitzBuffer
{
    // 書き込み可能な連続または非連続メモリ領域へのアクセスを提供します。
    // IOwnedResource を継承し、リソースの所有権とライフサイクル管理の責務を持ちます。
    public interface IWritableBuffer<T> : IOwnedResource where T : struct
    {
        // バッファの現在の書き込み可能な容量をバイト単位で示します。
        // これは、バッファが現在保持しているデータの長さではなく、書き込み可能な最大サイズです。
        long Capacity { get; }

        // バッファに現在書き込まれているデータの長さをバイト単位で示します。
        // 初期状態では 0 です。Advance() メソッドによって進められます。
        long WrittenCount { get; }

        // バッファの残りの書き込み可能な容量をバイト単位で示します。
        // Capacity - WrittenCount と同等です。
        long FreeCapacity { get; }

        // バッファの先頭から書き込み用の Memory<T> を取得します。
        // sizeHint で要求されたサイズ以上の連続したメモリ領域を返そうと試みます。
        // 実際に取得できるサイズは、基になるバッファの実装に依存します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        // sizeHint が負数の場合、ArgumentOutOfRangeException をスローします。
        Memory<T> GetMemory(int sizeHint = 0);

        // バッファの先頭から書き込み用の Span<T> を取得します。
        // sizeHint で要求されたサイズ以上の連続したメモリ領域を返そうと試みます。
        // 実際に取得できるサイズは、基になるバッファの実装に依存します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        // sizeHint が負数の場合、ArgumentOutOfRangeException をスローします。
        Span<T> GetSpan(int sizeHint = 0);

        // 指定されたバイト数だけ書き込みポインタを進めます。
        // これにより、GetMemory() や GetSpan() で取得した領域にデータが書き込まれたことをバッファに通知します。
        // count が負数、または FreeCapacity を超える場合、ArgumentOutOfRangeException をスローします。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        void Advance(int count);

        // バッファの書き込み位置をリセットし、WrittenCount を 0 にします。
        // バッファの内容はクリアされません。既存のデータを上書きする準備をします。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        void Clear();

        // バッファに書き込まれた内容を IReadOnlyBuffer<T> として取得します。
        // このメソッドを呼び出すと、現在の IWritableBuffer<T> インスタンスの所有権は失われ (IsOwner = false)、
        // 新しく返される IReadOnlyBuffer<T> がリソースの所有権を引き継ぎます。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        IReadOnlyBuffer<T> AsReadOnly();
    }
}