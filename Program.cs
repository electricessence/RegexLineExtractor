using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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
          
            var sw = Stopwatch.StartNew();
            using (var skipped = new AsyncLineWriter(args.Length == 4 ? args[3] : null))
            using (var destination = new AsyncLineWriter(args[2])) // argument 2: destination
            {
                var matchCount = 0;
                using (var source = new StreamReader(args[1])) // argument 1: source
                {
                    var lineCount = 0;
                    await source
                        .ToChannel(100)
                        .ReadAllConcurrentlyAsync(4, async line =>
                        {
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

                            var ok = destination.WriteAsync(line);
                            if (!ok.IsCompletedSuccessfully)
                                await ok;

                            Interlocked.Increment(ref matchCount);
                            goto end;

                        skip:
                            var s = skipped.WriteAsync(line);
                            if (!s.IsCompletedSuccessfully)
                                await s;

                            end:
                            var count = Interlocked.Increment(ref lineCount);
                            if (count % 1000 == 0)
                                lock (sw) Console.WriteLine("Lines Processed: {0:N0}", count);
                        });

                    sw.Stop();
                }

                Console.WriteLine("Done: {0:N3} seconds", sw.Elapsed.TotalSeconds);
                Console.WriteLine("{0:N0} matches Found.", matchCount);

                await destination.CompleteAsync();
                await skipped.CompleteAsync();
            }
        }
    }
}
