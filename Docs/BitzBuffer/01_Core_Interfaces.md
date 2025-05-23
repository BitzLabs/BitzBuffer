# BitzBuffer 設計仕様 - コアインターフェース

このドキュメントは、バッファ管理ライブラリ「BitzBuffer」の中心となる共通インターフェース群について詳述します。

## 3. バッファ共通インターフェース

アプリケーション内で多様なメモリリソース（マネージド、アンマネージド、将来的にはGPUなど）を統一的に扱うため、中心となるバッファインターフェース群を定義します。これらのインターフェースは、非連続メモリへの対応、効率的な読み書き、そして明確な所有権管理とライフサイクルを考慮して設計されています。

### 3.1. 設計思想と主要なユースケース

*   **非連続メモリのサポート:** `System.IO.Pipelines` のように、物理的に連続していない複数のメモリセグメントを単一の論理バッファとして扱えるようにします。これにより、データの再コピーを避け、メモリ効率を向上させます。
    *   **ユースケース例:**
        *   ネットワークからのチャンク化されたデータ受信時、各チャンクを別々のセグメントに格納し、全体を一つのバッファとして処理。
        *   複数の小さなバッファ（例: ヘッダ、ペイロード、フッタ）をゼロコピーで連結し、単一のメッセージとして送信。
*   **効率的な読み書き:** `Span<T>`、`Memory<T>`、`ReadOnlySequence<T>` を活用し、型安全かつ高性能なデータアクセスを提供します。書き込み操作は `System.IO.Pipelines.PipeWriter` に似た `GetMemory`/`Advance` パターンをサポートし、非連続バッファへの効率的な書き込みを可能にします。
    *   `IWritableBuffer<T>.GetMemory(int sizeHint)`: バッファの現在の論理的な末尾以降の、書き込み可能な空きメモリ領域を要求します。
    *   `IWritableBuffer<T>.Advance(int count)`: `GetMemory` で取得した領域に `count` 分のデータを書き込んだことをバッファに通知し、バッファの論理的な書き込み済み長さを進めます。
*   **所有権管理とライフサイクル (`IOwnedResource`, `IBufferState`):**
    *   **`IBufferState`**: バッファが有効な所有権を持つか (`IsOwner`)、既に破棄されたか (`IsDisposed`) を示す状態プロパティを提供します。これにより、バッファが安全に使用可能かを確認できます。
    *   **`IOwnedResource`**: `IBufferState` を拡張し、`IDisposable` を実装することでリソース解放の責務も明確化します。バッファの利用者は、`Dispose()` を呼び出すことでリソースを適切に解放（プールへ返却または直接解放）する責任があります。
    *   **所有権の移譲:** `IWritableBuffer<T>.TryAttachZeroCopy(IEnumerable<BitzBufferSequenceSegment<T>> segmentsToAttach)` メソッド、または `AttachSequence` メソッドでゼロコピーアタッチが成功した場合、引数で渡されたセグメントの所有権が、呼び出し先の `IWritableBuffer<T>` インスタンス（アタッチ先バッファ）に移譲されます。
        *   **解放責任の移行:** 各 `BitzBufferSequenceSegment<T>` が保持する `SegmentSpecificOwner` の `Dispose()` 責任が、アタッチ先バッファに完全に移行します。
        *   **元のバッファの責任変更:** アタッチされたセグメントの元の所有者は、もはやその `SegmentSpecificOwner` を `Dispose()` する責任を負いません。これは `BitzBufferSequenceSegment<T>.IsOwnershipTransferred` フラグ ( `true` に設定される) によって示されます。
        *   **アタッチ先バッファの `Dispose()` 時の挙動:** アタッチ先バッファが `Dispose()` される際には、所有権を引き継いだ全ての `SegmentSpecificOwner` を確実に `Dispose()` します。
        *   **ユースケース例:** FAプロトコル処理で、受信パイプラインから得たデータ（ペイロード）の所有権を新しいバッファに移し、その前後にヘッダとフッタを追加して送信パイプラインに渡す。ペイロードのコピーは発生せず、ライフサイクル管理は新しいバッファが一元的に行う。
    *   **`Dispose()` の挙動:**
        *   プール管理下のバッファ (`IsOwner == true`): プールへ返却。返却時、`IBufferLifecycleHooks.OnReturn` が呼び出され、バッファの状態リセット（論理長クリアなど）やオプションに応じたデータクリアが行われることがあります。
        *   直接生成されたバッファ (`IsOwner == true`): リソースを直接解放。
        *   所有権を失ったバッファ (`IsOwner == false`, `IsDisposed == false`): `Dispose()` が呼ばれると `IsDisposed = true` となり、デバッグビルドでは警告ログが出力される。実質的なリソース解放は行わない（既に責任がないため）。
        *   複数回の `Dispose()` 呼び出しは安全です（2回目以降は何もしない）。
