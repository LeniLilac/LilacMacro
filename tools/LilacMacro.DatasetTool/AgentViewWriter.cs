using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.DatasetTool;

internal sealed class AgentViewWriter
{
    private const int FramesPerSheet = 12;
    private const int Columns = 4;
    private const int TileWidth = 360;
    private const int TileHeight = 260;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions JsonLinesOptions = new(JsonOptions)
    {
        WriteIndented = false,
    };

    public async Task<string> WriteAsync(
        DatasetLocation dataset,
        string? requestedOutput,
        CancellationToken cancellationToken = default)
    {
        string output = ResolveOutput(dataset, requestedOutput);
        EnsureEmptyOrMissing(output);
        string sheetDirectory = Path.Combine(output, "contact-sheets");
        string cropDirectory = Path.Combine(output, "crops");
        string ocrMapDirectory = Path.Combine(output, "ocr-maps");
        Directory.CreateDirectory(sheetDirectory);
        Directory.CreateDirectory(cropDirectory);
        Directory.CreateDirectory(ocrMapDirectory);

        List<string> sheets = WriteContactSheets(dataset, sheetDirectory, cancellationToken);
        Dictionary<Guid, string> cropPaths = WriteAnnotationCrops(dataset, cropDirectory, cancellationToken);
        List<string> ocrMaps = WriteOcrMaps(dataset, ocrMapDirectory, cancellationToken);
        await WriteFramesJsonLinesAsync(dataset, output, cropPaths, cancellationToken);
        await WriteIndexAsync(dataset, output, sheets, ocrMaps, cropPaths, cancellationToken);
        await WriteSummaryAsync(dataset, output, sheets, ocrMaps, cropPaths, cancellationToken);
        return output;
    }

