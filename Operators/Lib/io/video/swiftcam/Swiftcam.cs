using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
#if !(NETFX_CORE || NETCOREAPP || WINDOWS_UWP)
using System.Security.Permissions;
using System.Runtime.ConstrainedExecution;
#endif
using System.Collections.Generic;
using System.Threading;

namespace Lib.io.video.swiftcam;

/*
    Version: 59.29807.20251021

    For .NET Framework & .NET Core

    We use P/Invoke to call into the swiftcam.dll API, the c# class Swiftcam is a thin wrapper class to the native API of swiftcam.dll.
    So the manual en.html(English) and hans.html(Simplified Chinese) are also applicable for programming with swiftcam.cs.
    See them in the 'doc' directory:
       (1) en.html, English
       (2) hans.html, Simplified Chinese
*/

/*
    Please distinguish between camera ID (camId) and camera SN:
        (a) SN is unique and persistent, fixed inside the camera and remains unchanged, and does not change with connection or system restart.
        (b) Camera ID (camId) may change due to connection or system restart. Enumerate the cameras to get the camera ID, and then call the Open function to pass in the camId parameter to open the camera.
*/

/*
    Coordinate:
        (a) Functions with coordinate parameters, such as Swiftcam_put_Roi, Swiftcam_put_AEAuxRect, etc., the coordinate is always relative to the original resolution,
            even that the image has been flipped, rotated, digital binning, ROI, or combination of the previous operations.
        (b) Exception: if the image is upside down (see here), the coordinate must be also upsize down.
        (c) Exception: hardware binning.
*/

internal class Swiftcam : IDisposable
{
    [Flags]
    public enum eFLAG : ulong
    {
        /* cmos sensor */
        FLAG_CMOS = 0x00000001,
        /* progressive ccd sensor */
        FLAG_CCD_PROGRESSIVE = 0x00000002,
        /* interlaced ccd sensor */
        FLAG_CCD_INTERLACED = 0x00000004,
        /* support hardware ROI */
        FLAG_ROI_HARDWARE = 0x00000008,
        /* monochromatic */
        FLAG_MONO = 0x00000010,
        /* support bin/skip mode */
        FLAG_BINSKIP_SUPPORTED = 0x00000020,
        /* usb3.0 */
        FLAG_USB30 = 0x00000040,
        /* Thermoelectric Cooler */
        FLAG_TEC = 0x00000080,
        /* usb3.0 camera connected to usb2.0 port */
        FLAG_USB30_OVER_USB20 = 0x00000100,
        /* ST4 */
        FLAG_ST4 = 0x00000200,
        /* support to get the temperature of the sensor */
        FLAG_GETTEMPERATURE = 0x00000400,
        /* high fullwell capacity */
        FLAG_HIGH_FULLWELL = 0x00000800,
        /* pixel format, RAW 10bits */
        FLAG_RAW10 = 0x00001000,
        /* pixel format, RAW 12bits */
        FLAG_RAW12 = 0x00002000,
        /* pixel format, RAW 14bits */
        FLAG_RAW14 = 0x00004000,
        /* pixel format, RAW 16bits */
        FLAG_RAW16 = 0x00008000,
        /* cooling fan */
        FLAG_FAN = 0x00010000,
        /* Thermoelectric Cooler can be turn on or off, support to set the target temperature of TEC */
        FLAG_TEC_ONOFF = 0x00020000,
        /* ISP (Image Signal Processing) chip */
        FLAG_ISP = 0x00040000,
        /* support software trigger */
        FLAG_TRIGGER_SOFTWARE = 0x00080000,
        /* support external trigger */
        FLAG_TRIGGER_EXTERNAL = 0x00100000,
        /* only support trigger single: one trigger, one image */
        FLAG_TRIGGER_SINGLE = 0x00200000,
        /* support set and get the black level */
        FLAG_BLACKLEVEL = 0x00400000,
        /* support auto focus */
        FLAG_AUTO_FOCUS = 0x00800000,
        /* frame buffer */
        FLAG_BUFFER = 0x01000000,
        /* use very large capacity DDR (Double Data Rate SDRAM) for frame buffer. The capacity is not less than one full frame */
        FLAG_DDR = 0x02000000,
        /* support Conversion Gain mode: HCG, LCG */
        FLAG_CG = 0x04000000,
        /* pixel format, yuv411 */
        FLAG_YUV411 = 0x08000000,
        /* pixel format, yuv422, VUYY */
        FLAG_VUYY = 0x10000000,
        /* pixel format, yuv444 */
        FLAG_YUV444 = 0x20000000,
        /* pixel format, RGB888 */
        FLAG_RGB888 = 0x40000000,
        /* obsolete, same as FLAG_RAW10 */
        [Obsolete("Use FLAG_RAW10")]
        FLAG_BITDEPTH10 = FLAG_RAW10,
        /* obsolete, same as FLAG_RAW12 */
        [Obsolete("Use FLAG_RAW12")]
        FLAG_BITDEPTH12 = FLAG_RAW12,
        /* obsolete, same as FLAG_RAW14 */
        [Obsolete("Use FLAG_RAW14")]
        FLAG_BITDEPTH14 = FLAG_RAW14,
        /* obsolete, same as FLAG_RAW16 */
        [Obsolete("Use FLAG_RAW16")]
        FLAG_BITDEPTH16 = FLAG_RAW16,
        /* pixel format, RAW 8 bits */
        FLAG_RAW8 = 0x80000000,
        /* pixel format, GMCY, 8 bits */
        FLAG_GMCY8 = 0x0000000100000000,
        /* pixel format, GMCY, 12 bits */
        FLAG_GMCY12 = 0x0000000200000000,
        /* pixel format, yuv422, UYVY */
        FLAG_UYVY = 0x0000000400000000,
        /* Conversion Gain: HCG, LCG, HDR */
        FLAG_CGHDR = 0x0000000800000000,
        /* global shutter */
        FLAG_GLOBALSHUTTER = 0x0000001000000000,
        /* support focus motor */
        FLAG_FOCUSMOTOR = 0x0000002000000000,
        /* support precise framerate & bandwidth, see OPTION_PRECISE_FRAMERATE & OPTION_BANDWIDTH */
        FLAG_PRECISE_FRAMERATE = 0x0000004000000000,
        /* support heat to prevent fogging up */
        FLAG_HEAT = 0x0000008000000000,
        /* support low noise mode (Higher signal noise ratio, lower frame rate) */
        FLAG_LOW_NOISE = 0x0000010000000000,
        /* hardware level range, put(get)_LevelRangeV2 */
        FLAG_LEVELRANGE_HARDWARE = 0x0000020000000000,
        /* hardware event, such as exposure start & stop */
        FLAG_EVENT_HARDWARE = 0x0000040000000000,
        /* embedded light source */
        FLAG_LIGHTSOURCE = 0x0000080000000000,
        /* astro filter wheel */
        FLAG_FILTERWHEEL = 0x0000100000000000,
        /* 1 Gigabit GigE */
        FLAG_GIGE = 0x0000200000000000,
        /* 10 Gigabit GigE */
        FLAG_10GIGE = 0x0000400000000000,
        /* 5 Gigabit GigE */
        FLAG_5GIGE = 0x0000800000000000,
        /* 40 Gigabit GigE */
        FLAG_40GIGE = 0x0001000000000000,
        /* astro auto focuser */
        FLAG_AUTOFOCUSER = 0x0002000000000000,
        /* stand alone light source */
        FLAG_LIGHT_SOURCE = 0x0004000000000000,
        /* camera link */
        FLAG_CAMERALINK = 0x0008000000000000,
        /* CXP: CoaXPress */
        FLAG_CXP = 0x0010000000000000,
        /* pixel format, RAW 12bits packed */
        FLAG_RAW12PACK = 0x0020000000000000,
        /* self trigger */
        FLAG_SELFTRIGGER = 0x0040000000000000,
        /* pixel format, RAW 11bits */
        FLAG_RAW11 = 0x0080000000000000,
        /* ghopto sensor */
        FLAG_GHOPTO = 0x0100000000000000,
        /* pixel format, RAW 10bits packed */
        FLAG_RAW10PACK = 0x0200000000000000,
        /* USB 3.2 Gen 2 */
        FLAG_USB32 = 0x0400000000000000,
        /* USB 3.2 Gen 2 camera connected to usb3.0 port */
        FLAG_USB32_OVER_USB30 = 0x0800000000000000,
        /* line scan camera */
        FLAG_LINESCAN = 0x1000000000000000
    };

    public enum eEVENT : uint
    {
        /* exposure time or gain changed */
        EVENT_EXPOSURE = 0x0001,
        /* white balance changed, Temp/Tint mode */
        EVENT_TEMPTINT = 0x0002,
        /* reversed, do not use it */
        EVENT_CHROME = 0x0003,
        /* live image arrived, use PullImage to get this image */
        EVENT_IMAGE = 0x0004,
        /* snap (still) frame arrived, use PullStillImage to get this frame */
        EVENT_STILLIMAGE = 0x0005,
        /* white balance changed, RGB Gain mode */
        EVENT_WBGAIN = 0x0006,
        /* trigger failed */
        EVENT_TRIGGERFAIL = 0x0007,
        /* black balance changed */
        EVENT_BLACK = 0x0008,
        /* flat field correction status changed */
        EVENT_FFC = 0x0009,
        /* dark field correction status changed */
        EVENT_DFC = 0x000a,
        /* roi changed */
        EVENT_ROI = 0x000b,
        /* level range changed */
        EVENT_LEVELRANGE = 0x000c,
        /* auto exposure convergence */
        EVENT_AUTOEXPO_CONV = 0x000d,
        /* auto exposure once mode convergence failed */
        EVENT_AUTOEXPO_CONVFAIL = 0x000e,
        /* fix pattern noise correction status changed */
        EVENT_FPNC = 0x000f,
        /* generic error */
        EVENT_ERROR = 0x0080,
        /* camera disconnected */
        EVENT_DISCONNECTED = 0x0081,
        /* no frame timeout error */
        EVENT_NOFRAMETIMEOUT = 0x0082,
        /* focus positon */
        EVENT_FOCUSPOS = 0x0084,
        /* no packet timeout */
        EVENT_NOPACKETTIMEOUT = 0x0085,
        /* hardware event: exposure start */
        EVENT_EXPO_START = 0x4000,
        /* hardware event: exposure stop */
        EVENT_EXPO_STOP = 0x4001,
        /* hardware event: next trigger allow */
        EVENT_TRIGGER_ALLOW = 0x4002,
        /* hardware event: heartbeat, can be used to monitor whether the camera is alive */
        EVENT_HEARTBEAT = 0x4003,
        /* hardware event: trigger in */
        EVENT_TRIGGER_IN = 0x4004,
        /* restore factory settings */
        EVENT_FACTORY = 0x8001
    };