*   **バッファの状態操作:**
    *   **論理長のリセット:** `IWritableBuffer<T>.Clear()` メソッドは、バッファの論理的な書き込み済み長さを0にし、オプションで内容もクリアします。プールから再利用されるバッファは、`IBufferLifecycleHooks.OnRent` を通じてこの状態にリセットされることが期待されます。
    *   **論理長の切り詰め:** `IWritableBuffer<T>.Truncate(long length)` メソッドは、バッファの論理長を指定された長さに短縮します。
    *   **初期の論理長設定:** 既存データをラップしてバッファを生成する場合、その初期の論理長はバッファ生成時のオプション（`IBufferFactory` 経由）で設定されます。
*   **読み取り専用スライス:** `IReadOnlyBuffer<T>.Slice` 操作は常に読み取り専用のバッファ (`IReadOnlyBuffer<T>`) を返します。スライスは元のバッファのデータを参照するビューであり、データを所有しません (`IsOwner == false`)。元のバッファが無効になるとスライスも無効になります。
*   **スレッドセーフティ:** `IBufferProvider` から取得した個々の `IBuffer<T>` インスタンスのメソッドはスレッドセーフではありません。単一の `IBuffer<T>` インスタンスを複数のスレッドから同時に操作する場合は、呼び出し側で適切な同期を行う必要があります。プーリング機構自体はスレッドセーフに設計されます（詳細は [`Docs/DesignSpecs/03_Pooling.md`](Docs/DesignSpecs/03_Pooling.md) を参照）。
*   **ゼロコピーアタッチのためのセグメント情報 (`BitzBufferSequenceSegment<T>`):**
    *   ゼロコピーでの所有権移譲を安全かつ効率的に行うため、本ライブラリは `System.Buffers.ReadOnlySequenceSegment<T>` を拡張した `BitzBufferSequenceSegment<T>` という内部的なカスタムセグメントクラスの概念を導入します。
    *   このカスタムセグメントは、標準のメモリ情報に加え、そのセグメントの「所有者」（解放責任を持つ `IDisposable` オブジェクト）や、それが属する元の `IBuffer<T>` インスタンスへの参照、所有権移譲の可否といったメタデータを保持します。
    *   `IReadOnlyBuffer<T>` は、この `BitzBufferSequenceSegment<T>` のシーケンスを提供する `AsAttachableSegments()` メソッドを公開します。これにより、BitzBuffer.Pipelinesのようなコンポーネントが、これらのメタ情報を利用して安全なゼロコピーアタッチを実現できます。

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
using System.Collections.Generic; // IEnumerable のため
// BitzBufferSequenceSegment<T> の名前空間を using する (実際の名前空間に合わせてください)
// 例: using BitzBuffer.Core.Internals;
// または、BitzBufferSequenceSegment<T> が公開APIなら using BitzBuffer;

// バッファの所有権と破棄状態を示す基本的な状態インターフェース。
public interface IBufferState
{
    /// <summary>
    /// このインスタンスが現在、基になるリソースに対する有効な所有権を持っているかどうかを示します。
    /// 所有権が移譲されたり、リソースが破棄されたりすると false になります。
    /// </summary>
    bool IsOwner { get; }

    /// <summary>
    /// このインスタンスが既に破棄 (Dispose) されているかどうかを示します。
    /// true の場合、このオブジェクトは使用できません (特にリソース解放後)。
    /// </summary>
    bool IsDisposed { get; }
}

// IBufferState を拡張し、リソース解放の責務 (IDisposable) を追加したインターフェース。
public interface IOwnedResource : IBufferState, IDisposable
{
    // IsOwner, IsDisposed は IBufferState から継承。
    // Dispose は IDisposable から継承。
    // Dispose() はリソースを解放（プール返却または直接解放）し、IsOwner=false, IsDisposed=true に設定します。
    // 所有権がない場合、IsDisposed=true にするのみです（詳細は 3.1 設計思想と主要なユースケース を参照）。
}

