# BitzBuffer プロジェクト コーディングスタイルガイド

このドキュメントは、BitzBuffer ライブラリ開発におけるコーディングスタイル、規約、およびベストプラクティスを定義します。
Gemini Code Assist は、このガイドを参照してコードレビューを行います。レビューなどは、すべて日本語で回答してください。

## 1. 全般的な原則 (General Principles)

*   **読みやすさ (Readability):** コードは書く時間よりも読まれる時間の方が長いため、常に読みやすいコードを心がけます。明確で理解しやすい名前を使用し、複雑なロジックは適切に分割します。
*   **保守性 (Maintainability):** 将来の変更や修正が容易に行えるように、疎結合で凝集度の高いコードを目指します。（XMLドキュメントコメントは使用しません。）
*   **一貫性 (Consistency):** プロジェクト全体で一貫したコーディングスタイルを保ちます。このガイドラインに従ってください。
*   **シンプルさ (Simplicity):** 不必要に複雑なコードは避け、可能な限りシンプルな解決策を選びます。いわゆるYAGNI (You Ain't Gonna Need It) の原則を意識します。

## 2. 命名規則 (Naming Conventions)

Microsoft の [C# コーディング規則](https://docs.microsoft.com/ja-jp/dotnet/csharp/fundamentals/coding-style/coding-conventions) に準拠することを基本とします。

*   **クラス、インターフェース、構造体、列挙型、デリゲート、メソッド、プロパティ、イベント:** `PascalCase` を使用します。
    *   例: `public class BufferManager`, `public interface IDataProcessor`, `public void ProcessData()`
*   **インターフェース名:** `I` プレフィックスを付けます。
    *   例: `IStreamReader`
*   **メソッドの引数、ローカル変数:** `camelCase` を使用します。
    *   例: `int itemCount`, `string userName`
*   **プライベートフィールド:** `_camelCase` (アンダースコア + camelCase) を使用します。
    *   例: `private int _bufferSize;`
*   **定数 (const, static readonly):** `PascalCase` または `ALL_CAPS_SNAKE_CASE` (もし慣例があれば) を使用します。基本は `PascalCase` を推奨します。
    *   例: `public const int DefaultTimeout = 100;`, `public static readonly string DefaultName = "Unknown";`
*   **型パラメータ (ジェネリクス):** `T` プレフィックスを付けた `PascalCase` を使用します。
    *   例: `List<TItem>`, `Dictionary<TKey, TValue>`
*   **略語の扱い:** 2文字の略語は大文字 (例: `IOStream`)、3文字以上の略語はPascalCase (例: `XmlDocument`) を基本としますが、一般的な略語 (Id, Okなど) はこの限りではありません。一貫性を保ちます。

## 3. コーディングスタイル (Coding Style)

*   **インデント:** 4つのスペースを使用します。タブは使用しません。
*   **括弧 (`{ }`):**
    *   クラス、メソッド、プロパティ、制御構文 (if, for, while, foreach, switch, try-catch-finally) の波括弧は、常に新しい行から開始します。
    *   1行の `if` 文でも、常に波括弧を使用し、複数行で記述します。
        ```csharp
        // 推奨
        if (condition)
        {
            DoSomething();
        }

        // 非推奨
        if (condition) DoSomething();
        if (condition)
            DoSomething();
        ```
*   **1行の文字数:** 約120文字を目安とし、それを超える場合は適切に改行します。ただし、可読性を損なわない範囲で判断します。
*   **`var` の使用:** 型が右辺から明らかである場合は `var` を使用します。型が明確でない場合や、可読性を高めるためには明示的な型を使用します。
    *   例: `var stream = new MemoryStream();`, `var items = new List<string>();`
    *   例: `int count = GetCount();` (GetCountの戻り値の型が自明でない場合)
*   **`using` ディレクティブ:**
    *   ファイルの先頭に記述します。
    *   System関連のnamespaceを最初に、次にその他のnamespaceをアルファベット順に記述します。
    *   未使用の `using` ディレクティブは削除します。
*   **`this` キーワード:**
    *   ローカル変数とフィールド名が衝突する場合にのみ使用します。
    *   それ以外の場合は冗長なので省略します。
*   **アクセス修飾子:** 常に明示的に指定します (例: `private void MyMethod()` であり、単に `void MyMethod()` とはしない)。
*   **空白行:** 論理的なコードのまとまりの間や、メソッド定義の前後に適切に空白行を入れ、可読性を高めます。
*   **nullチェック:** `ArgumentNullException.ThrowIfNull()` ( .NET 6以降) や `if (argument is null)` を推奨します。

## 4. コメント (Comments)

*   **必要な箇所にのみ:** コード自身がドキュメントとなるように心がけ、自明なコードにはコメントを付けません。
*   **「何を」ではなく「なぜ」:** コードが何をしているか (What) ではなく、なぜそのような実装にしたのか (Why) や、複雑なアルゴリズムの意図などを説明します。
*   **TODOコメント:** `// TODO: [説明]` の形式で、将来対応すべきタスクを記述します。具体的なIssue番号も併記すると良いでしょう。
*   **コメントアウトされたコード:** 原則としてコミットしません。Gitの履歴で管理します。一時的なデバッグのためのコメントアウトは、コミット前に削除します。

## 5. エラーハンドリング (Error Handling)

*   **例外処理:**
    *   予期せぬエラーや不正な状態を検出した場合は、適切な例外をスローします。
    *   `ArgumentNullException`, `ArgumentOutOfRangeException`, `InvalidOperationException` などの標準例外を適切に使用します。
    *   ライブラリ固有の例外が必要な場合は、`System.Exception` を継承したカスタム例外クラスを定義します。
    *   空の `catch` ブロック (`catch (Exception) {}`) は原則として使用しません。例外を握りつぶす場合は、その理由を明確にコメントします。
    *   例外を再スローする場合は `throw;` を使用し、スタックトレースを維持します (`throw ex;` は避ける)。
*   **リソース管理:** `IDisposable` を実装するオブジェクトは、必ず `using` ステートメントまたは `try-finally` ブロックで適切に破棄します。

## 6. 非同期処理 (Async/Await)

*   **`async`/`await` の適切な使用:** I/Oバウンドな操作や長時間実行される可能性のある操作では、非同期処理 (`async`/`await`) を積極的に使用し、スレッドのブロッキングを避けます。
*   **`ConfigureAwait(false)`:** ライブラリコード内では、UIスレッドなど特定の同期コンテキストに戻る必要がない限り、`await` の後に `.ConfigureAwait(false)` を付けることを推奨します。これにより、デッドロックのリスクを軽減できます。
    *   例: `await stream.ReadAsync(buffer, 0, count).ConfigureAwait(false);`
*   **非同期メソッドの命名:** 非同期メソッドには `Async` サフィックスを付けます (例: `GetDataAsync`)。
*   **`async void` の回避:** イベントハンドラ以外での `async void` の使用は避けます。代わりに `async Task` を使用します。

## 7. LINQ (Language Integrated Query)

*   **可読性:** LINQはコードを簡潔にし、可読性を高めるために使用します。複雑すぎるLINQクエリは、複数のステップに分割するか、通常のループ処理に置き換えることを検討します。
*   **遅延実行:** LINQの多くは遅延実行されることを理解し、副作用やパフォーマンスに注意します。必要に応じて `.ToList()` や `.ToArray()` で即時実行します。
*   **クエリ構文 vs メソッド構文:** 一貫性を保つため、プロジェクト内でどちらかのスタイルに統一することを推奨します。一般的にはメソッド構文の方が柔軟性が高いですが、クエリ構文の方が読みやすい場合もあります。

## 8. パフォーマンスに関する考慮事項 (Performance Considerations)

*   クリティカルなパスでは、パフォーマンスを意識したコーディングを行います。
*   文字列の結合が多い場合は `StringBuilder` を使用します。
*   大きなコレクションの処理では、不必要なアロケーションを避けます。
*   パフォーマンスが問題となる場合は、プロファイラを使用してボトルネックを特定し、最適化を行います。マイクロ最適化は避け、測定に基づいて判断します。

## 9. C# のバージョンと言語機能

*   **対象C#バージョン:** [プロジェクトで使用するC#のバージョンを記述。例: C# 10]
*   **言語機能の利用:** 新しい言語機能（例: レコード型、パターンマッチングの強化、null許容参照型など）は、コードの簡潔性や安全性を高めるために積極的に活用します。ただし、可読性を損なわない範囲で使用します。
*   **null許容参照型 (`#nullable enable`):** プロジェクト全体で有効にすることを強く推奨します。これにより、null参照に起因するバグを減らすことができます。

## 10. テスト (Testing)

*   **ユニットテスト:** 公開APIや重要な内部ロジックに対しては、ユニットテストを作成します。
*   **テストフレームワーク:** [使用するテストフレームワークを記述。例: xUnit, MSTest, NUnit]
*   **テストの命名:** テストメソッド名は、テスト対象のメソッド、テストする条件、期待される結果が分かるように命名します (例: `MethodName_Scenario_ExpectedBehavior`)。
*   **AAAパターン (Arrange, Act, Assert):** テストはこのパターンに従って構造化します。