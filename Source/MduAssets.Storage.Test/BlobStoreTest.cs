using System.Security.Cryptography;
using System.Text;
using MduAssets.Storage;

namespace MduAssets.Storage.Test;

public sealed class BlobStoreTest : IDisposable
{
    private readonly TempDataDirectory _temp = new();
    private readonly BlobStore _store;

    public BlobStoreTest()
    {
        _store = new BlobStore(_temp.Directory);
    }

    public void Dispose() => _temp.Dispose();

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task StoresContentUnderItsHash()
    {
        var blob = await _store.PutAsync(Content("hello"), TestContext.Current.CancellationToken);

        var expected = Convert.ToHexStringLower(SHA256.HashData("hello"u8));
        Assert.Equal(expected, blob.Hash);
        Assert.Equal(5, blob.Size);
        Assert.Equal(Path.Combine(_temp.Directory.BlobsPath, expected[..2], expected[2..4], expected), _store.PathOf(blob.Hash));
        await using var stream = _store.OpenRead(blob.Hash);
        Assert.Equal("hello", await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoresIdenticalContentOnce()
    {
        var first = await _store.PutAsync(Content("same"), TestContext.Current.CancellationToken);
        var second = await _store.PutAsync(Content("same"), TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Single(Directory.EnumerateFiles(_temp.Directory.BlobsPath, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_temp.Directory.TempPath));
    }

    [Fact]
    public async Task HandlesConcurrentWritesOfTheSameContent()
    {
        var bytes = RandomNumberGenerator.GetBytes(256 * 1024);

        var blobs = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => _store.PutAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken))));

        Assert.Single(blobs.Distinct());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(_store.PathOf(blobs[0].Hash), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(_temp.Directory.TempPath));
    }

    [Fact]
    public async Task LeavesNoTempFileWhenWritingFails()
    {
        await Assert.ThrowsAsync<IOException>(() => _store.PutAsync(new FailingStream(), TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFiles(_temp.Directory.TempPath));
        Assert.Empty(Directory.EnumerateFiles(_temp.Directory.BlobsPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DeletesBlobs()
    {
        var blob = await _store.PutAsync(Content("gone"), TestContext.Current.CancellationToken);

        _store.Delete(blob.Hash);

        Assert.False(_store.Exists(blob.Hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void RejectsInvalidHashes(string hash)
    {
        Assert.Throws<ArgumentException>(() => _store.PathOf(hash));
    }

    [Fact]
    public void PrepareRemovesLeftoverUploads()
    {
        File.WriteAllText(Path.Combine(_temp.Directory.TempPath, "interrupted"), "partial");

        _temp.Directory.Prepare();

        Assert.Empty(Directory.EnumerateFiles(_temp.Directory.TempPath));
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            _reads++ == 0 ? Fill(buffer, offset, count) : throw new IOException("Connection lost.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private static int Fill(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)1, offset, count); return count; }
    }
}
