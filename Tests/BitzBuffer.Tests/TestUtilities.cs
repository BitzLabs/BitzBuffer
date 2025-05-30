using System;
using System.Buffers;
using System.Linq; // Concat のために必要になる場合がある

namespace BitzLabs.BitzBuffer.Tests // または BitzLabs.BitzBuffer.Tests.Common など
{
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
            return new ReadOnlySequence<T>(first!, 0, last!, last!.Memory.Length);
        }

        // SegmentBase と Segment の定義は TestUtils の内部クラスである必要はないかもしれない。
        // TestUtils と同じ名前空間に直接定義しても良い。
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