    /* [RW] = Read/Write; [RO] = Read Only; [WO] = Write Only */
    public enum eOPTION : uint
    {
        /* [RW] no frame timeout: 0 => disable, positive value (>= NOFRAME_TIMEOUT_MIN) => timeout milliseconds. default: disable */
        OPTION_NOFRAME_TIMEOUT = 0x01,
        /* [RW] set the priority of the internal thread which grab data from the usb device.
            Win: iValue: 0 => THREAD_PRIORITY_NORMAL; 1 => THREAD_PRIORITY_ABOVE_NORMAL; 2 => THREAD_PRIORITY_HIGHEST; 3 => THREAD_PRIORITY_TIME_CRITICAL; default: 1; see: https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadpriority
            Linux & macOS: similar to Win
        */
        OPTION_THREAD_PRIORITY = 0x02,
        /* [RW]
           0: RGB mode
           1: RAW mode, read the CMOS or CCD raw data
          -1: RAW mode, the difference from 1 is the execution of FFC, DFC, FPNC, black balance, and white balance
          default value: 0
        */
        OPTION_RAW = 0x04,
        /* [RW] 0 = only one, 1 = continue mode */
        OPTION_HISTOGRAM = 0x05,
        /* [RW] 0 = 8 bits mode, 1 = 16 bits mode */
        OPTION_BITDEPTH = 0x06,
        /* [RW] 0 = turn off the cooling fan, [1, max] = fan speed, set to "-1" means to use default fan speed */
        OPTION_FAN = 0x07,
        /* [RW] 0 = turn off the thermoelectric cooler, 1 = turn on the thermoelectric cooler */
        OPTION_TEC = 0x08,
        /* [RW] 0 = turn off the builtin linear tone mapping, 1 = turn on the builtin linear tone mapping, default value: 1 */
        OPTION_LINEAR = 0x09,
        /* [RW] 0 = turn off the builtin curve tone mapping, 1 = turn on the builtin polynomial curve tone mapping, 2 = logarithmic curve tone mapping, default value: 2 */
        OPTION_CURVE = 0x0a,
        /* [RW] 0 = video mode, 1 = software or simulated trigger mode, 2 = external trigger mode, 3 = external + software trigger, default value = 0 */
        OPTION_TRIGGER = 0x0b,
        /* [RW] 0 => RGB24; 1 => enable RGB48 format when bitdepth > 8; 2 => RGB32; 3 => 8 Bits Grey (only for mono camera); 4 => 16 Bits Grey (only for mono camera when bitdepth > 8); 5 => RGB64 */
        OPTION_RGB = 0x0c,
        /* [RW] enable or disable the builtin color matrix, default value: 1 */
        OPTION_COLORMATIX = 0x0d,
        /* [RW] enable or disable the builtin white balance gain, default value: 1 */
        OPTION_WBGAIN = 0x0e,
        /* [RW] get or set the target temperature of the thermoelectric cooler, in 0.1 degree Celsius. For example, 125 means 12.5 degree Celsius, -35 means -3.5 degree Celsius. Set "-2730" or below means using the default for that model */
        OPTION_TECTARGET = 0x0f,
        /* [RW] auto exposure policy:
                0: Exposure Only
                1: Exposure Preferred
                2: Gain Only
                3: Gain Preferred
                default value: 1
        */
        OPTION_AUTOEXP_POLICY = 0x10,
        /* [RW] limit the frame rate, range=[0, 63], the default value 0 means no limit */
        OPTION_FRAMERATE = 0x11,
        /* [RW] demosaic method for both video and still image: BILINEAR = 0, VNG(Variable Number of Gradients) = 1, PPG(Patterned Pixel Grouping) = 2, AHD(Adaptive Homogeneity Directed) = 3, EA(Edge Aware) = 4, see https://en.wikipedia.org/wiki/Demosaicing
            In terms of CPU usage, EA is the lowest, followed by BILINEAR, and the others are higher.
            default value: 0
        */
        OPTION_DEMOSAIC = 0x12,
        /* [RW] demosaic method for video */
        OPTION_DEMOSAIC_VIDEO = 0x13,
        /* [RW] demosaic method for still image */
        OPTION_DEMOSAIC_STILL = 0x14,
        /* [RW] black level */
        OPTION_BLACKLEVEL = 0x15,
        /* [RW] multithread image processing */
        OPTION_MULTITHREAD = 0x16,
        /* [RW] digital binning
                0x01: (no binning)
                n: (saturating add, n*n), 0x02(2*2), 0x03(3*3), 0x04(4*4), 0x05(5*5), 0x06(6*6), 0x07(7*7), 0x08(8*8). The Bitdepth of the data remains unchanged.
                0x40 | n: (unsaturated add, n*n, works only in RAW mode), 0x42(2*2), 0x43(3*3), 0x44(4*4), 0x45(5*5), 0x46(6*6), 0x47(7*7), 0x48(8*8). The Bitdepth of the data is increased. For example, the original data with bitdepth of 12 will increase the bitdepth by 2 bits and become 14 after 2*2 binning.
                0x80 | n: (average, n*n), 0x02(2*2), 0x03(3*3), 0x04(4*4), 0x05(5*5), 0x06(6*6), 0x07(7*7), 0x08(8*8). The Bitdepth of the data remains unchanged.
            The final image size is rounded down to an even number, such as 640/3 to get 212
        */
        OPTION_BINNING = 0x17,
        /* [RW] rotate clockwise: 0, 90, 180, 270 */
        OPTION_ROTATE = 0x18,
        /* [RW] Conversion Gain:
                0 = LCG
                1 = HCG
                2 = HDR (for camera with flag FLAG_CGHDR)
                2 = MCG (for camera with flag FLAG_GHOPTO)
        */
        OPTION_CG = 0x19,
        /* [RW] pixel format */
        OPTION_PIXEL_FORMAT = 0x1a,
        /* [RW] flat field correction
            set:
                0: disable
                1: enable
                -1: reset
                (0xff000000 | n): set the average number to n, [1~255]
            get:
                (val & 0xff): 0 => disable, 1 => enable, 2 => inited
                ((val & 0xff00) >> 8): sequence
                ((val & 0xff0000) >> 16): average number
        */
        OPTION_FFC = 0x1b,
        /* [RW] the number of the frames that DDR can cache
                1: DDR cache only one frame
                0: Auto:
                    => one for video mode when auto exposure is enabled
                    => full capacity for others
                1: DDR can cache frames to full capacity
        */
        OPTION_DDR_DEPTH = 0x1c,
        /* [RW] dark field correction
            set:
                0: disable
                1: enable
                -1: reset
                (0xff000000 | n): set the average number to n, [1~255]
            get:
                (val & 0xff): 0 => disable, 1 => enable, 2 => inited
                ((val & 0xff00) >> 8): sequence
                ((val & 0xff0000) >> 16): average number
        */
        OPTION_DFC = 0x1d,
        /* [RW] Sharpening: (threshold << 24) | (radius << 16) | strength)
                strength: [0, 500], default: 0 (disable)
                radius: [1, 10]
                threshold: [0, 255]
        */
        OPTION_SHARPENING = 0x1e,
        /* [WO] restore the factory settings */
        OPTION_FACTORY = 0x1f,
        /* [RO] get the current TEC voltage in 0.1V, 59 mean 5.9V */
        OPTION_TEC_VOLTAGE = 0x20,
        /* [RW] TEC maximum voltage in 0.1V */
        OPTION_TEC_VOLTAGE_MAX = 0x21,
        /* [WO] reset usb device, simulate a replug */
        OPTION_DEVICE_RESET = 0x22,
        /* [RW] upsize down:
            1: yes
            0: no
            default: 1 (win), 0 (linux/macos)
        */
        OPTION_UPSIDE_DOWN = 0x23,
        /* [RW] focus positon */
        OPTION_FOCUSPOS = 0x24,
        /* [RW] auto focus mode, see AFMode */
        OPTION_AFMODE = 0x25,
        /* [RO] auto focus status, see AFStaus */
        OPTION_AFSTATUS = 0x27,
        /* [RW] test pattern:
            0: off
            3: monochrome diagonal stripes
            5: monochrome vertical stripes
            7: monochrome horizontal stripes
            9: chromatic diagonal stripes
        */
        OPTION_TESTPATTERN = 0x28,
        /* [RW] threshold of auto exposure, default value: 5, range = [2, 15] */
        OPTION_AUTOEXP_THRESHOLD = 0x29,
        /* [RW] Byte order, BGR or RGB: 0 => RGB, 1 => BGR, default value: 1(Win), 0(macOS, Linux, Android) */
        OPTION_BYTEORDER = 0x2a,
        /* [RW] no packet timeout: 0 => disable, positive value (>= NOPACKET_TIMEOUT_MIN) => timeout milliseconds. default: disable */
        OPTION_NOPACKET_TIMEOUT = 0x2b,
        /* [RO] get the precise frame rate maximum value in 0.1 fps, such as 115 means 11.5 fps. E_NOTIMPL means not supported */
        OPTION_MAX_PRECISE_FRAMERATE = 0x2c,
        /* [RW] precise frame rate current value in 0.1 fps. use OPTION_MAX_PRECISE_FRAMERATE, OPTION_MIN_PRECISE_FRAMERATE to get the range. if the set value is out of range, E_INVALIDARG will be returned */
        OPTION_PRECISE_FRAMERATE = 0x2d,
        /* [RW] bandwidth, [1-100]% */
        OPTION_BANDWIDTH = 0x2e,
        /* [RW] reload the last frame in trigger mode */
        OPTION_RELOAD = 0x2f,
        /* [RW] dedicated thread for callback: 0 => disable, 1 => enable, default: 0 */
        OPTION_CALLBACK_THREAD = 0x30,
        /* [RW] frontend (raw) frame buffer deque length, range: [2, 1024], default: 4
            All the memory will be pre-allocated when the camera starts, so, please attention to memory usage
        */
        OPTION_FRONTEND_DEQUE_LENGTH = 0x31,
        /* [RW] alias of OPTION_FRONTEND_DEQUE_LENGTH */
        OPTION_FRAME_DEQUE_LENGTH = 0x31,
        /* [RO] get the precise frame rate minimum value in 0.1 fps, such as 15 means 1.5 fps */
        OPTION_MIN_PRECISE_FRAMERATE = 0x32,
        /* [RW] sequencer trigger: on/off */
        OPTION_SEQUENCER_ONOFF = 0x33,
        /* [RW] sequencer trigger: number, range = [1, 255] */
        OPTION_SEQUENCER_NUMBER = 0x34,
        /* [RW] sequencer trigger: exposure time, iOption = OPTION_SEQUENCER_EXPOTIME | index, iValue = exposure time
            For example, to set the exposure time of the third group to 50ms, call:
            put_Option(OPTION_SEQUENCER_EXPOTIME | 3, 50000)
        */
        OPTION_SEQUENCER_EXPOTIME = 0x01000000,
        /* [RW] sequencer trigger: exposure gain, iOption = OPTION_SEQUENCER_EXPOGAIN | index, iValue = gain */
        OPTION_SEQUENCER_EXPOGAIN = 0x02000000,
        /* [RW] denoise, strength range: [0, 100], 0 means disable */
        OPTION_DENOISE = 0x35,
        /* [RO] get maximum level: heat to prevent fogging up */
        OPTION_HEAT_MAX = 0x36,
        /* [RW] heat to prevent fogging up */
        OPTION_HEAT = 0x37,
        /* [RW] low noise mode (Higher signal noise ratio, lower frame rate): 1 => enable */
        OPTION_LOW_NOISE = 0x38,
        /* [RO] get power consumption, unit: milliwatt */
        OPTION_POWER = 0x39,
        /* [RW] global reset mode */
        OPTION_GLOBAL_RESET_MODE = 0x3a,
        /* [RO] get the open camera error code */
        OPTION_OPEN_ERRORCODE = 0x3b,
        /* [WO]
            1 = hard flush, discard frames cached by camera DDR (if any)
            2 = soft flush, discard frames cached by swiftcam.dll (if any)
            3 = both flush
            Swiftcam_Flush means 'both flush'
            return the number of soft flushed frames if successful, HRESULT if failed
        */
        OPTION_FLUSH = 0x3d,
        /* [RO] get the number of frames that have been grabbed from the USB but dropped by the software */
        OPTION_NUMBER_DROP_FRAME = 0x3e,
        /* [RW] 0 = when camera is stopped, do not dump configuration automatically
            1 = when camera is stopped, dump configuration automatically
            -1 = explicitly dump configuration once
            default: 1
        */
        OPTION_DUMP_CFG = 0x3f,
        /* [RW] Defect Pixel Correction: 0 => disable, 1 => enable; default: 1 */
        OPTION_DEFECT_PIXEL = 0x40,
        /* [RW] backend (pipelined) frame buffer deque length (Only available in pull mode), range: [2, 1024], default: 3
            All the memory will be pre-allocated when the camera starts, so, please attention to memory usage
        */
        OPTION_BACKEND_DEQUE_LENGTH = 0x41,
        /* [RO] get the light source range, [0 ~ max] */
        OPTION_LIGHTSOURCE_MAX = 0x42,
        /* [RW] light source */
        OPTION_LIGHTSOURCE = 0x43,
        /* [RW] Heartbeat interval in millisecond, range = [HEARTBEAT_MIN, HEARTBEAT_MAX], 0 = disable, default: disable */
        OPTION_HEARTBEAT = 0x44,
        /* [RO] get the current number in frontend deque */
        OPTION_FRONTEND_DEQUE_CURRENT = 0x45,
        /* [RO] get the current number in backend deque */
        OPTION_BACKEND_DEQUE_CURRENT = 0x46,
        /* [RW] enable or disable hardware event: 0 => disable, 1 => enable; default: disable
                (1) iOption = OPTION_EVENT_HARDWARE, master switch for notification of all hardware events
                (2) iOption = OPTION_EVENT_HARDWARE | (event type), a specific type of sub-switch
            Only if both the master switch and the sub-switch of a particular type remain on are actually enabled for that type of event notification.
        */
        OPTION_EVENT_HARDWARE = 0x04000000,
        /* [RO] get the received packet number */
        OPTION_PACKET_NUMBER = 0x47,
        /* [RW] filter wheel slot number */
        OPTION_FILTERWHEEL_SLOT = 0x48,
        /* [RW] filter wheel position:
            set:
                -1: calibrate
                val & 0xff: position between 0 and N-1, where N is the number of filter slots
                (val >> 8) & 0x1: direction, 0 => clockwise spinning, 1 => auto direction spinning
            get:
                -1: in motion
                val: position arrived
        */
        OPTION_FILTERWHEEL_POSITION = 0x49,
        /* [RW] auto exposure percent to average:
            1~99: peak percent average
            0 or 100: full roi average, means "disabled"
        */
        OPTION_AUTOEXPOSURE_PERCENT = 0x4a,
        /* [RW] anti shutter effect: 1 => disable, 0 => disable; default: 0 */
        OPTION_ANTI_SHUTTER_EFFECT = 0x4b,
        /* [RO] get chamber humidity & temperature:
            high 16 bits: humidity, in 0.1%, such as: 325 means humidity is 32.5%
            low 16 bits: temperature, in 0.1 degrees Celsius, such as: 32 means 3.2 degrees Celsius
        */
        OPTION_CHAMBER_HT = 0x4c,
        /* [RO] get environment humidity & temperature */
        OPTION_ENV_HT = 0x4d,
        /* [RW] exposure signal pre-delay, microsecond */
        OPTION_EXPOSURE_PRE_DELAY = 0x4e,
        /* [RW] exposure signal post-delay, microsecond */
        OPTION_EXPOSURE_POST_DELAY = 0x4f,
        /* [RO] get auto exposure convergence status: 1(YES) or 0(NO), -1(PENDING) */
        OPTION_AUTOEXPO_CONV = 0x50,
        /* [RW] auto exposure on trigger mode: 0 => disable, 1 => enable; default: 0 */
        OPTION_AUTOEXPO_TRIGGER = 0x51,
        /* [RW] specified line signal pre-delay, microsecond */
        OPTION_LINE_PRE_DELAY = 0x52,
        /* [RW] specified line signal post-delay, microsecond */
        OPTION_LINE_POST_DELAY = 0x53,
        /* [RO] get the tec maximum voltage range:
            high 16 bits: max
            low 16 bits: min
        */
        OPTION_TEC_VOLTAGE_MAX_RANGE = 0x54,
        /* [RW] high fullwell capacity: 0 => disable, 1 => enable */
        OPTION_HIGH_FULLWELL = 0x55,
        /* [RW] dynamic defect pixel correction:
            dead pixel ratio, t1: (high 16 bits): [0, 100], means: [0.0, 1.0]
            hot pixel ratio, t2: (low 16 bits): [0, 100], means: [0.0, 1.0]
        */
        OPTION_DYNAMIC_DEFECT = 0x56,
        /* [RW] HDR synthesize
            K (high 16 bits): [1, 25500]
            B (low 16 bits): [0, 65535]
            0xffffffff => set to default
        */
        OPTION_HDR_KB = 0x57,
        /* [RW] HDR synthesize
            threshold: [1, 4094]
            0xffffffff => set to default
        */
        OPTION_HDR_THRESHOLD = 0x58,
        /* [RW] For GigE cameras, the application periodically sends heartbeat signals to the camera to keep the connection to the camera alive.
            If the camera doesn't receive heartbeat signals within the time period specified by the heartbeat timeout counter, the camera resets the connection.
            When the application is stopped by the debugger, the application cannot send the heartbeat signals
                0 => auto: when the camera is opened, enable if no debugger is present or disable if debugger is present
                1 => enable
                2 => disable
                default: auto
        */
        OPTION_GIGETIMEOUT = 0x5a,
        /* [RO] get EEPROM size */
        OPTION_EEPROM_SIZE = 0x5b,
        /* [RO] get overclock range: [0, max] */
        OPTION_OVERCLOCK_MAX = 0x5c,
        /* [RW] overclock, default: 0 */
        OPTION_OVERCLOCK = 0x5d,
        /* [WO] reset sensor */
        OPTION_RESET_SENSOR = 0x5e,
        /* [RW] Enable hardware ISP: 0 => auto (disable in RAW mode, otherwise enable), 1 => enable, -1 => disable; default: 0 */
        OPTION_ISP = 0x5f,
        /* [RW] Auto exposure damp: time (thousandths). The larger the damping coefficient, the smoother and slower the exposure time changes */
        OPTION_AUTOEXP_EXPOTIME_DAMP = 0x60,
        /* [RW] Auto exposure damp: gain (thousandths). The larger the damping coefficient, the smoother and slower the gain changes */
        OPTION_AUTOEXP_GAIN_DAMP = 0x61,
        /* [RW] range: [1, 20] */
        OPTION_MOTOR_NUMBER = 0x62,
        /* [RW] range: [1, 702] */
        OPTION_MOTOR_POS = 0x10000000,
        /* [RW] Pseudo: start color, BGR format */
        OPTION_PSEUDO_COLOR_START = 0x63,
        /* [RW] Pseudo: end color, BGR format */
        OPTION_PSEUDO_COLOR_END = 0x64,
        /* [RW] Pseudo:
            -1 => custom: use startcolor & endcolor to generate the colormap
            0 => disable
            1 => spot
            2 => spring
            3 => summer
            4 => autumn
            5 => winter
            6 => bone
            7 => jet
            8 => rainbow
            9 => deepgreen
            10 => ocean
            11 => cool
            12 => hsv
            13 => pink
            14 => hot
            15 => parula
            16 => magma
            17 => inferno
            18 => plasma
            19 => viridis
            20 => cividis
            21 => twilight
            22 => twilight_shifted
            23 => turbo
            24 => red
            25 => green
            26 => blue
        */
        OPTION_PSEUDO_COLOR_ENABLE = 0x65,
        /* [RW] Low Power Consumption: 0 => disable, 1 => enable */
        OPTION_LOW_POWERCONSUMPTION = 0x66,
        /* [RW] Fix Pattern Noise Correction
                set:
                    0: disable
                    1: enable
                    -1: reset
                    (0xff000000 | n): set the average number to n, [1~255]
                get:
                    (val & 0xff): 0 => disable, 1 => enable, 2 => inited
                    ((val & 0xff00) >> 8): sequence
                    ((val & 0xff0000) >> 16): average number
        */
        OPTION_FPNC = 0x67,
        /* [RW] Auto exposure over exposure policy: when overexposed,
                0 => directly reduce the exposure time/gain to the minimum value; or
                1 => reduce exposure time/gain in proportion to current and target brightness.
                n(n>1) => first adjust the exposure time to (maximum automatic exposure time * maximum automatic exposure gain) * n / 1000, and then adjust according to the strategy of 1
            The advantage of policy 0 is that the convergence speed is faster, but there is black screen.
            Policy 1 avoids the black screen, but the convergence speed is slower.
            Default: 0
        */
        OPTION_OVEREXP_POLICY = 0x68,
        /* [RW] Readout mode: 0 = IWR (Integrate While Read), 1 = ITR (Integrate Then Read)
           The working modes of the detector readout circuit can be divided into two types: ITR and IWR. Using the IWR readout mode can greatly increase the frame rate. In the ITR mode, the integration of the (n+1)th frame starts after all the data of the nth frame are read out, while in the IWR mode, the data of the nth frame is read out at the same time when the (n+1)th frame is integrated
        */
        OPTION_READOUT_MODE = 0x69,
        /* [RW] Turn on/off tail Led light: 0 => off, 1 => on; default: on */
        OPTION_TAILLIGHT = 0x6a,
        /* [RW] Load/Save lens state to EEPROM: 0 => load, 1 => save */
        OPTION_LENSSTATE = 0x6b,
        /* [RW] Auto White Balance: continuous mode
               0:  disable (default)
               n>0: every n millisecond(s)
               n<0: every -n frame
        */
        OPTION_AWB_CONTINUOUS = 0x6c,
        /* [RO] TEC target range: min(low 16 bits) = (short)(val & 0xffff), max(high 16 bits) = (short)((val >> 16) & 0xffff) */
        OPTION_TECTARGET_RANGE = 0x6d,
        /* [RW] Correlated Double Sampling: 0~100 */
        OPTION_CDS = 0x6e,
        /* [RW] Low Power Consumption: Enable if exposure time is greater than the set value */
        OPTION_LOW_POWER_EXPOTIME = 0x6f,
        /* [RW] Sensor output offset to zero: 0 => disable, 1 => eanble; default: 0 */
        OPTION_ZERO_OFFSET = 0x70,
        /* [RW] GVCP Timeout: millisecond, range = [3, 75], default: 15
             Unless in very special circumstances, generally no modification is required, just use the default value
        */
        OPTION_GVCP_TIMEOUT = 0x71,
        /* [RW] GVCP Retry: range = [2, 8], default: 4
             Unless in very special circumstances, generally no modification is required, just use the default value
        */
        OPTION_GVCP_RETRY = 0x72,
        /* [RW] GVSP wait percent: range = [0, 100], default = (trigger mode: 100, realtime: 0, other: 1) */
        OPTION_GVSP_WAIT_PERCENT = 0x73,
        /* [WO] Reset to 0: 1 => seq; 2 => timestamp; 3 => both */
        OPTION_RESET_SEQ_TIMESTAMP = 0x74,
        /* [RW] Trigger cancel mode: 0 => no frame, 1 => output frame; default: 0 */
        OPTION_TRIGGER_CANCEL_MODE = 0x75,
        /* [RW] Mechanical shutter: 0 => open, 1 => close; default: 0 */
        OPTION_MECHANICALSHUTTER = 0x76,
        /* [RO] Line-time of sensor in nanosecond */
        OPTION_LINE_TIME = 0x77,
        /* [RW] Zero padding: 0 => high, 1 => low; default: 0 */
        OPTION_ZERO_PADDING = 0x78,
        /* [RO] device uptime in millisecond */
        OPTION_UPTIME = 0x79,
        /* [RW] Bit range: [0, 8] */
        OPTION_BITRANGE = 0x7a,
        /* [RW] Mode of seq & timestamp: 0 => reset to 0 automatically; 1 => never reset automatically; default: 0 */
        OPTION_MODE_SEQ_TIMESTAMP = 0x7b,
        /* [RW] Timed trigger number */
        OPTION_TIMED_TRIGGER_NUM = 0x7c,
        /* [RW] Timed trigger: lower 32 bits of  64-bit integer, nanosecond since epoch (00:00:00 UTC on Thursday, 1 January 1970, see https://en.wikipedia.org/wiki/Unix_time) */
        OPTION_TIMED_TRIGGER_LOW = 0x20000000,
        /* [RW] Timed trigger: high 32 bits. The lower 32 bits must be set first, followed by the higher 32 bits */
        OPTION_TIMED_TRIGGER_HIGH = 0x40000000,
        /* [RW] trigger threshold of auto exposure */
        OPTION_AUTOEXP_THLD_TRIGGER = 0x7d,
        /* [RW] Lane */
        OPTION_LANE = 0x7e,
        /* [RW] Voltage bias */
        OPTION_VOLTAGEBIAS = 0x7f,
        /* [RO] Voltage bias range: min (low 16bits), max (high 16bits) */
        OPTION_VOLTAGEBIAS_RANGE = 0x80,
        /* [RO] */
        OPTION_READ_TIME = 0x81,
        /* [RO] */
        OPTION_ALL_EXPO_TIME = 0x82,
        /* [RO] */
        OPTION_CAMERA_PERIOD = 0x83,
        /* [RO] get the number of frontend deque full */
        OPTION_FRONTEND_FULL = 0x84,
        /* [RO] get the number of backend deque full */
        OPTION_BACKEND_FULL = 0x85,
        /* [RO] gps status: 0 => not supported; -1 => gps device offline; 1 => gps device online */
        OPTION_GPS = 0x86,
        /* [RW] Line length in pixel clock */
        OPTION_LINE_LENGTH = 0x87,
        /* [RW] Scan direction: 0(forward), 1(reverse), 2(alternate) */
        OPTION_SCAN_DIRECTION = 0x88,
        /* [RW] Black level automatic adjustment function: 0: off, 1: on
            This setting turn on/off black level auto adjust function by OB(Optical Black) level.
            In case of long exposure and so on, OB level is offset by leak or any other reason.
            Because of it, if the adjustment becomes a problem, this setting is introduced for one of the solution.
        */
        OPTION_BLACKLEVEL_AUTOADJUST = 0x89,
        /* [RW] user set */
        OPTION_USER_SET = 0x8a,
        /* [RW] digital gain */
        OPTION_DIGITAL_GAIN = 0x1001,
        /* [RW] Anti Blooming */
        OPTION_ANTI_BLOOMING = 0x8b,
        /* [RO} Anti Blooming */
        OPTION_ANTI_BLOOMING_MAX = 0x8c
    };

