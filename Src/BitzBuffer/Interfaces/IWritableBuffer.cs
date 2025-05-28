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

 // 指定された読み取り専用シーケンス sequenceToAttach の内容を、
        // 条件が合えば物理的なコピーなしに現在のバッファの末尾にアタッチ（追加）しようと試みます。
        // アタッチに成功した場合は true を返し、現在のバッファの IReadOnlyBuffer<TItem>.Length および内部構造 (セグメント) が更新されます。
        // アタッチに失敗した（ゼロコピーの条件を満たせなかった、または他の理由による）場合は false を返し、現在のバッファの状態は変更されません。
        // ゼロコピーアタッチが成功した場合、sequenceToAttach がBitzBufferライブラリ管理下のセグメントから構成されていれば、
        // それらのセグメントの所有権が現在のバッファに移譲されることがあります。具体的な振る舞いはバッファの実装クラスに依存します。
        // このメソッドを呼び出す前に、IBufferState.IsOwner が true であり、IBufferState.IsDisposed が false であることを確認してください。
        // そうでない場合、InvalidOperationException または ObjectDisposedException がスローされます。
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