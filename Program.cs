using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Open.ChannelExtensions;
using Open.Text.CSV;
using System.Net;

namespace CsvLineValidator
{
    class Program
    {
        const string ResultsDirectoryName = "results";

        // dotnet run filename.csv column urlPrefix
        static async Task Main(string[] args)
        {
            var filePath = args.ElementAtOrDefault(0);
            if (string.IsNullOrWhiteSpace(filePath)) filePath = "urls.csv";
            if (!filePath.EndsWith(".csv", StringComparison.InvariantCultureIgnoreCase))
                throw new Exception("Must be a valid .csv file name");
            var columnName = args.ElementAtOrDefault(1);
            if (string.IsNullOrWhiteSpace(columnName)) columnName = "0";
            string? prefix = args.ElementAtOrDefault(2);
            if (string.IsNullOrWhiteSpace(columnName)) prefix = null;

            using var source = new StreamReader(filePath); // argument 0: source
            var firstLine = await source.ReadLineAsync();
            if (firstLine == null) return;

            if (!int.TryParse(columnName, out var columnIndex))
            {
                var firstLineValues = CsvUtility.GetLine(firstLine).ToArray();
                columnIndex = Array.IndexOf(firstLineValues, columnName);
                if (columnIndex == -1) throw new Exception("Column name not found.");
            }

			#region Pre-Cleanup
            // Make sure we can dump our results.
            if (!Directory.Exists(ResultsDirectoryName))
                Directory.CreateDirectory(ResultsDirectoryName);

            // Clean any previous runs.
            foreach (var file in Directory.GetFiles(ResultsDirectoryName))
                File.Delete(file);
			#endregion

            var writers = new ConcurrentDictionary<string, AsyncLineWriter>();
            #region Helpers
            AsyncLineWriter GetLineWriterFor(string name)
                => writers.GetOrAdd(name, key => new AsyncLineWriter($"{ResultsDirectoryName}/{key}.csv", firstLine));
            AsyncLineWriter GetLineWriterForCode(HttpStatusCode code)
                => GetLineWriterFor((int)code + "-" + code.ToString());
            #endregion

            using var httpClient = new HttpClient();
            var lineCount = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                await source
                    .ToChannel(100)
                    .ReadAllConcurrentlyAsync(200, async line =>
                    {
                        AsyncLineWriter resultWriter;

                        var url = CsvUtility.GetLine(line).ElementAtOrDefault(columnIndex);
                        if (url == null || url.StartsWith('/') && prefix == null)
                        {
                            resultWriter = GetLineWriterFor("skipped");
                        }
                        else
                        {
                            if (url.StartsWith('/')) url = prefix + url;
                            var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                            var code = response.StatusCode;

                            resultWriter = GetLineWriterForCode(code);
                        }

                        await resultWriter.WriteAsync(line);

                        var count = Interlocked.Increment(ref lineCount);
                        if (count % 100 == 0)
                            lock (sw) Console.WriteLine("Lines Processed: {0:N0}", count);
                    });

                Console.WriteLine("File read complete: {0:N3} seconds", sw.Elapsed.TotalSeconds);
				source.Close();
            }
            finally
            {
                await Task.WhenAll(writers.Values.Select(e => e.CompleteAsync()));
            }

            Console.WriteLine("Total time: {0:N3} seconds", sw.Elapsed.TotalSeconds);

        }
    }
}
