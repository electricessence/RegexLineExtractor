﻿using Open.ChannelExtensions;
using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RegexLineExtractor
{
    public class AsyncLineWriter : IDisposable
    {
        readonly Channel<string> _channel;

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

                _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = true
                });

                Completion = _channel
                    .ReadAllAsync(line => new ValueTask(file.Value.WriteLineAsync(line)))
                    .AsTask()
                    .ContinueWith(t => {
                        if (file.IsValueCreated)
                        {
                            file.Value
                                .FlushAsync() // Async flush, then sync dispose.
                                .ContinueWith(f => file.Value.Dispose());
                        }
                        return t;
                    })
                    .Unwrap();
            }
        }

        public ValueTask WriteAsync(string line)
            => _channel == null ? new ValueTask()
                : _channel.Writer.WriteIfOpenAsync(line);

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
