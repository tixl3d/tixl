# Exporting Videos and Image Sequences

## TL;DR
You can use TiXL to render your project as a video or image sequence:
- Select an image Operator like [RenderTarget].
- Use the output window's resolution selector to set your output resolution.
- From the *main menu*, open Windows → *Render Video* or *Render Image Sequence*.
- Adjust your settings.
- Make sure the file name has the .mp4 extension.
- Press *Start Export*.

![Alt text](/images/01-export-as-video.gif)


### Settings

- **Time Format** - Switch between bars (TiXL's default method of storing time), seconds or frames
- **Use range** - Select *Custom* to define a precise start and end time. If you added *Soundtrack* to your project, you can also select that. Finally you can enable the the *Loop* option in the time line controls, adjust it's range and use that option.
- **Motion Blur Samples** - is only useful when adding a [RenderWithMotionBlur] operator to your project (see below)
- When rendering as video **Bitrate** handles the quality of your output. It defines the number of bits used for each second of your video. To make this more readable, TiXL gives an indication of the current setting for your current resolution and framerate: 

![Alt text](/images/03-quality-preview.gif)


## Saving Screenshots

The screenshot (camera) icon in the output window's toolbar saves the current frame as a PNG into your project's `Screenshots/` folder.

**Ctrl-click** the icon to start **continuous screenshot mode**: TiXL then saves a screenshot on a fixed interval and the icon pulses in the attention colour, brightest right after each capture. Click the icon again to stop. Continuous capture pauses while a video or image-sequence export is running and resumes once it finishes.

**Right-click** the icon for options: start/stop continuous capture, pick the interval (1 s … 10 min), and choose the file format. PNG is lossless; **JPG** produces much smaller files, which is handy when capturing long continuous sequences. The interval can also be set under *Settings → Interface → Output*.

## Optimizing Export Performance **(_Since version 3.9.1, TiXL is taking care of this automatically_)** 

Rendering a 5-minute project at 60fps will be roughly 20000 frames. With a naive export this will take several hours. Your CPU will spend most of this time converting pixels. But we can use DirectX and the graphics card to convert the texture into the correct format. This will reduce rendering by an order of magnitude:

Append a [ConvertFormat] operator and set its Format parameter to `B8G8R8A8_UNorm` and pin this to your output window.

On most computers DirectX's Media Foundation will use hardware-accelerated encoding to H.264. So, exporting videos will be much faster than rendering image sequences. PNG is especially slow to write.

## Increase Output Quality

When rendering to an image sequence or video, we can sacrifice rendering performance for quality: Only a very small fraction of the time is spent on generating the image. The most expensive steps are format conversion and encoding. This means you could increase the output resolution to 4K or increase quality settings to your primary [RenderTarget]'s multisampling quality. Because of TiXL's default [smart resolution setup](https://www.youtube.com/watch?v=f9E7lwUXfBM), the requested resolution will be applied to your whole project.

## Rendering with Motion Blur

Another awesome feature is [RenderWithMotionBlur]: It uses TiXL's timeline features to render multiple slightly offset passes for each frame, resulting in a very high-quality motion blur effect. Because this process is normally too performance-intensive for real-time, you can adjust the parameter to 0 (or another acceptable low value that will provide real-time rendering).

Then you can use override this setting while rendering with the "Motion Blur Samples" parameter provided in the Render Window:

[Watch the tutorial here](https://www.youtube.com/watch?v=WK4j0H-oM3g&t=626s)

## Technical Background

To get the most out of this feature, it can be useful to provide some background about the exporting process:

TiXL uses image operators like [RenderTarget] to render content into an image buffer on the GPU. We use DirectX for that. In technical terms, these images are called *Textures*, and DirectX provides a wide variety of texture formats. By default, TiXL's render output format is `R16G16B16A16_Float`, which means that each pixel uses 16 bits to store brightness for the Red, Green, Blue, and Alpha channels. That is 65536 different values with higher resolution in the range 0 .. 1.

Exporting images and textures in files is complicated because:

- For historical reasons, many image formats store their pixels in the order Blue, Green, Red, Alpha.
- Downloading textures from the GPU takes up to 3 frames.
- For performance reasons, DirectX stores textures with a width of 16 pixels increments. So, if your image width is 1080 pixels, each pixel row will be padded with 8 pixels.
- Converting between pixel formats can be very slow and complex (especially from 16-bit floats to C# default 32-bit floats).
- Frequently, TiXL's effects are audio-reactive, but exporting is not real-time.
- Some of TiXL's operators are asynchronous: preferring rendering performance over precision, but when exporting, we want to enforce repeatability, so we have to wait until each operator (e.g., loading an image from the web or seeking a frame in a video) is completed.

## Exporting with Audio **(_Since version 3.9.1, TiXL can export video with Audio_)**

It is still an experimental feature, please report if you have issues.

### Exporting Other Formats

Currently, the output formats are limited to:
- 8-bit H.264 for video
- 8-bit RGB JPEG 
- 8-bit RGBA PNG

Rendering to HDR-Output with formats like EXR is on our roadmap.
