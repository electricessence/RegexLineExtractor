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
        static async Task Main(string[] args)
        {
            // argument 0: pattern
            var pattern = await File.ReadAllTextAsync(args[0]);
            var regex = new Regex(pattern, RegexOptions.Compiled);
            const string outputGroup = "output";
            var hasOutput = regex.GetGroupNames().Contains(outputGroup);
            Console.WriteLine(pattern);

            var sw = Stopwatch.StartNew();
            using (var outputStream = File.OpenWrite(args[2])) // argument 2: destination
            using (var output = new StreamWriter(outputStream))
            using (var inputStream = File.OpenRead(args[1])) // argument 1: source
            using (var input = new StreamReader(inputStream))
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
                        if (m.Success)
                        {
                            if (hasOutput)
                            {
                                var o = m.Groups["output"];
                                if (!o.Success)
                                    return;

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
                        }
                    });

                // Write at a time (thread safe).
                var writeOutput = matchedChannel
                    .ReadAllAsync(async e => await output.WriteLineAsync(e));
                {
                    string line;
                    var lineCount = 0;
                    var length = inputStream.Length;
                    var next = input.ReadLineAsync();
                    while ((line = await next) != null)
                    {
                        // Prefetch the next line.
                        next = input.ReadLineAsync();
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

                await outputStream.FlushAsync();
            }
        }
    }
}
