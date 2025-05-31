# BitzBuffer 設計仕様 - エラーハンドリング

このドキュメントは、C# Buffer管理ライブラリ「BitzBuffer」全体のエラーハンドリング戦略、使用する例外の種類（標準例外およびカスタム例外）、そして `Try...` パターンの適用箇所について詳述します。堅牢で予測可能なエラー処理は、ライブラリの信頼性と使いやすさにとって不可欠です。

## 1. エラーハンドリングの基本方針

BitzBufferライブラリにおけるエラーハンドリングは、以下の基本方針に基づきます。

*   **標準例外の積極的な使用:**
    *   可能な限り、.NET標準ライブラリで定義されている既存の例外クラス（`ArgumentNullException`, `ArgumentOutOfRangeException`, `InvalidOperationException`, `ObjectDisposedException` など）を適切に使用します。これにより、ライブラリの利用者は馴染みのある例外処理パターンを適用できます。
*   **限定的なカスタム例外の導入:**
    *   標準例外では表現しきれない、ライブラリ固有のエラー状況に対してのみ、カスタム例外を導入します。
    *   全てのカスタム例外は、基底となる `BufferManagementException` から派生することを原則とします。
    *   初期実装では、プールが枯渇した場合の `PoolExhaustedException` を定義します。
*   **`Try...` パターンの限定的な提供:**
    *   主にリソースの確保や、失敗が正常系の範囲内で起こりうる操作（例: `IBufferProvider` の `Rent` や `CreateBuffer` の試行版、`IReadOnlyBuffer<T>` の `TryGetSingleSpan`/`Memory`）に対して `Try...` パターン（例: `TryRent`, `TryCreateBuffer`）を提供します。
    *   これらのメソッドは、処理の成否を `bool` 値で返し、失敗時には例外をスローしません（ただし、後述する引数バリデーション例外は除く）。
*   **前提条件違反は明確な例外で通知:**
    *   メソッドの事前条件（例: 引数がnullでないこと、特定の状態であること）が満たされない場合は、`ArgumentNullException` や `InvalidOperationException` などの適切な例外をスローし、APIの誤用を早期に開発者に知らせます。これは `Try...` パターンを提供するメソッドにおいても、引数自体の不正に関しては適用されます。
*   **ドキュメンテーション:**
    *   各公開APIのドキュメントコメント（XMLドキュメントコメント）には、スローされる可能性のある主要な例外の種類とその条件を明記します。

## 2. 標準例外の使用ガイドライン

以下に、BitzBufferライブラリ内で使用される主要な標準例外とその典型的な使用ケースを示します。

*   **`ArgumentNullException`**:
    *   メソッドの引数として `null` が許容されないにもかかわらず `null` が渡された場合にスローされます。
    *   例: `new ManagedBuffer<T>(null, ...)`、`provider.Rent<T>(null)` (もしプロバイダがオプションオブジェクトを取る場合など)。
*   **`ArgumentOutOfRangeException`**:
    *   メソッドの引数値が許容される範囲外である場合にスローされます。
    *   例: `buffer.Slice(-1, 10)` (負のオフセット)、`buffer.Advance(-5)` (負のカウント)、`buffer.Advance(buffer.Capacity + 1)` (キャパシティ超過)。
    *   `IBufferProvider.Rent<T>(-1)` (負の最小長)。
*   **`ArgumentException`**:
    *   上記以外の引数に関する問題（例: 引数の組み合わせが不正、期待されるフォーマットでないなど）が発生した場合にスローされます。
    *   例: バッファへの書き込み時にソースデータのサイズがバッファの残り容量を超える場合 (`Write(source)` で `source` が大きすぎる)。
*   **`InvalidOperationException`**:
    *   オブジェクトが特定の操作を実行するための適切な状態にない場合にスローされます。
    *   例: 所有権がない (`IsOwner == false`) バッファに対して書き込み操作を行おうとした場合。
    *   例: 初期化が完了していないプロバイダやマネージャのメソッドを呼び出した場合（もしそのような状態があり得るなら）。
    *   例: 固定長のバッファに対して、サイズを変更しようとする操作（例: `Prepend` が実装されていない場合に呼び出すなど、より具体的な例外がない場合）。
*   **`ObjectDisposedException`**:
    *   既に破棄 (`IsDisposed == true`) されたオブジェクトのメソッドやプロパティにアクセスしようとした場合にスローされます。
    *   例: `buffer.Dispose()` 呼び出し後に `buffer.Write(...)` や `buffer.Length` を呼び出す。
*   **`OutOfMemoryException`**:
    *   バッファの確保（特に大きなサイズの配列やネイティブメモリの確保）に失敗した場合にスローされる可能性があります。これはライブラリが直接スローするというより、基盤となるメモリアロケーションAPIからスローされるものを受け渡す形になります。
*   **`NotSupportedException`**:
    *   特定のバッファ実装が、インターフェースで定義されている特定の操作をサポートしていない場合にスローされます。
    *   例: 読み取り専用バッファに対して書き込み操作を試みる（インターフェースレベルで分離されていれば通常発生しないが、実装クラスの内部ロジックなど）。`Prepend` のように、特定のバッファタイプでは効率的に実装できない操作など。
*   **`NotImplementedException`**:
    *   開発途中の機能で、まだ実装されていないメソッドが呼び出された場合にスローされます。これは最終的なリリース版からは取り除かれるべきです。

## 3. カスタム例外

ライブラリ固有のエラー状態を示すために、以下のカスタム例外を定義します。
全てのカスタム例外は `BufferManagementException` から派生します。

