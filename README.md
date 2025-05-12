# BitzBuffer: C# High-Performance Buffer Management Library

**C# で書かれた高性能なバッファ管理ライブラリ「BitzBuffer」です。大量のデータを効率的に扱うアプリケーション（画像処理、CAD、機械学習、FA通信など）向けに設計されています。**

このライブラリは、メモリ効率の最大化 (GC負荷軽減、LOH回避、ゼロコピー操作の促進)、多様なメモリ種別（マネージド、アンマネージド、将来的にGPU）の統一的な取り扱い、そして開発者にとって使いやすいAPIの提供を目指しています。「ちょっとした」ニーズにも応え、「かゆいところに手が届く」ような柔軟性を持ち合わせることを目標としています。

**このリポジトリは、ライブラリの要求仕様と設計ドキュメントを管理しています。実装は別途進行予定です。**

<!-- (以下、内容は前回と同じ) -->

## ✨ 主な機能と設計目標

*   **統一されたバッファインターフェース (`IBuffer<T>`)**:
    *   マネージドメモリ (`byte[]` など) とアンマネージドメモリ (`NativeMemory`) を同じように扱えます。
    *   将来的にGPUバッファ (Vulkan, OpenGLなど) もサポート可能な拡張性の高い設計。
*   **高性能プーリング戦略**:
    *   `ArrayPool<T>` のコンセプトを参考に、サイズ別のバケット管理を行う効率的なプーリング機構を独自実装。
    *   GCプレッシャーを大幅に削減し、メモリの再利用を促進します。
    *   クリアポリシー（返却時/レンタル時/なし）を設定可能。
*   **非連続メモリのサポート (`ReadOnlySequence<T>`)**:
    *   複数のメモリセグメントを単一の論理バッファとして効率的に扱えます。
    *   `System.IO.Pipelines` のようなデータ処理に適しています。
*   **ゼロコピー操作の促進 (`TryAttachSequence`)**:
    *   条件が合えば、既存のバッファセグメントを物理的にコピーすることなく、新しい複合バッファの一部として「アタッチ」できます。
*   **明確な所有権管理とライフサイクル**:
    *   `IsOwner`, `IsDisposed` プロパティと `IDisposable` パターンにより、バッファの有効期間と解放責任を明確にします。
    *   所有権の移譲メカニズムにより、複雑なデータフローでも安全なリソース管理を支援します。
*   **柔軟な設定と拡張性**:
    *   `BufferManager` を介して、使用するメモリプロバイダやプーリング戦略をアプリケーションのニーズに合わせて設定可能。
    *   Fluent Interfaceによる設定API。
    *   新しいメモリ種別やプーリング戦略を容易に追加できる設計。
*   **型安全性と効率性**:
    *   `Span<T>`, `Memory<T>`, `ReadOnlySequence<T>` を最大限に活用し、型安全で高性能なデータアクセスを実現します。
    *   ネイティブメモリ操作には `SafeHandle` や `MemoryManager<T>` を利用し、安全性を確保。

## 🚀 クイックスタート (構想)

```csharp
// (将来のAPI使用イメージ)

// --- BufferManager のセットアップ (DIコンテナなどで行う想定) ---
var services = new ServiceCollection();
services.AddBitzBufferManager(options => // プロジェクト名に合わせて変更
{
    options.AddManagedProvider("MyManagedPool", managedOptions =>
    {
        managedOptions.ConfigurePooling(pool =>
        {
            pool.AddBucket<byte>(4096, maxItems: 100); // 4KBのbyteバッファを100個までプール
            pool.SetDefaultClearOption(BufferClearOption.ClearOnReturn);
        });
    });

    options.AddNativeProvider("MyNativePool", nativeOptions =>
    {
        nativeOptions.ConfigurePooling(pool =>
        {
            pool.AddBucket<float>(1024 * 16, maxItems: 50); // 16K要素のfloatバッファ
            pool.SetDefaultAlignment(32); // 32バイトアライメント
        });
    });
});
var serviceProvider = services.BuildServiceProvider();
var bufferManager = serviceProvider.GetRequiredService<IBufferManager>(); // IBufferManager は BitzBuffer が提供

// --- マネージドバッファの利用 ---
var managedProvider = bufferManager.GetProvider("MyManagedPool");
if (managedProvider.TryRent<byte>(1000, out var managedBuffer)) // 1000バイト以上のバッファを要求
{
    try
    {
        Span<byte> span = managedBuffer.GetSpan(100); // 書き込み用に100バイト取得
        // ... span にデータを書き込む ...
        managedBuffer.Advance(100); // 100バイト書き込んだことを通知

        ReadOnlySequence<byte> sequence = managedBuffer.AsReadOnlySequence();
        // ... sequence を使ってデータを読み取る ...
    }
    finally
    {
        managedBuffer.Dispose(); // プールに返却
    }
}

// --- ネイティブバッファの利用 (T は unmanaged 型) ---
var nativeProvider = bufferManager.GetProvider("MyNativePool");
using (var nativeBuffer = nativeProvider.Rent<float>(2048)) // 2048要素のfloatバッファ
{
    Memory<float> memory = nativeBuffer.GetMemory(512);
    // ... memory にデータを書き込む ...
    nativeBuffer.Advance(512);

    // SIMD演算など、ネイティブメモリの特性を活かした処理
}
```

## 📚 詳細ドキュメント (要求仕様)

このライブラリの詳細な設計思想、API定義、アーキテクチャについては、以下の要求仕様書を参照してください。

*   **[`docs/requirements/00_Overview.md`](docs/requirements/00_Overview.md):** ライブラリ全体の目的、スコープ、アーキテクチャ概要。
*   **[`docs/requirements/01_Core_Interfaces.md`](docs/requirements/01_Core_Interfaces.md):** 中核となるバッファインターフェース (`IBuffer<T>` など) の詳細。
*   **[`docs/requirements/02_Providers_And_Buffers.md`](docs/requirements/02_Providers_And_Buffers.md):** 具体的なバッファ実装クラスとプロバイダ。
*   **[`docs/requirements/03_Pooling.md`](docs/requirements/03_Pooling.md):** プーリング戦略とライフサイクル管理。
*   **[`docs/requirements/04_GPU_Support.md`](docs/requirements/04_GPU_Support.md):** GPUサポートの拡張方針。
*   **[`docs/requirements/05_Error_Handling.md`](docs/requirements/05_Error_Handling.md):** エラーハンドリングと例外戦略。
*   **[`docs/requirements/06_Future_Extensions.md`](docs/requirements/06_Future_Extensions.md):** 将来の拡張と検討事項。

## 🛠️ 開発状況

現在は**要求仕様定義フェーズ**です。
活発に議論と設計を行っており、今後、実装フェーズへと進む予定です。

## 🤝 コントリビューション

設計に関するフィードバック、アイデア、ユースケースの提案など、あらゆる形でのコントリビューションを歓迎します！ Issue を作成するか、関連する議論にご参加ください。

(将来的に実装が始まったら、プルリクエストに関するガイドラインなどもここに追記)

## 📝 ライセンス

(未定 - 将来的に MIT, Apache 2.0 などを検討)
