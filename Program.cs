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
		const string ResultsDirectoryName = "results";

		static async Task Main(string[] args)
		{
			// argument 1: patterns
			var patternsFileName = args.ElementAtOrDefault(1) ?? "patterns.regex";
			var patterns = File
				.ReadLines(patternsFileName)
				.Select((p, i) =>
				{
					var regex = new Regex(p, RegexOptions.Compiled);
					return (
						pattern: regex,
						hasOutput: regex.GetGroupNames().Contains(OutputGroupName),
						writer: new AsyncLineWriter($"results/pattern-{i + 1}.lines.txt")
					);
				})
				.ToArray();

			// Make sure we can dump our results.
			if (!Directory.Exists(ResultsDirectoryName))
				Directory.CreateDirectory(ResultsDirectoryName);

			// Clean any previous runs.
			foreach(var file in Directory.GetFiles(ResultsDirectoryName))
				File.Delete(file);

			try
			{
				var sw = Stopwatch.StartNew();
				using (var notMatched = new AsyncLineWriter($"results/not-matched.lines.txt"))
				{
					var matchCount = 0;
					using (var source = new StreamReader(args[0])) // argument 0: source
					{
						var lineCount = 0;
						await source
							.ToChannel(100)
							.ReadAllConcurrentlyAsync(4, async line =>
							{
								var found = false;
								var result = ReadOnlyMemory<char>.Empty;
								foreach (var (pattern, hasOutput, writer) in patterns)
								{
									if (hasOutput)
									{
										var m = pattern.Match(line);
										if (!m.Success)
											continue;

										var o = m.Groups[OutputGroupName];
										if (!o.Success)
											continue;

										result = line.AsMemory().Slice(o.Index, o.Length);

										line = o.Value;
									}
									else if(pattern.IsMatch(line))
									{
										result = line.AsMemory();
									}
									else
									{
										continue;
									}

									await writer.WriteAsync(result);

									Interlocked.Increment(ref matchCount);
									found = true;
									break;
								}

								if (!found)
									await notMatched.WriteAsync(result);
  
								var count = Interlocked.Increment(ref lineCount);
								if (count % 10000 == 0)
									lock (sw) Console.WriteLine("Lines Processed: {0:N0}", count);
							});

						sw.Stop();
					}

					Console.WriteLine("Done: {0:N3} seconds", sw.Elapsed.TotalSeconds);
					Console.WriteLine("{0:N0} matches Found.", matchCount);

					await Task.WhenAll(patterns.Select(p => p.writer.CompleteAsync()));
					await notMatched.CompleteAsync();
				}
			}
			finally
			{
				foreach (var (pattern, hasOutput, writer) in patterns)
					await writer.DisposeAsync();
			}
		}
	}
}
