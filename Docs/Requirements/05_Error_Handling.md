# C# Buffer管理ライブラリ 要求仕様 - エラーハンドリング

このドキュメントは、C# Buffer管理ライブラリ全体のエラーハンドリング戦略、使用する例外の種類（標準例外およびカスタム例外）、そして `Try...` パターンの適用箇所について詳述します。堅牢で予測可能なエラー処理は、ライブラリの信頼性と使いやすさにとって不可欠です。

## 1. エラーハンドリングの基本方針

*   **標準例外の積極的な使用:** .NET の標準例外クラス（`ArgumentNullException`, `ArgumentOutOfRangeException`, `InvalidOperationException`, `ObjectDisposedException`, `OutOfMemoryException`, `NotSupportedException` など）を、それぞれのセマンティクスに従って積極的に使用します。これにより、.NET 開発者は既存の知識と例外処理パターンを活かすことができます。
*   **限定的なカスタム例外の導入:** 標準例外ではライブラリ固有のエラー状況を十分に表現できない、または利用者が特定のエラーを区別して処理する必要性が高い場合に限り、カスタム例外を導入します。
    *   全てのカスタム例外は、共通の基底クラス `BufferManagementException` を継承します。
*   **`Try...` パターンの提供:** 失敗が比較的一般的に起こりうる操作（特にリソース確保関連）については、例外をスローするメソッドに加えて、`bool` 値を返し `out` パラメータで結果を取得する `Try...` パターンも提供します。これにより、パフォーマンスクリティカルな場面での例外処理コストを回避できます。
*   **ドキュメンテーション:** 各パブリックAPIがどのような条件下でどのような例外をスローしうるかについては、APIドキュメント（LLM支援で生成されるMarkdownドキュメントや、この要求仕様書）で明確に記述します。

## 2. 標準例外の使用ガイドライン

以下は、主要な標準例外の具体的な使用場面のガイドラインです。

*   **`ArgumentNullException`**: `null` が許可されていない引数に `null` が渡された場合。
*   **`ArgumentOutOfRangeException`**: 引数の値が許容される範囲外である場合（例: 長さが負、オフセットが不正など）。
*   **`ArgumentException`**: 上記以外の引数に関する問題（例: 引数の組み合わせが不正、enum値が無効など）。
*   **`InvalidOperationException`**: オブジェクトがメソッドを呼び出すのに適切な状態でない場合。
    *   例: `IBuffer<T>.IsOwner` が `false` の状態で書き込みメソッドを呼び出した。
    *   例: プールからバッファを `Rent` しようとしたが、プロバイダが初期化されていない。
    *   例: 固定長バッファに対して拡張を伴うメモリ確保を要求した。
*   **`ObjectDisposedException`**: 既に `Dispose()` されたオブジェクト (`IBuffer<T>.IsDisposed` が `true`) に対して操作を行おうとした場合。
*   **`OutOfMemoryException`**: マネージドメモリまたはネイティブメモリの確保に失敗した場合。通常、これはランタイムまたは低レベルAPIから直接スローされます。ライブラリがこれをラップして再スローすることは稀ですが、もしネイティブAPIのエラーコードから判断できる場合は、この例外をスローすることが適切です。
*   **`NotSupportedException`**: ある操作が、その特定の実装や現在の設定ではサポートされていない場合（例: 特定のプロバイダがオプショナルな機能を実装していない）。

## 3. カスタム例外

初期実装では、以下のカスタム例外を定義します。これらは `BufferManagementException` を基底クラスとします。