    /* HRESULT: error code */
    /* Success */
    public const int S_OK = 0x00000000;
    /* Yet another success */ /* Remark: Different from S_OK, such as internal values and user-set values have coincided, equivalent to noop */
    public const int S_FALSE = 0x00000001;
    /* Catastrophic failure */ /* Remark: Generally indicates that the conditions are not met, such as calling put_Option setting some options that do not support modification when the camera is running, and so on */
    public const int E_UNEXPECTED = unchecked((int)0x8000ffff);
    /* Not supported or not implemented */ /* Remark: This feature is not supported on this model of camera */
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    /* Permission denied */ /* Remark: The program on Linux does not have permission to open the USB device, please enable udev rules file or run as root */
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);
    /* Out of memory */
    public const int E_OUTOFMEMORY = unchecked((int)0x8007000e);
    /* One or more arguments are not valid */
    public const int E_INVALIDARG = unchecked((int)0x80070057);
    /* Pointer that is not valid */ /* Remark: Pointer is NULL */
    public const int E_POINTER = unchecked((int)0x80004003);
    /* Generic failure */
    public const int E_FAIL = unchecked((int)0x80004005);
    /* Call function in the wrong thread */
    public const int E_WRONG_THREAD = unchecked((int)0x8001010e);
    /* Device not functioning */ /* Remark: It is generally caused by hardware errors, such as cable problems, USB port problems, poor contact, insufficient power supply, camera hardware damage, etc */
    public const int E_GEN_FAILURE = unchecked((int)0x8007001f);
    /* The requested resource is in use */ /* Remark: The camera is already in use, such as duplicated opening/starting the camera, or being used by other application, etc */
    public const int E_BUSY = unchecked((int)0x800700aa);
    /* The data necessary to complete this operation is not yet available */ /* Remark: No data is available at this time */
    public const int E_PENDING = unchecked((int)0x8000000a);
    /* This operation returned because the timeout period expired */
    public const int E_TIMEOUT = unchecked((int)0x8001011f);
    /* Network is unreachable */
    public const int E_UNREACH = unchecked((int)0x80072743);
    /* The operation was canceled by the user */
    public const int E_CANCELLED = unchecked((int)0x800704c7);

    public const int EXPOGAIN_DEF = 100;      /* exposure gain, default value */
    public const int EXPOGAIN_MIN = 100;      /* exposure gain, minimum value */
    public const int TEMP_DEF = 6503;     /* color temperature, default value */
    public const int TEMP_MIN = 2000;     /* color temperature, minimum value */
    public const int TEMP_MAX = 15000;    /* color temperature, maximum value */
    public const int TINT_DEF = 1000;     /* tint */
    public const int TINT_MIN = 200;      /* tint */
    public const int TINT_MAX = 2500;     /* tint */
    public const int HUE_DEF = 0;        /* hue */
    public const int HUE_MIN = -180;     /* hue */
    public const int HUE_MAX = 180;      /* hue */
    public const int SATURATION_DEF = 128;      /* saturation */
    public const int SATURATION_MIN = 0;        /* saturation */
    public const int SATURATION_MAX = 255;      /* saturation */
    public const int BRIGHTNESS_DEF = 0;        /* brightness */
    public const int BRIGHTNESS_MIN = -255;     /* brightness */
    public const int BRIGHTNESS_MAX = 255;      /* brightness */
    public const int CONTRAST_DEF = 0;        /* contrast */
    public const int CONTRAST_MIN = -255;     /* contrast */
    public const int CONTRAST_MAX = 255;      /* contrast */
    public const int GAMMA_DEF = 100;      /* gamma */
    public const int GAMMA_MIN = 20;       /* gamma */
    public const int GAMMA_MAX = 180;      /* gamma */
    public const int AETARGET_DEF = 120;      /* target of auto exposure */
    public const int AETARGET_MIN = 16;       /* target of auto exposure */
    public const int AETARGET_MAX = 220;      /* target of auto exposure */
    public const int WBGAIN_DEF = 0;        /* white balance gain */
    public const int WBGAIN_MIN = -127;     /* white balance gain */
    public const int WBGAIN_MAX = 127;      /* white balance gain */
    public const int BLACKLEVEL_MIN = 0;        /* minimum black level */
    public const int BLACKLEVEL8_MAX = 31;       /* maximum black level for bitdepth = 8 */
    public const int BLACKLEVEL10_MAX = 31 * 4;   /* maximum black level for bitdepth = 10 */
    public const int BLACKLEVEL11_MAX = 31 * 8;   /* maximum black level for bitdepth = 11 */
    public const int BLACKLEVEL12_MAX = 31 * 16;  /* maximum black level for bitdepth = 12 */
    public const int BLACKLEVEL14_MAX = 31 * 64;  /* maximum black level for bitdepth = 14 */
    public const int BLACKLEVEL16_MAX = 31 * 256; /* maximum black level for bitdepth = 16 */
    public const int SHARPENING_STRENGTH_DEF = 0;        /* sharpening strength */
    public const int SHARPENING_STRENGTH_MIN = 0;        /* sharpening strength */
    public const int SHARPENING_STRENGTH_MAX = 500;      /* sharpening strength */
    public const int SHARPENING_RADIUS_DEF = 2;        /* sharpening radius */
    public const int SHARPENING_RADIUS_MIN = 1;        /* sharpening radius */
    public const int SHARPENING_RADIUS_MAX = 10;       /* sharpening radius */
    public const int SHARPENING_THRESHOLD_DEF = 0;        /* sharpening threshold */
    public const int SHARPENING_THRESHOLD_MIN = 0;        /* sharpening threshold */
    public const int SHARPENING_THRESHOLD_MAX = 255;      /* sharpening threshold */
    public const int AUTOEXPO_THRESHOLD_DEF = 5;        /* auto exposure threshold */
    public const int AUTOEXPO_THRESHOLD_MIN = 2;        /* auto exposure threshold */
    public const int AUTOEXPO_THRESHOLD_MAX = 15;       /* auto exposure threshold */
    public const int AUTOEXPO_THLD_TRIGGER_DEF = 5;     /* auto exposure trigger threshold */
    public const int AUTOEXPO_THLD_TRIGGER_MIN = 2;     /* auto exposure trigger threshold */
    public const int AUTOEXPO_THLD_TRIGGER_MAX = 64;    /* auto exposure trigger threshold */
    public const int AUTOEXPO_DAMP_DEF = 0;        /* auto exposure damping coefficient: thousandths */
    public const int AUTOEXPO_DAMP_MIN = 0;        /* auto exposure damping coefficient: thousandths */
    public const int AUTOEXPO_DAMP_MAX = 1000;     /* auto exposure damping coefficient: thousandths */
    public const int BANDWIDTH_DEF = 100;      /* bandwidth */
    public const int BANDWIDTH_MIN = 1;        /* bandwidth */
    public const int BANDWIDTH_MAX = 100;      /* bandwidth */
    public const int DENOISE_DEF = 0;        /* denoise */
    public const int DENOISE_MIN = 0;        /* denoise */
    public const int DENOISE_MAX = 100;      /* denoise */
    public const int HEARTBEAT_MIN = 100;      /* millisecond */
    public const int HEARTBEAT_MAX = 10000;    /* millisecond */
    public const int AE_PERCENT_MIN = 0;        /* auto exposure percent; 0 or 100 => full roi average, means "disabled" */
    public const int AE_PERCENT_MAX = 100;
    public const int AE_PERCENT_DEF = 1;       /* auto exposure percent: enabled, percentage = 1% */
    public const int NOPACKET_TIMEOUT_MIN = 500;      /* no packet timeout minimum: 500ms */
    public const int NOFRAME_TIMEOUT_MIN = 500;       /* no frame timeout minimum: 500ms */
    public const int DYNAMIC_DEFECT_T1_MIN = 0;       /* dynamic defect pixel correction, dead pixel ratio: the smaller the dead ratio is, the more stringent the conditions for processing dead pixels are, and fewer pixels will be processed */
    public const int DYNAMIC_DEFECT_T1_MAX = 100;     /* means: 1.0 */
    public const int DYNAMIC_DEFECT_T1_DEF = 90;      /* means: 0.9 */
    public const int DYNAMIC_DEFECT_T2_MIN = 0;       /* dynamic defect pixel correction, hot pixel ratio: the smaller the hot ratio is, the more stringent the conditions for processing hot pixels are, and fewer pixels will be processed */
    public const int DYNAMIC_DEFECT_T2_MAX = 100;
    public const int DYNAMIC_DEFECT_T2_DEF = 90;
    public const int HDR_K_MIN = 1;        /* HDR synthesize */
    public const int HDR_K_MAX = 25500;
    public const int HDR_B_MIN = 0;
    public const int HDR_B_MAX = 65535;
    public const int HDR_THRESHOLD_MIN = 0;
    public const int HDR_THRESHOLD_MAX = 4094;
    public const int CDS_MIN = 0;        /* Correlated Double Sampling */
    public const int CDS_MAX = 100;

    public enum ePIXELFORMAT : uint
    {
        PIXELFORMAT_RAW8 = 0x00,
        PIXELFORMAT_RAW10 = 0x01,
        PIXELFORMAT_RAW12 = 0x02,
        PIXELFORMAT_RAW14 = 0x03,
        PIXELFORMAT_RAW16 = 0x04,
        PIXELFORMAT_YUV411 = 0x05,
        PIXELFORMAT_VUYY = 0x06,
        PIXELFORMAT_YUV444 = 0x07,
        PIXELFORMAT_RGB888 = 0x08,
        /* map to RGGB 8 bits */
        PIXELFORMAT_GMCY8 = 0x09,
        /* map to RGGB 12 bits */
        PIXELFORMAT_GMCY12 = 0x0a,
        PIXELFORMAT_UYVY = 0x0b,
        PIXELFORMAT_RAW12PACK = 0x0c,
        PIXELFORMAT_RAW11 = 0x0d,
        /* HDR, Bitdepth: 8, Conversion Gain: High + Low */
        PIXELFORMAT_HDR8HL = 0x0e,
        /* HDR, Bitdepth: 10, Conversion Gain: High + Low */
        PIXELFORMAT_HDR10HL = 0x0f,
        /* HDR, Bitdepth: 11, Conversion Gain: High + Low */
        PIXELFORMAT_HDR11HL = 0x10,
        /* HDR, Bitdepth: 12, Conversion Gain: High + Low */
        PIXELFORMAT_HDR12HL = 0x11,
        /* HDR, Bitdepth: 14, Conversion Gain: High + Low */
        PIXELFORMAT_HDR14HL = 0x12,
        PIXELFORMAT_RAW10PACK = 0x13
    };

    [Flags]
    public enum eFRAMEINFO_FLAG : uint
    {
        /* frame sequence number */
        FRAMEINFO_FLAG_SEQ = 0x00000001,
        /* timestamp */
        FRAMEINFO_FLAG_TIMESTAMP = 0x00000002,
        /* exposure time */
        FRAMEINFO_FLAG_EXPOTIME = 0x00000004,
        /* exposure gain */
        FRAMEINFO_FLAG_EXPOGAIN = 0x00000008,
        /* black level */
        FRAMEINFO_FLAG_BLACKLEVEL = 0x00000010,
        /* sequence shutter counter */
        FRAMEINFO_FLAG_SHUTTERSEQ = 0x00000020,
        /* GPS */
        FRAMEINFO_FLAG_GPS = 0x00000040,
        /* auto focus: uLum & uFV */
        FRAMEINFO_FLAG_AUTOFOCUS = 0x00000080,
        /* timecount, framecount, tricount */
        FRAMEINFO_FLAG_COUNT = 0x00000100,
        /* Mechanical shutter: closed */
        FRAMEINFO_FLAG_MECHANICALSHUTTER = 0x00000200,
        /* still image */
        FRAMEINFO_FLAG_STILL = 0x00008000,
        /* Conversion Gain: High */
        FRAMEINFO_FLAG_CG = 0x00010000
    };

    public enum eIoControType : uint
    {
        /* 0x01 => Input, 0x02 => Output, (0x01 | 0x02) => support both Input and Output */
        IOCONTROLTYPE_GET_SUPPORTEDMODE = 0x01,
        /* 0x00 => Input, 0x01 => Output */
        IOCONTROLTYPE_GET_GPIODIR = 0x03,
        IOCONTROLTYPE_SET_GPIODIR = 0x04,
        /*  0x00 => not connected
            0x01 => Tri-state: Tri-state mode (Not driven)
            0x02 => TTL: TTL level signals
            0x03 => LVDS: LVDS level signals
            0x04 => RS422: RS422 level signals
            0x05 => Opto-coupled
        */
        IOCONTROLTYPE_GET_FORMAT = 0x05,
        IOCONTROLTYPE_SET_FORMAT = 0x06,
        /* boolean, only support output signal */
        IOCONTROLTYPE_GET_OUTPUTINVERTER = 0x07,
        IOCONTROLTYPE_SET_OUTPUTINVERTER = 0x08,
        /* 0x00 => Rising edge, 0x01 => Falling edge, 0x02 => Level high, 0x03 => Level low */
        IOCONTROLTYPE_GET_INPUTACTIVATION = 0x09,
        IOCONTROLTYPE_SET_INPUTACTIVATION = 0x0a,
        /* debouncer time in microseconds, range: [0, 20000] */
        IOCONTROLTYPE_GET_DEBOUNCERTIME = 0x0b,
        IOCONTROLTYPE_SET_DEBOUNCERTIME = 0x0c,
        /*  0x00 => Opto-isolated input
            0x01 => GPIO0
            0x02 => GPIO1
            0x03 => Counter
            0x04 => PWM
            0x05 => Software
        */
        IOCONTROLTYPE_GET_TRIGGERSOURCE = 0x0d,
        IOCONTROLTYPE_SET_TRIGGERSOURCE = 0x0e,
        /* Trigger delay time in microseconds, range: [0, 5000000] */
        IOCONTROLTYPE_GET_TRIGGERDELAY = 0x0f,
        IOCONTROLTYPE_SET_TRIGGERDELAY = 0x10,
        /* Burst Counter, range: [1 ~ 65535] */
        IOCONTROLTYPE_GET_BURSTCOUNTER = 0x11,
        IOCONTROLTYPE_SET_BURSTCOUNTER = 0x12,
        /* 0x00 => Opto-isolated input, 0x01 => GPIO0, 0x02=> GPIO1 */
        IOCONTROLTYPE_GET_COUNTERSOURCE = 0x13,
        IOCONTROLTYPE_SET_COUNTERSOURCE = 0x14,
        /* Counter Value, range: [1 ~ 65535] */
        IOCONTROLTYPE_GET_COUNTERVALUE = 0x15,
        IOCONTROLTYPE_SET_COUNTERVALUE = 0x16,
        IOCONTROLTYPE_SET_RESETCOUNTER = 0x18,
        /* PWM Frequency */
        IOCONTROLTYPE_GET_PWM_FREQ = 0x19,
        IOCONTROLTYPE_SET_PWM_FREQ = 0x1a,
        /* PWM Duty Ratio */
        IOCONTROLTYPE_GET_PWM_DUTYRATIO = 0x1b,
        IOCONTROLTYPE_SET_PWM_DUTYRATIO = 0x1c,
        /* PWM Source: 0x00 => Opto-isolated input, 0x01 => GPIO0, 0x02 => GPIO1 */
        IOCONTROLTYPE_GET_PWMSOURCE = 0x1d,
        IOCONTROLTYPE_SET_PWMSOURCE = 0x1e,
        /*  0x00 => Frame Trigger Wait
            0x01 => Exposure Active
            0x02 => Strobe
            0x03 => User output
            0x04 => Counter Output
            0x05 => Timer Output
        */
        IOCONTROLTYPE_GET_OUTPUTMODE = 0x1f,
        IOCONTROLTYPE_SET_OUTPUTMODE = 0x20,
        /* boolean, 1 => delay, 0 => pre-delay; compared to exposure active signal */
        IOCONTROLTYPE_GET_STROBEDELAYMODE = 0x21,
        IOCONTROLTYPE_SET_STROBEDELAYMODE = 0x22,
        /* Strobe delay or pre-delay time in microseconds, range: [0, 5000000] */
        IOCONTROLTYPE_GET_STROBEDELAYTIME = 0x23,
        IOCONTROLTYPE_SET_STROBEDELAYTIME = 0x24,
        /* Strobe duration time in microseconds, range: [0, 5000000] */
        IOCONTROLTYPE_GET_STROBEDURATION = 0x25,
        IOCONTROLTYPE_SET_STROBEDURATION = 0x26,
        /*  bit0 => Opto-isolated output
            bit1 => GPIO0 output
            bit2 => GPIO1 output
        */
        IOCONTROLTYPE_GET_USERVALUE = 0x27,
        IOCONTROLTYPE_SET_USERVALUE = 0x28,
        /* enable: 1 => on; 0 => off */
        IOCONTROLTYPE_GET_UART_ENABLE = 0x29,
        IOCONTROLTYPE_SET_UART_ENABLE = 0x2a,
        /* baud rate: 0 => 9600; 1 => 19200; 2 => 38400; 3 => 57600; 4 => 115200 */
        IOCONTROLTYPE_GET_UART_BAUDRATE = 0x2b,
        IOCONTROLTYPE_SET_UART_BAUDRATE = 0x2c,
        /* line mode: 0 => TX(GPIO_0)/RX(GPIO_1); 1 => TX(GPIO_1)/RX(GPIO_0) */
        IOCONTROLTYPE_GET_UART_LINEMODE = 0x2d,
        IOCONTROLTYPE_SET_UART_LINEMODE = 0x2e,
        /* exposure time signal: 0 => specified line, 1 => common exposure time */
        IOCONTROLTYPE_GET_EXPO_ACTIVE_MODE = 0x2f,
        IOCONTROLTYPE_SET_EXPO_ACTIVE_MODE = 0x30,
        /* exposure start line, default: 0 */
        IOCONTROLTYPE_GET_EXPO_START_LINE = 0x31,
        IOCONTROLTYPE_SET_EXPO_START_LINE = 0x32,
        /*  exposure end line, default: 0
            end line must be no less than start line
        */
        IOCONTROLTYPE_GET_EXPO_END_LINE = 0x33,
        IOCONTROLTYPE_SET_EXPO_END_LINE = 0x34,
        /* exposure event: 0 => specified line, 1 => common exposure time */
        IOCONTROLTYPE_GET_EXEVT_ACTIVE_MODE = 0x35,
        IOCONTROLTYPE_SET_EXEVT_ACTIVE_MODE = 0x36,
        /* Output Counter Value, range: [0 ~ 65535] */
        IOCONTROLTYPE_GET_OUTPUTCOUNTERVALUE = 0x37,
        IOCONTROLTYPE_SET_OUTPUTCOUNTERVALUE = 0x38,
        /* Output pause: 1 => puase, 0 => unpause */
        IOCONTROLTYPE_SET_OUTPUT_PAUSE = 0x3a,
        /* Input state: 0 (low level) or 1 (high level) */
        IOCONTROLTYPE_GET_INPUT_STATE = 0x3b,
        /* User pulse high level time: us */
        IOCONTROLTYPE_GET_USER_PULSE_HIGH = 0x3d,
        IOCONTROLTYPE_SET_USER_PULSE_HIGH = 0x3e,
        /* User pulse low level time: us */
        IOCONTROLTYPE_GET_USER_PULSE_LOW = 0x3f,
        IOCONTROLTYPE_SET_USER_PULSE_LOW = 0x40,
        /* User pulse number: default 0 */
        IOCONTROLTYPE_GET_USER_PULSE_NUMBER = 0x41,
        IOCONTROLTYPE_SET_USER_PULSE_NUMBER = 0x42,
        /* External trigger number */
        IOCONTROLTYPE_GET_EXTERNAL_TRIGGER_NUMBER = 0x43,
        /* Trigger signal number after debounce */
        IOCONTROLTYPE_GET_DEBOUNCER_TRIGGER_NUMBER = 0x45,
        /* Effective trigger signal number */
        IOCONTROLTYPE_GET_EFFECTIVE_TRIGGER_NUMBER = 0x47
    };

    public const int IOCONTROL_DELAYTIME_MAX = 5 * 1000 * 1000;

    /* AAF: Astro Auto Focuser */
    public enum eAAF : int
    {
        AAF_SETPOSITION = 0x01,
        AAF_GETPOSITION = 0x02,
        AAF_SETZERO = 0x03,
        AAF_SETDIRECTION = 0x05,
        AAF_GETDIRECTION = 0x06,
        AAF_SETMAXINCREMENT = 0x07,
        AAF_GETMAXINCREMENT = 0x08,
        AAF_SETFINE = 0x09,
        AAF_GETFINE = 0x0a,
        AAF_SETCOARSE = 0x0b,
        AAF_GETCOARSE = 0x0c,
        AAF_SETBUZZER = 0x0d,
        AAF_GETBUZZER = 0x0e,
        AAF_SETBACKLASH = 0x0f,
        AAF_GETBACKLASH = 0x10,
        AAF_GETAMBIENTTEMP = 0x12,
        /* in 0.1 degrees Celsius, such as: 32 means 3.2 degrees Celsius */
        AAF_GETTEMP = 0x14,
        AAF_ISMOVING = 0x16,
        AAF_HALT = 0x17,
        AAF_SETMAXSTEP = 0x1b,
        AAF_GETMAXSTEP = 0x1c,
        AAF_GETSTEPSIZE = 0x1e,
        /* Range: min value */
        AAF_RANGEMIN = 0xfd,
        /* Range: max value */
        AAF_RANGEMAX = 0xfe,
        /* Range: default value */
        AAF_RANGEDEF = 0xff
    };

    /* hardware level range mode */
    public enum eLevelRange : ushort
    {
        /* manual */
        LEVELRANGE_MANUAL = 0x0000,
        /* once */
        LEVELRANGE_ONCE = 0x0001,
        /* continue */
        LEVELRANGE_CONTINUE = 0x0002,
        /* update roi rect only */
        LEVELRANGE_ROI = 0xffff
    };

    public struct Resolution
    {
        public uint width;
        public uint height;
    };
    public struct ModelV2
    {
        public string name;         /* model name */
        public ulong flag;          /* FLAG_xxx, 64 bits */
        public uint maxspeed;       /* number of speed level, same as get_MaxSpeed(), the speed range = [0, maxspeed], closed interval */
        public uint preview;        /* number of preview resolution, same as get_ResolutionNumber() */
        public uint still;          /* number of still resolution, same as get_StillResolutionNumber() */
        public uint maxfanspeed;    /* maximum fan speed, fan speed range = [0, max], closed interval */
        public uint ioctrol;        /* number of input/output control */
        public float xpixsz;        /* physical pixel size in micrometer */
        public float ypixsz;        /* physical pixel size in micrometer */
        public Resolution[] res;
    };
    public struct DeviceV2
    {
        public string displayname; /* display name */
        public string id;          /* unique and opaque id of a connected camera */
        public ModelV2 model;
    };
    [StructLayout(LayoutKind.Sequential)]
    public struct FrameInfoV3
    {
        public uint width;
        public uint height;
        public uint flag;          /* FRAMEINFO_FLAG_xxxx */
        public uint seq;           /* frame sequence number */
        public ulong timestamp;    /* microsecond */
        public uint shutterseq;    /* sequence shutter counter */
        public uint expotime;      /* exposure time */
        public ushort expogain;    /* exposure gain */
        public ushort blacklevel;  /* black level */
    };
    [StructLayout(LayoutKind.Sequential)]
    public struct Gps
    {
        public ulong utcstart;    /* exposure start time: nanosecond since epoch (00:00:00 UTC on Thursday, 1 January 1970, see https://en.wikipedia.org/wiki/Unix_time) */
        public ulong utcend;      /* exposure end time */
        public int longitude;     /* millionth of a degree, 0.000001 degree */
        public int latitude;
        public int altitude;      /* millimeter */
        public ushort satellite;  /* number of satellite */
        public ushort reserved;   /* not used */
    };
    [StructLayout(LayoutKind.Sequential)]
    public struct FrameInfoV4
    {
        public FrameInfoV3 v3;
        public uint reserved; /* not used */
        public uint uLum;
        public ulong uFV;
        public ulong timecount;
        public uint framecount, tricount;
        public Gps gps;
    };
    [StructLayout(LayoutKind.Sequential), Obsolete("Use FrameInfoV4")]
    public struct FrameInfoV2
    {
        public uint width;
        public uint height;
        public uint flag;          /* FRAMEINFO_FLAG_xxxx */
        public uint seq;           /* frame sequence number */
        public ulong timestamp;    /* microsecond */
    };
    [StructLayout(LayoutKind.Sequential)]
    public struct SelfTrigger
    {
        public uint sensingLeft, sensingTop, sensingWidth, sensingHeight; /* Sensing Area */
        public uint hThreshold, lThreshold; /* threshold High side, threshold Low side */
        public uint expoTime;   /* Exposure Time */
        public ushort expoGain; /* Exposure Gain */
        public ushort hCount, lCount; /* Count threshold High side, Count threshold Low side, thousandths of Sensing Area */
        public ushort reserved;
    };
    public struct LensInfo
    {
        public ushort lensID;
        public byte lensType;
        public byte statusAfmf;      /* LENS_AF = 0x00, LENS_MF = 0x80 */

        public ushort maxFocalLength;
        public ushort curFocalLength;
        public ushort minFocalLength;

        public short farFM;           /* focus motor, absolute value */
        public short curFM;           /* current focus motor */
        public short nearFM;

        public ushort maxFocusDistance;
        public ushort minFocusDistance;

        public sbyte curAM;
        public byte maxAM;           /* maximum Aperture, mimimum F# */
        public byte minAM;           /* mimimum Aperture, maximum F# */
        public byte posAM;           /* used for set aperture motor to posAM, it is an index */
        public int posFM;            /* used for set focus motor to posFM */

        public string[] arrayFN;
        public string lensName;
    };
    public enum eAFMode : uint
    {
        AFMode_CALIBRATE = 0x0, /* lens calibration mode */
        AFMode_MANUAL = 0x1,    /* manual focus mode */
        AFMode_ONCE = 0x2,      /* onepush focus mode */
        AFMode_AUTO = 0x3,      /* autofocus mode */
        AFMode_NONE = 0x4,      /* no active selection of focus mode */
        AFMode_IDLE = 0x5
    };
    public enum eAFStatus : uint
    {
        AFStatus_NA = 0x0,           /* Not available */
        AFStatus_PEAKPOINT = 0x1,    /* Focus completed, find the focus position */
        AFStatus_DEFOCUS = 0x2,      /* End of focus, defocus */
        AFStatus_NEAR = 0x3,         /* Focusing ended, object too close */
        AFStatus_FAR = 0x4,          /* Focusing ended, object too far */
        AFStatus_ROICHANGED = 0x5,   /* Focusing ends, roi changes */
        AFStatus_SCENECHANGED = 0x6, /* Focusing ends, scene changes */
        AFStatus_MODECHANGED = 0x7,  /* The end of focusing and the change in focusing mode is usually determined by the user moderator */
        AFStatus_UNFINISH = 0x8      /* The focus is not complete. At the beginning of focusing, it will be set as incomplete */
    };
    [StructLayout(LayoutKind.Sequential)]
    public struct AFState
    {
        public eAFMode AF_Mode;
        public eAFStatus AF_Status;
        public byte AF_LensAP_Update_Flag;  /* mark for whether the lens aperture is calibrated */
        public byte AF_LensManual_Flag;     /* if true, allows manual operation */
        public byte Reserved0;
        public byte Reserved1;
    };
    [StructLayout(LayoutKind.Sequential)]
    public struct FocusMotor
    {
        public int imax;           /* maximum auto focus sensor board positon */
        public int imin;           /* minimum auto focus sensor board positon */
        public int idef;           /* conjugate calibration positon */
        public int imaxabs;        /* maximum absolute auto focus sensor board positon, micrometer */
        public int iminabs;        /* maximum absolute auto focus sensor board positon, micrometer */
        public int zoneh;          /* zone horizontal */
        public int zonev;          /* zone vertical */
    };
    [Obsolete("Use ModelV2")]
    public struct Model
    {
        public string name;
        public uint flag;
        public uint maxspeed;
        public uint preview;
        public uint still;
        public Resolution[] res;
    };
    [Obsolete("Use DeviceV2")]
    public struct Device
    {
        public string displayname; /* display name */
        public string id;          /* unique and opaque id of a connected camera */
        public Model model;
    };

