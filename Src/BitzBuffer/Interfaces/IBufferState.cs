namespace BitzLabs.BitzBuffer
{
    // バッファの所有権と破棄状態を示す基本的な状態インターフェース。
    public interface IBufferState
    {
        // このインスタンスが現在、基になるリソースに対する有効な所有権を持っているかどうかを示します。
        // 所有権が移譲されたり、リソースが破棄されたりすると false になります。
        bool IsOwner { get; }

        // このインスタンスが既に破棄 (Dispose) されているかどうかを示します。
        // true の場合、このオブジェクトは使用できません (特にリソース解放後)。
        bool IsDisposed { get; }
    }
}