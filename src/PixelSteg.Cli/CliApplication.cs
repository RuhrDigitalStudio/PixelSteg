using PixelSteg.Core;

namespace PixelSteg.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        TextReader? input = null,
        Func<string, string?>? readEnvironment = null)
    {
        input ??= TextReader.Null;
        readEnvironment ??= Environment.GetEnvironmentVariable;
        try
        {
            if (args.Length == 3 && string.Equals(args[0], "encode", StringComparison.OrdinalIgnoreCase))
                return await EncodeCommand.RunAsync(args[1], args[2], error, cancellationToken);
            if (args.Length is 3 or 4 && string.Equals(args[0], "decode", StringComparison.OrdinalIgnoreCase) && (args.Length == 3 || args[3] == "--overwrite"))
                return await DecodeCommand.RunAsync(args[1], args[2], args.Length == 4, error, cancellationToken);
            if (args.Length >= 3 && string.Equals(args[0], "embed-message", StringComparison.OrdinalIgnoreCase))
            {
                var options = StegoCliOptions.Parse(args[3..]);
                if (options.Positionals.Count != 0 || options.PasswordFromStandardInput)
                    throw new ArgumentException("embed-message reads the message from stdin; use --password-env for password protection.");
                var message = await input.ReadToEndAsync(cancellationToken);
                var password = await options.ReadPasswordAsync(input, readEnvironment, cancellationToken);
                return await EmbedCommand.RunMessageAsync(args[1], args[2], message, options.Profile, options.Compress, password, error, cancellationToken);
            }
            if (args.Length >= 4 && string.Equals(args[0], "embed-file", StringComparison.OrdinalIgnoreCase))
            {
                var options = StegoCliOptions.Parse(args[3..]);
                if (options.Positionals.Count == 0) throw new ArgumentException("Choose at least one payload file.");
                var password = await options.ReadPasswordAsync(input, readEnvironment, cancellationToken);
                return await EmbedCommand.RunFilesAsync(args[1], args[2], options.Positionals, options.Profile, options.Compress, password, error, cancellationToken);
            }
            if (args.Length >= 3 && string.Equals(args[0], "extract", StringComparison.OrdinalIgnoreCase))
            {
                var options = StegoCliOptions.Parse(args[3..]);
                if (options.Positionals.Count != 0) throw new ArgumentException("Unexpected positional argument for extract.");
                var password = await options.ReadPasswordAsync(input, readEnvironment, cancellationToken);
                return await ExtractCommand.RunAsync(args[1], args[2], options.Overwrite, password, error, cancellationToken);
            }
            if (args.Length >= 2 && string.Equals(args[0], "read-message", StringComparison.OrdinalIgnoreCase))
            {
                var options = StegoCliOptions.Parse(args[2..]);
                if (options.Positionals.Count != 0) throw new ArgumentException("Unexpected positional argument for read-message.");
                var password = await options.ReadPasswordAsync(input, readEnvironment, cancellationToken);
                return await ExtractCommand.ReadMessagesAsync(args[1], password, output, cancellationToken);
            }
            if (args.Length == 2 && string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase))
                return await InspectCommand.RunAsync(args[1], output, cancellationToken);
            await error.WriteLineAsync(Usage);
            return 2;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("Operation cancelled."); return 4; }
        catch (FileExistsException ex) { await error.WriteLineAsync(ex.Message); return 3; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PixelStegException or ArgumentException) { await error.WriteLineAsync($"Error: {ex.Message}"); return 1; }
    }

    private const string Usage = """
        Usage:
          pixelsteg embed-file <cover.png> <output.png> <file> [file...] [--profile balanced|dense|adaptive|resilient] [--no-compress] [--password-stdin|--password-env NAME]
          pixelsteg embed-message <cover.png> <output.png> [--profile balanced|dense|adaptive|resilient] [--no-compress] [--password-env NAME] < message.txt
          pixelsteg extract <carrier.png> <output-directory> [--overwrite] [--password-stdin|--password-env NAME]
          pixelsteg read-message <carrier.png> [--password-stdin|--password-env NAME]
          pixelsteg inspect <carrier.png>
          pixelsteg encode <input> <output.png>
          pixelsteg decode <input.png> <output-directory> [--overwrite]
        """;
}

public sealed class FileExistsException(string message) : IOException(message);
