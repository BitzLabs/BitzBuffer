// In SimpleManagedProviderTests.cs
using System;
using System.Collections.Generic;
using BitzLabs.BitzBuffer;
using BitzLabs.BitzBuffer.Managed;
using BitzLabs.BitzBuffer.Providers.Managed;
using Xunit;

namespace BitzLabs.BitzBuffer.Tests.Providers.Managed
{
    public class SimpleManagedProviderTests : IDisposable
    {
        private SimpleManagedProvider _provider;

        public SimpleManagedProviderTests()
        {
            _provider = new SimpleManagedProvider();
        }

        public void Dispose()
        {
            _provider.Dispose();
            GC.SuppressFinalize(this); // ファイナライザを持つクラスのDisposeパターンとして
        }

        // --- CreateBuffer / TryCreateBuffer のテスト ---
        [Fact(DisplayName = "CreateBuffer: 指定した長さでManagedBufferを生成しCapacityが正しい")]
        public void CreateBuffer_WithValidLength_ReturnsManagedBufferOfCorrectCapacityAndOwner()
        {
            // Arrange
            int length = 128;

            // Act
            using var buffer = _provider.CreateBuffer<byte>(length);

            // Assert
            Assert.NotNull(buffer);
            var managedBuffer = Assert.IsAssignableFrom<ManagedBuffer<byte>>(buffer); // 型キャストと確認
            Assert.Equal(length, managedBuffer.Capacity); // ★★★ Capacity プロパティで物理長を確認 ★★★
            Assert.Equal(0, buffer.Length);          // 初期論理長は0
            Assert.True(buffer.IsOwner);
            Assert.False(buffer.IsDisposed);
        }