#if !(NETFX_CORE || NETCOREAPP || WINDOWS_UWP)
    [DllImport("ntdll.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
    public static extern void memcpy(IntPtr dest, IntPtr src, IntPtr count);
#endif
    static public uint MAKEFOURCC(uint a, uint b, uint c, uint d)
    {
        return ((uint)(byte)(a) | ((uint)(byte)(b) << 8) | ((uint)(byte)(c) << 16) | ((uint)(byte)(d) << 24));
    }

    static public int TDIBWIDTHBYTES(int bits)
    {
        return ((bits + 31) & (~31)) / 8;
    }

    /* only for compatibility with .Net 4.0 and below */
    public static IntPtr IncIntPtr(IntPtr p, int offset)
    {
        return new IntPtr(p.ToInt64() + offset);
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()  // Follow the Dispose pattern - public nonvirtual.
    {
        Dispose(true);
        map_.Remove(id_.ToInt32());
        GC.SuppressFinalize(this);
    }

    /* get the version of this dll/so, which is: 59.29807.20251021 */
    public static string Version()
    {
        if (IsWindows())
            return Marshal.PtrToStringUni(Swiftcam_Version());
        else
            return Marshal.PtrToStringAnsi(Swiftcam_Version());
    }

    private static DeviceV2[] Ptr2Device(IntPtr p, uint cnt)
    {
        IntPtr ti = p;
        DeviceV2[] arr = new DeviceV2[cnt];
        if (cnt > 0) {
            for (uint i = 0; i < cnt; ++i) {
                if (IsWindows()) {
                    arr[i].displayname = Marshal.PtrToStringUni(p);
                    p = IncIntPtr(p, sizeof(char) * 64);
                    arr[i].id = Marshal.PtrToStringUni(p);
                    p = IncIntPtr(p, sizeof(char) * 64);
                }
                else {
                    arr[i].displayname = Marshal.PtrToStringAnsi(p);
                    p = IncIntPtr(p, 64);
                    arr[i].id = Marshal.PtrToStringAnsi(p);
                    p = IncIntPtr(p, 64);
                }

                IntPtr q = Marshal.ReadIntPtr(p);
                p = IncIntPtr(p, IntPtr.Size);
                arr[i].model = toModelV2(q);
            }
        }
        Marshal.FreeHGlobal(ti);
        return arr;
    }

    /* enumerate Swiftcam cameras that are currently connected to computer */
    public static DeviceV2[] EnumV2()
    {
        IntPtr p = Marshal.AllocHGlobal(512 * 128);
        return Ptr2Device(p, Swiftcam_EnumV2(p));
    }

    public static DeviceV2[] EnumWithName()
    {
        IntPtr p = Marshal.AllocHGlobal(512 * 128);
        return Ptr2Device(p, Swiftcam_EnumWithName(p));
    }

    /* Initialize support for GigE cameras. If online/offline notifications are not required, the callback function can be set to null */
    public static int GigeEnable(DelegateHotplug funHotplug)
    {
        return GigeEnable(funHotplug, null);
    }

    /* opt: semicolon separated options:
    *        "wifi": Enable WiFi adapter support
    */
    public static int GigeEnable(DelegateHotplug funHotplug, string opt)
    {
        HOTPLUG_CALLBACK pHotplug = delegate (IntPtr ctxHotplug)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxHotplug.ToInt32(), out obj) && (obj != null)) {
                DelegateHotplug p = obj as DelegateHotplug;
                if (p != null)
                    p();
            }
        };
        IntPtr id = new IntPtr(Interlocked.Increment(ref sid_));
        map_.Add(id.ToInt32(), funHotplug);
        if (string.IsNullOrEmpty(opt))
            return Swiftcam_GigeEnable(pHotplug, id);
        else
            return Swiftcam_GigeEnableV2(pHotplug, id, opt);
    }
    
    /* Initialize support for PCIe cameras. If online/offline notifications are not required, the callback function can be set to null */
    public static int PciEnable(DelegateHotplug funHotplug)
    {
        HOTPLUG_CALLBACK pHotplug = delegate (IntPtr ctxHotplug)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxHotplug.ToInt32(), out obj) && (obj != null)) {
                DelegateHotplug p = obj as DelegateHotplug;
                if (p != null)
                    p();
            }
        };
        IntPtr id = new IntPtr(Interlocked.Increment(ref sid_));
        map_.Add(id.ToInt32(), funHotplug);
        return Swiftcam_PciEnable(pHotplug, id);
    }
    
    /* Initialize support for *.cti cameras. If online/offline notifications are not required, the callback function can be set to null
    * (1) ctiPath = null means all *.cti in GENICAM_GENTL64_PATH/GENICAM_GENTL32_PATH
    * or
    * (2) string[] ctiPath = {
                "/full/path/to/file.cti",
                ...
            }
    */
    public static int CtiEnable(DelegateHotplug funHotplug, string[] ctiPath)
    {
        StringHandle[] HandleArray = null;
        IntPtr[] PtrArray = null;
        if (ctiPath != null && ctiPath.Length > 0) {
            HandleArray = new StringHandle[ctiPath.Length];
            for (int i = 0; i < ctiPath.Length; ++i)
                HandleArray[i] = new StringHandle(ctiPath[i]);
            PtrArray = new IntPtr[ctiPath.Length + 1];
            for (int i = 0; i < ctiPath.Length; ++i)
                PtrArray[i] = HandleArray[i].Get();
        }
        HOTPLUG_CALLBACK pHotplug = delegate (IntPtr ctxHotplug)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxHotplug.ToInt32(), out obj) && (obj != null)) {
                DelegateHotplug p = obj as DelegateHotplug;
                if (p != null)
                    p();
            }
        };
        IntPtr id = new IntPtr(Interlocked.Increment(ref sid_));
        map_.Add(id.ToInt32(), funHotplug);
        if (PtrArray == null)
            return Swiftcam_CtiEnable(pHotplug, id, IntPtr.Zero);
        else {
            GCHandle gch = GCHandle.Alloc(PtrArray, GCHandleType.Pinned);
            try {
                return Swiftcam_CtiEnable(pHotplug, id, gch.AddrOfPinnedObject());
            }
            finally {
                gch.Free();
            }
        }
    }

    [Obsolete("Use EnumV2")]
    public static Device[] Enum()
    {
        IntPtr p = Marshal.AllocHGlobal(512 * 128);
        IntPtr ti = p;
        uint cnt = Swiftcam_Enum(p);
        Device[] arr = new Device[cnt];
        if (cnt > 0) {
            for (uint i = 0; i < cnt; ++i) {
                if (IsWindows()) {
                    arr[i].displayname = Marshal.PtrToStringUni(p);
                    p = IncIntPtr(p, sizeof(char) * 64);
                    arr[i].id = Marshal.PtrToStringUni(p);
                    p = IncIntPtr(p, sizeof(char) * 64);
                }
                else {
                    arr[i].displayname = Marshal.PtrToStringAnsi(p);
                    p = IncIntPtr(p, 64);
                    arr[i].id = Marshal.PtrToStringAnsi(p);
                    p = IncIntPtr(p, 64);
                }

                IntPtr q = Marshal.ReadIntPtr(p);
                p = IncIntPtr(p, IntPtr.Size);

                {
                    if (IsWindows())
                        arr[i].model.name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(q));
                    else
                        arr[i].model.name = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(q));
                    q = IncIntPtr(q, IntPtr.Size);
                    arr[i].model.flag = (uint)Marshal.ReadInt32(q);
                    q = IncIntPtr(q, sizeof(int));
                    arr[i].model.maxspeed = (uint)Marshal.ReadInt32(q);
                    q = IncIntPtr(q, sizeof(int));
                    arr[i].model.preview = (uint)Marshal.ReadInt32(q);
                    q = IncIntPtr(q, sizeof(int));
                    arr[i].model.still = (uint)Marshal.ReadInt32(q);
                    q = IncIntPtr(q, sizeof(int));

                    uint resn = Math.Max(arr[i].model.preview, arr[i].model.still);
                    arr[i].model.res = new Resolution[resn];
                    for (uint j = 0; j < resn; ++j) {
                        arr[i].model.res[j].width = (uint)Marshal.ReadInt32(q);
                        q = IncIntPtr(q, sizeof(int));
                        arr[i].model.res[j].height = (uint)Marshal.ReadInt32(q);
                        q = IncIntPtr(q, sizeof(int));
                    }
                }
            }
        }
        return arr;
    }

    /*
        the object of Swiftcam must be obtained by static mothod Open or OpenByIndex, it cannot be obtained by obj = new Swiftcam (The constructor is private on purpose)
    */
    // camId: enumerated by EnumV2, null means the first enumerated camera
    // For USB, GigE, CameraLink or CXP camera, the camId can also be specified as (case sensitive):
    //    (a) "sn:xxxxxxxxxxxx" (Use SN, such as sn:ZP250212241204105), or
    //    (b) "name:xxxxxx" (Use user-defined name, such as name:Camera1)
    // Moreover, for GigE camera, the camId can also be specified as (case sensitive):
    //    (a) "ip:xxx.xxx.xxx.xxx" (Use IP address, such as ip:192.168.1.100), or
    //    (b) "mac:xxxxxxxxxxxx" (Use MAC address, such as mac:d05f64ffff23)
    public static Swiftcam Open(string camId)
    {
        StringHandle camIdHandle = new StringHandle(camId);
        SafeCamHandle tmphandle = Swiftcam_Open(camIdHandle.Get());
        if (tmphandle == null || tmphandle.IsInvalid || tmphandle.IsClosed)
            return null;
        return new Swiftcam(tmphandle);
    }

    /*
        the object of Swiftcam must be obtained by static mothod Open or OpenByIndex, it cannot be obtained by obj = new Swiftcam (The constructor is private on purpose)
    */
    /*
        the same with Open, but use the index as the parameter. such as:
        index == 0, open the first camera,
        index == 1, open the second camera,
        etc
    */
    public static Swiftcam OpenByIndex(uint index)
    {
        SafeCamHandle tmphandle = Swiftcam_OpenByIndex(index);
        if (tmphandle == null || tmphandle.IsInvalid || tmphandle.IsClosed)
            return null;
        return new Swiftcam(tmphandle);
    }

    public SafeCamHandle Handle
    {
        get
        {
            return handle_;
        }
    }

    /* the last HRESULT return code of api call */
    public int HResult
    {
        get
        {
            return hResult_;
        }
    }

    public uint ResolutionNumber
    {
        get
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return 0;
            return Swiftcam_get_ResolutionNumber(handle_);
        }
    }

    public uint StillResolutionNumber
    {
        get
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return 0;
            return Swiftcam_get_StillResolutionNumber(handle_);
        }
    }

    public bool MonoMode
    {
        get
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return false;
            return (0 == Swiftcam_get_MonoMode(handle_));
        }
    }

    /* get the maximum speed, "Frame Speed Level" */
    public uint MaxSpeed
    {
        get
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return 0;
            return Swiftcam_get_MaxSpeed(handle_);
        }
    }

    /* get the max bitdepth of this camera, such as 8, 10, 12, 14, 16 */
    public uint MaxBitDepth
    {
        get
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return 0;
            return Swiftcam_get_MaxBitDepth(handle_);
        }
    }

    /* get the maximum fan speed, the fan speed range = [0, max], closed interval */
    public uint FanMaxSpeed
    {
        get
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return 0;
            return Swiftcam_get_FanMaxSpeed(handle_);
        }
    }

    public short TecTargetMax
    {
        get
        {
            if (handle_ != null && !handle_.IsInvalid && !handle_.IsClosed) {
                int range = 0;
                if (CheckHResult(Swiftcam_get_Option(handle_, eOPTION.OPTION_TECTARGET_RANGE, out range)))
                    return unchecked((short)((range >> 16) & 0xffff));
            }
            return short.MaxValue;
        }
    }

    public short TecTargetMin
    {
        get
        {
            if (handle_ != null && !handle_.IsInvalid && !handle_.IsClosed) {
                int range = 0;
                if (CheckHResult(Swiftcam_get_Option(handle_, eOPTION.OPTION_TECTARGET_RANGE, out range)))
                    return unchecked((short)(range & 0xffff));
            }
            return short.MinValue;
        }
    }

    /* get the revision */
    public ushort Revision
    {
        get
        {
            ushort rev = 0;
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return rev;
            Swiftcam_get_Revision(handle_, out rev);
            return rev;
        }
    }

    /* get the serial number which is always 32 chars which is zero-terminated such as "TP110826145730ABCD1234FEDC56787" */
    public string SerialNumber
    {
        get
        {
            string str = string.Empty;
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return str;
            IntPtr ptr = Marshal.AllocHGlobal(32);
            if (Swiftcam_get_SerialNumber(handle_, ptr) >= 0)
                str = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr);
            return str;
        }
    }

    /* get the camera firmware version, such as: 3.2.1.20140922 */
    public string FwVersion
    {
        get
        {
            string str = string.Empty;
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return str;
            IntPtr ptr = Marshal.AllocHGlobal(32);
            if (Swiftcam_get_FwVersion(handle_, ptr) >= 0)
                str = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr);
            return str;
        }
    }

    /* get the camera hardware version, such as: 3.2.1.20140922 */
    public string HwVersion
    {
        get
        {
            string str = string.Empty;
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return str;
            IntPtr ptr = Marshal.AllocHGlobal(32);
            if (Swiftcam_get_HwVersion(handle_, ptr) >= 0)
                str = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr);
            return str;
        }
    }

    /* such as: 20150327 */
    public string ProductionDate
    {
        get
        {
            string str = string.Empty;
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return str;
            IntPtr ptr = Marshal.AllocHGlobal(32);
            if (Swiftcam_get_ProductionDate(handle_, ptr) >= 0)
                str = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr);
            return str;
        }
    }

    /* such as: 1.3 */
    public string FpgaVersion
    {
        get
        {
            string str = string.Empty;
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return str;
            IntPtr ptr = Marshal.AllocHGlobal(32);
            if (Swiftcam_get_FpgaVersion(handle_, ptr) >= 0)
                str = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr);
            return str;
        }
    }
    
    public string Name
    {
        get
        {
            string str = string.Empty;
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return str;
            IntPtr ptr = Marshal.AllocHGlobal(64);
            if (Swiftcam_query_Name(handle_, ptr) >= 0)
                str = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr);
            return str;
        }
        set
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return;
            Swiftcam_set_Name(handle_, value);
        }
    }

    public uint Field
    {
        get
        {
            if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
                return 0;
            return (uint)Swiftcam_get_Field(handle_);
        }
    }

    public ModelV2 queryModel()
    {
        return toModelV2(Swiftcam_query_Model(handle_));
    }

#if !(WINDOWS_UWP)
    public bool StartPullModeWithWndMsg(IntPtr hWnd, uint nMsg)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_StartPullModeWithWndMsg(handle_, hWnd, nMsg));
    }
