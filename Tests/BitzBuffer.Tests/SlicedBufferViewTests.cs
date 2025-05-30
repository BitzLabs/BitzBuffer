using System;
using System.Buffers;
using System.Collections.Generic; // For EqualityComparer
using System.Linq;
using BitzLabs.BitzBuffer; // IReadOnlyBuffer<T> がここにある想定
using BitzLabs.BitzBuffer.Managed; // ManagedBuffer<T> がここにある想定
using Xunit;

namespace BitzLabs.BitzBuffer.Tests // または BitzLabs.BitzBuffer.Tests.Views
{
    public class SlicedBufferViewTests
    {
        // テスト用のソースバッファを作成するヘルパーメソッド。
        // size: バッファの物理的な容量。
        // fillData: trueの場合、バッファ全体にデータを書き込み、Lengthをsizeに設定する。
        // initialValue: fillDataがtrueの場合に使用する初期値。デフォルト値と比較して設定。
        private IReadOnlyBuffer<T> CreateSourceBuffer<T>(int size, bool fillData = false, T initialValue = default) where T : struct
        {
            var array = new T[size];
            if (fillData)
            {
                for (int i = 0; i < size; i++)
                {
                    // Tがstructなので、initialValueがデフォルト値でないかを確認して設定
                    array[i] = !EqualityComparer<T>.Default.Equals(initialValue, default)
                        ? initialValue
                        : (T)Convert.ChangeType(i, typeof(T)); // Convert.ChangeTypeは一部の型で失敗する可能性あり注意
                }
            }
            // ManagedBufferのコンストラクタはinternalなので、テストアセンブリからアクセス可能にする必要がある
            // (InternalsVisibleTo属性など)
            var managedBuffer = new ManagedBuffer<T>(array, true);
            if (fillData)
            {
                // fillDataがtrueの場合、バッファの論理長を物理サイズと同じにする
                managedBuffer.Advance(size);
            }
            return managedBuffer;
        }

        // --- コンストラクタと初期状態のテスト ---

        [Fact(DisplayName = "コンストラクタ: 正常な引数で正しく初期化される")]
        public void Constructor_WithValidArguments_InitializesCorrectly()
        {
            // Arrange
            // source.Length が 10 であるソースバッファを作成
            var source = CreateSourceBuffer<int>(10, fillData: true);
            long offset = 2;
            long length = 5; // 2 + 5 <= 10 (source.Length) なので有効

            // Act
            var slice = new SlicedBufferView<int>(source, offset, length);

            // Assert
            Assert.False(slice.IsOwner);
            Assert.False(slice.IsDisposed);
            Assert.Equal(length, slice.Length);
            Assert.False(slice.IsEmpty);
        }

        [Fact(DisplayName = "コンストラクタ: sourceBufferがnullでArgumentNullException")]
        public void Constructor_NullSourceBuffer_ThrowsArgumentNullException()
        {
            // Arrange
            IReadOnlyBuffer<int>? nullSource = null;

            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new SlicedBufferView<int>(nullSource!, 0, 0));
            Assert.Equal("sourceBuffer", exception.ParamName);
        }

