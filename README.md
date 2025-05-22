# BitzBuffer: C# High-Performance Buffer Management Library

**C# で書かれた高性能なバッファ管理ライブラリ「BitzBuffer」です。大量のデータを効率的に扱うアプリケーション（画像処理、CAD、機械学習、FA通信など）向けに設計されています。**

このライブラリは、メモリ効率の最大化 (GC負荷軽減、LOH回避、ゼロコピー操作の促進)、多様なメモリ種別（マネージド、アンマネージド、将来的にGPU）の統一的な取り扱い、そして開発者にとって使いやすいAPIの提供を目指しています。「ちょっとした」ニーズにも応え、「かゆいところに手が届く」ような柔軟性を持ち合わせることを目標としています。

**このリポジトリは、ライブラリの設計仕様とドキュメントを管理しています。実装は別途進行予定です。**

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
*   **ゼロコピー操作の促進 (`TryAttachSequence` / `TryAttachZeroCopy`)**:
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

## 🚀 クイックスタート (API利用イメージ)

```csharp
// (BitzBuffer API 利用イメージ)
using BitzBuffer; // 仮の名前空間
using System;
using System.Buffers;
using System.Text;
using Microsoft.Extensions.DependencyInjection; // DI利用の場合

// --- BufferManager のセットアップ (DIコンテナなどで行う想定) ---
var services = new ServiceCollection();
services.AddBitzBufferManager(options => // BitzBuffer が提供する拡張メソッド
{
    options.AddManagedProvider("MyManagedPool", managedOptions =>
    {
        // IManagedProviderOptionsBuilder を使用して専用プーリング設定などを行う
        managedOptions.ConfigureDedicatedPooling<byte[], ManagedArrayPoolStrategy, ManagedPoolConfig>(poolConfig => // 型は仮
        {
            poolConfig.AddBucket(4096, maxItems: 100);
            poolConfig.DefaultClearOption = BufferClearOption.ClearOnReturn;
        });
    });

    options.AddNativeProvider("MyNativePool", nativeOptions =>
    {
        // INativeProviderOptionsBuilder を使用
        nativeOptions.ConfigureDedicatedPooling<SafeNativeMemoryBlockHandle, NativeMemoryPoolStrategy, NativePoolConfig>(poolConfig => // 型は仮
        {
            poolConfig.AddBucket(1024 * 16 * sizeof(float), alignment: 32, maxItems: 50); // 16K要素のfloatバッファ用
        });
        nativeOptions.SetDefaultAlignment(32);
    });
});
var serviceProvider = services.BuildServiceProvider();
var bufferManager = serviceProvider.GetRequiredService<IBufferManager>(); // IBufferManager は BitzBuffer が提供

// --- マネージドバッファの利用 ---
var managedProvider = bufferManager.GetProvider("MyManagedPool"); // 正しくは TryGetProvider
if (managedProvider != null && managedProvider.TryRent<byte>(1000, out var managedBuffer))
{
    try
    {
        // 書き込み用に100バイト以上の領域をバッファの末尾に要求
        Memory<byte> writeMemory = managedBuffer.GetMemory(100);
        // 実際に書き込むのは Memory<T>.Length まで、または要求した100バイトまで
        int bytesToWrite = Math.Min(100, writeMemory.Length);
        Span<byte> writeSpan = writeMemory.Span.Slice(0, bytesToWrite);

        Encoding.UTF8.GetBytes("Hello BitzBuffer!", writeSpan); // 例として文字列を書き込む
        int actualBytesWritten = Encoding.UTF8.GetByteCount("Hello BitzBuffer!"); // 実際のバイト数

        managedBuffer.Advance(actualBytesWritten); // 実際に書き込んだバイト数を通知 (Length が進む)

        Console.WriteLine($"ManagedBuffer Length: {managedBuffer.Length}");

        ReadOnlySequence<byte> sequence = managedBuffer.AsReadOnlySequence(); // 書き込み済みのデータを読み取り
        foreach (var segment in sequence)
        {
            Console.WriteLine($"Segment data: {Encoding.UTF8.GetString(segment.Span)}");
        }
    }
    finally
    {
        managedBuffer.Dispose(); // プールに返却
    }
}

// --- デフォルトネイティブプロバイダの利用 (T は unmanaged 型) ---
using (var nativeBuffer = bufferManager.DefaultNativeProvider.Rent<float>(2048))
{
    // 書き込み用に512要素分の領域をバッファの末尾に要求
    Memory<float> writeMemory = nativeBuffer.GetMemory(512);
    int elementsToWrite = Math.Min(512, writeMemory.Length);
    Span<float> writeSpan = writeMemory.Span.Slice(0, elementsToWrite);

    for (int i = 0; i < elementsToWrite; i++)
    {
        writeSpan[i] = i * 1.1f; // データを書き込む
    }

    nativeBuffer.Advance(elementsToWrite); // 実際に書き込んだ要素数を通知

    Console.WriteLine($"NativeBuffer Length: {nativeBuffer.Length}");

    // SIMD演算など、ネイティブメモリの特性を活かした処理
    // (例: nativeBuffer.AsReadOnlySequence() からデータを取得して処理)
}
```
*(注: 上記コードはAPIの利用イメージであり、実際の型名やメソッド名は設計・実装段階で変更される可能性があります。プーリング設定の型パラメータなども簡略化しています。)*

## 📚 詳細ドキュメント (設計仕様)

このライブラリの詳細な設計思想、API定義、アーキテクチャについては、以下の設計仕様書を参照してください。

*   **[`00_Overview.md`](./Docs/BitzBuffer/00_Overview.md):** ライブラリ全体の目的、スコープ、アーキテクチャ概要。
*   **[`01_Core_Interfaces.md`](./Docs/BitzBuffer/01_Core_Interfaces.md):** 中核となるバッファインターフェース (`IBuffer<T>` など) の詳細。
*   **[`02_Providers_And_Buffers.md`](./Docs/BitzBuffer/02_Providers_And_Buffers.md):** 具体的なバッファ実装クラスとプロバイダ。
*   **[`03_Pooling.md`](./Docs/BitzBuffer/03_Pooling.md):** プーリング戦略とライフサイクル管理。
*   **[`04_GPU_Support.md`](./Docs/BitzBuffer/04_GPU_Support.md):** GPUサポートの拡張方針。
*   **[`05_Error_Handling.md`](./Docs/BitzBuffer/05_Error_Handling.md):** エラーハンドリングと例外戦略。

## 🛠️ 開発状況

現在は**設計仕様定義フェーズの最終段階**です。
今後、この設計仕様に基づいて実装フェーズへと進む予定です。

## 🤝 コントリビューション

設計に関するフィードバック、アイデア、ユースケースの提案など、あらゆる形でのコントリビューションを歓迎します！ Issue を作成するか、関連する議論にご参加ください。

## 📝 ライセンス

このプロジェクトは MIT License のもとで公開されています。

Copyright (c) [Year] [Your Name or Organization Name]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.