#endif

    public bool StartPullModeWithCallback(DelegateEventCallback funEvent)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        funEvent_ = funEvent;
        if (funEvent != null) {
            pEvent_ = new EVENT_CALLBACK(EventCallback);
            return CheckHResult(Swiftcam_StartPullModeWithCallback(handle_, pEvent_, id_));
        }
        else {
            return CheckHResult(Swiftcam_StartPullModeWithCallback(handle_, null, IntPtr.Zero));
        }
    }

    /*  bits: 24 (RGB24), 32 (RGB32), 8 (Grey), 16 (Grey), 48(RGB48), 64(RGB64) */
    [Obsolete("Use FrameInfoV4")]
    public bool PullImage(IntPtr pImageData, int bits, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImage(handle_, pImageData, bits, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImage(byte[] pImageData, int bits, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImage(handle_, pImageData, bits, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImage(ushort[] pImageData, int bits, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImage(handle_, pImageData, bits, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageV2(IntPtr pImageData, int bits, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo.width = pInfo.height = pInfo.flag = pInfo.seq = 0;
            pInfo.timestamp = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV2(handle_, pImageData, bits, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageV2(byte[] pImageData, int bits, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo.width = pInfo.height = pInfo.flag = pInfo.seq = 0;
            pInfo.timestamp = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV2(handle_, pImageData, bits, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageV2(ushort[] pImageData, int bits, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo.width = pInfo.height = pInfo.flag = pInfo.seq = 0;
            pInfo.timestamp = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV2(handle_, pImageData, bits, out pInfo));
    }

    /*  bits: 24 (RGB24), 32 (RGB32), 8 (Grey), 16 (Grey), 48(RGB48), 64(RGB64) */
    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImage(IntPtr pImageData, int bits, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImage(handle_, pImageData, bits, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImage(byte[] pImageData, int bits, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImage(handle_, pImageData, bits, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImage(ushort[] pImageData, int bits, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImage(handle_, pImageData, bits, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageV2(IntPtr pImageData, int bits, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV2();
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageV2(handle_, pImageData, bits, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageV2(byte[] pImageData, int bits, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV2();
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageV2(handle_, pImageData, bits, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageV2(ushort[] pImageData, int bits, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV2();
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageV2(handle_, pImageData, bits, out pInfo));
    }

    /*
       nWaitMS: The timeout interval, in milliseconds. If a non-zero value is specified, the function either successfully fetches the image or waits for a timeout.
                If nWaitMS is zero, the function does not wait when there are no images to fetch; It always returns immediately; this is equal to PullImage.
       bStill: to pull still image, set to 1, otherwise 0
       bits: 24 (RGB24), 32 (RGB32), 48 (RGB48), 8 (Grey), 16 (Grey), 64 (RGB64).
             In RAW mode, this parameter is ignored.
             bits = 0 means using default bits base on OPTION_RGB.
             When bits and OPTION_RGB are inconsistent, format conversion will have to be performed, resulting in loss of efficiency.
             See the following bits and OPTION_RGB correspondence table:
               ----------------------------------------------------------------------------------------------------------------------
               | OPTION_RGB         |   0 (RGB24)   |   1 (RGB48)   |   2 (RGB32)   |   3 (Grey8)   |  4 (Grey16)   |   5 (RGB64)   |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
               | bits = 0           |      24       |       48      |      32       |       8       |       16      |       64      |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
               | bits = 24          |      24       |       NA      | Convert to 24 | Convert to 24 |       NA      |       NA      |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
               | bits = 32          | Convert to 32 |       NA      |       32      | Convert to 32 |       NA      |       NA      |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
               | bits = 48          |      NA       |       48      |       NA      |       NA      | Convert to 48 | Convert to 48 |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
               | bits = 8           | Convert to 8  |       NA      | Convert to 8  |       8       |       NA      |       NA      |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
               | bits = 16          |      NA       | Convert to 16 |       NA      |       NA      |       16      | Convert to 16 |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
               | bits = 64          |      NA       | Convert to 64 |       NA      |       NA      | Convert to 64 |       64      |
               |--------------------|---------------|---------------|---------------|---------------|---------------|---------------|
       rowPitch: The distance from one row to the next row. rowPitch = 0 means using the default row pitch. rowPitch = -1 means zero padding, see below:
               ----------------------------------------------------------------------------------------------
               | format                             | 0 means default row pitch     | -1 means zero padding |
               |------------------------------------|-------------------------------|-----------------------|
               | RGB       | RGB24                  | TDIBWIDTHBYTES(24 * Width)    | Width * 3             |
               |           | RGB32                  | Width * 4                     | Width * 4             |
               |           | RGB48                  | TDIBWIDTHBYTES(48 * Width)    | Width * 6             |
               |           | GREY8                  | TDIBWIDTHBYTES(8 * Width)     | Width                 |
               |           | GREY16                 | TDIBWIDTHBYTES(16 * Width)    | Width * 2             |
               |           | RGB64                  | Width * 8                     | Width * 8             |
               |-----------|------------------------|-------------------------------|-----------------------|
               | RAW       | 8bits Mode             | Width                         | Width                 |
               |           | 10/12/14/16bits Mode   | Width * 2                     | Width * 2             |
               |-----------|------------------------|-------------------------------|-----------------------|
    */
    public bool PullImage(IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV4(handle_, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    public bool PullImage(byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV4(handle_, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    public bool PullImage(ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV4(handle_, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageV3(IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV3(handle_, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageV3(byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV3(handle_, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageV3(ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageV3(handle_, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    public bool WaitImage(uint nWaitMS, IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_WaitImageV4(handle_, nWaitMS, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    public bool WaitImage(uint nWaitMS, byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_WaitImageV4(handle_, nWaitMS, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    public bool WaitImage(uint nWaitMS, ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_WaitImageV4(handle_, nWaitMS, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool WaitImageV3(uint nWaitMS, IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_WaitImageV3(handle_, nWaitMS, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool WaitImageV3(uint nWaitMS, byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_WaitImageV3(handle_, nWaitMS, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool WaitImageV3(uint nWaitMS, ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_WaitImageV3(handle_, nWaitMS, pImageData, bStill, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageWithRowPitch(IntPtr pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImageWithRowPitch(handle_, pImageData, bits, rowPitch, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageWithRowPitch(byte[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImageWithRowPitch(handle_, pImageData, bits, rowPitch, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageWithRowPitch(ushort[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullImageWithRowPitch(handle_, pImageData, bits, rowPitch, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageWithRowPitchV2(IntPtr pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV2();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageWithRowPitchV2(handle_, pImageData, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageWithRowPitchV2(byte[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV2();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageWithRowPitchV2(handle_, pImageData, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullImageWithRowPitchV2(ushort[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV2();
            return false;
        }
        return CheckHResult(Swiftcam_PullImageWithRowPitchV2(handle_, pImageData, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageWithRowPitch(IntPtr pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageWithRowPitch(handle_, pImageData, bits, rowPitch, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageWithRowPitch(byte[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageWithRowPitch(handle_, pImageData, bits, rowPitch, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageWithRowPitch(ushort[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pnWidth = pnHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageWithRowPitch(handle_, pImageData, bits, rowPitch, out pnWidth, out pnHeight));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageWithRowPitchV2(IntPtr pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo.width = pInfo.height = pInfo.flag = pInfo.seq = 0;
            pInfo.timestamp = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageWithRowPitchV2(handle_, pImageData, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageWithRowPitchV2(byte[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo.width = pInfo.height = pInfo.flag = pInfo.seq = 0;
            pInfo.timestamp = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageWithRowPitchV2(handle_, pImageData, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool PullStillImageWithRowPitchV2(ushort[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo.width = pInfo.height = pInfo.flag = pInfo.seq = 0;
            pInfo.timestamp = 0;
            return false;
        }
        return CheckHResult(Swiftcam_PullStillImageWithRowPitchV2(handle_, pImageData, bits, rowPitch, out pInfo));
    }

    public bool StartPushModeV4(DelegateDataCallbackV4 funData, DelegateEventCallback funEvent)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        funDataV4_ = funData;
        funEvent_ = funEvent;
        pDataV4_ = delegate (IntPtr pData, IntPtr pInfo, bool bSnap, IntPtr ctxData)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxData.ToInt32(), out obj) && (obj != null)) {
                Swiftcam pthis = obj as Swiftcam;
                if (pthis != null)
                    pthis.DataCallbackV4(pData, pInfo, bSnap);
            }
        };
        pEvent_ = new EVENT_CALLBACK(EventCallback);
        return CheckHResult(Swiftcam_StartPushModeV4(handle_, pDataV4_, id_, pEvent_, id_));
    }
    
    [Obsolete("Use FrameInfoV4")]
    public bool StartPushModeV3(DelegateDataCallbackV3 funData, DelegateEventCallback funEvent)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        funDataV3_ = funData;
        funEvent_ = funEvent;
        pDataV3_ = delegate (IntPtr pData, IntPtr pInfo, bool bSnap, IntPtr ctxData)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxData.ToInt32(), out obj) && (obj != null)) {
                Swiftcam pthis = obj as Swiftcam;
                if (pthis != null)
                    pthis.DataCallbackV3(pData, pInfo, bSnap);
            }
        };
        pEvent_ = new EVENT_CALLBACK(EventCallback);
        return CheckHResult(Swiftcam_StartPushModeV3(handle_, pDataV3_, id_, pEvent_, id_));
    }

    public bool Stop()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_Stop(handle_));
    }

    /* 1 => pause, 0 => continue */
    public bool Pause(bool bPause)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_Pause(handle_, bPause ? 1 : 0));
    }

    /* nResolutionIndex = 0xffffffff means use the cureent preview resolution */
    public bool Snap(uint nResolutionIndex)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_Snap(handle_, nResolutionIndex));
    }

    /* multiple still image snap, nResolutionIndex = 0xffffffff means use the cureent preview resolution */
    public bool SnapN(uint nResolutionIndex, uint nNumber)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_SnapN(handle_, nResolutionIndex, nNumber));
    }

    /* multiple RAW still image snap, nResolutionIndex = 0xffffffff means use the cureent preview resolution */
    public bool SnapR(uint nResolutionIndex, uint nNumber)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_SnapR(handle_, nResolutionIndex, nNumber));
    }

    /*
        soft trigger:
        nNumber:    0xffff:     trigger continuously
                    0:          cancel trigger
                    others:     number of images to be triggered
    */
    public bool Trigger(ushort nNumber)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_Trigger(handle_, nNumber));
    }

    /*
        trigger synchronously
        nWaitMS:    0:              by default, exposure * 102% + 4000 milliseconds
                    0xffffffff:     wait infinite
                    other:          milliseconds to wait
    */
    public bool TriggerSync(uint nWaitMS, IntPtr pImageData, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_TriggerSyncV4(handle_, nWaitMS, pImageData, bits, rowPitch, out pInfo));
    }

    public bool TriggerSync(uint nWaitMS, byte[] pImageData, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_TriggerSyncV4(handle_, nWaitMS, pImageData, bits, rowPitch, out pInfo));
    }

    public bool TriggerSync(uint nWaitMS, ushort[] pImageData, int bits, int rowPitch, out FrameInfoV4 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV4();
            return false;
        }
        return CheckHResult(Swiftcam_TriggerSyncV4(handle_, nWaitMS, pImageData, bits, rowPitch, out pInfo));
    }
    
    [Obsolete("Use FrameInfoV4")]
    public bool TriggerSync(uint nWaitMS, IntPtr pImageData, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_TriggerSync(handle_, nWaitMS, pImageData, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool TriggerSync(uint nWaitMS, byte[] pImageData, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_TriggerSync(handle_, nWaitMS, pImageData, bits, rowPitch, out pInfo));
    }

    [Obsolete("Use FrameInfoV4")]
    public bool TriggerSync(uint nWaitMS, ushort[] pImageData, int bits, int rowPitch, out FrameInfoV3 pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pInfo = new FrameInfoV3();
            return false;
        }
        return CheckHResult(Swiftcam_TriggerSync(handle_, nWaitMS, pImageData, bits, rowPitch, out pInfo));
    }

    public bool put_Size(int nWidth, int nHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Size(handle_, nWidth, nHeight));
    }

    public bool get_Size(out int nWidth, out int nHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nWidth = nHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_Size(handle_, out nWidth, out nHeight));
    }

    /*
        put_Size, put_eSize, can be used to set the video output resolution BEFORE Start.
        put_Size use width and height parameters, put_eSize use the index parameter.
        for example, UCMOS03100KPA support the following resolutions:
            index 0:    2048,   1536
            index 1:    1024,   768
            index 2:    680,    510
        so, we can use put_Size(h, 1024, 768) or put_eSize(h, 1). Both have the same effect.
    */
    public bool put_eSize(uint nResolutionIndex)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_eSize(handle_, nResolutionIndex));
    }

    public bool get_eSize(out uint nResolutionIndex)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nResolutionIndex = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_eSize(handle_, out nResolutionIndex));
    }

    /*
        final size after ROI, rotate, binning
    */
    public bool get_FinalSize(out int nWidth, out int nHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nWidth = nHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_FinalSize(handle_, out nWidth, out nHeight));
    }

    public bool get_Resolution(uint nResolutionIndex, out int pWidth, out int pHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pWidth = pHeight = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_Resolution(handle_, nResolutionIndex, out pWidth, out pHeight));
    }

    /*
        get the sensor pixel size, such as: 2.4um x 2.4um
    */
    public bool get_PixelSize(uint nResolutionIndex, out float x, out float y)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            x = y = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_PixelSize(handle_, nResolutionIndex, out x, out y));
    }

    /*
        numerator/denominator, such as: 1/1, 1/2, 1/3
    */
    public bool get_ResolutionRatio(uint nResolutionIndex, out int pNumerator, out int pDenominator)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pNumerator = pDenominator = 1;
            return false;
        }
        return CheckHResult(Swiftcam_get_ResolutionRatio(handle_, nResolutionIndex, out pNumerator, out pDenominator));
    }

    /*
    see: http://www.fourcc.org
    FourCC:
        MAKEFOURCC('G', 'B', 'R', 'G'), see http://www.siliconimaging.com/RGB%20Bayer.htm
        MAKEFOURCC('R', 'G', 'G', 'B')
        MAKEFOURCC('B', 'G', 'G', 'R')
        MAKEFOURCC('G', 'R', 'B', 'G')
        MAKEFOURCC('Y', 'Y', 'Y', 'Y'), monochromatic sensor
        MAKEFOURCC('Y', '4', '1', '1'), yuv411
        MAKEFOURCC('V', 'U', 'Y', 'Y'), yuv422
        MAKEFOURCC('U', 'Y', 'V', 'Y'), yuv422
        MAKEFOURCC('Y', '4', '4', '4'), yuv444
        MAKEFOURCC('R', 'G', 'B', '8'), RGB888
    */
    public bool get_RawFormat(out uint nFourCC, out uint bitdepth)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nFourCC = bitdepth = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_RawFormat(handle_, out nFourCC, out bitdepth));
    }

    /*
        0: no realtime
              stop grab frame when frame buffer deque is full, until the frames in the queue are pulled away and the queue is not full
        1: realtime
              use minimum frame buffer. When new frame arrive, drop all the pending frame regardless of whether the frame buffer is full.
              If DDR present, also limit the DDR frame buffer to only one frame.
        2: soft realtime
              Drop the oldest frame when the queue is full and then enqueue the new frame
        default: 0
    */
    public bool put_RealTime(int val)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_RealTime(handle_, val));
    }

    public bool get_RealTime(out int val)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            val = 0;
            return false;
        }

        return CheckHResult(Swiftcam_get_RealTime(handle_, out val));
    }

    /* Flush is obsolete, recommend using put_Option(OPTION_FLUSH, 3) */
    public bool Flush()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_Flush(handle_));
    }

    /*
    * mode:
    *   0: disable auto exposure
    *   1: auto exposure continue mode
    *   2: auto exposure once mode
    */
    public bool get_AutoExpoEnable(out int mode)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            mode = 0;
            return false;
        }

        return CheckHResult(Swiftcam_get_AutoExpoEnable(handle_, out mode));
    }

    public bool put_AutoExpoEnable(int mode)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AutoExpoEnable(handle_, mode));
    }

    public bool get_AutoExpoEnable(out bool bAutoExposure)
    {
        bAutoExposure = false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        int iEnable = 0;
        if (!CheckHResult(Swiftcam_get_AutoExpoEnable(handle_, out iEnable)))
            return false;

        bAutoExposure = (iEnable != 0);
        return true;
    }

    public bool put_AutoExpoEnable(bool bAutoExposure)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AutoExpoEnable(handle_, bAutoExposure ? 1 : 0));
    }

    public bool get_AutoExpoTarget(out ushort Target)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Target = AETARGET_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_AutoExpoTarget(handle_, out Target));
    }

    public bool put_AutoExpoTarget(ushort Target)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AutoExpoTarget(handle_, Target));
    }

    /* set the maximum/minimal auto exposure time and agin. The default maximum auto exposure time is 350ms */
    public bool put_AutoExpoRange(uint maxTime, uint minTime, ushort maxGain, ushort minGain)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AutoExpoRange(handle_, maxTime, minTime, maxGain, minGain));
    }

    public bool get_AutoExpoRange(out uint maxTime, out uint minTime, out ushort maxGain, out ushort minGain)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            maxTime = minTime = 0;
            maxGain = minGain = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_AutoExpoRange(handle_, out maxTime, out minTime, out maxGain, out minGain));
    }

    public bool put_MaxAutoExpoTimeAGain(uint maxTime, ushort maxGain)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_MaxAutoExpoTimeAGain(handle_, maxTime, maxGain));
    }

    public bool get_MaxAutoExpoTimeAGain(out uint maxTime, out ushort maxGain)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            maxTime = 0;
            maxGain = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_MaxAutoExpoTimeAGain(handle_, out maxTime, out maxGain));
    }

    public bool put_MinAutoExpoTimeAGain(uint minTime, ushort minGain)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_MinAutoExpoTimeAGain(handle_, minTime, minGain));
    }

    public bool get_MinAutoExpoTimeAGain(out uint minTime, out ushort minGain)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            minTime = 0;
            minGain = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_MinAutoExpoTimeAGain(handle_, out minTime, out minGain));
    }

    public bool get_ExpoTime(out uint Time)/* in microseconds */
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Time = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_ExpoTime(handle_, out Time));
    }

    public bool get_RealExpoTime(out uint Time)/* actual exposure time */
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Time = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_RealExpoTime(handle_, out Time));
    }

    public bool put_ExpoTime(uint Time)/* in microseconds */
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_ExpoTime(handle_, Time));
    }

    public bool get_ExpTimeRange(out uint nMin, out uint nMax, out uint nDef)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nMin = nMax = nDef = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_ExpTimeRange(handle_, out nMin, out nMax, out nDef));
    }

    public bool get_ExpoAGain(out ushort Gain)/* percent, such as 300 */
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Gain = EXPOGAIN_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_ExpoAGain(handle_, out Gain));
    }

    public bool put_ExpoAGain(ushort Gain)/* percent */
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_ExpoAGain(handle_, Gain));
    }

    public bool get_ExpoAGainRange(out ushort nMin, out ushort nMax, out ushort nDef)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nMin = nMax = nDef = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_ExpoAGainRange(handle_, out nMin, out nMax, out nDef));
    }

    public bool put_LevelRange(ushort[] aLow, ushort[] aHigh)
    {
        if (aLow.Length != 4 || aHigh.Length != 4)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_LevelRange(handle_, aLow, aHigh));
    }

    public bool get_LevelRange(ushort[] aLow, ushort[] aHigh)
    {
        if (aLow.Length != 4 || aHigh.Length != 4)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_LevelRange(handle_, aLow, aHigh));
    }

    public bool put_LevelRangeV2(ushort mode, int roiX, int roiY, int roiWidth, int roiHeight, ushort[] aLow, ushort[] aHigh)
    {
        if (aLow.Length != 4 || aHigh.Length != 4)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        RECT rc = new RECT();
        rc.left = roiX;
        rc.right = roiX + roiWidth;
        rc.top = roiY;
        rc.bottom = roiY + roiHeight;
        return CheckHResult(Swiftcam_put_LevelRangeV2(handle_, mode, ref rc, aLow, aHigh));
    }

    public bool get_LevelRangeV2(out ushort mode, out int roiX, out int roiY, out int roiWidth, out int roiHeight, ushort[] aLow, ushort[] aHigh)
    {
        mode = 0;
        roiX = roiY = roiWidth = roiHeight = 0;
        if (aLow.Length != 4 || aHigh.Length != 4)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        RECT rc = new RECT();
        if (!CheckHResult(Swiftcam_get_LevelRangeV2(handle_, out mode, out rc, aLow, aHigh)))
            return false;
        roiX = rc.left;
        roiY = rc.top;
        roiWidth = rc.right - rc.left;
        roiHeight = rc.bottom - rc.top;
        return true;
    }

    public bool put_Hue(int Hue)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Hue(handle_, Hue));
    }

    public bool get_Hue(out int Hue)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Hue = HUE_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_Hue(handle_, out Hue));
    }

    public bool put_Saturation(int Saturation)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Saturation(handle_, Saturation));
    }

    public bool get_Saturation(out int Saturation)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Saturation = SATURATION_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_Saturation(handle_, out Saturation));
    }

    public bool put_Brightness(int Brightness)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Brightness(handle_, Brightness));
    }

    public bool get_Brightness(out int Brightness)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Brightness = BRIGHTNESS_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_Brightness(handle_, out Brightness));
    }

    public bool get_Contrast(out int Contrast)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Contrast = CONTRAST_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_Contrast(handle_, out Contrast));
    }

    public bool put_Contrast(int Contrast)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Contrast(handle_, Contrast));
    }

    public bool get_Gamma(out int Gamma)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            Gamma = GAMMA_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_Gamma(handle_, out Gamma));
    }

    public bool put_Gamma(int Gamma)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Gamma(handle_, Gamma));
    }

    public bool get_Chrome(out bool bChrome)    /* monochromatic mode */
    {
        bChrome = false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        int iEnable = 0;
        if (!CheckHResult(Swiftcam_get_Chrome(handle_, out iEnable)))
            return false;

        bChrome = (iEnable != 0);
        return true;
    }

    public bool put_Chrome(bool bChrome)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Chrome(handle_, bChrome ? 1 : 0));
    }

    public bool get_VFlip(out bool bVFlip) /* vertical flip */
    {
        bVFlip = false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        int iVFlip = 0;
        if (!CheckHResult(Swiftcam_get_VFlip(handle_, out iVFlip)))
            return false;

        bVFlip = (iVFlip != 0);
        return true;
    }

    public bool put_VFlip(bool bVFlip)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_VFlip(handle_, bVFlip ? 1 : 0));
    }

    public bool get_HFlip(out bool bHFlip)
    {
        bHFlip = false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        int iHFlip = 0;
        if (!CheckHResult(Swiftcam_get_HFlip(handle_, out iHFlip)))
            return false;

        bHFlip = (iHFlip != 0);
        return true;
    }

    public bool put_HFlip(bool bHFlip)  /* horizontal flip */
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_HFlip(handle_, bHFlip ? 1 : 0));
    }

    /* negative film */
    public bool get_Negative(out bool bNegative)
    {
        bNegative = false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        int iNegative = 0;
        if (!CheckHResult(Swiftcam_get_Negative(handle_, out iNegative)))
            return false;

        bNegative = (iNegative != 0);
        return true;
    }

    /* negative film */
    public bool put_Negative(bool bNegative)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Negative(handle_, bNegative ? 1 : 0));
    }

    public bool put_Speed(ushort nSpeed)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Speed(handle_, nSpeed));
    }

    public bool get_Speed(out ushort pSpeed)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pSpeed = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_Speed(handle_, out pSpeed));
    }

    /* power supply:
            0 => 60HZ AC
            1 => 50Hz AC
            2 => DC
    */
    public bool put_HZ(int nHZ)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_HZ(handle_, nHZ));
    }

    public bool get_HZ(out int nHZ)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nHZ = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_HZ(handle_, out nHZ));
    }

    public bool put_Mode(bool bSkip) /* skip or bin */
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Mode(handle_, bSkip ? 1 : 0));
    }

    public bool get_Mode(out bool bSkip)
    {
        bSkip = false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        int iSkip = 0;
        if (!CheckHResult(Swiftcam_get_Mode(handle_, out iSkip)))
            return false;

        bSkip = (iSkip != 0);
        return true;
    }

    /* White Balance, Temp/Tint mode */
    public bool put_TempTint(int nTemp, int nTint)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_TempTint(handle_, nTemp, nTint));
    }

    /* White Balance, Temp/Tint mode */
    public bool get_TempTint(out int nTemp, out int nTint)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nTemp = TEMP_DEF;
            nTint = TINT_DEF;
            return false;
        }
        return CheckHResult(Swiftcam_get_TempTint(handle_, out nTemp, out nTint));
    }

    /* White Balance, RGB Gain Mode */
    public bool put_WhiteBalanceGain(int[] aGain)
    {
        if (aGain.Length != 3)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_WhiteBalanceGain(handle_, aGain));
    }

    /* White Balance, RGB Gain Mode */
    public bool get_WhiteBalanceGain(int[] aGain)
    {
        if (aGain.Length != 3)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_WhiteBalanceGain(handle_, aGain));
    }

    public bool put_AWBAuxRect(int X, int Y, int Width, int Height)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        RECT rc = new RECT();
        rc.left = X;
        rc.right = X + Width;
        rc.top = Y;
        rc.bottom = Y + Height;
        return CheckHResult(Swiftcam_put_AWBAuxRect(handle_, ref rc));
    }

    public bool get_AWBAuxRect(out int X, out int Y, out int Width, out int Height)
    {
        X = Y = Width = Height = 0;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        RECT rc = new RECT();
        if (!CheckHResult(Swiftcam_get_AWBAuxRect(handle_, out rc)))
            return false;

        X = rc.left;
        Y = rc.top;
        Width = rc.right - rc.left;
        Height = rc.bottom - rc.top;
        return true;
    }

    public bool put_AEAuxRect(int X, int Y, int Width, int Height)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        RECT rc = new RECT();
        rc.left = X;
        rc.right = X + Width;
        rc.top = Y;
        rc.bottom = Y + Height;
        return CheckHResult(Swiftcam_put_AEAuxRect(handle_, ref rc));
    }

    public bool get_AEAuxRect(out int X, out int Y, out int Width, out int Height)
    {
        X = Y = Width = Height = 0;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        RECT rc = new RECT();
        if (!CheckHResult(Swiftcam_get_AEAuxRect(handle_, out rc)))
            return false;

        X = rc.left;
        Y = rc.top;
        Width = rc.right - rc.left;
        Height = rc.bottom - rc.top;
        return true;
    }

    public bool put_BlackBalance(ushort[] aSub)
    {
        if (aSub.Length != 3)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_BlackBalance(handle_, aSub));
    }

    public bool get_BlackBalance(ushort[] aSub)
    {
        if (aSub.Length != 3)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_BlackBalance(handle_, aSub));
    }

    public bool put_ABBAuxRect(int X, int Y, int Width, int Height)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        RECT rc = new RECT();
        rc.left = X;
        rc.right = X + Width;
        rc.top = Y;
        rc.bottom = Y + Height;
        return CheckHResult(Swiftcam_put_ABBAuxRect(handle_, ref rc));
    }

    public bool get_ABBAuxRect(out int X, out int Y, out int Width, out int Height)
    {
        X = Y = Width = Height = 0;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        RECT rc = new RECT();
        if (!CheckHResult(Swiftcam_get_ABBAuxRect(handle_, out rc)))
            return false;

        X = rc.left;
        Y = rc.top;
        Width = rc.right - rc.left;
        Height = rc.bottom - rc.top;
        return true;
    }

    public bool get_StillResolution(uint nResolutionIndex, out int pWidth, out int pHeight)
    {
        pWidth = pHeight = 0;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_StillResolution(handle_, nResolutionIndex, out pWidth, out pHeight));
    }

    public bool put_VignetEnable(bool bEnable)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_VignetEnable(handle_, bEnable ? 1 : 0));
    }

    public bool get_VignetEnable(out bool bEnable)
    {
        bEnable = false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;

        int iEanble = 0;
        if (!CheckHResult(Swiftcam_get_VignetEnable(handle_, out iEanble)))
            return false;

        bEnable = (iEanble != 0);
        return true;
    }

    public bool put_VignetAmountInt(int nAmount)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_VignetAmountInt(handle_, nAmount));
    }

    public bool get_VignetAmountInt(out int nAmount)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nAmount = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_VignetAmountInt(handle_, out nAmount));
    }

    public bool put_VignetMidPointInt(int nMidPoint)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_VignetMidPointInt(handle_, nMidPoint));
    }

    public bool get_VignetMidPointInt(out int nMidPoint)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nMidPoint = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_VignetMidPointInt(handle_, out nMidPoint));
    }

    /* led state:
        iLed: Led index, (0, 1, 2, ...)
        iState: 1 => Ever bright; 2 => Flashing; other => Off
        iPeriod: Flashing Period (>= 500ms)
    */
    public bool put_LEDState(ushort iLed, ushort iState, ushort iPeriod)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_LEDState(handle_, iLed, iState, iPeriod));
    }

    public int write_EEPROM(uint addr, IntPtr pBuffer, uint nBufferLen)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_write_EEPROM(handle_, addr, pBuffer, nBufferLen);
    }

    public int write_EEPROM(uint addr, byte[] pBuffer)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_write_EEPROM(handle_, addr, pBuffer, pBuffer.Length);
    }

    public int read_EEPROM(uint addr, IntPtr pBuffer, uint nBufferLen)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_read_EEPROM(handle_, addr, pBuffer, nBufferLen);
    }

    public int read_EEPROM(uint addr, byte[] pBuffer)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_read_EEPROM(handle_, addr, pBuffer, pBuffer.Length);
    }

    public const uint FLASH_SIZE = 0x00;    /* query total size */
    public const uint FLASH_EBLOCK = 0x01;    /* query erase block size */
    public const uint FLASH_RWBLOCK = 0x02;    /* query read/write block size */
    public const uint FLASH_STATUS = 0x03;    /* query status */
    public const uint FLASH_READ = 0x04;    /* read */
    public const uint FLASH_WRITE = 0x05;    /* write */
    public const uint FLASH_ERASE = 0x06;    /* erase */
    /* Flash:
       action = FLASH_XXXX: read, write, erase, query total size, query read/write block size, query erase block size
       addr = address
       see democpp
    */
    public int rwc_Flash(uint action, uint addr, uint len, IntPtr pData)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_rwc_Flash(handle_, action, addr, len, pData);
    }

    public int rwc_Flash(uint action, uint addr, uint len, byte[] pData)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_rwc_Flash(handle_, action, addr, len, pData);
    }

    public int write_Pipe(uint pipeId, IntPtr pBuffer, uint nBufferLen)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_write_Pipe(handle_, pipeId, pBuffer, nBufferLen);
    }

    public int read_Pipe(uint pipeId, IntPtr pBuffer, uint nBufferLen)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_read_Pipe(handle_, pipeId, pBuffer, nBufferLen);
    }

    public int feed_Pipe(uint pipeId)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_feed_Pipe(handle_, pipeId);
    }

    public int write_UART(IntPtr pBuffer, uint nBufferLen)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_write_UART(handle_, pBuffer, nBufferLen);
    }

    public int read_UART(IntPtr pBuffer, uint nBufferLen)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return E_UNEXPECTED;
        return Swiftcam_read_UART(handle_, pBuffer, nBufferLen);
    }

    public bool put_Option(eOPTION iOption, int iValue)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Option(handle_, iOption, iValue));
    }

    public bool get_Option(eOPTION iOption, out int iValue)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            iValue = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_Option(handle_, iOption, out iValue));
    }

    public int[] get_PixelFormatSupport()
    {
        int num = 0;
        if (get_PixelFormatSupport(-1, out num) && (num > 0)) {
            int[] arr = new int[num];
            for (int i = 0; i < num; ++i)
                get_PixelFormatSupport((sbyte)i, out arr[i]);
            return arr;
        }
        return null;
    }

    /*
    * cmd: input
    *    -1:       query the number
    *    0~number: query the nth pixel format
    * pixelFormat: output, PIXELFORMAT_xxxx
    */
    public bool get_PixelFormatSupport(sbyte cmd, out int pixelFormat)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pixelFormat = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_PixelFormatSupport(handle_, cmd, out pixelFormat));
    }

    /* Hardware Binning
    * Value: 1x1, 2x2, etc
    * Method: Average, Add, Skip
    */
    public bool put_Binning(string Value, string Method)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Binning(handle_, Value, Method));
    }

    public bool get_Binning(out string Value, out string Method)
    {
        Value = Method = null;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        IntPtr valuePtr = new IntPtr(0);
        IntPtr methodPtr = new IntPtr(0);
        bool ret = CheckHResult(Swiftcam_get_Binning(handle_, out valuePtr, out methodPtr));
        if (ret) {
            Value = Marshal.PtrToStringAnsi(valuePtr);
            Method = Marshal.PtrToStringAnsi(methodPtr);
        }
        return ret;
    }

    public int get_BinningNumber()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return 0;
        return Swiftcam_get_BinningNumber(handle_);
    }

    public string[] get_BinningValue()
    {
        int num = get_BinningNumber();
        if (num <= 0)
            return null;

        string[] arr = new string[num];
        for (int i = 0; i < num; ++i) {
            arr[i] = string.Empty;
            get_BinningValue((uint)i, out arr[i]);
        }
        return arr;
    }

    public bool get_BinningValue(uint index, out string Value)
    {
        Value = null;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        IntPtr valuePtr = new IntPtr(0);
        bool ret = CheckHResult(Swiftcam_get_BinningValue(handle_, index, out valuePtr));
        if (ret)
            Value = Marshal.PtrToStringAnsi(valuePtr);
        return ret;
    }

    public string[] get_BinningMethod()
    {
        List<string> list = new List<string>();
        uint index = 0;
        do {
            string strMethod;
            if (!get_BinningMethod(index, out strMethod))
                break;
            list.Add(strMethod);
            ++index;
        } while (true);
        return list.ToArray();
    }

    public bool get_BinningMethod(uint index, out string Method)
    {
        Method = null;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        IntPtr methodPtr = new IntPtr(0);
        bool ret = CheckHResult(Swiftcam_get_BinningMethod(handle_, index, out methodPtr));
        if (ret)
            Method = Marshal.PtrToStringAnsi(methodPtr);
        return ret;
    }

    public bool put_SelfTrigger(ref SelfTrigger pSt)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_SelfTrigger(handle_, ref pSt));
    }

    public bool get_SelfTrigger(ref SelfTrigger pSt)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_SelfTrigger(handle_, ref pSt));
    }

    public bool put_Linear(byte[] v8, ushort[] v16)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Linear(handle_, v8, v16));
    }

    public bool put_Curve(byte[] v8, ushort[] v16)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Curve(handle_, v8, v16));
    }

    public bool put_ColorMatrix(double[] v)
    {
        if (v.Length != 9)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_ColorMatrix(handle_, v));
    }

    public bool put_InitWBGain(ushort[] v)
    {
        if (v.Length != 3)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_InitWBGain(handle_, v));
    }

    /* get the temperature of the sensor, in 0.1 degrees Celsius (32 means 3.2 degrees Celsius, -35 means -3.5 degree Celsius) */
    public bool get_Temperature(out short pTemperature)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            pTemperature = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_Temperature(handle_, out pTemperature));
    }

    /* set the target temperature of the sensor or TEC, in 0.1 degrees Celsius (32 means 3.2 degrees Celsius, -35 means -3.5 degree Celsius)
        set "-2730" or below means using the default value of this model
    */
    public bool put_Temperature(short nTemperature)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Temperature(handle_, nTemperature));
    }

    /* xOffset, yOffset, xWidth, yHeight: must be even numbers */
    public bool put_Roi(uint xOffset, uint yOffset, uint xWidth, uint yHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_Roi(handle_, xOffset, yOffset, xWidth, yHeight));
    }

    public bool get_Roi(out uint pxOffset, out uint pyOffset, out uint pxWidth, out uint pyHeight)
    {
        pxOffset = pyOffset = pxWidth = pyHeight = 0;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_Roi(handle_, out pxOffset, out pyOffset, out pxWidth, out pyHeight));
    }

    /* multiple Roi */
    public bool put_RoiN(uint[] xOffset, uint[] yOffset, uint[] xWidth, uint[] yHeight)
    {
        if (xOffset.Length != yOffset.Length || xOffset.Length != xWidth.Length || xOffset.Length != yHeight.Length)
            return false;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_RoiN(handle_, xOffset, yOffset, xWidth, yHeight, (uint)(xOffset.Length)));
    }

    /*
        get the actual frame rate of the camera at the most recent time (about a few seconds):
        framerate (fps) = nFrame * 1000.0 / nTime
    */
    public bool get_FrameRate(out uint nFrame, out uint nTime, out uint nTotalFrame)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            nFrame = nTime = nTotalFrame = 0;
            return false;
        }
        return CheckHResult(Swiftcam_get_FrameRate(handle_, out nFrame, out nTime, out nTotalFrame));
    }

    public bool LevelRangeAuto()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_LevelRangeAuto(handle_));
    }

    /* Auto White Balance "Once", Temp/Tint Mode */
    public bool AwbOnce()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_AwbOnce(handle_, IntPtr.Zero, IntPtr.Zero));
    }

    [Obsolete("Use AwbOnce")]
    public bool AwbOnePush()
    {
        return AwbOnce();
    }

    /* Auto White Balance "Once", RGB Gain Mode */
    public bool AwbInit()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_AwbInit(handle_, IntPtr.Zero, IntPtr.Zero));
    }

    public bool AbbOnce()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_AbbOnce(handle_, IntPtr.Zero, IntPtr.Zero));
    }

    [Obsolete("Use AbbOnce")]
    public bool AbbOnePush()
    {
        return AbbOnce();
    }

    public bool FfcOnce()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_FfcOnce(handle_));
    }

    [Obsolete("Use FfcOnce")]
    public bool FfcOnePush()
    {
        return FfcOnce();
    }

    public bool DfcOnce()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_DfcOnce(handle_));
    }

    [Obsolete("Use DfcOnce")]
    public bool DfcOnePush()
    {
        return DfcOnce();
    }

    public bool FpncOnce()
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_FpncOnce(handle_));
    }

    public bool FfcExport(string filePath)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        StringHandle filePathHandle = new StringHandle(filePath);
        return CheckHResult(Swiftcam_FfcExport(handle_, filePathHandle.Get()));
    }

    public bool FfcImport(string filePath)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        StringHandle filePathHandle = new StringHandle(filePath);
        return CheckHResult(Swiftcam_FfcImport(handle_, filePathHandle.Get()));
    }

    public bool DfcExport(string filePath)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        StringHandle filePathHandle = new StringHandle(filePath);
        return CheckHResult(Swiftcam_DfcExport(handle_, filePathHandle.Get()));
    }

    public bool DfcImport(string filePath)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        StringHandle filePathHandle = new StringHandle(filePath);
        return CheckHResult(Swiftcam_DfcImport(handle_, filePathHandle.Get()));
    }

    public bool FpncExport(string filePath)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        StringHandle filePathHandle = new StringHandle(filePath);
        return CheckHResult(Swiftcam_FpncExport(handle_, filePathHandle.Get()));
    }

    public bool FpncImport(string filePath)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        StringHandle filePathHandle = new StringHandle(filePath);
        return CheckHResult(Swiftcam_FpncImport(handle_, filePathHandle.Get()));
    }

    public bool IoControl(uint ioLine, eIoControType eType, int outVal, out int inVal)
    {
        inVal = 0;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_IoControl(handle_, ioLine, eType, outVal, out inVal));
    }

    public bool IoControl(uint ioLine, eIoControType eType, int outVal)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        int inVal = 0;
        return CheckHResult(Swiftcam_IoControl(handle_, ioLine, eType, outVal, out inVal));
    }

    public bool AAF(eAAF action, int outVal, out int inVal)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed) {
            inVal = 0;
            return false;
        }
        return CheckHResult(Swiftcam_AAF(handle_, action, outVal, out inVal));
    }

    public bool AAF(eAAF action, int outVal)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        int inVal = 0;
        return CheckHResult(Swiftcam_AAF(handle_, action, outVal, out inVal));
    }

    static private bool Ptr2LensInfo(ref LensInfo pInfo, IntPtr p)
    {
        IntPtr saveptr = p;

        pInfo.lensID = (ushort)Marshal.ReadInt16(p);
        p = IncIntPtr(p, sizeof(ushort));
        pInfo.lensType = Marshal.ReadByte(p);
        p = IncIntPtr(p, sizeof(byte));
        pInfo.statusAfmf = Marshal.ReadByte(p);      /* LENS_AF = 0x00,  LENS_MF = 0x80 */
        p = IncIntPtr(p, sizeof(byte));

        pInfo.maxFocalLength = (ushort)Marshal.ReadInt16(p);
        p = IncIntPtr(p, sizeof(ushort));
        pInfo.curFocalLength = (ushort)Marshal.ReadInt16(p);
        p = IncIntPtr(p, sizeof(ushort));
        pInfo.minFocalLength = (ushort)Marshal.ReadInt16(p);
        p = IncIntPtr(p, sizeof(ushort));

        pInfo.farFM = Marshal.ReadInt16(p);           /* focus motor, absolute value */
        p = IncIntPtr(p, sizeof(ushort));
        pInfo.curFM = Marshal.ReadInt16(p);           /* current focus motor */
        p = IncIntPtr(p, sizeof(ushort));
        pInfo.nearFM = Marshal.ReadInt16(p);
        p = IncIntPtr(p, sizeof(ushort));

        pInfo.maxFocusDistance = (ushort)Marshal.ReadInt16(p);
        p = IncIntPtr(p, sizeof(ushort));
        pInfo.minFocusDistance = (ushort)Marshal.ReadInt16(p);
        p = IncIntPtr(p, sizeof(ushort));

        pInfo.curAM = (sbyte)Marshal.ReadByte(p);
        p = IncIntPtr(p, sizeof(sbyte));
        pInfo.maxAM = Marshal.ReadByte(p);           /* maximum Aperture, mimimum F# */
        p = IncIntPtr(p, sizeof(byte));
        pInfo.minAM = Marshal.ReadByte(p);           /* mimimum Aperture, maximum F# */
        p = IncIntPtr(p, sizeof(byte));
        pInfo.posAM = Marshal.ReadByte(p);           /* used for set aperture motor to posAM, it is an index */
        p = IncIntPtr(p, sizeof(byte));
        pInfo.posFM = Marshal.ReadInt32(p);
        p = IncIntPtr(p, sizeof(int));

        int sizeFN = Marshal.ReadInt32(p);
        p = IncIntPtr(p, sizeof(int));

        if (sizeFN >= 0) {
            IntPtr sp = Marshal.ReadIntPtr(p);
            if (sp != IntPtr.Zero) {
                pInfo.arrayFN = new string[sizeFN];
                for (int i = 0; i < sizeFN; ++i) {
                    pInfo.arrayFN[i] = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(sp));
                    sp = IncIntPtr(sp, IntPtr.Size);
                }
            }
        }

        p = IncIntPtr(p, IntPtr.Size);
        pInfo.lensName = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(p));
        Marshal.FreeHGlobal(saveptr);
        return true;
    }

    public bool get_LensInfo(ref LensInfo pInfo)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        IntPtr p = Marshal.AllocHGlobal(128);
        if (!CheckHResult(Swiftcam_get_LensInfo(handle_, p))) {
            Marshal.FreeHGlobal(p);
            return false;
        }
        return Ptr2LensInfo(ref pInfo, p);
    }

    public bool get_AFState(ref AFState pState)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_AFState(handle_, ref pState));
    }

    public bool put_AFMode(eAFMode mode, int bFixedWD, uint uiNear, uint uiFar)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AFMode(handle_, mode, bFixedWD, uiNear, uiFar));
    }

    public bool put_AFRoi(uint xOffset, uint yOffset, uint xWidth, uint yHeight)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AFRoi(handle_, xOffset, yOffset, xWidth, yHeight));
    }

    public bool put_AFAperture(int iAperture)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AFAperture(handle_, iAperture));
    }

    public bool put_AFFMPos(int iFMPos)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_AFFMPos(handle_, iFMPos));
    }

    public bool get_FocusMotor(out FocusMotor pFocusMotor)
    {
        pFocusMotor.idef = pFocusMotor.imax = pFocusMotor.imin = pFocusMotor.imaxabs = pFocusMotor.iminabs = pFocusMotor.zoneh = pFocusMotor.zonev = 0;
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_get_FocusMotor(handle_, out pFocusMotor));
    }

    public bool GetHistogram(DelegateHistogramCallback funHistogram)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        funHistogramV1_ = funHistogram;
        pHistogramV1_ = delegate (IntPtr aHistY, IntPtr aHistR, IntPtr aHistG, IntPtr aHistB, IntPtr ctxHistogramV1)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxHistogramV1.ToInt32(), out obj) && (obj != null)) {
                Swiftcam pthis = obj as Swiftcam;
                if (pthis != null) {
                    if (pthis.funHistogramV1_ != null) {
                        float[] arrHistY = new float[256];
                        float[] arrHistR = new float[256];
                        float[] arrHistG = new float[256];
                        float[] arrHistB = new float[256];
                        Marshal.Copy(aHistY, arrHistY, 0, 256);
                        Marshal.Copy(aHistR, arrHistR, 0, 256);
                        Marshal.Copy(aHistG, arrHistG, 0, 256);
                        Marshal.Copy(aHistB, arrHistB, 0, 256);

                        pthis.funHistogramV1_(arrHistY, arrHistR, arrHistG, arrHistB);
                        pthis.funHistogramV1_ = null;
                    }
                    pthis.pHistogramV1_ = null;
                }
            }
        };
        return CheckHResult(Swiftcam_GetHistogram(handle_, pHistogramV1_, id_));
    }

    public bool GetHistogramV2(DelegateHistogramCallbackV2 funHistogramV2)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        funHistogramV2_ = funHistogramV2;
        pHistogramV2_ = delegate (IntPtr aHist, uint nFlag, IntPtr ctxHistogramV2)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxHistogramV2.ToInt32(), out obj) && (obj != null)) {
                Swiftcam pthis = obj as Swiftcam;
                if ((pthis != null) && (pthis.funHistogramV2_ != null)) {
                    int arraySize = 1 << ((int)(nFlag & 0x0f));
                    if ((nFlag & 0x00008000) == 0)
                        arraySize *= 3;
                    int[] arrHist = new int[arraySize];
                    Marshal.Copy(aHist, arrHist, 0, arraySize);
                    pthis.funHistogramV2_(arrHist);
                }
            }
        };
        return CheckHResult(Swiftcam_GetHistogramV2(handle_, pHistogramV2_, id_));
    }

    public bool put_XY(int x, int y)
    {
        if (handle_ == null || handle_.IsInvalid || handle_.IsClosed)
            return false;
        return CheckHResult(Swiftcam_put_XY(handle_, x, y));
    }
    
    public static string query_Name(string camId)
    {
        StringHandle camIdHandle = new StringHandle(camId);
        IntPtr ptr = Marshal.AllocHGlobal(64);
        string str = string.Empty;
        if (Swiftcam_get_Name(camIdHandle.Get(), ptr) >= 0)
            str = Marshal.PtrToStringAnsi(ptr);
        Marshal.FreeHGlobal(ptr);
        return str;
    }

    public static int put_Name(string camId, string name)
    {
        StringHandle camIdHandle = new StringHandle(camId);
        return Swiftcam_put_Name(camIdHandle.Get(), name);
    }
    
    public static string query_SerialNumber(string camId)
    {
        StringHandle camIdHandle = new StringHandle(camId);
        IntPtr ptr = Marshal.AllocHGlobal(32);
        string str = string.Empty;
        if (Swiftcam_query_SerialNumber(camIdHandle.Get(), ptr) >= 0)
            str = Marshal.PtrToStringAnsi(ptr);
        Marshal.FreeHGlobal(ptr);
        return str;
    }

    /*
        calculate the clarity factor:
        pImageData: pointer to the image data
        bits: 8(Grey), 16(Grey), 24(RGB24), 32(RGB32), 48(RGB48), 64(RGB64)
        nImgWidth, nImgHeight: the image width and height
        xOffset, yOffset, xWidth, yHeight: the Roi used to calculate. If not specified, use 1/5 * 1/5 rectangle in the center
        return < 0.0 when error
    */
    public static double calcClarityFactor(IntPtr pImageData, int bits, uint nImgWidth, uint nImgHeight)
    {
        return Swiftcam_calc_ClarityFactor(pImageData, bits, nImgWidth, nImgHeight);
    }

    public static double calcClarityFactorV2(IntPtr pImageData, int bits, uint nImgWidth, uint nImgHeight, uint xOffset, uint yOffset, uint xWidth, uint yHeight)
    {
        return Swiftcam_calc_ClarityFactorV2(pImageData, bits, nImgWidth, nImgHeight, xOffset, yOffset, xWidth, yHeight);
    }

    /*
        nBitCount: output bitmap bit count
        when nBitDepth == 8:
            nBitCount must be 24 or 32
        when nBitDepth > 8
            nBitCount:  24 => RGB24
                        32 => RGB32
                        48 => RGB48
                        64 => RGB64
    */
    public static void deBayerV2(uint nBayer, int nW, int nH, IntPtr input, IntPtr output, byte nBitDepth, byte nBitCount)
    {
        Swiftcam_deBayerV2(nBayer, nW, nH, input, output, nBitDepth, nBitCount);
    }

    /*
        simulate replug:
        return > 0, the number of device has been replug
        return = 0, no device found
        return E_ACCESSDENIED if without UAC Administrator privileges
        for each device found, it will take about 3 seconds
    */
    public static int Replug(string camId)
    {
        StringHandle camIdHandle = new StringHandle(camId);
        return Swiftcam_Replug(camIdHandle.Get());
    }

    /* firmware update:
        camId: camera ID
        filePath: ufw file full path
        funProgress: progress percent callback
    Please do not unplug the camera or lost power during the upgrade process, this is very very important.
    Once an unplugging or power outage occurs during the upgrade process, the camera will no longer be available and can only be returned to the factory for repair.
    */
    public static int Update(string camId, string filePath, DelegateProgress funProgress)
    {
        PROGRESS_CALLBACK pProgress = delegate (int percent, IntPtr ctxProgess)
        {
            Object obj = null;
            if (map_.TryGetValue(ctxProgess.ToInt32(), out obj) && (obj != null)) {
                DelegateProgress p = obj as DelegateProgress;
                if (p != null)
                    p(percent);
            }
        };
        IntPtr id = new IntPtr(Interlocked.Increment(ref sid_));
        map_.Add(id.ToInt32(), funProgress);
        StringHandle camIdHandle = new StringHandle(camId);
        StringHandle filePathHandle = new StringHandle(filePath);
        int ret = Swiftcam_Update(camIdHandle.Get(), filePathHandle.Get(), pProgress, id);
        map_.Remove(id.ToInt32());
        return ret;
    }

    private static ModelV2 toModelV2(IntPtr q)
    {
        ModelV2 model = new ModelV2();
        if (q != IntPtr.Zero) {
            if (IsWindows())
                model.name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(q));
            else
                model.name = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(q));
            q = IncIntPtr(q, IntPtr.Size);
            if ((4 == IntPtr.Size) && IsWindows())   /* 32bits windows */
                q = IncIntPtr(q, 4); //skip 4 bytes, different from the linux version
            model.flag = (ulong)Marshal.ReadInt64(q);
            q = IncIntPtr(q, sizeof(long));
            model.maxspeed = (uint)Marshal.ReadInt32(q);
            q = IncIntPtr(q, sizeof(int));
            model.preview = (uint)Marshal.ReadInt32(q);
            q = IncIntPtr(q, sizeof(int));
            model.still = (uint)Marshal.ReadInt32(q);
            q = IncIntPtr(q, sizeof(int));
            model.maxfanspeed = (uint)Marshal.ReadInt32(q);
            q = IncIntPtr(q, sizeof(int));
            model.ioctrol = (uint)Marshal.ReadInt32(q);
            q = IncIntPtr(q, sizeof(int));
            float[] tmp = new float[1];
            Marshal.Copy(q, tmp, 0, 1);
            model.xpixsz = tmp[0];
            q = IncIntPtr(q, sizeof(float));
            Marshal.Copy(q, tmp, 0, 1);
            model.ypixsz = tmp[0];
            q = IncIntPtr(q, sizeof(float));
            uint resn = Math.Max(model.preview, model.still);
            model.res = new Resolution[resn];
            for (uint j = 0; j < resn; ++j) {
                model.res[j].width = (uint)Marshal.ReadInt32(q);
                q = IncIntPtr(q, sizeof(int));
                model.res[j].height = (uint)Marshal.ReadInt32(q);
                q = IncIntPtr(q, sizeof(int));
            }
        }
        return model;
    }

    public static ModelV2 getModel(ushort idVendor, ushort idProduct)
    {
        return toModelV2(Swiftcam_get_Model(idVendor, idProduct));
    }

    public static int Gain2TempTint(int[] gain, out int temp, out int tint)
    {
        if (gain.Length != 3) {
            temp = TEMP_DEF;
            tint = TINT_DEF;
            return E_INVALIDARG;
        }
        return Swiftcam_Gain2TempTint(gain, out temp, out tint);
    }

    public static void TempTint2Gain(int temp, int tint, int[] gain)
    {
        Swiftcam_TempTint2Gain(temp, tint, gain);
    }

    public static ModelV2[] allModel()
    {
        List<ModelV2> a = new List<ModelV2>();
        IntPtr q, p = Swiftcam_all_Model();
        do {
            q = Marshal.ReadIntPtr(p);
            if (q == IntPtr.Zero)
                break;
            a.Add(toModelV2(q));
            p = IncIntPtr(p, IntPtr.Size);
        } while (true);
        return a.ToArray();
    }

    public static string PixelFormatName(ePIXELFORMAT pixelFormat)
    {
        IntPtr ptr = Swiftcam_get_PixelFormatName(pixelFormat);
        if (IntPtr.Zero == ptr)
            return null;
        return Marshal.PtrToStringAnsi(ptr);
    }
    
    public static int FfcFile(IntPtr[] data, uint num, uint width, uint height, uint fourcc, uint bits, string outfile)
    {
        return Swiftcam_FfcFile(data, num, width, height, fourcc, bits, outfile);
    }

    public static string HResult2String(int hResult)
    {
        switch (hResult) {
            case S_OK:
                return "Success";
            case S_FALSE: /* Remark: Different from S_OK, such as internal values and user-set values have coincided, equivalent to noop */
                return "Yet another success";
            case E_INVALIDARG:
                return "One or more arguments are not valid";
            case E_NOTIMPL: /* Remark: This feature is not supported on this model of camera */
                return "Not supported or not implemented";
            case E_POINTER: /* Remark: Pointer is NULL */
                return "Pointer that is not valid";
            case E_UNEXPECTED:/* Remark: Generally indicates that the conditions are not met, such as calling put_Option setting some options that do not support modification when the camera is running, and so on */
                return "Catastrophic failure";
            case E_ACCESSDENIED: /* Remark: The program on Linux does not have permission to open the USB device, please enable udev rules file or run as root */
                return "General access denied error";
            case E_OUTOFMEMORY:
                return "Out of memory";
            case E_WRONG_THREAD:
                return "Call function in the wrong thread";
            case E_GEN_FAILURE: /* Remark: It is generally caused by hardware errors, such as cable problems, USB port problems, poor contact, insufficient power supply, camera hardware damage, etc */
                return "Device not functioning";
            case E_BUSY: /* Remark: The camera is already in use, such as duplicated opening/starting the camera, or being used by other application, etc */
                return "The requested resource is in use";
            case E_PENDING: /* Remark: No data is available at this time */
                return "The data necessary to complete this operation is not yet available";
            case E_TIMEOUT:
                return "This operation returned because the timeout period expired";
            case E_UNREACH:
                return "Network is unreachable";
            case E_CANCELLED:
                return "The operation was canceled by the user";
            default:
                return "Unspecified failure";
        }
    }

    private static int sid_ = 0;
    private static Dictionary<int, Object> map_ = new Dictionary<int, Object>();

    private SafeCamHandle handle_;
    private IntPtr id_;
    private DelegateDataCallbackV4 funDataV4_;
    [Obsolete]
    private DelegateDataCallbackV3 funDataV3_;
    private DelegateEventCallback funEvent_;
    private DelegateHistogramCallback funHistogramV1_;
    private DelegateHistogramCallbackV2 funHistogramV2_;
    private DATA_CALLBACK_V4 pDataV4_;
    [Obsolete]
    private DATA_CALLBACK_V3 pDataV3_;
    private EVENT_CALLBACK pEvent_;
    private HISTOGRAM_CALLBACKV1 pHistogramV1_;
    private HISTOGRAM_CALLBACKV2 pHistogramV2_;
    private int hResult_;

    /*
        the object of Swiftcam must be obtained by static mothod Open or OpenByIndex, it cannot be obtained by obj = new Swiftcam (The constructor is private on purpose)
    */
    private Swiftcam(SafeCamHandle h)
    {
        handle_ = h;
        id_ = new IntPtr(Interlocked.Increment(ref sid_));
        map_.Add(id_.ToInt32(), this);
    }

    private bool CheckHResult(int r)
    {
        hResult_ = r;
        return (hResult_ >= 0);
    }

    ~Swiftcam()
    {
        Dispose(false);
    }

