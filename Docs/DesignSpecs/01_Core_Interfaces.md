# C# Buffer管理ライブラリ 要求仕様 - コアインターフェース

このドキュメントは、バッファ管理ライブラリの中心となる共通インターフェース群について詳述します。

## 3. バッファ共通インターフェース

アプリケーション内で多様なメモリリソース（マネージド、アンマネージド、将来的にはGPUなど）を統一的に扱うため、中心となるバッファインターフェース群を定義します。これらのインターフェースは、非連続メモリへの対応、効率的な読み書き、そして明確な所有権管理とライフサイクルを考慮して設計されています。

### 3.1. 設計思想と主要なユースケース

*   **非連続メモリのサポート:** `System.IO.Pipelines` のように、物理的に連続していない複数のメモリセグメントを単一の論理バッファとして扱えるようにします。これにより、データの再コピーを避け、メモリ効率を向上させます。
    *   **ユースケース例:**
        *   ネットワークからのチャンク化されたデータ受信時、各チャンクを別々のセグメントに格納し、全体を一つのバッファとして処理。
        *   複数の小さなバッファ（例: ヘッダ、ペイロード、フッタ）をゼロコピーで連結し、単一のメッセージとして送信。
*   **効率的な読み書き:** `Span<T>`、`Memory<T>`、`ReadOnlySequence<T>` を活用し、型安全かつ高性能なデータアクセスを提供します。書き込み操作は `System.IO.Pipelines.PipeWriter` に似た `GetMemory`/`Advance` パターンをサポートし、非連続バッファへの効率的な書き込みを可能にします。
*   **所有権管理とライフサイクル (`IOwnedResource`, `IBufferState`):**
    *   **`IBufferState`**: バッファが有効な所有権を持つか (`IsOwner`)、既に破棄されたか (`IsDisposed`) を示す状態プロパティを提供します。これにより、バッファが安全に使用可能かを確認できます。
    *   **`IOwnedResource`**: `IBufferState` を拡張し、`IDisposable` を実装することでリソース解放の責務も明確化します。バッファの利用者は、`Dispose()` を呼び出すことでリソースを適切に解放（プールへ返却または直接解放）する責任があります。
    *   **所有権の移譲:** `IWritableBuffer<T>.TryAttachSequence` メソッドにより、あるバッファ (`ReadOnlySequence<T>`) の所有権を別の書き込み可能バッファに（ゼロコピーで）移譲できます。移譲元のバッファは `IsOwner = false` となり、解放責任は移譲先に移ります。移譲元のバッファインスタンス経由でのデータアクセスはできなくなります（例外スロー）。
        *   **ユースケース例:** FAプロトコル処理で、受信パイプラインから得たデータ（ペイロード）の所有権を新しいバッファに移し、その前後にヘッダとフッタを追加して送信パイプラインに渡す。ペイロードのコピーは発生せず、ライフサイクル管理は新しいバッファが一元的に行う。
    *   **`Dispose()` の挙動:**
        *   プール管理下のバッファ (`IsOwner == true`): プールへ返却。
        *   直接生成されたバッファ (`IsOwner == true`): リソースを直接解放。
        *   所有権を失ったバッファ (`IsOwner == false`, `IsDisposed == false`): `Dispose()` が呼ばれると `IsDisposed = true` となり、デバッグビルドでは警告ログが出力される。実質的なリソース解放は行わない（既に責任がないため）。
        *   複数回の `Dispose()` 呼び出しは安全です（2回目以降は何もしない）。
*   **読み取り専用スライス:** `IReadOnlyBuffer<T>.Slice` 操作は常に読み取り専用のバッファ (`IReadOnlyBuffer<T>`) を返します。スライスは元のバッファのデータを参照するビューであり、データを所有しません (`IsOwner == false`)。元のバッファが無効になるとスライスも無効になります。

### 3.2. インターフェース階層

以下の主要なインターフェースを定義します。これらは継承関係を通じて関連付けられます。

*   **`IBufferState`**:
    *   プロパティ: `IsOwner`, `IsDisposed`
    *   役割: バッファの基本的な状態を示す。
*   **`IOwnedResource`**:
    *   継承: `IBufferState`, `IDisposable`
    *   役割: 状態管理に加え、リソース解放の責任を持つ。
*   **`IReadOnlyBuffer<T>`**:
    *   継承: `IOwnedResource`
    *   役割: 読み取り専用のデータアクセスとスライス機能を提供。
*   **`IWritableBuffer<T>`**:
    *   継承: `IBufferState`
    *   役割: 書き込み専用の操作を提供 (`IDisposable` は含まない)。
*   **`IBuffer<T>`**:
    *   継承: `IReadOnlyBuffer<T>`, `IWritableBuffer<T>`
    *   役割: 読み書き可能な完全なバッファ表現。ライブラリにおける最も完全なバッファインターフェース。

### 3.3. インターフェース定義

