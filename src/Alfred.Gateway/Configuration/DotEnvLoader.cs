namespace Alfred.Gateway.Configuration;

/// <summary>
/// Loads environment variables from .env file
/// Ported from Alfred.Identity to maintain consistency
/// </summary>
public static class DotEnvLoader
{
    public static void Load(string? filePath = null)
    {
        // If no path specified, try to find .env in solution root
        if (filePath == null)
        {
            var solutionRoot = FindSolutionRoot(Directory.GetCurrentDirectory());
            if (solutionRoot == null)
            {
                Console.WriteLine("Warning: Could not find solution root. Skipping .env file loading.");
                return;
            }

            filePath = Path.Combine(solutionRoot, ".env");
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine(
                $"Warning: .env file not found at '{filePath}'. Using system environment variables only.");
            return;
        }

        Console.WriteLine($"Loading environment variables from '{filePath}'");

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmedLine = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#")) continue;

            var parts = trimmedLine.Split('=', 2, StringSplitOptions.None);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            // Remove quotes if present
            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                (value.StartsWith("'") && value.EndsWith("'")))
                value = value[1..^1];

            // Only set if not already set (system env vars take precedence)
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }

        Console.WriteLine("Environment variables loaded successfully");
    }

    public static void LoadForEnvironment(string environment)
    {
        // Load base .env file first
        Load();

        // Then load environment-specific .env file (e.g., .env.development)
        var solutionRoot = FindSolutionRoot(Directory.GetCurrentDirectory());
        if (solutionRoot != null)
        {
            var envFile = Path.Combine(solutionRoot, $".env.{environment.ToLowerInvariant()}");
            if (File.Exists(envFile)) Load(envFile);
        }
    }

    private static string? FindSolutionRoot(string startDirectory)
    {
        DirectoryInfo? current = new(startDirectory);

        while (current != null)
        {
            // Look for .sln file or docker-compose.yml as indicators of solution root
            if (current.GetFiles("*.sln").Length > 0 ||
                current.GetFiles("docker-compose.yml").Length > 0)
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }
}