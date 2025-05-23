# BitzBuffer 設計仕様 - エラーハンドリング

このドキュメントは、C# Buffer管理ライブラリ「BitzBuffer」全体のエラーハンドリング戦略、使用する例外の種類（標準例外およびカスタム例外）、そして `Try...` パターンの適用箇所について詳述します。堅牢で予測可能なエラー処理は、ライブラリの信頼性と使いやすさにとって不可欠です。

## 1. エラーハンドリングの基本方針

*   **標準例外の積極的な使用**
*   **限定的なカスタム例外の導入** (`BufferManagementException` 基底、初期は `PoolExhaustedException`)
*   **`Try...` パターンの提供** (`IBufferProvider.Rent/CreateBuffer` のみ)
*   **ドキュメンテーション**での例外明記

## 2. 標準例外の使用ガイドライン

*   **`ArgumentNullException`**: 引数 `null`。
*   **`ArgumentOutOfRangeException`**: 引数値が範囲外 (例: `Slice` 範囲、`Advance` でキャパシティ超過)。
*   **`ArgumentException`**: その他引数問題。
*   **`InvalidOperationException`**: 不正状態でのメソッド呼び出し (例: `IsOwner=false` で書き込み、未初期化プロバイダ、固定長バッファで拡張要求)。
*   **`ObjectDisposedException`**: `IsDisposed=true` オブジェクトへの操作。
*   **`OutOfMemoryException`**: メモリ確保失敗。
*   **`NotSupportedException`**: サポートされない操作。

## 3. カスタム例外

```csharp
public class BufferManagementException : Exception { /* コンストラクタ */ }
public class PoolExhaustedException : BufferManagementException
{
    public string? PoolIdentifier { get; }
    public int RequestedSize { get; }
    public int AvailableCount { get; }
    public int MaxCapacity { get; }
    public PoolExhaustedException(string message, string? poolIdentifier = null, int requestedSize = 0, int availableCount = 0, int maxCapacity = -1)
        : base(message) { /* プロパティ設定 */ }
    public PoolExhaustedException(string message, Exception innerException, string? poolIdentifier = null, int requestedSize = 0, int availableCount = 0, int maxCapacity = -1)
        : base(message, innerException) { /* プロパティ設定 */ }
}
```
*   **`PoolExhaustedException`**: `IBufferProvider.Rent<T>()` でプール枯渇かつ新規確保不可時にスロー。

## 4. `Try...` パターンの適用

*   **`IBufferProvider`**: `Rent/TryRent`, `CreateBuffer/TryCreateBuffer` を提供。
*   他のAPI（`IBuffer<T>`インスタンスメソッド等）は前提条件違反時に例外をスローし、過度な`Try...`パターンは避ける。

## 5. 例外メッセージとデバッグ情報

*   具体的で役立つメッセージ。
*   デバッグビルドでの詳細情報/警告ログ。
*   `InnerException` の適切な設定。

## 6. 将来の拡張 (エラーハンドリングと診断関連)

*   **カスタム例外の追加検討** (`NativeMemoryAllocationException`, `BufferConfigurationException` など)
*   **二重解放/解放後アクセス検知の強化**
*   **イベントトレースの統合**