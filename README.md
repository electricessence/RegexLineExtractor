# RegexLineExtractor

Reads all lines from a source file and for every line that matches the provided pattern(s), writes that match to a destination file to a `results` directory.

Optimizes for concurrency by using `System.Threading.Channels`.

## Setup

1) Clone the repository locally.
2) Create a pattern file that contains the .NET supported regular expression patterns you'd like to use for each line.
3) Provide a source text file to read from.

## Usage

```powershell
dotnet run source-file.txt patterns.regex
```

> `patterns.regex` is optional and will default to the above if omitted.

### Pattern Files

A "pattern" file should contain a regular expression for each line.

#### Example

```regexp
^\.+ end of line.
^Result: (?<output>\.+)
```

The above pattern file will:

1) For each line that ends in " end of line." and write them to `results/pattern-1.lines.txt`.

2) If the first pattern is not a match, does it start with "Result: " and if so write the `output`\* group to `results/pattern-2.lines.txt`.

3) If no more patterns to match against then write the line to `results/not-matched.lines.txt`.

> \* If a pattern contains a named match called `output`, only that group will end up in the destination file.