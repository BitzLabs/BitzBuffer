# BitzBuffer.Pipelines 設計仕様 - プロセス間通信 (IPC) トランスポート

このドキュメントは、BitzBuffer.Pipelines におけるプロセス間通信 (IPC) を実現するためのトランスポート層の詳細設計について記述します。異なるプロセス間で効率的にデータを送受信するための仕組みを提供します。

トランスポート層の共通的な設計方針については、[`03_P_トランスポート層概要.md`](./03_P_トランスポート層概要.md) を参照してください。
コアパイプライン抽象については [`01_P_コアパイプライン抽象.md`](./01_P_コアパイプライン抽象.md) を参照してください。

## 1. はじめに

プロセス間通信は、同一マシン上で動作する複数の独立したアプリケーションやサービスが連携するために不可欠な技術です。BitzBuffer.Pipelines は、BitzBufferコアの効率的なメモリ管理を活用し、高性能なIPCメカニズムを提供することを目指します。

このドキュメントでは、主要なIPC手法（名前付きパイプ、共有メモリなど）を取り上げ、それぞれのBitzBuffer.Pipelinesにおけるトランスポート実装の設計方針を検討します。

## 2. IPCトランスポートの共通要件と課題

*   **効率性:** プロセス境界を越えるデータコピーのオーバーヘッドを最小限に抑える。
*   **信頼性:** メッセージの順序保証や、ある程度の送達保証（手法による）。
*   **同期:** プロセス間の動作を調整するための同期メカニズム。
*   **セキュリティ:** 不正なプロセスからのアクセス制御。
*   **名前解決/接続確立:** 通信相手のプロセスを特定し、接続を確立する手段。
*   **シリアライズ:** 構造化データをプロセス間で受け渡すためのシリアライズ/デシリアライズ。

## 3. 名前付きパイプ (Named Pipes) トランスポート

.NET では `System.IO.Pipes.NamedPipeServerStream` および `NamedPipeClientStream` を利用したIPCが可能です。

### 3.1. 設計方針

*   `IPipeConnection` および `IPipeConnectionListener` インターフェース（またはそれに類するIPC用抽象）を実装します。
*   内部で `NamedPipeServerStream` / `NamedPipeClientStream` をラップし、それらの非同期APIと `BitzPipe<byte>` を連携させます。
*   データの送受信はバイトストリームとして扱います。

### 3.2. APIとオプション (案)

*   **`NamedPipeConnectionListenerOptions`:**
    *   `string PipeName`: パイプ名。
    *   `int MaxNumberOfServerInstances`: 最大サーバーインスタンス数。
    *   `PipeDirection Direction`: `In`, `Out`, `InOut`。
    *   `PipeOptions Options`: `Asynchronous`, `WriteThrough` など。
    *   セキュリティ設定（`PipeSecurity`）。
*   **`NamedPipeConnectionOptions` (クライアント側):**
    *   `string ServerName`: サーバー名 (`.` はローカルマシン)。
    *   `string PipeName`: パイプ名。
    *   `PipeDirection Direction`: クライアントから見た方向。
    *   `PipeOptions Options`。
    *   偽装レベル（`TokenImpersonationLevel`）。
*   **提供するクラス (案):**
    *   `NamedPipeBitzConnectionListener : IPipeConnectionListener`
    *   `NamedPipeBitzConnection : IPipeConnection`

### 3.3. データフレーミング

名前付きパイプはストリームベースなので、メッセージ境界を維持するためにはデータのフレーミング（例: 長さプレフィックス）が必要です。これはトランスポート実装の責務となります。

## 4. 共有メモリ (Shared Memory) トランスポート

非常に高速なIPC手法ですが、同期や管理が複雑になります。.NET 6以降では `System.IO.MemoryMappedFiles.MemoryMappedFile` をOSの共有メモリ機能として利用できます（ただし、真のプロセス間共有メモリとしての利用にはプラットフォーム依存の考慮が必要な場合も）。

### 4.1. 設計方針

*   BitzBufferのネイティブバッファ (`NativeBuffer<T>`) を共有メモリ領域として利用する、または `MemoryMappedFile` を利用する。
*   プロセス間でのバッファのポインタやオフセットの共有、および同期（ミューテックス、セマフォ、イベントなど）の仕組みが不可欠。
*   リングバッファのような構造を共有メモリ上に構築し、`BitzPipe<byte>` と連携させることを検討。

### 4.2. APIとオプション (案)

*   **`SharedMemoryPipeOptions`:**
    *   `string MemoryName`: 共有メモリ領域を識別するための名前。
    *   `long CapacityBytes`: 共有メモリ領域のサイズ。
    *   同期プリミティブの名前（ミューテックス名など）。
*   **提供するクラス (案):**
    *   `SharedMemoryBitzPipePairFactory` (サーバー側で共有メモリとパイプのペアを作成)
    *   `SharedMemoryBitzPipe` (クライアント側で既存の共有メモリに接続)
        *   これらが `BitzPipeReader<byte>` と `BitzPipeWriter<byte>` を提供する。

### 4.3. 同期と一貫性

共有メモリを使用する場合、データの書き込みと読み取りのタイミングを正確に同期させるためのプロセス間同期オブジェクトが必須です。また、キャッシュ一貫性にも注意が必要です。

## 5. その他のIPC手法の検討 (将来)

*   **メッセージキュー (MSMQなど):** OSレベルのメッセージキューイングシステムとの連携。
*   **ソケット (ローカルループバック):** TCP/IPやUDPをローカルマシン内のIPCとして利用（これはネットワークトランスポートの特殊ケース）。

## 6. シリアライズ/デシリアライズ

IPCトランスポートは基本的にバイト列 (`byte`) を扱います。アプリケーションが構造化データを送受信する場合は、別途シリアライズ/デシリアライズ処理を行う必要があります。
BitzBuffer.Pipelines は、この処理を容易にするためのヘルパーや、特定のシリアライザと統合されたパイプアダプタを将来的に提供することを検討します。
(詳細は [`07_P_通信パターン.md`](./07_P_通信パターン.md) や将来のシリアライズ関連ドキュメントで検討)

## 7. 将来の拡張 (IPCトランスポート関連)

*   より多様なIPCメカニズムへの対応。
*   クロスプラットフォームでのIPC互換性の向上。
*   IPCレベルでの暗号化や認証機能のサポート。
