using System;
using System.Buffers;
using System.Linq; // ToArray() のために追加
using BitzLabs.BitzBuffer.Managed; // BitzLabs.BitzBuffer 名前空間に ManagedBuffer<T> がある前提
using Xunit;

// 名前空間はプロジェクトの構成に合わせてください
// 例: namespace BitzLabs.BitzBuffer.Tests.Managed
namespace BitzBuffer.Tests
{
    public class ManagedBufferTests
    {
        // --- コンストラクタと初期状態のテスト ---

        [Fact(DisplayName = "コンストラクタ(所有権あり): 状態を正しく初期化する")]
        public void Constructor_WhenOwning_InitializesStateCorrectly()
        {
            // Arrange
            var array = new int[10];
            var takeOwnership = true;

            // Act
            var buffer = new ManagedBuffer<int>(array, takeOwnership);

            // Assert
            Assert.Equal(0, buffer.Length);
            Assert.True(buffer.IsOwner);
            Assert.False(buffer.IsDisposed);
            Assert.True(buffer.IsEmpty);
            Assert.True(buffer.IsSingleSegment);
        }

        [Fact(DisplayName = "コンストラクタ(所有権なし): IsOwnerをfalseに初期化する")]
        public void Constructor_WhenNotOwning_InitializesIsOwnerToFalse()
        {
            // Arrange
            var array = new int[10];
            var takeOwnership = false;

            // Act
            var buffer = new ManagedBuffer<int>(array, takeOwnership);

            // Assert
            Assert.False(buffer.IsOwner);
            Assert.Equal(0, buffer.Length); // 他の初期状態も確認
            Assert.False(buffer.IsDisposed);
        }

        [Fact(DisplayName = "コンストラクタ: null配列でArgumentNullExceptionをスローする")]
        public void Constructor_WithNullArray_ThrowsArgumentNullException()
        {
            // Arrange
            int[]? nullArray = null;

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new ManagedBuffer<int>(nullArray!, true));
            Assert.Equal("array", exception.ParamName);
        }

        // --- GetMemoryとAdvanceのテスト ---

