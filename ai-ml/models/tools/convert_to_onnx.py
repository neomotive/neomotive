#!/usr/bin/env python3
"""
Convert YOLOv8 model to ONNX format
"""
from ultralytics import YOLO
import sys

def convert_model_to_onnx(model_path, opset=12):
    """
    Convert a YOLOv8 PyTorch model to ONNX format

    Args:
        model_path: Path to the .pt model file
        opset: ONNX opset version (default: 12)
    """
    try:
        print(f"Loading model from: {model_path}")
        model = YOLO(model_path)

        print(f"Converting to ONNX with opset {opset}...")
        # Export to ONNX format
        # This will create a .onnx file in the same directory as the .pt file
        model.export(format='onnx', opset=opset)

        print("✓ Conversion successful!")
        print(f"✓ ONNX model saved as: {model_path.replace('.pt', '.onnx')}")

    except Exception as e:
        print(f"✗ Error during conversion: {e}")
        sys.exit(1)

if __name__ == "__main__":
    model_path = "speed-limits-us.pt"

    # Allow custom model path from command line
    if len(sys.argv) > 1:
        model_path = sys.argv[1]

    # Allow custom opset from command line
    opset = 12
    if len(sys.argv) > 2:
        opset = int(sys.argv[2])

    convert_model_to_onnx(model_path, opset)