        [Fact(DisplayName = "CreateBuffer: 負の長さでArgumentOutOfRangeExceptionをスローする")]
        public void CreateBuffer_WithNegativeLength_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            int length = -1;

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _provider.CreateBuffer<byte>(length));
            Assert.Equal("exactLength", exception.ParamName);
        }

        [Fact(DisplayName = "TryCreateBuffer: 正常にバッファを生成しtrueと正しいCapacityのバッファを返す")]
        public void TryCreateBuffer_WhenSuccessful_ReturnsTrueAndBufferWithCorrectCapacity()
        {
            // Arrange
            int length = 64;

            // Act
            bool success = _provider.TryCreateBuffer<int>(length, out var buffer);

            // Assert
            Assert.True(success);
            Assert.NotNull(buffer);
            using (buffer)
            {
                var managedBuffer = Assert.IsAssignableFrom<ManagedBuffer<int>>(buffer);
                Assert.Equal(length, managedBuffer.Capacity); // ★★★ Capacity プロパティで物理長を確認 ★★★
                Assert.Equal(0, buffer.Length);
                Assert.True(buffer.IsOwner);
            }
        }

        [Fact(DisplayName = "TryCreateBuffer: 負の長さでArgumentOutOfRangeExceptionをスローする")]
        public void TryCreateBuffer_WithNegativeLength_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            int length = -1;

            // Act & Assert
            // Try系メソッドでも、引数の事前条件違反は例外をスローする方針
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _provider.TryCreateBuffer<byte>(length, out _));
            Assert.Equal("exactLength", exception.ParamName);
        }

        // メモリ不足を安定してテストするのは難しいため、ここでは省略。
        // モックや特殊な環境設定が必要になる場合がある。


        // --- Rent / TryRent のテスト ---
        [Fact(DisplayName = "Rent: 指定した最小長さでManagedBufferを生成しCapacityが正しい")]
        public void Rent_WithValidMinimumLength_ReturnsManagedBufferOfCorrectCapacity()
        {
            // Arrange
            int minLength = 256;

            // Act
            using var buffer = _provider.Rent<short>(minLength);

            // Assert
            Assert.NotNull(buffer);
            var managedBuffer = Assert.IsAssignableFrom<ManagedBuffer<short>>(buffer);
            Assert.Equal(minLength, managedBuffer.Capacity); // SimpleManagedProviderではCreateBufferと同じなのでCapacityも一致
            Assert.Equal(0, buffer.Length);
            Assert.True(buffer.IsOwner);
        }

        [Fact(DisplayName = "Rent: 負の最小長さでArgumentOutOfRangeExceptionをスローする")]
        public void Rent_WithNegativeMinimumLength_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            int minLength = -5;

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _provider.Rent<byte>(minLength));
            Assert.Equal("minimumLength", exception.ParamName);
        }

        [Fact(DisplayName = "TryRent: 正常にバッファを生成しtrueと正しいCapacityのバッファを返す")]
        public void TryRent_WhenSuccessful_ReturnsTrueAndBufferWithCorrectCapacity()
        {
            // Arrange
            int minLength = 32;

            // Act
            bool success = _provider.TryRent<float>(minLength, out var buffer);

            // Assert
            Assert.True(success);
            Assert.NotNull(buffer);
            using (buffer)
            {
                var managedBuffer = Assert.IsAssignableFrom<ManagedBuffer<float>>(buffer);
                Assert.Equal(minLength, managedBuffer.Capacity);
                Assert.Equal(0, buffer.Length);
                Assert.True(buffer.IsOwner);
            }
        }

        [Fact(DisplayName = "TryRent: 負の最小長さでArgumentOutOfRangeExceptionをスローする")]
        public void TryRent_WithNegativeMinimumLength_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            int minLength = -10;

            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _provider.TryRent<double>(minLength, out _));
            Assert.Equal("minimumLength", exception.ParamName);
        }


        // --- Dispose後の挙動テスト ---
        [Fact(DisplayName = "Dispose後: CreateBuffer呼び出しでObjectDisposedExceptionをスローする")]
        public void CreateBuffer_AfterProviderDisposed_ThrowsObjectDisposedException()
        {
            // Arrange
            _provider.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _provider.CreateBuffer<int>(10));
        }

        [Fact(DisplayName = "Dispose後: TryCreateBuffer呼び出しでfalseを返す")]
        public void TryCreateBuffer_AfterProviderDisposed_ReturnsFalse()
        {
            // Arrange
            _provider.Dispose();

            // Act
            bool success = _provider.TryCreateBuffer<int>(10, out var buffer);

            // Assert
            Assert.False(success);
            Assert.Null(buffer); // または default
        }

        [Fact(DisplayName = "Dispose後: Rent呼び出しでObjectDisposedExceptionをスローする")]
        public void Rent_AfterProviderDisposed_ThrowsObjectDisposedException()
        {
            // Arrange
            _provider.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _provider.Rent<int>(10));
        }

        [Fact(DisplayName = "Dispose後: TryRent呼び出しでfalseを返す")]
        public void TryRent_AfterProviderDisposed_ReturnsFalse()
        {
            // Arrange
            _provider.Dispose();

            // Act
            bool success = _provider.TryRent<int>(10, out var buffer);

            // Assert
            Assert.False(success);
            Assert.Null(buffer);
        }

        // --- 統計APIのスタブ動作テスト ---
        [Fact(DisplayName = "GetPoolingOverallStatistics: Dispose前はダミーオブジェクトを返す")]
        public void GetPoolingOverallStatistics_BeforeDispose_ReturnsDummy()
        {
            // Arrange
            // Act
            var stats = _provider.GetPoolingOverallStatistics();
            // Assert
            Assert.NotNull(stats); // 現状は new object() を想定
        }

        [Fact(DisplayName = "GetPoolingOverallStatistics: Dispose後はObjectDisposedException")]
        public void GetPoolingOverallStatistics_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            _provider.Dispose();
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _provider.GetPoolingOverallStatistics());
        }


        [Fact(DisplayName = "GetPoolingBucketStatistics: Dispose前は空の辞書を返す")]
        public void GetPoolingBucketStatistics_BeforeDispose_ReturnsEmptyDictionary()
        {
            // Arrange
            // Act
            var stats = _provider.GetPoolingBucketStatistics();
            // Assert
            Assert.NotNull(stats);
            Assert.Empty(stats);
        }


        [Fact(DisplayName = "GetPoolingBucketStatistics: Dispose後はObjectDisposedException")]
        public void GetPoolingBucketStatistics_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            _provider.Dispose();
            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => _provider.GetPoolingBucketStatistics());
        }
        // IDisposableパターンに従い、ファイナライザ呼び出しも考慮
        ~SimpleManagedProviderTests()
        {
            Dispose(false);
        }

        private bool _disposedValue; // Disposeが複数回呼ばれるのを防ぐためのフラグはテストクラス側にもあると良いが、
                                     // xUnitが各テストで新しいインスタンスを作るので必須ではない。
                                     // _provider.Dispose() を呼ぶ形なので、_provider側で制御されていればOK。

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _provider?.Dispose(); // 管理しているプロバイダをDispose
                }
                _disposedValue = true;
            }
        }
    }
}