// 読み取り専用のバッファインターフェース。
public interface IReadOnlyBuffer<T> : IOwnedResource
    where T : struct
{
    /// <summary>
    /// バッファに書き込まれた有効なデータの論理的な長さを取得します。
    /// このプロパティへのアクセス前に IsOwner および !IsDisposed であることを確認することを推奨します。
    /// 無効な状態でアクセスした場合の挙動は実装に依存しますが、例外をスローすることがあります。
    /// </summary>
    long Length { get; }

    /// <summary>
    /// バッファが空 (Length == 0) かどうかを示します。
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// バッファが単一の連続したメモリセグメントで構成されているかどうかを示します。
    /// </summary>
    bool IsSingleSegment { get; }

    /// <summary>
    /// バッファの現在の書き込み済み内容 (Length で示される範囲) を表す ReadOnlySequence<T> を取得します。
    /// このメソッドが呼び出されるたびに、その時点のバッファの状態を反映したインスタンスが返されることがあります。
    /// IsOwner が false または IsDisposed が true の場合、例外 (InvalidOperationException または ObjectDisposedException) をスローすることがあります。
    /// </summary>
    ReadOnlySequence<T> AsReadOnlySequence();

    /// <summary>
    /// このバッファの内容を、所有者情報を含む BitzBufferSequenceSegment<T> のシーケンスとして取得します。
    /// 主に BitzBuffer.Pipelines の TryAttachZeroCopy のような、高度なゼロコピー所有権移譲シナリオでの使用を想定しています。
    /// </summary>
    /// <returns>所有者情報を含むカスタムセグメントのシーケンス。</returns>
    /// <remarks>
    /// 返される各セグメントの IsOwnershipTransferred フラグは初期状態では false です。
    /// このバッファの IsOwner が false または IsDisposed が true の場合、空のシーケンスを返すか、例外をスローすることがあります。
    /// </remarks>
    IEnumerable<BitzBufferSequenceSegment<T>> AsAttachableSegments();

    /// <summary>
    /// バッファ全体が単一の連続したメモリセグメントで構成され、かつデータが存在する場合 (Length > 0)、
    /// その書き込み済みデータ領域の ReadOnlySpan<T> を取得します。
    /// </summary>
    /// <param name="span">成功した場合、データ領域を指す ReadOnlySpan<T>。失敗した場合は default。</param>
    /// <returns>取得に成功した場合は true、それ以外の場合は false。</returns>
    /// <remarks>
    /// IsOwner が false または IsDisposed が true の場合、例外をスローするか false を返すことが推奨されます。
    /// </remarks>
    bool TryGetSingleSpan(out ReadOnlySpan<T> span);

    /// <summary>
    /// バッファ全体が単一の連続したメモリセグメントで構成され、かつデータが存在する場合 (Length > 0)、
    /// その書き込み済みデータ領域の ReadOnlyMemory<T> を取得します。
    /// </summary>
    /// <param name="memory">成功した場合、データ領域を指す ReadOnlyMemory<T>。失敗した場合は default。</param>
    /// <returns>取得に成功した場合は true、それ以外の場合は false。</returns>
    /// <remarks>
    /// IsOwner が false または IsDisposed が true の場合、例外をスローするか false を返すことが推奨されます。
    /// </remarks>
    bool TryGetSingleMemory(out ReadOnlyMemory<T> memory);

    /// <summary>
    /// バッファの指定された範囲を表す新しい読み取り専用バッファ (スライス) を作成します。
    /// 返されるスライスは IsOwner が false です。
    /// IsOwner が false または IsDisposed が true の場合、例外 (InvalidOperationException または ObjectDisposedException) をスローすることがあります。
    /// </summary>
    IReadOnlyBuffer<T> Slice(long start, long length);

    /// <summary>
    /// バッファの指定された開始位置から末尾までを表す新しい読み取り専用バッファ (スライス) を作成します。
    /// 返されるスライスは IsOwner が false です。
    /// IsOwner が false または IsDisposed が true の場合、例外 (InvalidOperationException または ObjectDisposedException) をスローすることがあります。
    /// </summary>
    IReadOnlyBuffer<T> Slice(long start);
}

// AttachmentResult enum: AttachSequence メソッドの操作結果を示す
public enum AttachmentResult
{
    AttachedAsZeroCopy,
    AttachedAsCopy,
    Failed // TryAttachZeroCopy でのみ使用
}

