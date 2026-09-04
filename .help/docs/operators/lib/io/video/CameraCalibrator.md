# CameraCalibrator

*in [Lib.io.video](README.md)*

Calibrates a camera to determine its intrinsic parameters (like focal length and lens distortion) and to remove distortion from the video feed.

This operator analyzes images of a known chessboard pattern to calculate the camera's properties. This process is essential for many computer vision tasks, such as 3D reconstruction, augmented reality, and spatial mapping.

To use:
1. Set the `ChessboardSize` to match your physical chessboard pattern.
2. Show the chessboard to the camera.
3. Trigger `CaptureImage` multiple times from different angles and distances.
4. Trigger `Calibrate` to compute the camera parameters.
5. Switch `Mode` to 'LensCorrected' to see the corrected image on the `TextureOut` output.
6. `Save` the calibration to a file for later use.

AKA: lens calibration, camera intrinsics, undistort, computer vision, chessboard

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Alpha** (Single) | Controls the cropping of the undistorted image. 0 crops to show only valid pixels, while 1 shows all original pixels, potentially with black areas at the edges. |
| **BorderInSquares** (Int32) | The width of the border around the generated checkerboard pattern, measured in squares. |
| **Calibrate** (Boolean) | A trigger to start the calibration process using all the captured images. |
| **CaptureImage** (Boolean) | A trigger to capture the current frame showing the chessboard. Capture at least 10-15 images from various angles for a good calibration. |
| **ChessboardSize** (Int2) | The number of inner corners on your chessboard pattern (e.g., a 9x6 board has 8x5 inner corners). |
| **DisplayResolution** (Int2) | The resolution of the display that is showing the generated checkerboard pattern. This should match your projector or screen resolution. |
| **FilePath** (String) | The path to the file for saving or loading the camera calibration data (as a .json file). |
| **Load** (Boolean) | A trigger to load camera calibration data from the specified `FilePath`. |
| **Mode** (Int32) | Switches the operator's function between 'Passthrough' (raw video), 'Calibration' (for capturing and calculating), and 'LensCorrected' (for applying the correction to the live feed). |
| **Reset** (Boolean) | A trigger to clear all captured images and reset the current calibration. |
| **Save** (Boolean) | A trigger to save the current calibration data to the specified `FilePath`. |
| **SquareInMm** (Single) | The real-world side length of a single square on your physical chessboard, in millimeters. This is crucial for accurate 3D measurements. |
| **TextureIn** (Texture2D) | The live video feed from the camera that you want to calibrate. |
| **UndistortMethod** (Int32) | The algorithm used to undistort the image after calibration. 'Undistort' is generally simpler, 'Remap' offers more control. |

## Outputs
| Name | Type |
|---|---|
| **CheckerboardTexture** | T3.Core.DataTypes.Texture2D |
| **IsCalibrated** | System.Boolean |
| **StatusMessage** | System.String |
| **TextureOut** | T3.Core.DataTypes.Texture2D |

