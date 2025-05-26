namespace BitzLabs.BitzBuffer
{
    // バッファ管理ライブラリ「BitzBuffer」における主要なバッファインターフェース。
    // 読み取りと書き込みの両方の機能を提供します。
    public interface IBuffer<TItem> : IReadOnlyBuffer<TItem>, IWritableBuffer<TItem>
        where TItem : struct
    {
        // IBufferState のメンバー (IsOwner, IsDisposed) は両方の親インターフェースから継承されますが、
        // 実装は単一の underlying state を持つべきです。
        // IDisposable は IReadOnlyBuffer<T> (経由で IOwnedResource) から継承されます。

        // 実装クラスは、IsOwner および IsDisposed の状態に基づいて、
        // 読み書きメソッドが呼び出された際に適切に例外をスローする必要があります。
    }
}