        [Theory(DisplayName = "コンストラクタ: 不正なoffsetまたはlengthでArgumentOutOfRangeException")]
        [InlineData(-1, 5, "offset")] // offset < 0
        [InlineData(0, -5, "length")] // length < 0
        public void Constructor_WithNegativeOffsetOrLength_ThrowsArgumentOutOfRangeException(long offset, long length, string paramName)
        {
            // Arrange
            var source = CreateSourceBuffer<int>(10, fillData: true); // source.Length は 10

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SlicedBufferView<int>(source, offset, length));
            Assert.Equal(paramName, exception.ParamName);
        }

        [Fact(DisplayName = "コンストラクタ: sourceBuffer破棄済みでObjectDisposedException")]
        public void Constructor_SourceBufferDisposed_ThrowsObjectDisposedException()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(10, fillData: true);
            (source as IDisposable)?.Dispose(); // source を破棄

            // Act & Assert
            var exception = Assert.Throws<ObjectDisposedException>(() => new SlicedBufferView<int>(source, 0, 5));
            Assert.Equal("sourceBuffer", exception.ObjectName); // メッセージが "参照元のバッファが既に破棄されています。" を期待
        }

        [Fact(DisplayName = "コンストラクタ: offset+lengthがsourceBuffer.Length超過でArgumentOutOfRangeException")]
        public void Constructor_OffsetPlusLengthExceedsSourceLength_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(10, fillData: true); // source.Length は 10

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SlicedBufferView<int>(source, 5, 6)); // 5 + 6 = 11 > 10
            Assert.Equal("length", exception.ParamName);
        }


        // --- プロパティのテスト ---

        [Fact(DisplayName = "Lengthプロパティ: 正しいスライス長を返す")]
        public void Length_ReturnsCorrectSliceLength()
        {
            // Arrange
            // source.Length が 20 であるソースバッファを作成
            var source = CreateSourceBuffer<byte>(20, fillData: true);
            var slice = new SlicedBufferView<byte>(source, 5, 10);

            // Assert
            Assert.Equal(10, slice.Length);
        }

        [Fact(DisplayName = "IsEmptyプロパティ: 長さ0のスライスでtrueを返す")]
        public void IsEmpty_WhenLengthIsZero_ReturnsTrue()
        {
            // Arrange
            // source.Length が 10 であるソースバッファを作成
            var source = CreateSourceBuffer<byte>(10, fillData: true);
            var emptySlice = new SlicedBufferView<byte>(source, 3, 0);
            var nonEmptySlice = new SlicedBufferView<byte>(source, 3, 1);

            // Assert
            Assert.True(emptySlice.IsEmpty);
            Assert.False(nonEmptySlice.IsEmpty);
        }

        [Fact(DisplayName = "IsOwnerプロパティ: 常にfalseを返す")]
        public void IsOwner_AlwaysReturnsFalse()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 1);

            // Assert
            Assert.False(slice.IsOwner);
        }

        [Fact(DisplayName = "IsDisposedプロパティ: 初期状態でfalse、Dispose後にtrue")]
        public void IsDisposed_ReturnsCorrectState()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 1);

            // Assert (初期状態)
            Assert.False(slice.IsDisposed);

            // Act
            slice.Dispose();

            // Assert (Dispose後)
            Assert.True(slice.IsDisposed);
        }

        [Fact(DisplayName = "IsSingleSegment: 元バッファが単一セグメント(ManagedBuffer)ならtrue")]
        public void IsSingleSegment_WhenSourceIsManagedBuffer_ReturnsTrue()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(10, fillData: true); // ManagedBufferは常に単一セグメント
            var slice = new SlicedBufferView<int>(source, 2, 5);

            // Act & Assert
            Assert.True(slice.IsSingleSegment);
        }

        // TODO: IsSingleSegment: 元バッファが非連続でスライスが単一セグメントに収まる場合/またがる場合のテスト (SegmentedBuffer実装後)


        // --- Disposeメソッドのテスト ---
        [Fact(DisplayName = "Dispose: 複数回呼び出しても例外をスローしない")]
        public void Dispose_MultipleTimes_DoesNotThrowException()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 1);

            // Act
            Exception? ex = Record.Exception(() =>
            {
                slice.Dispose();
                slice.Dispose();
            });

            // Assert
            Assert.Null(ex);
            Assert.True(slice.IsDisposed);
        }

        [Fact(DisplayName = "Dispose: 元のバッファには影響しない")]
        public void Dispose_DoesNotAffectSourceBuffer()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 3);

            // Act
            slice.Dispose();

            // Assert
            Assert.True(slice.IsDisposed);
            Assert.False(source.IsDisposed); // 元のバッファは破棄されていない
        }

        // --- AsReadOnlySequenceメソッドのテスト ---
        [Fact(DisplayName = "AsReadOnlySequence: 正しい範囲のシーケンスを返す (ManagedBufferソース)")]
        public void AsReadOnlySequence_ReturnsCorrectSlicedSequence_FromManagedBuffer()
        {
            // Arrange
            var sourceData = Enumerable.Range(0, 10).Select(i => i).ToArray(); // 0..9
            var source = new ManagedBuffer<int>(sourceData, true);
            source.Advance(sourceData.Length);

            var slice = new SlicedBufferView<int>(source, 3, 4); // index 3から4要素 (値は3, 4, 5, 6)

            // Act
            var sequence = slice.AsReadOnlySequence();

            // Assert
            var expectedSliceData = new int[] { 3, 4, 5, 6 };
            Assert.Equal(expectedSliceData.Length, sequence.Length);
            Assert.Equal(expectedSliceData, sequence.ToArray());
        }

        // TODO: AsReadOnlySequence: 元バッファが非連続の場合のテスト (SegmentedBuffer実装後)

        [Fact(DisplayName = "AsReadOnlySequence: 自身がDispose済みならObjectDisposedException")]
        public void AsReadOnlySequence_WhenSelfDisposed_ThrowsObjectDisposedException()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 1);
            slice.Dispose();

            // Act & Assert
            var ex = Assert.Throws<ObjectDisposedException>(() => slice.AsReadOnlySequence());
            Assert.Equal(typeof(SlicedBufferView<int>).FullName, ex.ObjectName);
        }

        [Fact(DisplayName = "AsReadOnlySequence: 元バッファがDispose済みならObjectDisposedException")]
        public void AsReadOnlySequence_WhenSourceDisposed_ThrowsObjectDisposedException()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 1);
            (source as IDisposable)?.Dispose();

            // Act & Assert
            var ex = Assert.Throws<ObjectDisposedException>(() => slice.AsReadOnlySequence());
            Assert.Equal("_sourceBuffer", ex.ObjectName); // SlicedBufferView内部で nameof(_sourceBuffer) を使っている場合
        }


        // --- TryGetSingleMemory/Spanメソッドのテスト ---
        [Fact(DisplayName = "TryGetSingleMemory/Span: 正しい範囲を返す (ManagedBufferソース)")]
        public void TryGetSingleMemoryAndSpan_ReturnsCorrectSlice_FromManagedBuffer()
        {
            // Arrange
            var sourceData = Enumerable.Range(0, 10).Select(i => i).ToArray(); // 0..9
            var source = new ManagedBuffer<int>(sourceData, true);
            source.Advance(sourceData.Length);

            var slice = new SlicedBufferView<int>(source, 2, 5); // index 2から5要素 (値は2, 3, 4, 5, 6)

            // Act & Assert for Memory
            Assert.True(slice.TryGetSingleMemory(out var memory));
            var expectedSliceData = new int[] { 2, 3, 4, 5, 6 };
            Assert.Equal(expectedSliceData.Length, memory.Length);
            Assert.Equal(expectedSliceData, memory.ToArray());

            // Act & Assert for Span
            Assert.True(slice.TryGetSingleSpan(out var span));
            Assert.Equal(expectedSliceData.Length, span.Length);
            Assert.Equal(expectedSliceData, span.ToArray());
        }

        // TODO: TryGetSingleMemory/Span: 元バッファが非連続の場合のテスト (SegmentedBuffer実装後)

        [Fact(DisplayName = "TryGetSingleMemory/Span: 自身がDispose済みならfalseを返す")]
        public void TryGetSingleMemoryAndSpan_WhenSelfDisposed_ReturnsFalse()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 1);
            slice.Dispose();

            // Act & Assert
            Assert.False(slice.TryGetSingleMemory(out var memOutput));
            Assert.Equal(default, memOutput);
            Assert.False(slice.TryGetSingleSpan(out var spanOutput));
            Assert.Equal(default, spanOutput);
        }

        [Fact(DisplayName = "TryGetSingleMemory/Span: 元バッファがDispose済みならfalseを返す")]
        public void TryGetSingleMemoryAndSpan_WhenSourceDisposed_ReturnsFalse()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(5, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 1);
            (source as IDisposable)?.Dispose();

            // Act & Assert
            Assert.False(slice.TryGetSingleMemory(out var memOutput));
            Assert.Equal(default, memOutput);
            Assert.False(slice.TryGetSingleSpan(out var spanOutput));
            Assert.Equal(default, spanOutput);
        }


        // --- Sliceメソッド (このビューからさらにスライス) のテスト ---
        [Fact(DisplayName = "Slice(start, length): 正しくさらにスライスできる")]
        public void Slice_StartLength_CreatesCorrectSubSlice()
        {
            // Arrange
            // 元バッファ 長さ20 (0-19), データは 0 から 19
            var source = CreateSourceBuffer<int>(20, fillData: true);
            // firstSlice: 元のindex 5 から 10要素 (元の値 5,6,...,14)
            var firstSlice = new SlicedBufferView<int>(source, 5, 10);

            // Act: firstSlice の内部index 2 から 5要素をスライス
            //      これは、元のバッファの index (5+2)=7 から 5要素。
            //      元のバッファの値で言うと、7, 8, 9, 10, 11
            var secondSlice = firstSlice.Slice(2, 5);

            // Assert
            Assert.Equal(5, secondSlice.Length);
            var expectedData = new int[] { 7, 8, 9, 10, 11 };
            Assert.Equal(expectedData, secondSlice.AsReadOnlySequence().ToArray());
            Assert.False(secondSlice.IsDisposed);
        }

        [Fact(DisplayName = "Slice(start): 正しくさらにスライスできる")]
        public void Slice_Start_CreatesCorrectSubSliceToEnd()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(20, fillData: true);
            var firstSlice = new SlicedBufferView<int>(source, 5, 10);  // 元の5-14を参照 (長さ10) (値5-14)

            // Act: firstSlice の内部index 7 から末尾までスライス
            //      長さは 10 - 7 = 3 要素。
            //      元のバッファの index (5+7)=12 から 3要素。
            //      元のバッファの値で言うと、12, 13, 14
            var secondSlice = firstSlice.Slice(7);

            // Assert
            Assert.Equal(3, secondSlice.Length);
            var expectedData = new int[] { 12, 13, 14 };
            Assert.Equal(expectedData, secondSlice.AsReadOnlySequence().ToArray());
        }

        [Theory(DisplayName = "Slice(start, length): 不正な引数でArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1, 1, "start")]  // start < 0
        [InlineData(11, 1, "start")]  // start > firstSlice.Length (10)
        [InlineData(0, -1, "length")] // length < 0
        [InlineData(5, 6, "length")]  // start + length > firstSlice.Length (10)
        public void Slice_StartLength_WithInvalidArguments_ThrowsArgumentOutOfRangeException(long start, long length, string paramName)
        {
            // Arrange
            var source = CreateSourceBuffer<int>(20, fillData: true);
            var firstSlice = new SlicedBufferView<int>(source, 5, 10); // Length = 10

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => firstSlice.Slice(start, length));
            Assert.Equal(paramName, exception.ParamName);
        }

        [Theory(DisplayName = "Slice(start): 不正な引数でArgumentOutOfRangeExceptionをスローする")]
        [InlineData(-1, "start")] // start < 0
        [InlineData(11, "start")] // start > firstSlice.Length (10)
        public void Slice_Start_WithInvalidArguments_ThrowsArgumentOutOfRangeException(long start, string paramName)
        {
            // Arrange
            var source = CreateSourceBuffer<int>(20, fillData: true);
            var firstSlice = new SlicedBufferView<int>(source, 5, 10); // Length = 10

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => firstSlice.Slice(start));
            Assert.Equal(paramName, exception.ParamName);
        }

        [Fact(DisplayName = "Slice: 自身がDispose済みならObjectDisposedException")]
        public void Slice_WhenSelfDisposed_ThrowsObjectDisposedException()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(10, fillData: true);
            var slice = new SlicedBufferView<int>(source, 0, 5);
            slice.Dispose();

            // Act & Assert
            var ex = Assert.Throws<ObjectDisposedException>(() => slice.Slice(0, 1));
            Assert.Equal(typeof(SlicedBufferView<int>).FullName, ex.ObjectName);
        }


        // --- ToStringメソッドのテスト ---
        [Fact(DisplayName = "ToString: バッファの状態を文字列で正しく出力する")]
        public void ToString_OutputsStateCorrectly()
        {
            // Arrange
            var source = CreateSourceBuffer<int>(20, fillData: true);
            var slice = new SlicedBufferView<int>(source, 5, 10);

            // Act
            var str = slice.ToString();

            // Assert
            Assert.Contains("SlicedBufferView<Int32>", str);
            Assert.Contains("Offset=5", str);
            Assert.Contains("Length=10", str);
            Assert.Contains($"SourceDisposed={source.IsDisposed}", str); // falseのはず
            Assert.Contains("Disposed=False", str);

            // sliceをDispose
            slice.Dispose();
            str = slice.ToString();
            Assert.Contains("Length=10", str); // 長さ情報は維持される
            Assert.Contains($"SourceDisposed={source.IsDisposed}", str); // sourceはまだfalse
            Assert.Contains("Disposed=True", str);

            // sourceもDispose
            (source as IDisposable)?.Dispose();
            str = slice.ToString(); // sliceは既にDispose済みだが、sourceのDispose状態も変わる
            Assert.Contains("SourceDisposed=True", str);
            Assert.Contains("Disposed=True", str);
        }
    }

    // Helper class for creating ReadOnlySequence<T> with multiple segments for testing
    // This should ideally be in a shared test utilities project or file
    internal static class TestUtils
    {
        public static ReadOnlySequence<T> CreateReadOnlySequence<T>(params T[][] segmentsData) where T : struct
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
            // null許容参照型を考慮し、firstとlastがnullでないことを表明
            return new ReadOnlySequence<T>(first!, 0, last!, last!.Memory.Length);
        }

        private abstract class SegmentBase<T> : ReadOnlySequenceSegment<T> where T : struct
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

        private sealed class Segment<T> : SegmentBase<T> where T : struct
        {
            public Segment(ReadOnlyMemory<T> memory) : base(memory) { }
        }
    }
}