        [Fact(DisplayName = "GetMemory: 要求サイズのメモリを返し、AdvanceでLengthが更新される")]
        public void GetMemoryAndAdvance_WritesAndReadsCorrectly()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[10], true);
            byte value1 = 0xAB;
            byte value2 = 0xCD;

            // Act
            var mem1 = buffer.GetMemory(2);
            Assert.True(mem1.Length >= 2); // 要求サイズ以上であることを確認 (実装による)
            mem1.Span[0] = value1;
            mem1.Span[1] = value2;
            buffer.Advance(2);

            // Assert
            Assert.Equal(2, buffer.Length);
            var seq = buffer.AsReadOnlySequence();
            Assert.Equal(2, seq.Length);
            var resultArray = seq.ToArray();
            Assert.Equal(value1, resultArray[0]);
            Assert.Equal(value2, resultArray[1]);

            // さらに追記
            byte value3 = 0xEF;
            var mem2 = buffer.GetMemory(1);
            Assert.True(mem2.Length >= 1);
            mem2.Span[0] = value3;
            buffer.Advance(1);

            Assert.Equal(3, buffer.Length);
            seq = buffer.AsReadOnlySequence();
            Assert.Equal(3, seq.Length);
            resultArray = seq.ToArray();
            Assert.Equal(value1, resultArray[0]);
            Assert.Equal(value2, resultArray[1]);
            Assert.Equal(value3, resultArray[2]);
        }

        [Fact(DisplayName = "GetMemory: sizeHint=0の場合、残り全容量のメモリを返す")]
        public void GetMemory_WithZeroHint_ReturnsRemainingCapacity()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[10], true);
            buffer.Advance(3); // Length = 3, RemainingCapacity = 7

            // Act
            var mem = buffer.GetMemory(0); // sizeHint = 0

            // Assert
            Assert.Equal(7, mem.Length);
        }

        [Fact(DisplayName = "GetMemory: 残容量より大きな要求でも残容量分のメモリを返す")]
        public void GetMemory_WhenHintIsLargerThanRemaining_ReturnsUpToRemainingCapacity()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[10], true);
            buffer.Advance(8); // Length = 8, RemainingCapacity = 2

            // Act
            var mem = buffer.GetMemory(5); // 5を要求

            // Assert
            Assert.Equal(2, mem.Length); // 実際には残容量の2が返る
        }

        [Fact(DisplayName = "GetMemory: 残容量が0の場合、空のメモリを返す")]
        public void GetMemory_WhenNoRemainingCapacity_ReturnsEmptyMemory()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[10], true);
            buffer.Advance(10); // Length = 10, RemainingCapacity = 0

            // Act
            var mem = buffer.GetMemory(1); // 1を要求 (sizeHint=0でも同じはず)

            // Assert
            Assert.True(mem.IsEmpty);
        }


        [Theory(DisplayName = "Advance: 不正なカウントでArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1)] // 負のカウント
        [InlineData(11)] // 容量(10)を超えるカウント
        public void Advance_WithInvalidCount_ThrowsArgumentOutOfRangeException(int countToAdvance)
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[10], true);

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Advance(countToAdvance));
            Assert.Equal("count", exception.ParamName);
        }

        [Fact(DisplayName = "Advance: ちょうど容量いっぱいに進めることができる")]
        public void Advance_ToFullCapacity_Works()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);

            // Act
            buffer.Advance(5);

            // Assert
            Assert.Equal(5, buffer.Length);
        }

        // --- 読み取り操作のテスト (AsReadOnlySequence, TryGetSingleSpan, TryGetSingleMemory) ---

        [Fact(DisplayName = "AsReadOnlySequence: 書き込まれた正しいデータを返す")]
        public void AsReadOnlySequence_ReturnsCorrectWrittenData()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[10], true);
            var data = new int[] { 10, 20, 30, 40, 50 };
            buffer.Write(data.AsSpan()); // Write(ReadOnlySpan<T>) を使用して書き込み

            // Act
            var seq = buffer.AsReadOnlySequence();

            // Assert
            Assert.Equal(data.Length, seq.Length);
            Assert.Equal(data, seq.ToArray()); // ToArray() でシーケンス全体を比較
        }

        [Fact(DisplayName = "AsReadOnlySequence: 空バッファで空のシーケンスを返す")]
        public void AsReadOnlySequence_EmptyBuffer_ReturnsEmptySequence()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);

            // Act
            var seq = buffer.AsReadOnlySequence();

            // Assert
            Assert.True(seq.IsEmpty);
            Assert.Equal(0, seq.Length);
        }

        [Fact(DisplayName = "TryGetSingleSpan/Memory: データがある場合に正しく取得できる")]
        public void TryGetSingleSpanAndMemory_WhenDataExists_ReturnsTrueAndCorrectData()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[4], true);
            buffer.Write(77);
            buffer.Write(88);

            // Act & Assert for Span
            Assert.True(buffer.TryGetSingleSpan(out var span));
            Assert.Equal(2, span.Length);
            Assert.Equal(77, span[0]);
            Assert.Equal(88, span[1]);

            // Act & Assert for Memory
            Assert.True(buffer.TryGetSingleMemory(out var memory));
            Assert.Equal(2, memory.Length);
            Assert.Equal(77, memory.Span[0]);
            Assert.Equal(88, memory.Span[1]);
        }

        [Fact(DisplayName = "TryGetSingleSpan/Memory: 空バッファでfalseと空の領域を返す")]
        public void TryGetSingleSpanAndMemory_EmptyBuffer_ReturnsFalseAndEmpty()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[4], true);

            // Act & Assert for Span
            Assert.False(buffer.TryGetSingleSpan(out var emptySpan));
            Assert.True(emptySpan.IsEmpty);

            // Act & Assert for Memory
            Assert.False(buffer.TryGetSingleMemory(out var emptyMemory));
            Assert.True(emptyMemory.IsEmpty);
        }

        // --- Writeメソッド群のテスト ---

        [Fact(DisplayName = "Write(T): 単一要素を正しく書き込み、Lengthを更新する")]
        public void Write_SingleValue_UpdatesLengthAndContent()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[3], true);
            int valueToWrite = 55;

            // Act
            buffer.Write(valueToWrite);

            // Assert
            Assert.Equal(1, buffer.Length);
            Assert.Equal(valueToWrite, buffer.AsReadOnlySequence().First.Span[0]);
        }

        [Fact(DisplayName = "Write(ReadOnlySpan): Spanの内容を正しく書き込む")]
        public void Write_ReadOnlySpan_WritesCorrectly()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[5], true);
            var dataToWrite = new byte[] { 11, 22, 33 };

            // Act
            buffer.Write(dataToWrite.AsSpan());

            // Assert
            Assert.Equal(dataToWrite.Length, buffer.Length);
            Assert.Equal(dataToWrite, buffer.AsReadOnlySequence().ToArray());
        }

        [Fact(DisplayName = "Write(ReadOnlyMemory): Memoryの内容を正しく書き込む")]
        public void Write_ReadOnlyMemory_WritesCorrectly()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);
            var dataToWrite = new int[] { 71, 82, 93 };

            // Act
            buffer.Write(new ReadOnlyMemory<int>(dataToWrite));

            // Assert
            Assert.Equal(dataToWrite.Length, buffer.Length);
            Assert.Equal(dataToWrite, buffer.AsReadOnlySequence().ToArray());
        }

        [Fact(DisplayName = "Write(ReadOnlySequence): Sequenceの内容を正しく書き込む")]
        public void Write_ReadOnlySequence_WritesCorrectly()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[6], true);
            var segment1 = new int[] { 1, 2 };
            var segment2 = new int[] { 3, 4, 5 };
            // ReadOnlySequence<T> を作成 (複数のセグメントを持つ場合もテストできるよう)
            var ros = TestUtils.CreateReadOnlySequence(segment1, segment2); // TestUtilsは別途作成想定

            // Act
            buffer.Write(ros);

            // Assert
            var expectedData = segment1.Concat(segment2).ToArray();
            Assert.Equal(expectedData.Length, buffer.Length);
            Assert.Equal(expectedData, buffer.AsReadOnlySequence().ToArray());
        }

        [Fact(DisplayName = "Write: 初期容量を超えるデータ書き込みでArgumentExceptionをスローする")]
        public void Write_ThrowsIfSourceIsLargerThanInitialCapacity()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[2], true); // 容量2

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => buffer.Write(new byte[] { 1, 2, 3 }.AsSpan())); // 長さ3のデータ
            Assert.Equal("source", exception.ParamName);
        }

        [Fact(DisplayName = "Write: バッファ満杯後にさらに書き込もうとするとArgumentExceptionをスローする")]
        public void Write_ThrowsIfOverCapacityAfterPartialWrite()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[2], true);
            buffer.Write(new byte[] { 1, 2 }.AsSpan()); // これで満杯

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => buffer.Write(new byte[] { 3 }.AsSpan()));
            Assert.Equal("source", exception.ParamName); // またはvalue（Write(T)の場合）
        }

        // --- Sliceメソッドのテスト ---

        [Fact(DisplayName = "Slice: 未実装のためNotImplementedExceptionをスローする")]
        public void Slice_ThrowsNotImplemented()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[3], true);
            buffer.Write(new int[] { 1, 2, 3 }.AsSpan()); // データを書き込んでおく

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => buffer.Slice(0, 1));
            Assert.Throws<NotImplementedException>(() => buffer.Slice(1));
        }

        [Theory(DisplayName = "Slice: 不正な引数でArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1, 1)]  // start < 0
        [InlineData(4, 1)]   // start > length
        [InlineData(1, -1)]  // length < 0
        [InlineData(1, 3)]   // start + length > length
        public void Slice_WithInvalidArguments_ThrowsArgumentOutOfRangeException(long start, long length)
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[3], true);
            buffer.Write(new int[] { 1, 2, 3 }.AsSpan()); // Length = 3

            // Act & Assert
            if (length < 0 || start + length > buffer.Length || start < 0 || start > buffer.Length) // Slice(long)も考慮
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(start, length));
            }
            else // Slice(long) のテスト用 (length引数なし)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(start));
            }
        }


        // --- 状態変更メソッド (Clear, Truncate) のテスト ---

        [Fact(DisplayName = "Clear: Lengthを0にしIsEmptyをtrueにする")]
        public void Clear_ResetsLengthAndIsEmpty()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[5], true);
            buffer.Write(new byte[] { 10, 20, 30 }.AsSpan());
            Assert.Equal(3, buffer.Length);

            // Act
            buffer.Clear();

            // Assert
            Assert.Equal(0, buffer.Length);
            Assert.True(buffer.IsEmpty);
        }

        [Fact(DisplayName = "Truncate: Lengthを正しく短縮し、内容は維持される")]
        public void Truncate_ShortensLengthAndPreservesContent()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);
            var initialData = new int[] { 11, 22, 33, 44, 55 };
            buffer.Write(initialData.AsSpan());

            // Act
            buffer.Truncate(3); // 長さを3に

            // Assert
            Assert.Equal(3, buffer.Length);
            var expectedData = new int[] { 11, 22, 33 };
            Assert.Equal(expectedData, buffer.AsReadOnlySequence().ToArray());
        }

        [Theory(DisplayName = "Truncate: 不正な長さでArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1)]  // 負の長さ
        [InlineData(6)]   // 現在の長さ(5)を超える長さ
        public void Truncate_WithInvalidLength_ThrowsArgumentOutOfRangeException(long newLength)
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);
            buffer.Write(new int[] { 1, 2, 3, 4, 5 }.AsSpan()); // Length = 5

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Truncate(newLength));
            Assert.Equal("length", exception.ParamName);
        }

        // --- Disposeメソッドのテスト ---

        [Fact(DisplayName = "Dispose: IsDisposedをtrueにしIsOwnerをfalseにする")]
        public void Dispose_SetsIsDisposedAndIsOwnerToFalse()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[2], true);

            // Act
            buffer.Dispose();

            // Assert
            Assert.True(buffer.IsDisposed);
            Assert.False(buffer.IsOwner);
        }

        [Fact(DisplayName = "Dispose: 複数回呼び出しても例外をスローしない")]
        public void Dispose_MultipleTimes_DoesNotThrowException()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[2], true);

            // Act
            Exception? ex = Record.Exception(() =>
            {
                buffer.Dispose();
                buffer.Dispose();
            });

            // Assert
            Assert.Null(ex);
            Assert.True(buffer.IsDisposed); // 状態は維持
        }

        [Fact(DisplayName = "Dispose後: 書き込み/状態変更操作でObjectDisposedExceptionをスローする")]
        public void WriteOrStateChangeOperations_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[2], true);
            buffer.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => buffer.Write(1));
            Assert.Throws<ObjectDisposedException>(() => buffer.GetMemory());
            Assert.Throws<ObjectDisposedException>(() => buffer.Advance(1));
            Assert.Throws<ObjectDisposedException>(() => buffer.Clear());
            Assert.Throws<ObjectDisposedException>(() => buffer.Truncate(0));
            // Attach, Prependも同様
        }

        [Fact(DisplayName = "Dispose後: 読み取り操作でObjectDisposedExceptionをスローする")]
        public void ReadOperations_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[2], true);
            // データを書き込んでおく (読み取り対象がある状態を作るため)
            buffer.Write(1);
            buffer.Write(2);
            buffer.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => buffer.AsReadOnlySequence());
            Assert.Throws<ObjectDisposedException>(() => buffer.TryGetSingleSpan(out _));
            Assert.Throws<ObjectDisposedException>(() => buffer.TryGetSingleMemory(out _));
            Assert.Throws<ObjectDisposedException>(() => { var len = buffer.Length; }); // Lengthプロパティも
            Assert.Throws<ObjectDisposedException>(() => buffer.Slice(0));
        }

        // --- 所有権がない場合のテスト (IsOwner = false) ---

        [Fact(DisplayName = "非所有時: 書き込み系メソッドでInvalidOperationExceptionをスローする")]
        public void WriteOperations_WhenNotOwner_ThrowsInvalidOperationException()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[2], false); // isOwner = false

            // Act & Assert
            Assert.False(buffer.IsOwner);
            Assert.Throws<InvalidOperationException>(() => buffer.Write(1));
            Assert.Throws<InvalidOperationException>(() => buffer.GetMemory());
            Assert.Throws<InvalidOperationException>(() => buffer.Advance(1));
            Assert.Throws<InvalidOperationException>(() => buffer.Clear());
            Assert.Throws<InvalidOperationException>(() => buffer.Truncate(0));
        }

        [Fact(DisplayName = "非所有時: 読み取り操作は許可される")]
        public void ReadOperations_WhenNotOwner_AreAllowed()
        {
            // Arrange
            var array = new int[] { 10, 20 };
            var buffer = new ManagedBuffer<int>(array, false); // isOwner = false
            // 非所有バッファに直接書き込む手段は提供されない前提だが、テストのために内部状態を操作するか、
            // または、isOwner=trueで作成・書き込み後、何らかの方法でisOwner=falseに遷移させるケースを模倣する。
            // ManagedBufferの設計上、直接 _length を変更する手段はないため、
            // このテストは「もし非所有バッファが何らかの形でデータを持っていた場合」の読み取り可能性を見る。
            // もっとも、コンストラクタで初期長も渡せるようになれば、このテストはより自然になる。
            // 現状では、このテストの有効性は限定的かもしれないが、IsOwnerフラグの役割確認にはなる。
            // ここでは、読み取りが「例外をスローしないこと」を確認するに留める。

            // Act & Assert
            Assert.False(buffer.IsDisposed);
            Assert.False(buffer.IsOwner);

            Exception? ex = Record.Exception(() =>
            {
                var seq = buffer.AsReadOnlySequence();
                Assert.True(seq.IsEmpty); // 初期長0なので空のはず
                buffer.TryGetSingleSpan(out var span);
                Assert.True(span.IsEmpty);
                var len = buffer.Length;
                Assert.Equal(0, len);
                // Sliceも例外が出ないことを確認 (ただし Slice 自体は NotImplemented)
                Assert.Throws<NotImplementedException>(() => buffer.Slice(0));
            });
            Assert.Null(ex);
        }


        [Fact(DisplayName = "非所有時: Disposeしても基盤配列の内容に影響しない")]
        public void Dispose_WhenNotOwner_DoesNotAffectUnderlyingArrayContent()
        {
            // Arrange
            var array = new int[2] { 11, 22 };
            var buffer = new ManagedBuffer<int>(array, false); // isOwner = false

            // Act
            buffer.Dispose();

            // Assert
            Assert.True(buffer.IsDisposed);
            Assert.False(buffer.IsOwner); // Dispose後は常にfalse
            // 基盤配列の内容は変更されていないはず
            Assert.Equal(11, array[0]);
            Assert.Equal(22, array[1]);
        }

        // --- その他: ToStringの出力確認 ---

        [Fact(DisplayName = "ToString: バッファの状態を文字列で正しく出力する")]
        public void ToString_OutputsStateCorrectly()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[10], true);
            buffer.Write(99);
            buffer.Write(100);

            // Act
            var str = buffer.ToString();

            // Assert
            Assert.Contains("ManagedBuffer<Int32>", str);
            Assert.Contains("Length=2", str);
            Assert.Contains($"Capacity={buffer.AsReadOnlySequence().ToArray().Length + 8}", str); // Capacityは内部配列の長さ
            Assert.Contains("Owner=True", str);
            Assert.Contains("Disposed=False", str);

            buffer.Dispose();
            str = buffer.ToString();
            Assert.Contains("Length=2", str); // Lengthは維持される想定
            Assert.Contains("Capacity=0", str); // Dispose後は _array が null になるため Capacity は 0 (またはアクセス不可)
            Assert.Contains("Owner=False", str);
            Assert.Contains("Disposed=True", str);
        }
    }

    // Helper class for creating ReadOnlySequence<T> with multiple segments for testing
    // (This should ideally be in a shared test utilities project or file)
    internal static class TestUtils
    {
        public static ReadOnlySequence<T> CreateReadOnlySequence<T>(params T[][] segmentsData)
        {
            if (segmentsData == null || segmentsData.Length == 0)
            {
                return ReadOnlySequence<T>.Empty;
            }

            SegmentBase<T>? first = null;
            SegmentBase<T>? last = null;

            foreach (var data in segmentsData)
            {
                var segment = new Segment<T>(new ReadOnlyMemory<T>(data));
                if (first == null)
                {
                    first = segment;
                    last = segment;
                }
                else
                {
                    last = last!.Append(segment);
                }
            }
            return new ReadOnlySequence<T>(first!, 0, last!, last!.Memory.Length);
        }

        private abstract class SegmentBase<T> : ReadOnlySequenceSegment<T>
        {
            protected SegmentBase(ReadOnlyMemory<T> memory)
            {
                Memory = memory;
            }

            public SegmentBase<T> Append(SegmentBase<T> nextSegment)
            {
                Next = nextSegment;
                nextSegment.RunningIndex = RunningIndex + Memory.Length;
                return nextSegment;
            }
        }

        private sealed class Segment<T> : SegmentBase<T>
        {
            public Segment(ReadOnlyMemory<T> memory) : base(memory) { }
        }
    }
}