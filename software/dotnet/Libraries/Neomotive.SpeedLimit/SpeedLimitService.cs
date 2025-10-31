using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System.Diagnostics;

namespace NeoMotive.Services;

public class SpeedLimitService : IDisposable
{
    private const string ModelsFolder = "C:\\repos\\yoshimoshi\\CarCam\\models";
    private readonly string[] classNames = new[]
    {
        "mph25", "mph30", "mph35", "mph40", "mph45", "mph50",
        "mph55", "mph60", "mph65", "mph70", "mph75", "mph80"
    };

    private readonly InferenceSession? _session;
    private readonly string _inputName;
    private bool _disposed = false;

    public SpeedLimitService(string? modelPath = null)
    {
        modelPath ??= Path.Combine(ModelsFolder, "speed-limits-us.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Model not found", modelPath);
        }

        _session = new InferenceSession(modelPath);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public void CheckForSpeedLimit(string sourceImagePath)
    {
        using var img = Cv2.ImRead(sourceImagePath);
        CheckForSpeedLimit(img, saveToFile: true);
    }

    public List<SpeedLimitDetection> CheckForSpeedLimit(Mat img, bool saveToFile = false, bool drawDetections = true)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("Inference session is not initialized");
        }

        // --- Load and preprocess image ---
        int originalWidth = img.Width;
        int originalHeight = img.Height;

        var inputSize = new Size(640, 640);
        using var resized = new Mat();
        Cv2.Resize(img, resized, inputSize);
        Cv2.CvtColor(resized, resized, ColorConversionCodes.BGR2RGB);

        // Convert Mat to float array for tensor
        var blobData = new float[1 * 3 * 640 * 640];
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < 640; y++)
            {
                for (int x = 0; x < 640; x++)
                {
                    int idx = (c * 640 * 640) + (y * 640) + x;
                    blobData[idx] = resized.At<Vec3b>(y, x)[c] / 255.0f;
                }
            }
        }
        var inputTensor = new DenseTensor<float>(blobData, new[] { 1, 3, 640, 640 });

        // --- Run inference ---
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) });
        var outputTensor = results.First().AsTensor<float>();

        // YOLOv8 output is typically [1, 16, 8400] - need to transpose
        // Where 16 = 4 box coords + 12 classes, and 8400 is number of anchor boxes
        int numClasses = 12;
        int dimensions = 4 + numClasses; // 16
        int numBoxes = outputTensor.Dimensions[2]; // 8400

        Debug.WriteLine($"Output shape: [{outputTensor.Dimensions[0]}, {outputTensor.Dimensions[1]}, {outputTensor.Dimensions[2]}]");
        Debug.WriteLine($"Processing {numBoxes} potential detections");

        var boxes = new List<Rect2d>();
        var confidences = new List<float>();
        var classIds = new List<int>();
        const float confThreshold = 0.25f;

        // Process each detection box
        for (int i = 0; i < numBoxes; i++)
        {
            // YOLOv8 format: output[0, row, column] where row is feature index, column is box index
            // Rows 0-3: x, y, w, h (center coords and dimensions)
            // Rows 4-15: class probabilities for 12 classes
            float x = outputTensor[0, 0, i];
            float y = outputTensor[0, 1, i];
            float w = outputTensor[0, 2, i];
            float h = outputTensor[0, 3, i];

            // Find the class with highest probability
            float maxClassScore = float.MinValue;
            int classId = -1;
            for (int c = 0; c < numClasses; c++)
            {
                float score = outputTensor[0, 4 + c, i];
                if (score > maxClassScore)
                {
                    maxClassScore = score;
                    classId = c;
                }
            }

            float confidence = maxClassScore;
            if (confidence < confThreshold) continue;

            // Convert from 640x640 normalized coords to original image coords
            double cx = x * originalWidth / 640.0;
            double cy = y * originalHeight / 640.0;
            double boxW = w * originalWidth / 640.0;
            double boxH = h * originalHeight / 640.0;
            double left = cx - (boxW / 2);
            double top = cy - (boxH / 2);

            boxes.Add(new Rect2d(left, top, boxW, boxH));
            confidences.Add(confidence);
            classIds.Add(classId);
        }

        // --- Apply Non-Max Suppression (NMS) ---
        CvDnn.NMSBoxes(boxes, confidences, confThreshold, 0.45f, out int[] indices);

        // --- Process detections ---
        var detections = new List<SpeedLimitDetection>();
        foreach (int idx in indices)
        {
            var box = boxes[idx];
            var label = classNames[classIds[idx]];
            var conf = confidences[idx];

            // Extract speed from label (e.g., "mph30" -> "30")
            var speedMatch = System.Text.RegularExpressions.Regex.Match(label, @"\d+");
            var speed = speedMatch.Success ? int.Parse(speedMatch.Value) : 0;

            var detection = new SpeedLimitDetection
            {
                SpeedLimit = speed,
                Confidence = conf,
                BoundingBox = box,
                Label = label
            };
            detections.Add(detection);

            // Output to Debug
            Debug.WriteLine($"Speed Limit Detected: {speed} mph, Confidence: {conf:P1}");
            Console.WriteLine($"Detected: {label} ({conf:P1})");

            // Draw detections if requested
            if (drawDetections)
            {
                var drawRect = new Rect((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height);
                Cv2.Rectangle(img, drawRect, Scalar.Red, 2);
                Cv2.PutText(img, $"{speed} mph {conf:P1}", new Point((int)box.X, (int)box.Y - 5),
                    HersheyFonts.HersheySimplex, 0.6, Scalar.Yellow, 2);
            }
        }

        // --- Save result to temp folder if requested ---
        if (saveToFile)
        {
            var tempFolder = Path.GetTempPath();
            var tempFileName = $"speed_limit_detection_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            var outputPath = Path.Combine(tempFolder, tempFileName);
            Cv2.ImWrite(outputPath, img);
            Console.WriteLine($"Saved annotated image: {outputPath}");
        }

        return detections;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
    }
}

public class SpeedLimitDetection
{
    public int SpeedLimit { get; set; }
    public float Confidence { get; set; }
    public Rect2d BoundingBox { get; set; }
    public string Label { get; set; } = string.Empty;
}
