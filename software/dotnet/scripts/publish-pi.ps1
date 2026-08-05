$ErrorActionPreference = "Stop"

$Wilderness = "F:\repos\wilderness"
$Dotnet = "F:\repos\neomotive\software\dotnet"

# Force-rebuild wilderness dependencies so their output timestamps reflect
# today's source. When dotnet publish then evaluates the Pi project, MSBuild
# will see these DLLs as already up-to-date and copy them into the publish
# output without re-running incremental (stale) builds.
Write-Host "==> Rebuilding wilderness dependencies (forced)..."

dotnet build --no-incremental "$Wilderness\Meadow.Core\Source\Meadow.Core\Meadow.Core.csproj"
dotnet build --no-incremental "$Wilderness\Meadow.Core\Source\ui\Meadow.Avalonia\Meadow.Avalonia.csproj"
dotnet build --no-incremental "$Wilderness\Meadow.Core\Source\implementations\linux\Meadow.Linux\Meadow.Linux.csproj"
dotnet build --no-incremental "$Wilderness\Meadow.Foundation\Source\Meadow.Foundation.Peripherals\ICs.CAN.Mcp2515\Driver\ICs.CAN.Mcp2515.csproj"

Write-Host "==> Publishing Pi app..."

dotnet publish "$Dotnet\Neomotive.ModuleSimulator.RaspberryPi\Neomotive.ModuleSimulator.RaspberryPi.csproj" `
  -r linux-arm64 `
  --self-contained true `
  -o "$Dotnet\publish\pi"

Write-Host "==> Done. Output: $Dotnet\publish\pi"