#if !(NETFX_CORE || NETCOREAPP || WINDOWS_UWP)
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
#endif
    protected virtual void Dispose(bool disposing)
    {
        // Note there are three interesting states here:
        // 1) CreateFile failed, _handle contains an invalid handle
        // 2) We called Dispose already, _handle is closed.
        // 3) _handle is null, due to an async exception before
        //    calling CreateFile. Note that the finalizer runs
        //    if the constructor fails.
        if (handle_ != null && !handle_.IsInvalid) {
            // Free the handle
            handle_.Dispose();
        }
        // SafeHandle records the fact that we've called Dispose.
    }

    private void DataCallbackV4(IntPtr pData, IntPtr pInfo, bool bSnap)
    {
        if (pData == IntPtr.Zero || pInfo == IntPtr.Zero) /* pData == 0 means that something error, we callback to tell the application */
        {
            if (funDataV4_ != null) {
                FrameInfoV3 info = new FrameInfoV3();
                funDataV4_(IntPtr.Zero, ref info, bSnap);
            }
        }
        else {
#if !(NETFX_CORE || NETCOREAPP || WINDOWS_UWP)
            FrameInfoV3 info = (FrameInfoV3)Marshal.PtrToStructure(pInfo, typeof(FrameInfoV3));
#else
            FrameInfoV3 info = Marshal.PtrToStructure<FrameInfoV3>(pInfo);
#endif
            if (funDataV4_ != null)
                funDataV4_(pData, ref info, bSnap);
        }
    }

    [Obsolete]
    private void DataCallbackV3(IntPtr pData, IntPtr pInfo, bool bSnap)
    {
        if (pData == IntPtr.Zero || pInfo == IntPtr.Zero) /* pData == 0 means that something error, we callback to tell the application */
        {
            if (funDataV3_ != null) {
                FrameInfoV2 info = new FrameInfoV2();
                funDataV3_(IntPtr.Zero, ref info, bSnap);
            }
        }
        else {
#if !(NETFX_CORE || NETCOREAPP || WINDOWS_UWP)
            FrameInfoV2 info = (FrameInfoV2)Marshal.PtrToStructure(pInfo, typeof(FrameInfoV2));
#else
            FrameInfoV2 info = Marshal.PtrToStructure<FrameInfoV2>(pInfo);
#endif
            if (funDataV3_ != null)
                funDataV3_(pData, ref info, bSnap);
        }
    }

    private static void EventCallback(eEVENT nEvent, IntPtr ctxEvent)
    {
        Object obj = null;
        if (map_.TryGetValue(ctxEvent.ToInt32(), out obj) && (obj != null)) {
            Swiftcam pthis = obj as Swiftcam;
            if ((pthis != null) && (pthis.funEvent_ != null))
                pthis.funEvent_(nEvent);
        }
    }

    private static bool IsWindows()
    {
#if (WINDOWS_UWP)
        return true;
#else
        return (Environment.OSVersion.Platform == PlatformID.Win32NT);
#endif
    }

