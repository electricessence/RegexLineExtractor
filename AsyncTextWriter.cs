using Open.ChannelExtensions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RegexLineExtractor
{
	public class AsyncLineWriter : IDisposable
	{
		readonly Channel<ReadOnlyMemory<char>> _channel;

		/// <summary>
		/// Constructs an AsyncTextWriter.
		/// </summary>
		/// <param name="fileName">An optional file name.  If the name is null, then this will act as a dummy writer.</param>
		public AsyncLineWriter(string fileName = null)
		{
			if (fileName == null)
			{
				Completion = Task.CompletedTask;
			}
			else
			{
				var file = new Lazy<StreamWriter>(() => new StreamWriter(fileName));

				_channel = Channel.CreateBounded<ReadOnlyMemory<char>>(new BoundedChannelOptions(100)
				{
					SingleReader = true,
					SingleWriter = false,
					AllowSynchronousContinuations = true
				});

				Completion = _channel
					.TaskReadAllAsync(line => file.Value.WriteLineAsync(line))
					.AsTask()
					.ContinueWith(t =>
					{
						if (t.IsFaulted)
						{
							_channel.Writer
								.TryComplete(t.Exception);
						}

						if (!file.IsValueCreated)
							return t;

						return file
							.Value
							.FlushAsync() // Async flush, then sync dispose.
							.ContinueWith(
								f =>
								{
									file.Value.Dispose();
									return t;
								},
								TaskContinuationOptions.ExecuteSynchronously)
							.Unwrap();

					}, TaskContinuationOptions.ExecuteSynchronously)
					.Unwrap();
			}
		}

		public ValueTask WriteAsync(ReadOnlyMemory<char> line, CancellationToken cancellationToken = default)
			=> _channel == null || _channel.Writer.TryWrite(line)
				? new ValueTask()
				: _channel.Writer.WriteAsync(line, cancellationToken);

		public Task Completion { get; }

		public Task CompleteAsync()
		{
			_channel?.Writer.TryComplete();
			return Completion;
		}

		public ValueTask DisposeAsync()
			=> new ValueTask(CompleteAsync());

		public void Dispose()
			=> CompleteAsync().Wait();
	}
}