// 書き込み専用のバッファインターフェース。
public interface IWritableBuffer<T> : IBufferState
    where T : struct
{
    /// <summary>
    /// バッファの現在の論理的な末尾 (Length の位置) 以降に、
    /// 少なくとも sizeHint 要素分の書き込み可能な連続メモリ領域を要求します。
    /// 返される Memory<T> の実際の長さは、バッファの実装や空き容量に依存するため、
    /// Memory<T>.Length を確認する必要があります。
    /// IsOwner が false または IsDisposed が true の場合、例外をスローします。
    /// </summary>
    Memory<T> GetMemory(int sizeHint = 0);

    /// <summary>
    /// GetMemory で取得した領域に count 要素分のデータを書き込んだことをバッファに通知し、
    /// バッファの論理的な書き込み済み長さ (IReadOnlyBuffer<T>.Length プロパティ) を count だけ進めます。
    /// IsOwner が false または IsDisposed が true の場合、例外をスローします。
    /// count が負であるか、進めるとバッファの物理キャパシティを超える場合は ArgumentOutOfRangeException をスローします。
    /// </summary>
    void Advance(int count);

    // --- データ書き込みメソッド ---
    // IsOwner が false または IsDisposed が true の場合、各Writeメソッドは例外をスローします。
    void Write(ReadOnlySpan<T> source);
    void Write(ReadOnlyMemory<T> source);
    void Write(T value);
    void Write(ReadOnlySequence<T> source); // ReadOnlySequence<T> source の内容は常にコピーされて書き込まれます。

    // --- データアタッチメソッド ---
    // IsOwner が false または IsDisposed が true の場合、各アタッチメソッドは例外をスローします。

    /// <summary>
    /// 指定された ReadOnlySequence<T> をこのバッファの末尾に追加します。
    /// attemptZeroCopy が true の場合、まずゼロコピーでのアタッチ（所有権の奪取）を試みます。
    /// ゼロコピーの試行は、sequenceToAttach が BitzBuffer の IReadOnlyBuffer<T> から生成されたと判断できる場合に最も効果的です。
    /// ゼロコピーが不可能な場合は、データのコピーにフォールバックします。
    /// </summary>
    /// <param name="sequenceToAttach">追加するシーケンス。</param>
    /// <param name="attemptZeroCopy">ゼロコピーを試みる場合は true (デフォルト)。false の場合は常にコピーします。</param>
    /// <returns>アタッチ操作の結果 (ゼロコピー成功か、コピーが発生したか)。</returns>
    /// <remarks>
    /// ゼロコピーの条件は限定的です (詳細は Docs/DesignSpecs/02_Providers_And_Buffers.md および本ドキュメントの「3.4. 将来の拡張」を参照)。
    /// もし source が IReadOnlyBuffer<T> であることが分かっている場合は、AttachSequence(IReadOnlyBuffer<T>, bool) オーバーロードの使用を検討してください。
    /// </remarks>
    AttachmentResult AttachSequence(ReadOnlySequence<T> sequenceToAttach, bool attemptZeroCopy = true);

    /// <summary>
    /// 指定された BitzBuffer の IReadOnlyBuffer<T> の内容をこのバッファの末尾に追加します。
    /// attemptZeroCopy が true の場合、まずゼロコピーでのアタッチ（所有権の奪取）を試みます。
    /// これは sourceBitzBuffer.AsAttachableSegments() を介して行われます。
    /// ゼロコピーが不可能な場合は、データのコピーにフォールバックします。
    /// </summary>
    /// <param name="sourceBitzBuffer">追加する BitzBuffer インスタンス。</param>
    /// <param name="attemptZeroCopy">ゼロコピーを試みる場合は true (デフォルト)。false の場合は常にコピーします。</param>
    /// <returns>アタッチ操作の結果 (ゼロコピー成功か、コピーが発生したか)。</returns>
    AttachmentResult AttachSequence(IReadOnlyBuffer<T> sourceBitzBuffer, bool attemptZeroCopy = true);

    /// <summary>
    /// 指定された BitzBufferSequenceSegment<T> のシーケンスを、ゼロコピーでのアタッチ（所有権の奪取）によってのみ
    /// このバッファの末尾に追加しようと試みます。
    /// </summary>
    bool TryAttachZeroCopy(IEnumerable<BitzBufferSequenceSegment<T>> segmentsToAttach);

    // --- その他の書き込み関連メソッド ---
    // IsOwner が false または IsDisposed が true の場合、各メソッドは例外をスローします。
    void Prepend(ReadOnlySpan<T> source);
    void Prepend(ReadOnlyMemory<T> source);
    void Prepend(ReadOnlySequence<T> source); // データはコピーされます

    /// <summary>
    /// バッファの論理的な書き込み済み長さ (IReadOnlyBuffer<T>.Length) を0にリセットします。
    /// 確保されているメモリ領域の内容もクリアされるかどうかは、クリアポリシーに依存します。
    /// IsOwner が false または IsDisposed が true の場合、例外をスローします。
    /// </summary>
    void Clear();

    /// <summary>
    /// バッファの論理的な書き込み済み長さ (IReadOnlyBuffer<T>.Length) を指定された長さに切り詰めます。
    /// length が現在の Length より大きい場合、または負の場合は ArgumentOutOfRangeException をスローします。
    /// IsOwner が false または IsDisposed が true の場合、例外をスローします。
    /// </summary>
    void Truncate(long length);
}

