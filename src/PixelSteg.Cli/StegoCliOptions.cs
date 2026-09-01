using PixelSteg.Core;

namespace PixelSteg.Cli;

internal sealed class StegoCliOptions
{
    public EmbeddingProfile Profile { get; private set; } = EmbeddingProfile.Balanced;

    public bool Compress { get; private set; } = true;

    public bool PasswordFromStandardInput { get; private set; }

    public string? PasswordEnvironmentName { get; private set; }

    public bool Overwrite { get; private set; }

    public List<string> Positionals { get; } = [];

    public static StegoCliOptions Parse(IReadOnlyList<string> arguments)
    {
        var result = new StegoCliOptions();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--profile" when index + 1 < arguments.Count:
                    result.Profile = ParseProfile(arguments[++index]);
                    break;
                case "--no-compress":
                    result.Compress = false;
                    break;
                case "--password-stdin":
                    result.PasswordFromStandardInput = true;
                    break;
                case "--password-env" when index + 1 < arguments.Count:
                    result.PasswordEnvironmentName = arguments[++index];
                    break;
                case "--overwrite":
                    result.Overwrite = true;
                    break;
                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option '{argument}'.");
                    result.Positionals.Add(argument);
                    break;
            }
        }

        if (result.PasswordFromStandardInput && result.PasswordEnvironmentName is not null)
            throw new ArgumentException("Choose either --password-stdin or --password-env, not both.");
        if (string.IsNullOrWhiteSpace(result.PasswordEnvironmentName) && result.PasswordEnvironmentName is not null)
            throw new ArgumentException("The password environment variable name is empty.");
        return result;
    }

    public async Task<string?> ReadPasswordAsync(
        TextReader input,
        Func<string, string?> readEnvironment,
        CancellationToken cancellationToken)
    {
        string? password = null;
        if (PasswordFromStandardInput)
            password = await input.ReadLineAsync(cancellationToken);
        else if (PasswordEnvironmentName is not null)
            password = readEnvironment(PasswordEnvironmentName);
        if ((PasswordFromStandardInput || PasswordEnvironmentName is not null) && string.IsNullOrEmpty(password))
            throw new ArgumentException("The selected password source is empty.");
        return password;
    }

    private static EmbeddingProfile ParseProfile(string value) => value.ToLowerInvariant() switch
    {
        "balanced" => EmbeddingProfile.Balanced,
        "dense" => EmbeddingProfile.Dense,
        "adaptive" => EmbeddingProfile.Adaptive,
        "resilient" => EmbeddingProfile.Resilient,
        _ => throw new ArgumentException($"Unknown embedding profile '{value}'.")
    };
}
