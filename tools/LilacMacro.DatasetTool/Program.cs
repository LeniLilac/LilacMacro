using LilacMacro.Core.Datasets;

namespace LilacMacro.DatasetTool;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("validate" or "agent-view"))
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  LilacMacro.DatasetTool validate <dataset-directory>");
            Console.Error.WriteLine("  LilacMacro.DatasetTool agent-view <dataset-directory> [output-directory]");
            return 2;
        }

        try
        {
            DatasetStore store = new();
            DatasetLocation dataset = await store.LoadAsync(args[1]);
            DatasetValidator validator = new();
            IReadOnlyList<string> failures = await validator.ValidateAsync(dataset);
            if (failures.Count > 0)
            {
                foreach (string failure in failures) Console.Error.WriteLine($"ERROR: {failure}");
                return 1;
            }

            if (args[0] == "validate")
            {
                Console.WriteLine(
                    $"Dataset valid: {dataset.Manifest.Frames.Count} frames, " +
                    $"{dataset.Manifest.Frames.Sum(frame => frame.Annotations.Count)} annotations, " +
                    $"{dataset.Manifest.ClientWidth} × {dataset.Manifest.ClientHeight} pixels.");
                return 0;
            }

            string? requestedOutput = args.Length >= 3 ? args[2] : null;
            AgentViewWriter writer = new();
            string output = await writer.WriteAsync(dataset, requestedOutput);
            Console.WriteLine(output);
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}");
            return 1;
        }
    }
}
