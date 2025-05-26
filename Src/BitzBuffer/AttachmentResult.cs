namespace BitzLabs.BitzBuffer
{
    // AttachSequence メソッドの操作結果を示します。
    public enum AttachmentResult
    {
        AttachedAsZeroCopy, // ゼロコピーでアタッチ成功
        AttachedAsCopy,     // コピーしてアタッチ成功
        Failed              // アタッチ失敗 (TryAttachZeroCopy でのみ使用される想定)
    }
}