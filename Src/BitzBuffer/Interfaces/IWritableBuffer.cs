using System;
using System.Buffers; // For ReadOnlySequence<TItem>

namespace BitzLabs.BitzBuffer
{
    // 書き込み可能な連続または非連続メモリ領域へのアクセスを提供します。
    // IBufferState を継承し、バッファの状態管理の責務を持ちます。
    // 書き込み操作に特化しており、IDisposable は直接継承しません。
    public interface IWritableBuffer<TItem> : IBufferState where TItem : struct
    {
        // バッファの現在の書き込み終端以降に、書き込み用の Memory<TItem> を取得します。
        // sizeHint で要求された最小要素数以上の連続したメモリ領域を返そうと試みます。
        // 実際に確保されたサイズは返された Memory<TItem>.Length を確認してください。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローする場合があります。
        // sizeHint が負数の場合、ArgumentOutOfRangeException をスローする場合があります。
        Memory<TItem> GetMemory(int sizeHint = 0);

        // 指定された要素数だけ書き込みポインタを進めます。
        // これにより、GetMemory() で取得した領域にデータが書き込まれたことをバッファに通知します。
        // count が負数、または書き込みがバッファの物理的な容量を超える場合、ArgumentOutOfRangeException をスローする場合があります。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローする場合があります。
        void Advance(int count);

        // バッファの論理的な書き込み済み長さ (IReadOnlyBuffer<TItem>.Length に影響) を0にリセットします。
        // 確保されているメモリ領域の内容がクリアされるかどうかは、実装のクリアポリシーに依存します。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローする場合があります。
        void Clear();

        // source の内容をバッファに書き込みます。
        void Write(ReadOnlySpan<TItem> source);

        // source の内容をバッファに書き込みます。
        void Write(ReadOnlyMemory<TItem> source);

        // value をバッファに書き込みます。
        void Write(TItem value);

         // source の内容をバッファに書き込みます。この操作では、source の内容が常にコピーされます。
        void Write(ReadOnlySequence<TItem> source);

        // 指定されたシーケンスを現在のバッファにアタッチしようと試みます。
        // ゼロコピーが可能な場合はゼロコピーでアタッチし、そうでない場合は allowCopy が true であればコピーしてアタッチします。
        // 戻り値はアタッチの結果を示します (ゼロコピー成功、コピー成功、失敗など)。
        AttachmentResult AttachSequence(ReadOnlySequence<TItem> sequenceToAttach, bool attemptZeroCopy = true);

        // 指定されたシーケンスを現在のバッファにゼロコピーでアタッチしようと試みます。
        // 成功した場合、true を返し、attachedBuffer にアタッチされた部分を表す読み取り専用バッファが設定されます。
        // 失敗した場合は false を返します。 (設計書では、アタッチされたバッファを返す方法は明記されていません)
        bool TryAttachZeroCopy(ReadOnlySequence<TItem> sequenceToAttach);

        //  source の内容をバッファの先頭に挿入します。既存のデータは後ろにシフトされます。この操作では、source の内容がコピーされます。
        void Prepend(ReadOnlySpan<TItem> source);

        //  source の内容をバッファの先頭に挿入します。既存のデータは後ろにシフトされます。この操作では、source の内容がコピーされます。
        void Prepend(ReadOnlyMemory<TItem> source);

        //  source の内容をバッファの先頭に挿入します。既存のデータは後ろにシフトされます。この操作では、source の内容がコピーされます。
        void Prepend(ReadOnlySequence<TItem> source);

        // バッファの現在の書き込み済み内容を指定された長さに切り詰めます。
        // length が現在の書き込み済み長さを超える場合は何も起こりません。
        // length が負数の場合、ArgumentOutOfRangeException をスローする場合があります。
        // IsOwner が false または IsDisposed が true の場合、InvalidOperationException または ObjectDisposedException をスローする場合があります。
        void Truncate(long length);
    }
}