```csharp
using System;
using System.Buffers;

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

// IBufferState を拡張し、リソース解放の責務 (IDisposable) を追加したインターフェース。
public interface IOwnedResource : IBufferState, IDisposable
{
    // IsOwner, IsDisposed は IBufferState から継承。
    // Dispose は IDisposable から継承。
    // Dispose() はリソースを解放（プール返却または直接解放）し、IsOwner=false, IsDisposed=true に設定します。
    // 所有権がない場合、IsDisposed=true にするのみです（詳細は 3.1 参照）。
}

// 読み取り専用のバッファインターフェース。
// IOwnedResource (つまり IBufferState と IDisposable) を継承し、データへのアクセスとスライス機能を提供します。
public interface IReadOnlyBuffer<T> : IOwnedResource
{
    // バッファ内の要素の論理的な長さを取得します。
    // アクセス前に IsOwner/IsDisposed の確認を推奨。無効な場合は例外をスローすることがあります。
    long Length { get; }

    // バッファが空かどうか (Length == 0) を示します。
    bool IsEmpty { get; }

    // バッファが単一の連続したメモリセグメントで構成されているかどうかを示します。
    bool IsSingleSegment { get; }

    // バッファの内容を ReadOnlySequence<T> として取得します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることがあります。
    ReadOnlySequence<T> AsReadOnlySequence();

    // バッファが単一の連続したメモリセグメントで構成されている場合、そのセグメントの ReadOnlySpan<T> を取得します。
    // 成功した場合は true を返します。それ以外の場合は false を返します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることが推奨されます。
    bool TryGetSingleSpan(out ReadOnlySpan<T> span);

    // バッファが単一の連続したメモリセグメントで構成されている場合、そのセグメントの ReadOnlyMemory<T> を取得します。
    // 成功した場合は true を返します。それ以外の場合は false を返します。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることが推奨されます。
    bool TryGetSingleMemory(out ReadOnlyMemory<T> memory);

    // バッファの指定された範囲を表す新しい読み取り専用バッファ (スライス) を作成します。
    // 返されるスライスは IsOwner が false です。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることがあります。
    IReadOnlyBuffer<T> Slice(long start, long length);

    // バッファの指定された開始位置から末尾までを表す新しい読み取り専用バッファ (スライス) を作成します。
    // 返されるスライスは IsOwner が false です。
    // IsOwner が false または IsDisposed が true の場合、例外をスローすることがあります。
    IReadOnlyBuffer<T> Slice(long start);
}

// 書き込み専用のバッファインターフェース。
// IBufferState を継承し、データの書き込み、変更機能を提供します。IDisposable は含みません。
public interface IWritableBuffer<T> : IBufferState
{
    // IsOwner, IsDisposed は IBufferState から継承。
    // 書き込み操作の前には、これらのプロパティを確認し、
    // IsOwner が false または IsDisposed が true の場合は例外をスローすることが実装に期待されます。
    // (詳細は 05_Error_Handling.md を参照)

    // バッファの末尾に書き込むためのメモリ領域を取得します。
    Memory<T> GetMemory(int sizeHint = 0);

    // GetMemory で取得した領域への書き込みが完了したことを通知し、バッファの論理的な長さを進めます。
    void Advance(int count);

    // 指定されたソースからバッファの末尾にデータを書き込みます。
    void Write(ReadOnlySpan<T> source);
    void Write(ReadOnlyMemory<T> source);
    void Write(T value);
    void Write(ReadOnlySequence<T> source); // データはコピーされます

    // バッファの先頭にデータを追加（プリペンド）します。(コストが高い可能性あり)
    void Prepend(ReadOnlySpan<T> source);
    void Prepend(ReadOnlyMemory<T> source);
    void Prepend(ReadOnlySequence<T> source); // データはコピーされます

    // 指定された ReadOnlySequence<T> を、このバッファの論理的な一部として末尾に追加（アタッチ）します。
    // takeOwnership = true の場合、元のバッファセグメントの所有権を引き継ぎます (ゼロコピー)。
    // 元のバッファは IsOwner が false になり、解放責任はこのバッファに移ります。
    // takeOwnership = false の場合、データはコピーされます。
    // 所有権の奪取は限定的なサポートとなります(詳細は 02_Providers_And_Buffers.md, 06_Future_Extensions.md 参照)。
    bool TryAttachSequence(ReadOnlySequence<T> sequenceToAttach, bool takeOwnership);

    // バッファの内容をクリアし、論理的な長さを0にリセットします。
    void Clear();

    // バッファの論理的な長さを指定された長さに切り詰めます。
    void Truncate(long length);
}

// バッファ管理ライブラリにおける主要なバッファインターフェース。
// IReadOnlyBuffer<T> (IOwnedResource と IBufferState を含む) と
// IWritableBuffer<T> (IBufferState を含む) の両方を継承します。
// これにより、読み書き可能な機能と完全なライフサイクル管理を提供します。
public interface IBuffer<T> : IReadOnlyBuffer<T>, IWritableBuffer<T>
{
    // IBufferState のメンバー (IsOwner, IsDisposed) は両方の親インターフェースから継承されるが、
    // 実装は単一の underlying state を持つ。
    // IDisposable は IReadOnlyBuffer<T> (経由で IOwnedResource) から継承。

    // 実装クラスは、IsOwner および IsDisposed の状態に基づいて、
    // 読み書きメソッドが呼び出された際に適切に例外をスローする必要があります。
    // (例外の詳細は 05_Error_Handling.md を参照)

    // 将来的な拡張ポイント。
    // 例:
    // BufferType BufferType { get; } // マネージド、アンマネージドなどを示す enum
    // string? AssociatedProviderName { get; } // このバッファを生成したプロバイダ名
}
```