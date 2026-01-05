using NeoMotive.Services;

var imagesFolder = @"F:\repos\huggingface\speed-limit-signs\data\images\train\mph45";
var modelFile = @"F:\repos\neomotive\ai-ml\models\trained\speed-limit\speed-limits-us.onnx";

// Get all image files from the train folder
var imageFiles = Directory.GetFiles(imagesFolder, "*.png")
    .Concat(Directory.GetFiles(imagesFolder, "*.jpg"))
    .ToArray();

if (imageFiles.Length == 0)
{
    Console.WriteLine("No images found in the train folder!");
    return;
}

// Select a random image
var random = new Random();
var randomImage = imageFiles[random.Next(imageFiles.Length)];

Console.WriteLine($"Selected random image: {Path.GetFileName(randomImage)}");
Console.WriteLine($"Full path: {randomImage}");
Console.WriteLine();

// Call the SpeedLimitService
var service = new SpeedLimitService(modelFile);
var inference = service.CheckForSpeedLimit(randomImage);
Console.WriteLine("Inference Results:");
Console.WriteLine($"Detected Speed Limit: {inference.SpeedLimit} mph");
Console.WriteLine($"Confidence: {inference.Confidence:P2}");

Console.WriteLine();
Console.WriteLine("Processing complete! Check the output above for the temp file location.");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
