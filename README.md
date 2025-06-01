はい、承知いたしました。
それでは、ご提案いただいた新しいドキュメントフォルダ構成 (`Docs/01.システム仕様書/`, `Docs/02.開発仕様書/`, `Docs/03.詳細仕様書/`) に基づき、まずは `README.md` から順番に、パス参照などを修正した内容をファイルごとに全文表示します。

--- START OF FILE # `README.md` (フォルダ構成変更 反映版) ---

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
*   **ゼロコピー操作の促進 (`TryAttachZeroCopy` / `AttachSequence`)**:
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
using BitzBuffer; // 名前空間はプロジェクトに合わせて調整
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
if (bufferManager.TryGetProvider("MyManagedPool", out var managedProvider))
{
    if (managedProvider.TryRent<byte>(1000, out var managedBuffer))
    {
        try
        {
            Memory<byte> writeMemory = managedBuffer.GetMemory(100);
            int bytesToWrite = Math.Min(100, writeMemory.Length);
            Span<byte> writeSpan = writeMemory.Span.Slice(0, bytesToWrite);
            int actualBytesWritten = Encoding.UTF8.GetBytes("Hello BitzBuffer!", writeSpan);
            managedBuffer.Advance(actualBytesWritten);
            Console.WriteLine($"ManagedBuffer Length: {managedBuffer.Length}");
            ReadOnlySequence<byte> sequence = managedBuffer.AsReadOnlySequence();
            foreach (var segment in sequence)
            {
                Console.WriteLine($"Segment data: {Encoding.UTF8.GetString(segment.Span)}");
            }
        }
        finally
        {
            managedBuffer.Dispose();
        }
    }
}

