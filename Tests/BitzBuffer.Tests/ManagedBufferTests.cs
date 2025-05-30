using System;
using System.Buffers;
using System.Linq; // ToArray() のために追加
using BitzLabs.BitzBuffer; // AttachmentResult がここにある想定
using BitzLabs.BitzBuffer.Managed;
using Xunit;

namespace BitzLabs.BitzBuffer.Tests.Managed // 名前空間を SlicedBufferViewTests と合わせる例
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
            Assert.Equal(0, buffer.Length);
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
            Assert.True(mem1.Length >= 2);
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
            var mem = buffer.GetMemory(0);

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
            var mem = buffer.GetMemory(5);

            // Assert
            Assert.Equal(2, mem.Length);
        }

        [Fact(DisplayName = "GetMemory: 残容量が0の場合、空のメモリを返す")]
        public void GetMemory_WhenNoRemainingCapacity_ReturnsEmptyMemory()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[10], true);
            buffer.Advance(10); // Length = 10, RemainingCapacity = 0

            // Act
            var mem = buffer.GetMemory(1);

            // Assert
            Assert.True(mem.IsEmpty);
        }


        [Theory(DisplayName = "Advance: 不正なカウントでArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1)]
        [InlineData(11)]
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
            buffer.Write(data.AsSpan());

            // Act
            var seq = buffer.AsReadOnlySequence();

            // Assert
            Assert.Equal(data.Length, seq.Length);
            Assert.Equal(data, seq.ToArray());
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
            var ros = TestUtils.CreateReadOnlySequence(segment1, segment2);

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
            var buffer = new ManagedBuffer<byte>(new byte[2], true);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => buffer.Write(new byte[] { 1, 2, 3 }.AsSpan()));
            Assert.Equal("source", exception.ParamName);
        }

        [Fact(DisplayName = "Write: バッファ満杯後にさらに書き込もうとするとArgumentExceptionをスローする")]
        public void Write_ThrowsIfOverCapacityAfterPartialWrite()
        {
            // Arrange
            var buffer = new ManagedBuffer<byte>(new byte[2], true);
            buffer.Write(new byte[] { 1, 2 }.AsSpan());

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => buffer.Write(new byte[] { 3 }.AsSpan()));
            Assert.Equal("source", exception.ParamName);
        }

        // --- Sliceメソッドのテスト ---

        [Fact(DisplayName = "Slice: SlicedBufferViewを返し内容・長さが正しい")]
        public void Slice_ReturnsSlicedBufferViewWithCorrectContentAndLength()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);
            buffer.Write(new int[] { 10, 20, 30, 40, 50 }.AsSpan());

            // Act
            var slice = buffer.Slice(1, 3); // 20, 30, 40

            // Assert
            Assert.IsType<SlicedBufferView<int>>(slice);
            Assert.Equal(3, slice.Length);
            Assert.Equal(new int[] { 20, 30, 40 }, slice.AsReadOnlySequence().ToArray());
            Assert.False(slice.IsDisposed);
        }

        [Fact(DisplayName = "Slice(start): SlicedBufferViewを返し内容・長さが正しい")]
        public void Slice_SingleArgument_ReturnsSlicedBufferViewToEnd()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);
            buffer.Write(new int[] { 1, 2, 3, 4, 5 }.AsSpan());

            // Act
            var slice = buffer.Slice(2); // 3, 4, 5

            // Assert
            Assert.IsType<SlicedBufferView<int>>(slice);
            Assert.Equal(3, slice.Length);
            Assert.Equal(new int[] { 3, 4, 5 }, slice.AsReadOnlySequence().ToArray());
        }

        [Fact(DisplayName = "Slice: Disposeしても元バッファには影響しない")]
        public void Slice_Dispose_DoesNotAffectSourceBuffer()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[3], true);
            buffer.Write(new int[] { 1, 2, 3 }.AsSpan());
            var slice = buffer.Slice(0, 2);

            // Act
            (slice as IDisposable)?.Dispose();

            // Assert
            Assert.True(slice.IsDisposed);
            Assert.False(buffer.IsDisposed);
            Assert.Equal(3, buffer.Length);
        }

        [Fact(DisplayName = "Slice: 空スライスも正しく返す")]
        public void Slice_EmptySlice_ReturnsEmptySlicedBufferView()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[3], true);
            buffer.Write(new int[] { 1, 2, 3 }.AsSpan());

            // Act
            var slice = buffer.Slice(2, 0); // 空

            // Assert
            Assert.IsType<SlicedBufferView<int>>(slice);
            Assert.True(slice.IsEmpty);
            Assert.Equal(0, slice.Length);
            Assert.Empty(slice.AsReadOnlySequence().ToArray());
        }

        [Theory(DisplayName = "Slice(start, length): 不正な引数でArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1, 1, "start")]
        [InlineData(4, 1, "start")]   // buffer.Length が 3 の場合、start > Length
        [InlineData(0, -1, "length")]
        [InlineData(1, 3, "length")]   // buffer.Length が 3 の場合、start + length > Length
        public void Slice_StartLength_WithInvalidArguments_ThrowsArgumentOutOfRangeException(long start, long length, string paramName)
        {
            var buffer = new ManagedBuffer<int>(new int[3], true);
            buffer.Write(new int[] { 1, 2, 3 }.AsSpan());
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(start, length));
            Assert.Equal(paramName, exception.ParamName);
        }

        [Theory(DisplayName = "Slice(start): 不正な引数でArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1, "start")] // start < 0
        [InlineData(4, "start")]  // start > buffer.Length (Length=3 の場合)
        public void Slice_Start_WithInvalidArguments_ThrowsArgumentOutOfRangeException(long start, string paramName)
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[3], true);
            buffer.Write(new int[] { 1, 2, 3 }.AsSpan()); // Length = 3

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(start));
            Assert.Equal(paramName, exception.ParamName);
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
        [InlineData(-1)]
        [InlineData(6)]
        public void Truncate_WithInvalidLength_ThrowsArgumentOutOfRangeException(long newLength)
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[5], true);
            buffer.Write(new int[] { 1, 2, 3, 4, 5 }.AsSpan());

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
            Assert.True(buffer.IsDisposed);
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
        }

        [Fact(DisplayName = "Dispose後: 読み取り操作でObjectDisposedExceptionをスローする")]
        public void ReadOperations_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[2], true);
            buffer.Write(1);
            buffer.Write(2);
            buffer.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => buffer.AsReadOnlySequence());
            Assert.Throws<ObjectDisposedException>(() => buffer.TryGetSingleSpan(out _));
            Assert.Throws<ObjectDisposedException>(() => buffer.TryGetSingleMemory(out _));
            Assert.Throws<ObjectDisposedException>(() => { var len = buffer.Length; });
            Assert.Throws<ObjectDisposedException>(() => buffer.Slice(0));
            Assert.Throws<ObjectDisposedException>(() => { var isEmpty = buffer.IsEmpty; });
            Assert.Throws<ObjectDisposedException>(() => { var isSingle = buffer.IsSingleSegment; });
        }

        // --- 所有権がない場合のテスト (IsOwner = false) ---

        [Fact(DisplayName = "非所有時: 書き込み系メソッドでInvalidOperationExceptionをスローする")]
        public void WriteOperations_WhenNotOwner_ThrowsInvalidOperationException()
        {
            // Arrange
            var buffer = new ManagedBuffer<int>(new int[2], false);

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
            var array = new int[10];
            var buffer = new ManagedBuffer<int>(array, false); // isOwner = false, Length = 0

            // Act & Assert
            Assert.False(buffer.IsDisposed, "バッファは破棄されていないはずです。");
            Assert.False(buffer.IsOwner, "バッファは非所有のはずです。");

            Exception? ex = Record.Exception(() =>
            {
                var seq = buffer.AsReadOnlySequence();
                Assert.True(seq.IsEmpty, "初期状態(Length=0)ではシーケンスは空のはずです。");

                bool successSpan = buffer.TryGetSingleSpan(out var span);
                Assert.False(successSpan, "初期状態(Length=0)ではTryGetSingleSpanはfalseを返すはずです。");
                Assert.True(span.IsEmpty, "TryGetSingleSpanが失敗した場合、spanは空のはずです。");

                bool successMemory = buffer.TryGetSingleMemory(out var memory);
                Assert.False(successMemory, "初期状態(Length=0)ではTryGetSingleMemoryはfalseを返すはずです。");
                Assert.True(memory.IsEmpty, "TryGetSingleMemoryが失敗した場合、memoryは空のはずです。");

                var len = buffer.Length;
                // 初期状態のLengthは0のはずです。
                Assert.Equal(0L, len); // 期待値をlongに明示 (lenがlongのため)

                var isEmpty = buffer.IsEmpty;
                Assert.True(isEmpty, "初期状態のIsEmptyはtrueのはずです。");

                var isSingleSegment = buffer.IsSingleSegment;
                Assert.True(isSingleSegment, "ManagedBufferは常に単一セグメントのはずです。");

                var sliceView1 = buffer.Slice(0);
                Assert.NotNull(sliceView1);
                Assert.True(sliceView1.IsEmpty);
                Assert.Equal(0, sliceView1.Length);

                var sliceView2 = buffer.Slice(0, 0);
                Assert.NotNull(sliceView2);
                Assert.True(sliceView2.IsEmpty);
                Assert.Equal(0, sliceView2.Length);
            });
            Assert.Null(ex);
        }


        [Fact(DisplayName = "非所有時: Disposeしても基盤配列の内容に影響しない")]
        public void Dispose_WhenNotOwner_DoesNotAffectUnderlyingArrayContent()
        {
            // Arrange
            var array = new int[2] { 11, 22 };
            var buffer = new ManagedBuffer<int>(array, false);

            // Act
            buffer.Dispose();

            // Assert
            Assert.True(buffer.IsDisposed);
            Assert.False(buffer.IsOwner);
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
            Assert.Contains("Capacity=10", str);
            Assert.Contains("Owner=True", str);
            Assert.Contains("Disposed=False", str);

            buffer.Dispose();
            str = buffer.ToString();
            Assert.Contains("Length=2", str);
            Assert.Contains("Capacity=0", str); // _array is null after Dispose if owned
            Assert.Contains("Owner=False", str);
            Assert.Contains("Disposed=True", str);
        }
    }
}