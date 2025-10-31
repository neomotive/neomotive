using NeoMotive.Services;

var imagesFolder = @"C:\repos\yoshimoshi\CarCam\dataset\images\train";

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
var service = new SpeedLimitService();
service.CheckForSpeedLimit(randomImage);

Console.WriteLine();
Console.WriteLine("Processing complete! Check the output above for the temp file location.");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
