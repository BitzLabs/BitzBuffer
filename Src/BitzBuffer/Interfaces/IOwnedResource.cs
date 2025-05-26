using System;

namespace BitzLabs.BitzBuffer
{
    // IBufferState を拡張し、リソース解放の責務 (IDisposable) を追加したインターフェース。
    public interface IOwnedResource : IBufferState, IDisposable
    {
        // IsOwner, IsDisposed は IBufferState から継承。
        // Dispose は IDisposable から継承。
        // Dispose() はリソースを解放（プール返却または直接解放）し、IsOwner=false, IsDisposed=true に設定します。
        // 所有権がない場合、IsDisposed=true にするのみです（詳細は設計思想セクションを参照）。
    }
}