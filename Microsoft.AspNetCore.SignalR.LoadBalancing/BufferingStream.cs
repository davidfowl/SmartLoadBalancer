using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Sample
{
    public class BufferingStream : Stream
    {
        private readonly Stream _stream;
        private FileBufferingWriteStream _fileBufferingWriteStream = new();
        private bool _isBuffering = true;
        private readonly HttpContext _context;

        public BufferingStream(HttpContext context)
        {
            _context = context;
            _stream = context.Response.Body;
        }

        public override bool CanRead => false;

        public override bool CanSeek => _isBuffering;

        public override bool CanWrite => true;

        public override long Length => _isBuffering ? _fileBufferingWriteStream.Length : throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public async Task StopBufferingAsync(CancellationToken cancellationToken = default)
        {
            if (!_isBuffering)
            {
                return;
            }

            _isBuffering = false;

            await _fileBufferingWriteStream.DrainBufferAsync(_stream, cancellationToken);
        }

        private Stream ActiveStream => _isBuffering ? _fileBufferingWriteStream : _stream;

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            if (!_isBuffering || value > 0)
            {
                throw new NotSupportedException();
            }

            if (value > 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            // Discard the previous stream
            _fileBufferingWriteStream.Dispose();
            _fileBufferingWriteStream = new();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ActiveStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ActiveStream.WriteAsync(buffer, cancellationToken);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            // TODO: Use the disable buffering feature
            if (_isBuffering && _context.Response.ContentType == "text/event-stream")
            {
                await StopBufferingAsync(cancellationToken);
            }

            await ActiveStream.FlushAsync(cancellationToken);
        }

        public override async ValueTask DisposeAsync()
        {
            await StopBufferingAsync();
        }
    }
}
