using System;
using System.Buffers; // For ReadOnlySequence<TItem>

namespace BitzLabs.BitzBuffer
{
    // 書き込み可能な連続または非連続メモリ領域へのアクセスを提供します。
    // IBufferState を継承し、バッファの状態管理の責務を持ちます。
    // 書き込み操作に特化しており、IDisposable は直接継承しません。
    public interface IWritableBuffer<TItem> : IBufferState where TItem : struct
    {
        // バッファの先頭から書き込み用の Memory<TItem> を取得します。
        // sizeHint で要求されたサイズ以上の連続したメモリ領域を返そうと試みます。
        // 実際に取得できるサイズは、基になるバッファの実装に依存します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        // sizeHint が負数の場合、ArgumentOutOfRangeException をスローします。
        Memory<TItem> GetMemory(int sizeHint = 0);

        // 指定されたバイト数だけ書き込みポインタを進めます。
        // これにより、GetMemory() で取得した領域にデータが書き込まれたことをバッファに通知します。
        // count が負数、または FreeCapacity を超える場合、ArgumentOutOfRangeException をスローします。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        void Advance(int count);

        // バッファの書き込み位置をリセットし、WrittenCount を 0 にします。
        // バッファの内容はクリアされません。既存のデータを上書きする準備をします。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローします。
        void Clear();

        // source の内容をバッファに書き込みます。
        void Write(ReadOnlySpan<TItem> source);

        // source の内容をバッファに書き込みます。
        void Write(ReadOnlyMemory<TItem> source);

        // value をバッファに書き込みます。
        void Write(TItem value);

        // source の内容をバッファに書き込みます。
        void Write(ReadOnlySequence<TItem> source);

        // 指定されたシーケンスを現在のバッファにアタッチしようと試みます。
        // ゼロコピーが可能な場合はゼロコピーでアタッチし、そうでない場合は allowCopy が true であればコピーしてアタッチします。
        // 戻り値はアタッチの結果を示します。
        // アタッチ後、元の sequence はアタッチされた部分を表すように変更されることがあります。
        AttachmentResult AttachSequence(ref ReadOnlySequence<TItem> sequence, bool allowCopy = true);

        // 指定されたシーケンスを現在のバッファにゼロコピーでアタッチしようと試みます。
        // 成功した場合、true を返し、attachedBuffer にアタッチされた部分を表す読み取り専用バッファが設定されます。
        // 失敗した場合は false を返します。
        // アタッチ後、元の sequence はアタッチされた部分を表すように変更されることがあります。
        bool TryAttachZeroCopy(ref ReadOnlySequence<TItem> sequence, out IReadOnlyBuffer<TItem>? attachedBuffer);

        // source の内容をバッファの先頭に挿入します。既存のデータは後ろにシフトされます。
        void Prepend(ReadOnlySpan<TItem> source);

        // source の内容をバッファの先頭に挿入します。既存のデータは後ろにシフトされます。
        void Prepend(ReadOnlyMemory<TItem> source);

        // value をバッファの先頭に挿入します。既存のデータは後ろにシフトされます。
        void Prepend(TItem value);

        // source の内容をバッファの先頭に挿入します。既存のデータは後ろにシフトされます。
        void Prepend(ReadOnlySequence<TItem> source);
    }
}