#if !(NETFX_CORE || NETCOREAPP || WINDOWS_UWP)
    public class SafeCamHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void Swiftcam_Close(IntPtr h);

        public SafeCamHandle()
            : base(true)
        {
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        override protected bool ReleaseHandle()
        {
            // Here, we must obey all rules for constrained execution regions.
            Swiftcam_Close(handle);
            return true;
        }
    };
#else
    public class SafeCamHandle : SafeHandle
    {
        [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void Swiftcam_Close(IntPtr h);
        
        public SafeCamHandle()
            : base(IntPtr.Zero, true)
        {
        }
        
        override protected bool ReleaseHandle()
        {
            Swiftcam_Close(handle);
            return true;
        }
        
        public override bool IsInvalid
        {
            get { return base.handle == IntPtr.Zero || base.handle == (IntPtr)(-1); }
        }
    };
#endif

    public sealed class StringHandle : IDisposable
    {
        public IntPtr ptr_;

        public StringHandle(string s)
        {
            if (s != null) {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    ptr_ = Marshal.StringToHGlobalUni(s);
                else {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s + "\0");
                    ptr_ = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, ptr_, bytes.Length);
                }
            }
        }

        public IntPtr Get() { return ptr_; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (ptr_ != IntPtr.Zero) {
                Marshal.FreeHGlobal(ptr_);
                ptr_ = IntPtr.Zero;
            }
        }

        ~StringHandle()
        {
            Dispose(false);
        }
    }

#if NETCOREAPP
    private const string dll = "swiftcam";
#elif LINUX
    private const string dll = "libswiftcam.so";
#else
    private const string dll = "swiftcam.dll";
