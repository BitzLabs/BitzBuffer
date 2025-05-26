# BitzBuffer ソースコード (Src/)

このフォルダには、BitzBufferライブラリの全てのソースコードが含まれています。
本ライブラリの設計思想、API定義、アーキテクチャに関する詳細な仕様書は、リポジトリルートの [`Docs/BitzBuffer/`](../Docs/BitzBuffer/) フォルダにあります。

## フォルダ構成の概要

```
Src/
├── BitzBuffer/                     # ライブラリ本体のコアプロジェクト
│   ├── Interfaces/                 # 公開インターフェース群
│   │   ├── Configuration/
│   │   └── Pooling/
│   ├── Buffers/                    # IBuffer<T> の具体的な実装
│   │   ├── Managed/
│   │   └── Native/
│   ├── Configuration/              # 設定APIの具体的な実装クラス
│   │   └── Pooling/
│   ├── Diagnostics/                # 例外クラスと統計情報構造体
│   ├── Internal/                   # ライブラリ内部でのみ使用するヘルパークラスやユーティリティ
│   │   └── Handles/
│   ├── Memory/                     # MemoryManager<T> の実装など、低レベルメモリ管理関連
│   ├── Pooling/                    # プーリング戦略、アロケータ、ライフサイクルフックの具体的な実装
│   │   ├── Managed/
│   │   └── Native/
│   ├── Providers/                  # IBufferProvider の具体的な実装クラス
│   │   ├── Managed/
│   │   └── Native/
│   ├── BitzBuffer.csproj           # プロジェクトファイル
│   ├── BufferManager.cs            # BufferManager の実装
│   ├── BitzBufferServiceCollectionExtensions.cs # DI拡張メソッド
│   └── AttachmentResult.cs         # enum など、分類しにくい小規模な型
│
├── BitzBuffer.Pipelines/           # (将来) 高レベルデータ処理パイプラインプロジェクト
│   ├── BitzBuffer.Pipelines.csproj
│   └── ...
│
└── README.md                       # このファイル
```

## プロジェクト詳細

### 1. `BitzBuffer/`

BitzBufferライブラリの中核となる機能を提供するメインプロジェクトです。
各コンポーネントの実装は、[`Docs/BitzBuffer/`](../Docs/BitzBuffer/) にある設計仕様書に基づいています。

*   **主な機能:**
    *   高性能なバッファ (`IBuffer<T>`) の提供 (マネージド、アンマネージド、連続、非連続)
    *   効率的なバッファプーリング戦略
    *   統一されたバッファ管理 (`BufferManager`, `IBufferProvider`)
    *   柔軟な設定APIとDIコンテナ連携
*   **主要な名前空間のプレフィックス:** `BitzBuffer`
*   **サブフォルダ構成:**
    *   `Interfaces/`: ライブラリの公開APIコントラクトとなるインターフェース群を定義しています。詳細は [`01_Core_Interfaces.md`](../Docs/BitzBuffer/01_Core_Interfaces.md) などを参照してください。
        *   `Interfaces/Pooling/`: プーリング機構に関連するインターフェース。詳細は [`03_Pooling.md`](../Docs/BitzBuffer/03_Pooling.md) を参照してください。
        *   `Interfaces/Configuration/`: `BufferManager` の設定に関連するインターフェース。詳細は [`02_Providers_And_Buffers.md`](../Docs/BitzBuffer/02_Providers_And_Buffers.md) を参照してください。
    *   `Buffers/`: `IBuffer<T>` の具体的な実装クラスを格納しています。詳細は [`02_Providers_And_Buffers.md`](../Docs/BitzBuffer/02_Providers_And_Buffers.md) を参照してください。
    *   `Configuration/`: `BufferManager` の設定を行うためのオプションクラスの実装が含まれます。プーリング関連の設定オプションは `Pooling/` サブフォルダに配置されます。詳細は [`02_Providers_And_Buffers.md`](../Docs/BitzBuffer/02_Providers_And_Buffers.md) を参照してください。
    *   `Diagnostics/`: カスタム例外クラスやプーリングの統計情報を扱うための構造体などを定義しています。詳細は [`05_Error_Handling.md`](../Docs/BitzBuffer/05_Error_Handling.md) および [`03_Pooling.md`](../Docs/BitzBuffer/03_Pooling.md) を参照してください。
    *   `Internal/`: ライブラリ内部でのみ使用されるヘルパークラスや、低レベルなリソース管理クラスが含まれます。
    *   `Memory/`: より低レベルなメモリアクセスや管理に関連するクラスを配置しています。
    *   `Pooling/`: バッファプーリング戦略の具体的な実装、メモリリソースのアロケータ、バッファのライフサイクルフックなどが含まれます。詳細は [`03_Pooling.md`](../Docs/BitzBuffer/03_Pooling.md) を参照してください。
    *   `Providers/`: 特定の種類のバッファを提供する `IBufferProvider` の実装クラス群です。詳細は [`02_Providers_And_Buffers.md`](../Docs/BitzBuffer/02_Providers_And_Buffers.md) を参照してください。
*   **エントリーポイント:**
    *   `BufferManager.cs`: ライブラリの主要なエントリーポイントであり、各種バッファプロバイダを管理します。
    *   `BitzBufferServiceCollectionExtensions.cs`: `Microsoft.Extensions.DependencyInjection` を使用したDIコンテナへの登録を容易にするための拡張メソッドを提供します。
*   **その他:**
    *   プロジェクトルート直下には、特定のサブフォルダに分類しにくい小規模な型 (例: `AttachmentResult.cs`) が配置されることがあります。

### 2. `BitzBuffer.Pipelines/` (将来の拡張)

このプロジェクトは、BitzBufferのバッファ管理機能を基盤とした、高レベルな非同期データ処理パイプライン機能の提供を目指すものです。`System.IO.Pipelines` に似た、より柔軟で高性能な代替手段の実現を検討します。

**現在のステータス:** 計画段階であり、実装はまだ開始されていません。詳細は [`00_Overview.md`](../Docs/BitzBuffer/00_Overview.md) の関連プロジェクトセクションを参照してください。

## ビルドと開発

各プロジェクトは標準的な .NET CLI コマンドでビルドできます。

```bash
# BitzBuffer プロジェクトをビルド
dotnet build ./BitzBuffer/BitzBuffer.csproj
```

開発に関する詳細（コーディング規約、ブランチ戦略など）は、リポジトリルートの [`Docs/Development/`](../Docs/Development/) フォルダを参照してください。
ライブラリの設計仕様については、リポジトリルートの [`Docs/BitzBuffer/`](../Docs/BitzBuffer/) フォルダに各詳細ドキュメントが格納されています。
特に、全体の概要は [`00_Overview.md`](../Docs/BitzBuffer/00_Overview.md) を参照してください。