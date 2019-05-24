using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;
using Open.ChannelExtensions;

namespace RegexLineExtractor
{
    class Program
    {
        const string OutputGroupName = "output";

        static async Task Main(string[] args)
        {
            // argument 0: pattern
            var pattern = await File.ReadAllTextAsync(args[0]);
            var regex = new Regex(pattern, RegexOptions.Compiled);
            var hasOutput = regex.GetGroupNames().Contains(OutputGroupName);
            Console.WriteLine(pattern);

            var skipped = args.Length == 4 ? new StreamWriter(args[3]) : null;
            var skippedChannel = Channel.CreateBounded<string>(100);
            var skippedReceiver = skippedChannel.Writer;
            ValueTask Skip(string line)
                => skipped == null || skippedReceiver.TryWrite(line)
                ? new ValueTask() : skippedReceiver.WriteAsync(line);
            var writeSkipped = skipped==null ? new ValueTask() : skippedChannel
                .ReadAllAsync(async e => await skipped.WriteLineAsync(e));

            try
            {
                var sw = Stopwatch.StartNew();
                using (var destination = new StreamWriter(args[2])) // argument 2: destination
                using (var sourceFile = File.OpenRead(args[1])) // argument 1: source
                using (var source = new StreamReader(sourceFile))
                {
                    var inputChannel = Channel.CreateBounded<(string line, long remaining)>(100);
                    var inputReceiver = inputChannel.Writer;
                    var matchedChannel = Channel.CreateBounded<string>(100);
                    var matchReceiver = matchedChannel.Writer;

                    // Match concurrently.
                    var writeMatches = inputChannel
                        .ReadAllConcurrentlyAsync(4, async e =>
                        {
                            var (line, remaining) = e;
                            var m = regex.Match(line);
                            if (!m.Success)
                                goto skip;

                            if (hasOutput)
                            {
                                var o = m.Groups[OutputGroupName];
                                if (!o.Success)
                                    goto skip;

                                line = o.Value;
                            }

                            if (remaining != -1)
                            {
                                var write = matchReceiver.WriteAsync(line);
                                Console.WriteLine("Remaining: {0}", remaining);
                                await write;
                            }
                            else
                            {
                                if (!matchReceiver.TryWrite(line))
                                    await matchReceiver.WriteAsync(line);
                            }
                            return;

                        skip:
                            var s = Skip(line);
                            if (!s.IsCompletedSuccessfully)
                                await s;
                        });

                    // Write at a time (thread safe).
                    var writeOutput = matchedChannel
                        .ReadAllAsync(async e => await destination.WriteLineAsync(e));

                    {
                        string line;
                        var lineCount = 0;
                        var length = sourceFile.Length;
                        var next = source.ReadLineAsync();
                        while ((line = await next) != null)
                        {
                            // Prefetch the next line.
                            next = source.ReadLineAsync();
                            length -= line.Length + 2;
                            lineCount++;
                            var rem = lineCount % 1000 == 0 ? length : -1; // Output a console message every 1000 bytes.
                            if (!inputReceiver.TryWrite((line, rem)))
                                await inputReceiver.WriteAsync((line, rem));
                        }
                    }

                    inputChannel.Writer.Complete();
                    Console.WriteLine("Read Complete.");
                    await writeMatches;

                    matchedChannel.Writer.Complete();
                    Console.WriteLine("Matches Found.");
                    await writeOutput;

                    Console.WriteLine("Done: {0} seconds", sw.Elapsed.TotalSeconds);

                    await destination.FlushAsync();
                }
            }
            finally
            {
                skippedChannel.Writer.Complete();
                await writeSkipped;

                if (skipped != null)
                {
                    await skipped.FlushAsync();
                    skipped.Dispose();

                    Console.WriteLine("{0} created.", args[3]);
                }
            }
        }
    }
}