```csharp
// 例: Src/BitzBuffer/Diagnostics/BufferManagementException.cs

/// <summary>
/// BitzBuffer ライブラリに関連する操作でエラーが発生した場合にスローされる基底例外クラスです。
/// </summary>
public class BufferManagementException : Exception
{
    public BufferManagementException() { }
    public BufferManagementException(string message) : base(message) { }
    public BufferManagementException(string message, Exception innerException) : base(message, innerException) { }
    // 必要であれば、シリアライズ用のコンストラクタも追加
    // protected BufferManagementException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}

/// <summary>
/// バッファプールから要求されたサイズのバッファを確保できなかった場合（プールが枯渇し、かつ新規確保もできなかった場合）にスローされる例外です。
/// </summary>
public class PoolExhaustedException : BufferManagementException
{
    /// <summary>
    /// 枯渇したプールの識別子（もしあれば）。
    /// </summary>
    public string? PoolIdentifier { get; }

    /// <summary>
    /// 要求されたバッファのサイズ（要素数またはバイト数）。
    /// </summary>
    public int RequestedSize { get; }

    /// <summary>
    /// 要求時にプール内で利用可能だったアイテム数（もし把握できれば）。
    /// </summary>
    public int AvailableCount { get; }

    /// <summary>
    /// プールの最大キャパシティ（もし設定されていれば）。
    /// </summary>
    public int MaxCapacity { get; }

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
    // シリアライズ用コンストラクタも同様に追加可能
}
```

*   **`BufferManagementException`**: 全てのBitzBuffer固有例外の基底クラス。
*   **`PoolExhaustedException`**: プールが枯渇し、要求されたバッファを確保できなかった場合にスローされます。`IBufferProvider.Rent<T>()` などで使用される可能性があります。

## 4. `Try...` パターンの適用

本ライブラリでは、失敗する可能性があり、かつその失敗が致命的ではない操作（特にリソースの確保を試みる操作）に対して `Try...` パターンを提供します。

*   **対象API:**
    *   `IBufferProvider`: `TryRent<T>()`, `TryCreateBuffer<T>()`
    *   `IReadOnlyBuffer<T>`: `TryGetSingleSpan()`, `TryGetSingleMemory()`
*   **基本的な挙動:**
    *   操作が成功した場合は `true` を返し、`out` パラメータに結果を設定します。
    *   操作が実行時の要因（例: メモリ不足、プール枯渇、リソースが期待される状態でない（例: 破棄済み、非連続メモリで単一セグメントとして取得不可など））で失敗した場合は `false` を返し、`out` パラメータには `default` 値を設定します。**この際、例外はスローされません。**
*   **引数バリデーションとの関係:**
    *   `Try...` パターンを提供するメソッドであっても、**メソッドの事前条件（引数の有効範囲など、APIコントラクトとして定義されるもの）が満たされない場合**は、プログラミングエラーとして扱われ、`ArgumentNullException`, `ArgumentOutOfRangeException` などの引数関連の標準例外がスローされます。
    *   これは、APIの誤用を早期に開発者に知らせ、バグの発見を助けるための設計方針です。`Try...` パターンは、引数が妥当であるという前提のもとで、処理の実行成否を返すことを主眼としています。
*   **過度な適用は避ける:**
    *   `IBuffer<T>` インスタンスの各メソッド（例: `Advance`, `Write`）のように、前提条件（例: バッファが破棄されていない、所有権がある、十分な容量がある）が明確であり、それが満たされない場合に操作を続行できない場合は、例外をスローすることを基本とします。これらの操作に対して安易に `Try...` パターンを導入すると、エラーの原因が隠蔽され、デバッグが困難になる可能性があるためです。

## 5. 例外メッセージとデバッグ情報

*   スローされる例外のメッセージは、問題の原因を特定するのに役立つ、具体的で分かりやすいものにします。
*   可能な限り、`ArgumentException` やその派生クラスでは、不正な引数名を `ParamName` プロパティに設定します。
*   デバッグビルド時には、より詳細な情報や警告をログに出力することを検討します（標準の `Debug.WriteLine` や、ロギングライブラリとの連携など）。
*   例外をラップする場合（例: 内部的な例外をキャッチしてカスタム例外をスローする場合）は、元の例外を `InnerException` プロパティに設定し、根本原因の追跡を容易にします。

## 6. 将来の拡張 (エラーハンドリングと診断関連)

*   **より詳細なカスタム例外の追加検討:**
    *   必要に応じて、より具体的なエラー状況を示すカスタム例外（例: `NativeMemoryAllocationException`, `BufferConfigurationException`, `BufferAccessViolationException` など）の導入を検討します。ただし、過度に多くのカスタム例外を作ることは避け、標準例外で表現できる場合はそちらを優先します。
*   **二重解放や解放後アクセスの検知強化 (デバッグビルド向け):**
    *   デバッグビルド時に、バッファの二重解放や、解放後のオブジェクトへのアクセスをより積極的に検出し、警告や例外を発生させる仕組みを強化することを検討します。
*   **イベントトレースとの統合:**
    *   `System.Diagnostics.Tracing.EventSource` を利用して、ライブラリの重要なイベント（バッファの確保・解放、プールのヒット・ミス、エラー発生など）をトレースできるようにし、パフォーマンス分析や問題診断に役立てることを検討します。
*   **詳細な診断情報の提供API:**
    *   アプリケーションがライブラリの内部状態（例: プール内のオブジェクト数、メモリ使用状況など）を監視・診断するためのAPIの提供を検討します（現在の統計APIの拡充）。
```