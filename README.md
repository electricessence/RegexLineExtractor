# RegexLineExtractor

Reads all lines from a source file and for every line that matches the provided pattern, writes that match to a destination file.

Optimizes for concurrency by using `System.Threading.Channels`.

## Setup

1) Clone the repository locally.
2) Create any file that contains the .NET supported regular expression pattern you'd like to use for each line.
3) Provide a source text file to read from.

## Usage

If the pattern contains a named match called `output`, only that group will end up in the destination file.

### Example

```powershell
dotnet run pattern-file.regex source-file.txt destination-file.txt
```