#endif

    public delegate void DelegateEventCallback(eEVENT nEvent);
    public delegate void DelegateDataCallbackV4(IntPtr pData, ref FrameInfoV3 info, bool bSnap);
    [Obsolete]
    public delegate void DelegateDataCallbackV3(IntPtr pData, ref FrameInfoV2 info, bool bSnap);
    public delegate void DelegateHistogramCallback(float[] aHistY, float[] aHistR, float[] aHistG, float[] aHistB);
    public delegate void DelegateHistogramCallbackV2(int[] aHist);
    public delegate void DelegateProgress(int percent);
    public delegate void DelegateHotplug();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DATA_CALLBACK_V4(IntPtr pData, IntPtr pInfo, bool bSnap, IntPtr ctxData);
    [UnmanagedFunctionPointer(CallingConvention.Winapi), Obsolete]
    private delegate void DATA_CALLBACK_V3(IntPtr pData, IntPtr pInfo, bool bSnap, IntPtr ctxData);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EVENT_CALLBACK(eEVENT nEvent, IntPtr ctxEvent);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void HISTOGRAM_CALLBACKV1(IntPtr aHistY, IntPtr aHistR, IntPtr aHistG, IntPtr aHistB, IntPtr ctxHistogramV1);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void HISTOGRAM_CALLBACKV2(IntPtr aHist, uint nFlag, IntPtr ctxHistogramV2);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void PROGRESS_CALLBACK(int percent, IntPtr ctxProgress);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void HOTPLUG_CALLBACK(IntPtr ctxHotplug);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    };

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr Swiftcam_Version();
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern uint Swiftcam_Enum(IntPtr ptr);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_EnumV2(IntPtr ptr);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_EnumWithName(IntPtr ptr);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern SafeCamHandle Swiftcam_Open(IntPtr camId);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern SafeCamHandle Swiftcam_OpenByIndex(uint index);
#if !(WINDOWS_UWP)
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_StartPullModeWithWndMsg(SafeCamHandle h, IntPtr hWnd, uint nMsg);
#endif
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_StartPullModeWithCallback(SafeCamHandle h, EVENT_CALLBACK funEvent, IntPtr ctxEvent);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_PullImageV4(SafeCamHandle h, IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_PullImageV4(SafeCamHandle h, byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_PullImageV4(SafeCamHandle h, ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_WaitImageV4(SafeCamHandle h, uint nWaitMS, IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_WaitImageV4(SafeCamHandle h, uint nWaitMS, byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_WaitImageV4(SafeCamHandle h, uint nWaitMS, ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_PullImageV3(SafeCamHandle h, IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageV3(SafeCamHandle h, byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageV3(SafeCamHandle h, ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_WaitImageV3(SafeCamHandle h, uint nWaitMS, IntPtr pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_WaitImageV3(SafeCamHandle h, uint nWaitMS, byte[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_WaitImageV3(SafeCamHandle h, uint nWaitMS, ushort[] pImageData, int bStill, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImage(SafeCamHandle h, IntPtr pImageData, int bits, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImage(SafeCamHandle h, byte[] pImageData, int bits, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImage(SafeCamHandle h, ushort[] pImageData, int bits, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImage(SafeCamHandle h, IntPtr pImageData, int bits, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImage(SafeCamHandle h, byte[] pImageData, int bits, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImage(SafeCamHandle h, ushort[] pImageData, int bits, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageWithRowPitch(SafeCamHandle h, IntPtr pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageWithRowPitch(SafeCamHandle h, byte[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageWithRowPitch(SafeCamHandle h, ushort[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageWithRowPitch(SafeCamHandle h, IntPtr pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageWithRowPitch(SafeCamHandle h, byte[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageWithRowPitch(SafeCamHandle h, ushort[] pImageData, int bits, int rowPitch, out uint pnWidth, out uint pnHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageV2(SafeCamHandle h, IntPtr pImageData, int bits, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageV2(SafeCamHandle h, byte[] pImageData, int bits, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageV2(SafeCamHandle h, ushort[] pImageData, int bits, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageV2(SafeCamHandle h, IntPtr pImageData, int bits, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageV2(SafeCamHandle h, byte[] pImageData, int bits, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageV2(SafeCamHandle h, ushort[] pImageData, int bits, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageWithRowPitchV2(SafeCamHandle h, IntPtr pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageWithRowPitchV2(SafeCamHandle h, byte[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullImageWithRowPitchV2(SafeCamHandle h, ushort[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageWithRowPitchV2(SafeCamHandle h, IntPtr pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageWithRowPitchV2(SafeCamHandle h, byte[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_PullStillImageWithRowPitchV2(SafeCamHandle h, ushort[] pImageData, int bits, int rowPitch, out FrameInfoV2 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_StartPushModeV4(SafeCamHandle h, DATA_CALLBACK_V4 funData, IntPtr ctxData, EVENT_CALLBACK funEvent, IntPtr ctxEvent);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_StartPushModeV3(SafeCamHandle h, DATA_CALLBACK_V3 funData, IntPtr ctxData, EVENT_CALLBACK funEvent, IntPtr ctxEvent);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Stop(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Pause(SafeCamHandle h, int bPause);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Snap(SafeCamHandle h, uint nResolutionIndex);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_SnapN(SafeCamHandle h, uint nResolutionIndex, uint nNumber);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_SnapR(SafeCamHandle h, uint nResolutionIndex, uint nNumber);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Trigger(SafeCamHandle h, ushort nNumber);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_TriggerSyncV4(SafeCamHandle h, uint nWaitMS, IntPtr pImageData, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_TriggerSyncV4(SafeCamHandle h, uint nWaitMS, byte[] pImageData, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_TriggerSyncV4(SafeCamHandle h, uint nWaitMS, ushort[] pImageData, int bits, int rowPitch, out FrameInfoV4 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_TriggerSync(SafeCamHandle h, uint nWaitMS, IntPtr pImageData, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_TriggerSync(SafeCamHandle h, uint nWaitMS, byte[] pImageData, int bits, int rowPitch, out FrameInfoV3 pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi), Obsolete]
    private static extern int Swiftcam_TriggerSync(SafeCamHandle h, uint nWaitMS, ushort[] pImageData, int bits, int rowPitch, out FrameInfoV3 pInfo);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Size(SafeCamHandle h, int nWidth, int nHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Size(SafeCamHandle h, out int nWidth, out int nHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_eSize(SafeCamHandle h, uint nResolutionIndex);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_eSize(SafeCamHandle h, out uint nResolutionIndex);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_FinalSize(SafeCamHandle h, out int nWidth, out int nHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_get_ResolutionNumber(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Resolution(SafeCamHandle h, uint nResolutionIndex, out int pWidth, out int pHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_ResolutionRatio(SafeCamHandle h, uint nResolutionIndex, out int pNumerator, out int pDenominator);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_get_Field(SafeCamHandle h);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_RawFormat(SafeCamHandle h, out uint nFourCC, out uint bitdepth);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_RealTime(SafeCamHandle h, int val);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_RealTime(SafeCamHandle h, out int val);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Flush(SafeCamHandle h);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Temperature(SafeCamHandle h, out short pTemperature);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Temperature(SafeCamHandle h, short nTemperature);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Roi(SafeCamHandle h, out uint pxOffset, out uint pyOffset, out uint pxWidth, out uint pyHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Roi(SafeCamHandle h, uint xOffset, uint yOffset, uint xWidth, uint yHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_RoiN(SafeCamHandle h, uint[] xoffset, uint[] yoffset, uint[] xWidth, uint[] yHeight, uint Num);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_XY(SafeCamHandle h, int x, int y);

    /*
        ------------------------------------------------------------------|
        | Parameter               |   Range       |   Default             |
        |-----------------------------------------------------------------|
        | Auto Exposure Target    |   16~235      |   120                 |
        | Exposure Gain           |   100~        |   100                 |
        | Temp                    |   1000~25000  |   6503                |
        | Tint                    |   100~2500    |   1000                |
        | LevelRange              |   0~255       |   Low = 0, High = 255 |
        | Contrast                |   -255~255    |   0                   |
        | Hue                     |   -180~180    |   0                   |
        | Saturation              |   0~255       |   128                 |
        | Brightness              |   -255~255    |   0                   |
        | Gamma                   |   20~180      |   100                 |
        | WBGain                  |   -127~127    |   0                   |
        ------------------------------------------------------------------|
    */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_AutoExpoEnable(SafeCamHandle h, out int mode);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AutoExpoEnable(SafeCamHandle h, int mode);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_AutoExpoTarget(SafeCamHandle h, out ushort Target);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AutoExpoTarget(SafeCamHandle h, ushort Target);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AutoExpoRange(SafeCamHandle h, uint maxTime, uint minTime, ushort maxGain, ushort minGain);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_AutoExpoRange(SafeCamHandle h, out uint maxTime, out uint minTime, out ushort maxGain, out ushort minGain);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_MaxAutoExpoTimeAGain(SafeCamHandle h, uint maxTime, ushort maxGain);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_MaxAutoExpoTimeAGain(SafeCamHandle h, out uint maxTime, out ushort maxGain);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_MinAutoExpoTimeAGain(SafeCamHandle h, uint minTime, ushort minGain);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_MinAutoExpoTimeAGain(SafeCamHandle h, out uint minTime, out ushort minGain);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_ExpoTime(SafeCamHandle h, out uint Time)/* in microseconds */;
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_ExpoTime(SafeCamHandle h, uint Time)/* inmicroseconds */;
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_RealExpoTime(SafeCamHandle h, out uint Time)/* actual exposure time */;
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_ExpTimeRange(SafeCamHandle h, out uint nMin, out uint nMax, out uint nDef);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_ExpoAGain(SafeCamHandle h, out ushort Gain);/* percent, such as 300 */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_ExpoAGain(SafeCamHandle h, ushort Gain);/* percent */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_ExpoAGainRange(SafeCamHandle h, out ushort nMin, out ushort nMax, out ushort nDef);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_LevelRange(SafeCamHandle h, [In] ushort[] aLow, [In] ushort[] aHigh);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_LevelRange(SafeCamHandle h, [Out] ushort[] aLow, [Out] ushort[] aHigh);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_LevelRangeV2(SafeCamHandle h, ushort mode, ref RECT roiRect, [In] ushort[] aLow, [In] ushort[] aHigh);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_LevelRangeV2(SafeCamHandle h, out ushort mode, out RECT pRoiRect, [Out] ushort[] aLow, [Out] ushort[] aHigh);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Hue(SafeCamHandle h, int Hue);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Hue(SafeCamHandle h, out int Hue);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Saturation(SafeCamHandle h, int Saturation);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Saturation(SafeCamHandle h, out int Saturation);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Brightness(SafeCamHandle h, int Brightness);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Brightness(SafeCamHandle h, out int Brightness);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Contrast(SafeCamHandle h, out int Contrast);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Contrast(SafeCamHandle h, int Contrast);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Gamma(SafeCamHandle h, out int Gamma);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Gamma(SafeCamHandle h, int Gamma);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Chrome(SafeCamHandle h, out int bChrome);    /* monochromatic mode */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Chrome(SafeCamHandle h, int bChrome);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_VFlip(SafeCamHandle h, out int bVFlip);  /* vertical flip */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_VFlip(SafeCamHandle h, int bVFlip);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_HFlip(SafeCamHandle h, out int bHFlip);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_HFlip(SafeCamHandle h, int bHFlip);  /* horizontal flip */

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Negative(SafeCamHandle h, out int bNegative);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Negative(SafeCamHandle h, int bNegative);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Speed(SafeCamHandle h, ushort nSpeed);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Speed(SafeCamHandle h, out ushort pSpeed);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_get_MaxSpeed(SafeCamHandle h);/* get the maximum speed, "Frame Speed Level", speed range = [0, max] */

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_get_MaxBitDepth(SafeCamHandle h);/* get the max bitdepth of this camera, such as 8, 10, 12, 14, 16 */

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_get_FanMaxSpeed(SafeCamHandle h);/* get the maximum fan speed, the fan speed range = [0, max], closed interval */

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_HZ(SafeCamHandle h, int nHZ);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_HZ(SafeCamHandle h, out int nHZ);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Mode(SafeCamHandle h, int bSkip); /* skip or bin */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Mode(SafeCamHandle h, out int bSkip);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_TempTint(SafeCamHandle h, int nTemp, int nTint);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_TempTint(SafeCamHandle h, out int nTemp, out int nTint);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_WhiteBalanceGain(SafeCamHandle h, [In] int[] aGain);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_WhiteBalanceGain(SafeCamHandle h, [Out] int[] aGain);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_BlackBalance(SafeCamHandle h, [In] ushort[] aSub);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_BlackBalance(SafeCamHandle h, [Out] ushort[] aSub);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AWBAuxRect(SafeCamHandle h, ref RECT pAuxRect);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_AWBAuxRect(SafeCamHandle h, out RECT pAuxRect);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AEAuxRect(SafeCamHandle h, ref RECT pAuxRect);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_AEAuxRect(SafeCamHandle h, out RECT pAuxRect);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_ABBAuxRect(SafeCamHandle h, ref RECT pAuxRect);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_ABBAuxRect(SafeCamHandle h, out RECT pAuxRect);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_MonoMode(SafeCamHandle h);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern uint Swiftcam_get_StillResolutionNumber(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_StillResolution(SafeCamHandle h, uint nResolutionIndex, out int pWidth, out int pHeight);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Revision(SafeCamHandle h, out ushort pRevision);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_SerialNumber(SafeCamHandle h, IntPtr sn);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_FwVersion(SafeCamHandle h, IntPtr fwver);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_HwVersion(SafeCamHandle h, IntPtr hwver);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_FpgaVersion(SafeCamHandle h, IntPtr fpgaver);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_ProductionDate(SafeCamHandle h, IntPtr pdate);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_query_Name(SafeCamHandle h, IntPtr name);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_set_Name(SafeCamHandle h, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_PixelSize(SafeCamHandle h, uint nResolutionIndex, out float x, out float y);

    /*
                ------------------------------------------------------------|
                | Parameter         |   Range       |   Default             |
                |-----------------------------------------------------------|
                | VidgetAmount      |   -100~100    |   0                   |
                | VignetMidPoint    |   0~100       |   50                  |
                -------------------------------------------------------------
    */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_VignetEnable(SafeCamHandle h, int bEnable);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_VignetEnable(SafeCamHandle h, out int bEnable);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_VignetAmountInt(SafeCamHandle h, int nAmount);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_VignetAmountInt(SafeCamHandle h, out int nAmount);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_VignetMidPointInt(SafeCamHandle h, int nMidPoint);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_VignetMidPointInt(SafeCamHandle h, out int nMidPoint);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_AwbOnce(SafeCamHandle h, IntPtr funTT, IntPtr ctxTT);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_AwbInit(SafeCamHandle h, IntPtr funWB, IntPtr ctxWB);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_LevelRangeAuto(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_GetHistogram(SafeCamHandle h, HISTOGRAM_CALLBACKV1 funHistogramV1, IntPtr ctxHistogramV1);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_GetHistogramV2(SafeCamHandle h, HISTOGRAM_CALLBACKV2 funHistogramV2, IntPtr ctxHistogramV2);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_AbbOnce(SafeCamHandle h, IntPtr funBB, IntPtr ctxBB);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_LEDState(SafeCamHandle h, ushort iLed, ushort iState, ushort iPeriod);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_write_EEPROM(SafeCamHandle h, uint addr, IntPtr pBuffer, uint nBufferLen);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_write_EEPROM(SafeCamHandle h, uint addr, byte[] pBuffer, int nBufferLen);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_read_EEPROM(SafeCamHandle h, uint addr, IntPtr pBuffer, uint nBufferLen);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_read_EEPROM(SafeCamHandle h, uint addr, byte[] pBuffer, int nBufferLen);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_rwc_Flash(SafeCamHandle h, uint action, uint addr, uint len, IntPtr pData);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_rwc_Flash(SafeCamHandle h, uint action, uint addr, uint len, byte[] pData);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_write_Pipe(SafeCamHandle h, uint pipeId, IntPtr pBuffer, uint nBufferLen);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_read_Pipe(SafeCamHandle h, uint pipeId, IntPtr pBuffer, uint nBufferLen);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_feed_Pipe(SafeCamHandle h, uint pipeId);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_write_UART(SafeCamHandle h, IntPtr pBuffer, uint nBufferLen);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_read_UART(SafeCamHandle h, IntPtr pBuffer, uint nBufferLen);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Option(SafeCamHandle h, eOPTION iOption, int iValue);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Option(SafeCamHandle h, eOPTION iOption, out int iValue);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_PixelFormatSupport(SafeCamHandle h, sbyte cmd, out int iValue);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr Swiftcam_get_PixelFormatName(ePIXELFORMAT val);

    /* Hardware Binning
    * Value: 1x1, 2x2, etc
    * Method: Average, Add
    */
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Binning(SafeCamHandle h, [MarshalAs(UnmanagedType.LPStr)] string Value, [MarshalAs(UnmanagedType.LPStr)] string Method);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Binning(SafeCamHandle h, out IntPtr Value, out IntPtr Method);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_BinningNumber(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_BinningValue(SafeCamHandle h, uint index, out IntPtr Value);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_BinningMethod(SafeCamHandle h, uint index, out IntPtr Value);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_SelfTrigger(SafeCamHandle h, ref SelfTrigger pSt);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_SelfTrigger(SafeCamHandle h, ref SelfTrigger pSt);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Linear(SafeCamHandle h, byte[] v8, ushort[] v16);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Curve(SafeCamHandle h, byte[] v8, ushort[] v16);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_ColorMatrix(SafeCamHandle h, double[] v);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_InitWBGain(SafeCamHandle h, ushort[] v);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_FrameRate(SafeCamHandle h, out uint nFrame, out uint nTime, out uint nTotalFrame);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_FfcOnce(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_DfcOnce(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_FpncOnce(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_FfcExport(SafeCamHandle h, IntPtr filePath);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_FfcImport(SafeCamHandle h, IntPtr filePath);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_DfcExport(SafeCamHandle h, IntPtr filePath);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_DfcImport(SafeCamHandle h, IntPtr filePath);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_FpncExport(SafeCamHandle h, IntPtr filePath);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_FpncImport(SafeCamHandle h, IntPtr filePath);
    
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_FfcFile(IntPtr[] data, uint num, uint width, uint height, uint fourcc, uint bits, [MarshalAs(UnmanagedType.LPStr)] string outfile);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_IoControl(SafeCamHandle h, uint ioLine, eIoControType eType, int outVal, out int inVal);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_AAF(SafeCamHandle h, eAAF action, int outVal, out int inVal);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_LensInfo(SafeCamHandle h, IntPtr pInfo);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_AFState(SafeCamHandle h, ref AFState pState);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AFMode(SafeCamHandle h, eAFMode mode, int bFixedWD, uint uiNear, uint uiFar);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AFRoi(SafeCamHandle h, uint xOffset, uint yOffset, uint xWidth, uint yHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AFAperture(SafeCamHandle h, int iAperture);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_AFFMPos(SafeCamHandle h, int iFMPos);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_FocusMotor(SafeCamHandle h, out FocusMotor pFocusMotor);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern double Swiftcam_calc_ClarityFactor(IntPtr pImageData, int bits, uint nImgWidth, uint nImgHeight);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern double Swiftcam_calc_ClarityFactorV2(IntPtr pImageData, int bits, uint nImgWidth, uint nImgHeight, uint xOffset, uint yOffset, uint xWidth, uint yHeight);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern void Swiftcam_deBayerV2(uint nBayer, int nW, int nH, IntPtr input, IntPtr output, byte nBitDepth, byte nBitCount);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Replug(IntPtr camId);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Update(IntPtr camId, IntPtr filePath, PROGRESS_CALLBACK funProgress, IntPtr ctxProgress);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_GigeEnable(HOTPLUG_CALLBACK funHotplug, IntPtr ctxHotplug);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_GigeEnableV2(HOTPLUG_CALLBACK funHotplug, IntPtr ctxHotplug, [MarshalAs(UnmanagedType.LPStr)] string opt);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_PciEnable(HOTPLUG_CALLBACK funHotplug, IntPtr ctxHotplug);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_CtiEnable(HOTPLUG_CALLBACK funHotplug, IntPtr ctxHotplug, IntPtr ctiPath);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_put_Name(IntPtr camId, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_get_Name(IntPtr camId, IntPtr name);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_query_SerialNumber(IntPtr camId, IntPtr sn);

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr Swiftcam_get_Model(ushort idVendor, ushort idProduct);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr Swiftcam_query_Model(SafeCamHandle h);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr Swiftcam_all_Model();

    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern int Swiftcam_Gain2TempTint(int[] gain, out int temp, out int tint);
    [DllImport(dll, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern void Swiftcam_TempTint2Gain(int temp, int tint, [Out] int[] gain);
}