// バッファ管理ライブラリ「BitzBuffer」における主要なバッファインターフェース。
public interface IBuffer<T> : IReadOnlyBuffer<T>, IWritableBuffer<T>
    where T : struct
{
     // IBufferState のメンバー (IsOwner, IsDisposed) は両方の親インターフェースから継承されるが、
    // 実装は単一の underlying state を持つ。
    // IDisposable は IReadOnlyBuffer<T> (経由で IOwnedResource) から継承。

    // 実装クラスは、IsOwner および IsDisposed の状態に基づいて、
    // 読み書きメソッドが呼び出された際に適切に例外をスローする必要があります。
    // (例外の詳細は Docs/DesignSpecs/05_Error_Handling.md を参照)
}

// --- BitzBufferSequenceSegment<T> の概念定義 ---
// (実際のクラス定義は BitzBuffer コアライブラリの適切な名前空間に配置されます)
// namespace BitzBuffer.Core.Internals; // 例
/// <summary>
/// BitzBuffer ライブラリ内部で使用される、所有者情報を含む ReadOnlySequenceSegment。
/// ゼロコピーでの所有権移譲をサポートするために、元のバッファやセグメント固有の解放処理への参照を保持します。
/// </summary>
/// <typeparam name="T">セグメント内の要素の型。</typeparam>
public class BitzBufferSequenceSegment<T> : ReadOnlySequenceSegment<T> where T : struct
{
    /// <summary>
    /// このセグメントが属する元の IBuffer<T> インスタンスへの参照（オプション）。
    /// </summary>
    public IBuffer<T>? SourceBuffer { get; internal set; }
    /// <summary>
    /// この特定のセグメントのメモリ解放に責任を持つ IDisposable オブジェクト。
    /// </summary>
    public IDisposable? SegmentSpecificOwner { get; internal set; }
    /// <summary>
    /// このセグメントの所有権が移譲されたかどうかを示します。
    /// </summary>
    public bool IsOwnershipTransferred { get; internal set; }
    /// <summary>
    /// このセグメントが所有権移譲の対象として適格かどうかを示します。
    /// </summary>
    public bool IsEligibleForOwnershipTransfer { get; internal set; } = true;

    // コンストラクタ、Next プロパティ、RunningIndex プロパティは実装クラスで適切に定義されます。
    // 例: public BitzBufferSequenceSegment(ReadOnlyMemory<T> memory) { this.Memory = memory; }
    //     public new BitzBufferSequenceSegment<T>? Next { get; set; } // base.Next を隠蔽し型を強める
    //     public new long RunningIndex { get; set; } // base.RunningIndex を隠蔽
}
```

### 3.4. 将来の拡張 (コアインターフェース関連)

*   **高度な所有権管理**
*   **複雑なバッファ操作API**
*   **`AttachSequence` / `TryAttachZeroCopy` の機能強化** (特に `ReadOnlySequence<T>` からのゼロコピーサポートの向上)
*   **`TrySlice` パターンの導入**
*   **Stream連携機能** (詳細は [`Docs/BitzBuffer/02_Providers_And_Buffers.md`](Docs/BitzBuffer/02_Providers_And_Buffers.md) も参照)
*   **`IWritableBuffer<T>.GetMemory()` の高度化**
*   **非同期I/O向けバッファアクセスインターフェース** (`BitzBuffer.Pipelines` 関連)
