 スレッド（タスク）間通信の基盤となる、中核的なパイプライン抽象化について詳述予定

*   **内容:**
    *   `BitzPipeOptions<T>`: パイプの動作を構成するオプションクラスの詳細（`IBufferProvider` 指定、バッファリング閾値、スケジューラなど）。
    *   `BitzPipeScheduler`: 独自実装するスケジューラのAPI定義と、標準実装（`Inline`, `ThreadPool`）の概要。
    *   `BitzPipe<T>`: パイプ本体クラスのAPI定義（`Reader`, `Writer` プロパティ、`Reset()` メソッド）。
    *   `BitzPipeReader<T>`: 読み取り側抽象クラスのAPI定義（`ReadAsync`, `AdvanceTo`, `Complete`, `CancelPendingRead`, `TryRead` など）。
    *   `BitzPipeWriter<T>` (兼 `IBitzBufferWriter<T>`): 書き込み側抽象クラスのAPI定義（`GetMemory`, `Advance`, `AttachSequence`, `TryAttachZeroCopy`, `FlushAsync`, `Complete`, `CancelPendingFlush`, `WriteAsync` など）。
    *   `ReadResult<T>`, `FlushResult`, `AttachmentResult` などの関連データ構造の定義。
    *   基本的な状態遷移と同期メカニズムの設計思想。