    private static List<string> WriteContactSheets(
        DatasetLocation dataset,
        string output,
        CancellationToken cancellationToken)
    {
        List<string> paths = [];
        for (int start = 0, sheet = 1; start < dataset.Manifest.Frames.Count; start += FramesPerSheet, sheet++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DatasetFrame> frames = dataset.Manifest.Frames
                .Skip(start)
                .Take(FramesPerSheet)
                .ToArray();
            int rows = (int)Math.Ceiling(frames.Count / (double)Columns);
            int width = Columns * TileWidth + 24;
            int height = rows * TileHeight + 54;
            DrawingVisual visual = new();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 246, 255)), null, new Rect(0, 0, width, height));
                DrawText(drawing, $"{DisplayName(dataset)} · frames {start + 1}–{start + frames.Count}", 16, 14, 22, Brushes.Black, FontWeights.Bold);
                for (int index = 0; index < frames.Count; index++)
                {
                    int column = index % Columns;
                    int row = index / Columns;
                    DrawFrameTile(
                        drawing,
                        dataset,
                        frames[index],
                        start + index,
                        12 + column * TileWidth,
                        44 + row * TileHeight);
                }
            }

            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            string fileName = $"contact-sheet-{sheet:000}.png";
            SavePng(bitmap, Path.Combine(output, fileName));
            paths.Add($"contact-sheets/{fileName}");
        }
        return paths;
    }

    private static void DrawFrameTile(
        DrawingContext drawing,
        DatasetLocation dataset,
        DatasetFrame frame,
        int frameIndex,
        double x,
        double y)
    {
        Rect shadow = new(x + 5, y + 5, 336, 238);
        Rect card = new(x, y, 336, 238);
        drawing.DrawRectangle(Brushes.Black, null, shadow);
        drawing.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 2), card);

        Brush verdictBrush = frame.Verdict switch
        {
            FrameVerdict.Positive => new SolidColorBrush(Color.FromRgb(38, 201, 130)),
            FrameVerdict.Negative => new SolidColorBrush(Color.FromRgb(255, 90, 104)),
            FrameVerdict.Ignore => new SolidColorBrush(Color.FromRgb(223, 231, 247)),
            _ => new SolidColorBrush(Color.FromRgb(255, 225, 90)),
        };
        drawing.DrawRectangle(verdictBrush, null, new Rect(x + 1, y + 1, 334, 30));
        DrawText(drawing, $"#{frameIndex + 1:0000}  {frame.Verdict}", x + 10, y + 7, 13, Brushes.Black, FontWeights.Bold);

        BitmapFrame bitmap = DatasetValidator.LoadBitmap(Path.Combine(dataset.ImagesPath, frame.FileName));
        Rect imageBounds = Fit(bitmap.PixelWidth, bitmap.PixelHeight, new Rect(x + 8, y + 38, 320, 176));
        drawing.DrawRectangle(Brushes.Black, null, new Rect(x + 7, y + 37, 322, 178));
        drawing.DrawImage(bitmap, imageBounds);
        DrawAnnotations(drawing, frame, imageBounds);

        string footer = $"{frame.Annotations.Count} regions · {frame.CapturedAtUtc:HH:mm:ss.fff}Z";
        DrawText(drawing, footer, x + 9, y + 220, 11, new SolidColorBrush(Color.FromRgb(68, 75, 87)), FontWeights.SemiBold);
    }

    private static void DrawAnnotations(DrawingContext drawing, DatasetFrame frame, Rect imageBounds)
    {
        double scale = imageBounds.Width / frame.Width;
        foreach (BoxAnnotation annotation in frame.Annotations)
        {
            PixelRect box = annotation.Bounds;
            Rect rectangle = new(
                imageBounds.X + box.X * scale,
                imageBounds.Y + box.Y * scale,
                box.Width * scale,
                box.Height * scale);
            drawing.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(255, 79, 172)), 2), rectangle);
            if (string.IsNullOrWhiteSpace(annotation.Label)) continue;
            string displayLabel = annotation.IsGlobal ? $"GLOBAL · {annotation.Label}" : annotation.Label;
            FormattedText text = MakeText(displayLabel, 10, Brushes.Black, FontWeights.Bold);
            Rect label = new(rectangle.X, Math.Max(imageBounds.Y, rectangle.Y - 16), text.Width + 6, 16);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 225, 90)), null, label);
            drawing.DrawText(text, new Point(label.X + 3, label.Y + 1));
        }
    }

    private static Dictionary<Guid, string> WriteAnnotationCrops(
        DatasetLocation dataset,
        string output,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> paths = [];
        for (int frameIndex = 0; frameIndex < dataset.Manifest.Frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DatasetFrame frame = dataset.Manifest.Frames[frameIndex];
            BitmapFrame bitmap = DatasetValidator.LoadBitmap(Path.Combine(dataset.ImagesPath, frame.FileName));
            foreach (BoxAnnotation annotation in frame.Annotations)
            {
                PixelRect box = annotation.Bounds;
                CroppedBitmap crop = new(bitmap, new Int32Rect(box.X, box.Y, box.Width, box.Height));
                string label = string.IsNullOrWhiteSpace(annotation.Label)
                    ? "region"
                    : DatasetNaming.Slugify(annotation.Label);
                string identifier = annotation.Id.ToString("N")[..12];
                string fileName = $"frame-{frameIndex + 1:0000}--{label}--{identifier}.png";
                if (fileName.Length > 110) fileName = $"frame-{frameIndex + 1:0000}--region--{identifier}.png";
                SavePng(crop, Path.Combine(output, fileName));
                paths[annotation.Id] = $"crops/{fileName}";
            }
        }
        return paths;
    }

    private static List<string> WriteOcrMaps(
        DatasetLocation dataset,
        string output,
        CancellationToken cancellationToken)
    {
        List<string> paths = [];
        string[] models = ["PP-OCRv6_small_rec", "PP-OCRv6_tiny_rec"];
        for (int frameIndex = 0; frameIndex < dataset.Manifest.Frames.Count; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DatasetFrame frame = dataset.Manifest.Frames[frameIndex];
            BitmapFrame bitmap = DatasetValidator.LoadBitmap(Path.Combine(dataset.ImagesPath, frame.FileName));
            foreach (string model in models)
            {
                if (!frame.Annotations.Any(annotation => annotation.OcrTrials.Any(trial => trial.ModelName == model))) continue;
                const int width = 1620;
                const int height = 700;
                DrawingVisual visual = new();
                using (DrawingContext drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 246, 255)), null, new Rect(0, 0, width, height));
                    DrawText(drawing, $"Frame {frameIndex + 1:0000} · {model}", 18, 14, 22, Brushes.Black, FontWeights.Bold);
                    DrawText(drawing, "SOURCE + REGIONS", 18, 48, 12, Brushes.DimGray, FontWeights.Bold);
                    DrawText(drawing, "RECOGNIZED TEXT MAP", 828, 48, 12, Brushes.DimGray, FontWeights.Bold);
                    Rect left = Fit(bitmap.PixelWidth, bitmap.PixelHeight, new Rect(18, 70, 774, 610));
                    Rect right = new(828, left.Y, left.Width, left.Height);
                    drawing.DrawRectangle(Brushes.Black, null, new Rect(left.X - 2, left.Y - 2, left.Width + 4, left.Height + 4));
                    drawing.DrawImage(bitmap, left);
                    drawing.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 2), right);
                    DrawOcrRegions(drawing, frame, left, right, model);
                }
                RenderTargetBitmap rendered = new(width, height, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(visual);
                string shortModel = model.Contains("small", StringComparison.Ordinal) ? "small" : "tiny";
                string fileName = $"ocr-map-frame-{frameIndex + 1:0000}--{shortModel}.png";
                SavePng(rendered, Path.Combine(output, fileName));
                paths.Add($"ocr-maps/{fileName}");
            }
        }
        return paths;
    }

    private static void DrawOcrRegions(
        DrawingContext drawing,
        DatasetFrame frame,
        Rect sourceBounds,
        Rect textBounds,
        string model)
    {
        double scale = sourceBounds.Width / frame.Width;
        Brush color = model.Contains("small", StringComparison.Ordinal)
            ? new SolidColorBrush(Color.FromRgb(61, 140, 255))
            : new SolidColorBrush(Color.FromRgb(255, 79, 172));
        foreach (BoxAnnotation annotation in frame.Annotations)
        {
            OcrTrial? trial = annotation.OcrTrials
                .Where(item => item.ModelName == model)
                .OrderByDescending(item => item.RanAtUtc)
                .FirstOrDefault();
            Rect parentSource = ScaleRect(annotation.Bounds, sourceBounds, scale);
            Rect parentTarget = ScaleRect(annotation.Bounds, textBounds, scale);
            drawing.DrawRectangle(null, new Pen(color, 2), parentSource);
            if (trial is not { Regions.Count: > 0 })
            {
                string fallback = trial is null
                    ? "Not tested"
                    : (string.IsNullOrWhiteSpace(trial.Text) ? "No text" : trial.Text);
                DrawRecognizedRegion(drawing, parentTarget, fallback, color);
                continue;
            }

            foreach (OcrTextRegion region in trial.Regions)
            {
                Rect source = ScaleRect(region.Bounds, sourceBounds, scale);
                Rect target = ScaleRect(region.Bounds, textBounds, scale);
                drawing.DrawRectangle(null, new Pen(color, 2), source);
                DrawRecognizedRegion(drawing, target, region.Text, color);
            }
            string load = trial.ModelWasCached ? "cached" : $"load {trial.ModelLoadMilliseconds} ms";
            string metrics = $"{trial.Regions.Count} boxes · {trial.Device} · {trial.Confidence:P1} · {load} · inference {trial.InferenceMilliseconds} ms";
            DrawText(drawing, metrics, parentTarget.X, Math.Min(textBounds.Bottom - 13, parentTarget.Bottom + 2), 10, color, FontWeights.Bold);
        }
    }

    private static void DrawRecognizedRegion(DrawingContext drawing, Rect target, string text, Brush color)
    {
        FormattedText recognized = MakeText(
            text,
            Math.Clamp(target.Height * 0.5, 7, 26),
            Brushes.Black,
            FontWeights.SemiBold);
        recognized.MaxTextWidth = Math.Max(1, target.Width - 6);
        double labelHeight = Math.Max(target.Height, recognized.Height + 4);
        double labelY = target.Y - ((labelHeight - target.Height) / 2);
        recognized.MaxTextHeight = Math.Max(1, labelHeight - 3);
        recognized.Trimming = TextTrimming.CharacterEllipsis;
        drawing.DrawRectangle(Brushes.White, new Pen(color, 1), new Rect(target.X, labelY, target.Width, labelHeight));
        drawing.DrawRectangle(null, new Pen(color, 2), target);
        drawing.DrawText(recognized, new Point(target.X + 3, labelY + 1));
    }

    private static Rect ScaleRect(PixelRect box, Rect bounds, double scale) => new(
        bounds.X + box.X * scale,
        bounds.Y + box.Y * scale,
        box.Width * scale,
        box.Height * scale);

    private static async Task WriteFramesJsonLinesAsync(
        DatasetLocation dataset,
        string output,
        IReadOnlyDictionary<Guid, string> cropPaths,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(output, "frames.jsonl");
        await using StreamWriter writer = new(path, append: false, new UTF8Encoding(false));
        for (int index = 0; index < dataset.Manifest.Frames.Count; index++)
        {
            DatasetFrame frame = dataset.Manifest.Frames[index];
            object item = new
            {
                frame_index = index,
                image_path = RelativePath(output, Path.Combine(dataset.ImagesPath, frame.FileName)),
                frame.CapturedAtUtc,
                frame.Sha256,
                frame.Width,
                frame.Height,
                verdict = frame.Verdict.ToString().ToLowerInvariant(),
                frame.Notes,
                annotations = frame.Annotations.Select(annotation => new
                {
                    annotation.Id,
                    annotation.GlobalGroupId,
                    annotation.Bounds,
                    annotation.Label,
                    annotation.Notes,
                    annotation.MinimumPoolMatches,
                    crop_path = cropPaths[annotation.Id],
                    ocr_trials = annotation.OcrTrials.Select(trial => new
                    {
                        trial.ModelName,
                        trial.DetectorModelName,
                        trial.Device,
                        trial.Text,
                        trial.Confidence,
                        trial.ModelLoadMilliseconds,
                        trial.InferenceMilliseconds,
                        total_compute_milliseconds = trial.ModelLoadMilliseconds + trial.InferenceMilliseconds,
                        trial.RuntimeVersion,
                        trial.RanAtUtc,
                        trial.ModelWasCached,
                        regions = trial.Regions.Select(region => new
                        {
                            region.Bounds,
                            region.Text,
                            region.DetectionConfidence,
                            region.RecognitionConfidence,
                            region.IsOcrEvidence,
                            region.IsVisualAnchor,
                            match_mode = JsonNamingPolicy.SnakeCaseLower.ConvertName(region.MatchMode.ToString()),
                            evidence_role = JsonNamingPolicy.SnakeCaseLower.ConvertName(region.EvidenceRole.ToString()),
                            spatial_selector = JsonNamingPolicy.SnakeCaseLower.ConvertName(region.SpatialSelector.ToString()),
                            region.SpatialSelectorOverridden,
                            region.SpatialAnchorText,
                        }),
                    }),
                }),
            };
            string json = JsonSerializer.Serialize(item, JsonLinesOptions);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
    }

    private static async Task WriteIndexAsync(
        DatasetLocation dataset,
        string output,
        IReadOnlyList<string> sheets,
        IReadOnlyList<string> ocrMaps,
        IReadOnlyDictionary<Guid, string> cropPaths,
        CancellationToken cancellationToken)
    {
        object index = new
        {
            format = "lilacmacro.agent_view",
            schema_version = 1,
            source_manifest = RelativePath(output, dataset.ManifestPath),
            dataset_id = dataset.Manifest.Id,
            dataset_name = dataset.Manifest.Name,
            capture_mode = dataset.Manifest.CaptureMode.ToString().ToLowerInvariant(),
            coordinate_space = dataset.Manifest.CoordinateSpace,
            frame_count = dataset.Manifest.Frames.Count,
            annotation_count = cropPaths.Count,
            global_annotation_group_count = dataset.Manifest.Frames
                .SelectMany(frame => frame.Annotations)
                .Where(annotation => annotation.GlobalGroupId.HasValue)
                .Select(annotation => annotation.GlobalGroupId)
                .Distinct()
                .Count(),
            contact_sheets = sheets,
            ocr_maps = ocrMaps,
            frames_jsonl = "frames.jsonl",
            generated_at_utc = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(
            Path.Combine(output, "agent-index.json"),
            JsonSerializer.Serialize(index, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
    }

    private static async Task WriteSummaryAsync(
        DatasetLocation dataset,
        string output,
        IReadOnlyList<string> sheets,
        IReadOnlyList<string> ocrMaps,
        IReadOnlyDictionary<Guid, string> cropPaths,
        CancellationToken cancellationToken)
    {
        DatasetManifest manifest = dataset.Manifest;
        StringBuilder markdown = new();
        markdown.AppendLine($"# {DisplayName(dataset)}");
        markdown.AppendLine();
        markdown.AppendLine("> Local review artifact. It may contain private or third-party game imagery. Do not publish or commit it.");
        markdown.AppendLine();
        markdown.AppendLine($"- Dataset ID: `{manifest.Id}`");
        markdown.AppendLine($"- State: `{(manifest.IsFinalized ? "finalized" : "draft")}`");
        markdown.AppendLine($"- Capture mode: `{manifest.CaptureMode.ToString().ToLowerInvariant()}`");
        markdown.AppendLine($"- Capture: `{manifest.ClientWidth} × {manifest.ClientHeight}` client pixels");
        markdown.AppendLine($"- Frames: `{manifest.Frames.Count}`");
        markdown.AppendLine($"- Regions: `{cropPaths.Count}`");
        markdown.AppendLine($"- Global region groups: `{manifest.Frames.SelectMany(frame => frame.Annotations).Where(annotation => annotation.GlobalGroupId.HasValue).Select(annotation => annotation.GlobalGroupId).Distinct().Count()}`");
        markdown.AppendLine($"- Coordinate space: `{manifest.CoordinateSpace}`");
        markdown.AppendLine();
        if (!string.IsNullOrWhiteSpace(manifest.Notes)) markdown.AppendLine($"Dataset note: {manifest.Notes}\n");
        markdown.AppendLine("## Inspection order");
        markdown.AppendLine();
        markdown.AppendLine("1. Read `frames.jsonl` for exact metadata and paths.");
        markdown.AppendLine("2. Inspect contact sheets in chronological order.");
        markdown.AppendLine("3. Inspect `crops/` only for regions that need closer review.");
        markdown.AppendLine("4. Treat OCR confidence as advisory evidence, not ground truth.");
        if (ocrMaps.Count > 0) markdown.AppendLine("5. Compare source regions with clean text maps under `ocr-maps/`.");
        markdown.AppendLine();
        markdown.AppendLine("## Contact sheets");
        markdown.AppendLine();
        foreach (string sheet in sheets) markdown.AppendLine($"- [{Path.GetFileName(sheet)}]({sheet})");
        if (ocrMaps.Count > 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("## OCR maps");
            markdown.AppendLine();
            foreach (string map in ocrMaps) markdown.AppendLine($"- [{Path.GetFileName(map)}]({map})");
        }
        await File.WriteAllTextAsync(
            Path.Combine(output, "summary.md"),
            markdown.ToString(),
            new UTF8Encoding(false),
            cancellationToken);
    }

    private static string ResolveOutput(DatasetLocation dataset, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested);
        string root = Path.Combine(dataset.DirectoryPath, ".agent-view");
        Directory.CreateDirectory(root);
        string baseName = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (int suffix = 0; suffix < 1000; suffix++)
        {
            string candidate = Path.Combine(root, suffix == 0 ? baseName : $"{baseName}-{suffix + 1}");
            if (!Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not allocate an agent-view directory.");
    }

    private static void EnsureEmptyOrMissing(string path)
    {
        if (File.Exists(path)) throw new IOException($"Agent-view output is a file: {path}");
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new IOException($"Agent-view output must be empty: {path}");
        }
        Directory.CreateDirectory(path);
    }

    private static Rect Fit(double width, double height, Rect bounds)
    {
        double scale = Math.Min(bounds.Width / width, bounds.Height / height);
        double fittedWidth = width * scale;
        double fittedHeight = height * scale;
        return new Rect(
            bounds.X + (bounds.Width - fittedWidth) / 2,
            bounds.Y + (bounds.Height - fittedHeight) / 2,
            fittedWidth,
            fittedHeight);
    }

    private static void DrawText(
        DrawingContext drawing,
        string text,
        double x,
        double y,
        double size,
        Brush brush,
        FontWeight weight) => drawing.DrawText(MakeText(text, size, brush, weight), new Point(x, y));

    private static FormattedText MakeText(string text, double size, Brush brush, FontWeight weight) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1);

    private static void SavePng(BitmapSource source, string path)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string DisplayName(DatasetLocation dataset) =>
        string.IsNullOrWhiteSpace(dataset.Manifest.Name) ? "Unnamed draft dataset" : dataset.Manifest.Name;

    private static string RelativePath(string fromDirectory, string target) =>
        Path.GetRelativePath(fromDirectory, target).Replace('\\', '/');
}