```csharp
// ライブラリの全てのカスタム例外の基底クラス
public class BufferManagementException : Exception
{
    public BufferManagementException() { }
    public BufferManagementException(string message) : base(message) { }
    public BufferManagementException(string message, Exception innerException) : base(message, innerException) { }
    // 将来的にエラーコードなどの共通プロパティを追加する可能性あり
}

// プールが枯渇している、または設定された上限に達してリソースを確保できない場合にスローされる
public class PoolExhaustedException : BufferManagementException
{
    public string? PoolIdentifier { get; } // どのプールの問題かを示す識別子 (例: プロバイダ名 + バケットサイズ)
    public int RequestedSize { get; }      // 要求されたサイズ (要素数またはバイト数)
    public int AvailableCount { get; }     // プール内の利用可能なアイテム数 (枯渇時は0)
    public int MaxCapacity { get; }        // プールの最大容量 (もし設定されていれば)

    public PoolExhaustedException(string message, string? poolIdentifier = null, int requestedSize = 0, int availableCount = 0, int maxCapacity = -1)
        : base(message)
    {
        PoolIdentifier = poolIdentifier;
        RequestedSize = requestedSize;
        AvailableCount = availableCount;
        MaxCapacity = maxCapacity;
    }

    public PoolExhaustedException(string message, Exception innerException, string? poolIdentifier = null, int requestedSize = 0, int availableCount = 0, int maxCapacity = -1)
        : base(message, innerException)
    {
        PoolIdentifier = poolIdentifier;
        RequestedSize = requestedSize;
        AvailableCount = availableCount;
        MaxCapacity = maxCapacity;
    }
}

// (検討) ネイティブメモリ確保に関する特有のエラーで、OutOfMemoryException では情報が不足する場合
// public class NativeMemoryAllocationException : BufferManagementException { ... }
// -> 初期実装では OutOfMemoryException を基本とし、必要性が高まれば追加を検討。

// (検討) 設定に関するエラーで、ArgumentException/InvalidOperationException では情報が不足する場合
// public class BufferConfigurationException : BufferManagementException { ... }
// -> 初期実装では標準例外を基本とし、必要性が高まれば追加を検討。
```

**カスタム例外の使用場面:**

*   **`PoolExhaustedException`**:
    *   `IBufferProvider.Rent<T>()` が呼び出された際に、対応するプールからバッファを貸し出そうとしたが、プールが空で、かつプールの最大容量に達しているか、新しいリソースの確保も許容されていない（または失敗した）場合にスローされます。
    *   この例外をキャッチすることで、利用者は一時的なリソース不足に対するリトライ処理や、代替手段へのフォールバックを検討できます。

## 4. `Try...` パターンの適用

以下の主要なリソース確保APIについては、例外をスローするバージョンと `Try...` パターンの両方を提供します。

*   **`IBufferProvider`**:
    *   `IBuffer<T> Rent<T>(int minimumLength = 0)`
    *   `bool TryRent<T>(int minimumLength, [MaybeNullWhen(false)] out IBuffer<T> buffer)`
    *   `IBuffer<T> CreateBuffer<T>(int exactLength)`
    *   `bool TryCreateBuffer<T>(int exactLength, [MaybeNullWhen(false)] out IBuffer<T> buffer)`

これらの `Try...` メソッドは、失敗時（例: プール枯渇、メモリ確保失敗）に `false` を返し、例外をスローしません。これにより、呼び出し側は例外処理のコストを避けて、戻り値のチェックで成否を判断できます。

他のAPI（バッファの読み書きメソッドなど）については、前提条件（`IsOwner`, `IsDisposed` など）が満たされていない場合は明確に例外（`InvalidOperationException`, `ObjectDisposedException`）をスローすることを基本とし、過度な `Try...` パターンの導入は避けます。

## 5. 例外メッセージとデバッグ情報

*   スローされる例外のメッセージは、問題の原因と解決のヒントをできるだけ具体的に含むようにします。
    *   例: `InvalidOperationException("Cannot write to the buffer because it is not owned or has been disposed.")`
    *   例: `PoolExhaustedException("Buffer pool 'Managed_4KB' is exhausted. Requested: 1, Available: 0, MaxCapacity: 1024.")`
*   デバッグビルドでは、より詳細な診断情報や警告ログ（例: `Dispose()` が所有権のないバッファに対して呼ばれた場合）を出力することを検討します。
*   例外の `InnerException` プロパティは、根本的な原因となった例外（例: ネイティブAPIからのエラー）をラップする場合に適切に設定します。