// --- デフォルトネイティブプロバイダの利用 (T は unmanaged 型) ---
using (var nativeBuffer = bufferManager.DefaultNativeProvider.Rent<float>(2048))
{
    Memory<float> writeMemory = nativeBuffer.GetMemory(512);
    int elementsToWrite = Math.Min(512, writeMemory.Length);
    Span<float> writeSpan = writeMemory.Span.Slice(0, elementsToWrite);
    for (int i = 0; i < elementsToWrite; i++) { writeSpan[i] = i * 1.1f; }
    nativeBuffer.Advance(elementsToWrite);
    Console.WriteLine($"NativeBuffer Length: {nativeBuffer.Length}");
}
```
*(注: 上記コードはAPIの利用イメージであり、実際の型名やメソッド名は設計・実装段階で変更される可能性があります。プーリング設定の型パラメータなども簡略化しています。)*

## 📚 ドキュメント

本プロジェクトのドキュメントは、以下の構成で管理されています。

*   **[システム仕様書](./Docs/01.システム仕様書/README.md)**: プロジェクト全体の目的、背景、主要な機能要件など、上位レベルの仕様を記述します。(内容は今後定義)
*   **[開発仕様書](./Docs/02.開発仕様書/00_はじめに.md)**: 開発環境、フォルダ構成、ブランチ戦略、コーディングルールなど、開発を進める上での規約や情報をまとめたものです。
*   **詳細設計仕様:**
    *   **[BitzBuffer コア](./Docs/03.詳細仕様書/BitzBuffer/00_プロジェクト概要と目的.md)**: バッファ管理ライブラリ本体の設計思想、API定義、アーキテクチャに関する詳細な仕様書です。
    *   **[BitzBuffer.Pipelines](./Docs/03.詳細仕様書/BitzBuffer.Pipelines/00_P_プロジェクト概要と目的.md)**: 高レベルな非同期データ処理パイプライン機能の設計仕様書です。(内容は検討段階です。)

## 🛠️ 開発状況

BitzBufferライブラリの開発は、以下のマイルストーンに沿って進行中です。
各マイルストーンの詳細は、GitHubの [Milestonesページ](https://github.com/BitzLabs/BitzBuffer/milestones) でご確認いただけます。
[![GitHub Milestones](https://img.shields.io/badge/GitHub-Milestones-blue?logo=github&style=flat-square)](https://github.com/BitzLabs/BitzBuffer/milestones)

*   **M01: コア基盤と基本的なマネージドバッファ** ![Status](https://img.shields.io/badge/Status-作業中-orange) [![Progress](https://img.shields.io/github/milestones/progress/BitzLabs/BitzBuffer/M01:%20コア基盤と基本的なマネージドバッファ?label=Progress)](https://github.com/BitzLabs/BitzBuffer/milestones) [![Open Issues for M01](https://img.shields.io/github/issues/BitzLabs/BitzBuffer/M01:%20コア基盤と基本的なマネージドバッファ?label=Open%20Issues&color=yellow)](https://github.com/BitzLabs/BitzBuffer/issues?q=is%3Aopen+milestone%3A%22M01%3A+%E3%82%B3%E3%82%A2%E5%9F%BA%E7%9B%A4%E3%81%A8%E5%9F%BA%E6%9C%AC%E7%9A%84%E3%81%AA%E3%83%9E%E3%83%8D%E3%83%BC%E3%82%B8%E3%83%89%E3%83%90%E3%83%83%E3%83%95%E3%82%A1%22) [![Closed Issues for M01](https://img.shields.io/github/issues-closed/BitzLabs/BitzBuffer/M01:%20コア基盤と基本的なマネージドバッファ?label=Closed%20Issues&color=green)](https://github.com/BitzLabs/BitzBuffer/issues?q=is%3Aclosed+milestone%3A%22M01%3A+%E3%82%B3%E3%82%A2%E5%9F%BA%E7%9B%A4%E3%81%A8%E5%9F%BA%E6%9C%AC%E7%9A%84%E3%81%AA%E3%83%9E%E3%83%8D%E3%83%BC%E3%82%B8%E3%83%89%E3%83%90%E3%83%83%E3%83%95%E3%82%A1%22)
    *   **目標:** ライブラリの心臓部となるインターフェース群と、最も基本的なマネージド配列ベースのバッファ (`ManagedBuffer<T>`)、スライスビュー (`SlicedBufferView<T>`)、およびそれらの単体テストを実装する。プーリングはまだ導入しない。
    *   **主なIssue:**
        *   [![Issue #7 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/7?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/7) #7: コアインターフェース定義
        *   [![Issue #8 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/8?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/8) #8: `ManagedBuffer<T>` の基本実装 (Sliceメソッド含む)
        *   [![Issue #9 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/9?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/9) #9: `IBufferProvider` インターフェースと最小限のマネージドプロバイダ
        *   [![Issue #10 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/10?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/10) #10: `BufferManager` の基本実装
        *   [![Issue #11 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/11?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/11) #11: `SlicedBufferView<T>` の実装
        *   [![Issue #12 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/12?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/12) #12: 基本的なエラーハンドリング機構の定義
        *   [![Issue #13 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/13?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/13) #13: 単体テストプロジェクトのセットアップ (xUnit)
        *   [![Issue #14 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/14?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/14) #14: `ManagedBuffer<T>` の単体テスト作成
        *   (Issue #11に関連する `SlicedBufferView<T>` のテストも完了)
        *   [![Issue #15 Status](https://img.shields.io/github/issues/detail/state/BitzLabs/BitzBuffer/15?style=flat-square)](https://github.com/BitzLabs/BitzBuffer/issues/15) #15: `BufferManager` と `SimpleManagedProvider` の単体テスト作成

*   **M02: マネージドバッファ向けプーリング戦略の実装** ![Status](https://img.shields.io/badge/Status-未着手-lightgrey)
    *   **目標:** `ManagedBuffer<T>` のためのプーリング機構を実装し、GC負荷軽減の最初の効果を得る。統計情報APIも含む。

*   **M03: 基本的なネイティブバッファ** ![Status](https://img.shields.io/badge/Status-未着手-lightgrey)
    *   **目標:** アンマネージドメモリを扱う `NativeBuffer<T>` を実装する。プーリングはまだ。

*   **M04: ネイティブバッファ向けプーリング戦略の実装** ![Status](https://img.shields.io/badge/Status-未着手-lightgrey)
    *   **目標:** `NativeBuffer<T>` のためのプーリング機構を実装する。

*   **M05: 非連続バッファとゼロコピーアタッチの基礎** ![Status](https://img.shields.io/badge/Status-未着手-lightgrey)
    *   **目標:** `SegmentedManagedBuffer<T>` と `SegmentedNativeBuffer<T>` の基本実装。`AttachSequence` のコピーによるアタッチと、`TryAttachZeroCopy` のコンセプト実証。

*   **M06: 設定APIとプロバイダ管理の強化** ![Status](https://img.shields.io/badge/Status-未着手-lightgrey)
    *   **目標:** `BufferManager` と `IBufferProvider` の設定をFluent APIで詳細に行えるようにする。DI連携の準備。

*   **M07: 総合テスト、ドキュメント整備、最初のリリース候補** ![Status](https://img.shields.io/badge/Status-未着手-lightgrey)
    *   **目標:** 主要機能の結合テスト、READMEのクイックスタートの完成、APIドキュメントの整備、パフォーマンス測定を行い、最初の安定版リリースを目指す。

## 🤝 コントリビューション

設計に関するフィードバック、アイデア、ユースケースの提案など、あらゆる形でのコントリビューションを歓迎します！ Issue を作成するか、関連する議論にご参加ください。

## 📝 ライセンス

このプロジェクトは MIT License のもとで公開されています。

Copyright (c) 2025 BitzLabs

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

--- END OF FILE ---

**主な変更点:**

*   「📚 詳細ドキュメント (設計仕様)」セクションを「📚 ドキュメント」に改題し、新しいフォルダ構成に合わせてリンクを全面的に見直しました。
    *   「システム仕様書」、「開発仕様書」、「詳細設計仕様 (BitzBuffer コア, BitzBuffer.Pipelines)」というカテゴリ分けと、それぞれのエントリポイントへのリンクに変更。
    *   各リンクは、GitHub上で正しく機能する相対パス (`./Docs/...`) に修正。
*   クイックスタートのプロバイダ取得部分を `TryGetProvider` を使う形に修正。

この `README.md` の修正で、新しいドキュメント構成への案内がより明確になったかと思います。
ご確認いただき、問題なければ次に `Docs/02.開発仕様書/00_はじめに.md` のパス修正版を提示します。