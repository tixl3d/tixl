#nullable enable
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Lib.io.video;

/// <summary>
/// The Linux camera-control backend: raw libc <c>open</c>/<c>ioctl</c>/<c>close</c> against the device's
/// V4L2 node, replacing the old enum-backend's per-call open/close. Everything the device exposes is
/// discovered with a <c>VIDIOC_QUERYCTRL</c> walk seeded with <c>V4L2_CTRL_FLAG_NEXT_CTRL</c>, and menu
/// controls are expanded with <c>VIDIOC_QUERYMENU</c>. Two corrections against the old code are load
/// bearing and live-verified (doc §1.1): <c>QUERYCTRL</c> carries <c>sizeof(struct v4l2_queryctrl)</c> =
/// <b>68</b> bytes (the old 64 was rejected on every control), and the walk re-seeds with the RETURNED id
/// — <c>returned+1</c> silently dropped every control whose id equals the previous id + 1 (5 of 19 on the
/// C922). All methods run on the control worker's thread only; the cached fd per device index therefore
/// needs no lock. V4L2 allows several fds per device — the capture stream and the capability scan hold
/// their own (<see cref="WebcamScan"/>).
/// </summary>
internal sealed class V4L2CameraControls : ICameraControlBackend
{
    // ioctl request numbers via the kernel's _IOWR('V', nr, struct) macro — computed, not hand-hexed.
    // Sizes: v4l2_queryctrl = 68 (videodev2.h:1892-1902), v4l2_querymenu = 44 packed (:1922-1930),
    // v4l2_control = 8 (:1772-1776). G_CTRL/S_CTRL are unchanged from the old code — verified correct.
    private static ulong Iowr(byte number, uint structSize)
        => (3UL << 30) | ((ulong)structSize << 16) | (0x56UL << 8) | number;

    private static readonly ulong VidIocQueryCtrl = Iowr(36, 68); // was Iowr(36, 64) — rejected on every control
    private static readonly ulong VidIocQueryMenu = Iowr(37, 44);
    private static readonly ulong VidIocGCtrl = Iowr(27, 8);
    private static readonly ulong VidIocSCtrl = Iowr(28, 8);

    private const int O_RDWR = 2;

    // Control types (videodev2.h:1841-1849).
    private const uint CtrlTypeInteger = 1;
    private const uint CtrlTypeBoolean = 2;
    private const uint CtrlTypeMenu = 3;
    private const uint CtrlTypeButton = 4;
    private const uint CtrlTypeCtrlClass = 6;
    private const uint CtrlTypeIntegerMenu = 9;
    private const uint CtrlTypeCompoundMask = 0x0100;

    // Queryctrl flags (videodev2.h:1933-1948). UPDATE (0x008) and SLIDER (0x020) have no counterpart in
    // the shared state model and are ignored; DISABLED entries are skipped from the catalog entirely.
    private const uint FlagDisabled = 0x001;
    private const uint FlagGrabbed = 0x002;
    private const uint FlagReadOnly = 0x004;
    private const uint FlagInactive = 0x010;
    private const uint FlagWriteOnly = 0x040;
    private const uint FlagVolatile = 0x080;
    private const uint FlagNextCtrl = 0x80000000;

    // The walk seeds with NEXT_CTRL and re-seeds with the returned id masked back into CID space.
    private const uint IdMask = 0x0FFFFFFF;

    // Canonical CIDs (v4l2-controls.h) — the corrected table. Eight of the sixteen CIDs hardcoded by the
    // old enum backend were wrong against this UAPI (doc §1.1 result 5); Roll has no mainline CID at all,
    // so Linux never reports it.
    private const uint CidPan = 0x009A0908;
    private const uint CidTilt = 0x009A0909;
    private const uint CidZoom = 0x009A090D;
    private const uint CidFocus = 0x009A090A;
    private const uint CidIris = 0x009A0911;
    private const uint CidExposure = 0x009A0902;
    private const uint CidBrightness = 0x00980900;
    private const uint CidContrast = 0x00980901;
    private const uint CidSaturation = 0x00980902;
    private const uint CidHue = 0x00980903;
    private const uint CidGamma = 0x00980910;
    private const uint CidSharpness = 0x0098091B;
    private const uint CidWhiteBalance = 0x0098091A;
    private const uint CidBacklightCompensation = 0x0098091C;
    private const uint CidGain = 0x00980913;

    // The Linux-only auto controls (not in the canonical table — they are first-class catalog entries via
    // driver-label normalization).
    private const uint CidWhiteBalanceAuto = 0x0098090C; // BOOL: 1 = auto
    private const uint CidFocusAuto = 0x009A090C;        // BOOL: 1 = auto
    private const uint CidExposureAuto = 0x009A0901;     // MENU of v4l2_exposure_auto_type

    // v4l2_exposure_auto_type (v4l2-controls.h:973-978): manual is EXACTLY 1; auto is any of
    // {0 = auto, 2 = shutter priority, 3 = aperture priority}. Engaging auto therefore WRITES 0 —
    // writing 3 would engage aperture priority, and an is-auto check keyed on a specific mode misreads
    // a device sitting at plain auto (0) as manual.
    private const int ExposureAutoTypeAuto = 0;
    private const int ExposureAutoTypeManual = 1;

    // Canonical controls in catalog order (user class, then camera class — the order the walk returns).
    private static readonly (string Name, uint Cid)[] CanonicalControls =
    [
        (CameraControlNames.Brightness, CidBrightness),
        (CameraControlNames.Contrast, CidContrast),
        (CameraControlNames.Saturation, CidSaturation),
        (CameraControlNames.Hue, CidHue),
        (CameraControlNames.Gamma, CidGamma),
        (CameraControlNames.Sharpness, CidSharpness),
        (CameraControlNames.WhiteBalance, CidWhiteBalance),
        (CameraControlNames.BacklightCompensation, CidBacklightCompensation),
        (CameraControlNames.Gain, CidGain),
        (CameraControlNames.Pan, CidPan),
        (CameraControlNames.Tilt, CidTilt),
        (CameraControlNames.Focus, CidFocus),
        (CameraControlNames.Iris, CidIris),
        (CameraControlNames.Exposure, CidExposure),
        (CameraControlNames.Zoom, CidZoom),
    ];

    private static readonly Dictionary<uint, string> CanonicalCidToName =
        CanonicalControls.ToDictionary(entry => entry.Cid, entry => entry.Name);

    // Conventional UVC auto pairs, all live-present on the C922: the control's descriptor carries the
    // partner's normalized Name, resolved through CameraControlCatalog.Find by the worker.
    private static readonly (uint Control, uint Partner, string PartnerName)[] AutoLinks =
    [
        (CidWhiteBalance, CidWhiteBalanceAuto, CameraControlNames.WhiteBalanceAuto),
        (CidFocus, CidFocusAuto, CameraControlNames.FocusAuto),
        (CidExposure, CidExposureAuto, CameraControlNames.ExposureAuto),
    ];

    // Mechanical PascalCase forms whose canonical vocabulary name differs — the comma variants of the
    // UVC driver labels ("White Balance, Automatic" → WhiteBalanceAutomatic → WhiteBalanceAuto) and the
    // one label whose canonical form reorders the words ("Auto Exposure" → ExposureAuto).
    private static readonly Dictionary<string, string> DriverLabelAliases = new(StringComparer.Ordinal)
    {
        ["WhiteBalanceAutomatic"] = CameraControlNames.WhiteBalanceAuto,
        ["FocusAutomaticContinuous"] = CameraControlNames.FocusAuto,
        ["AutoExposure"] = CameraControlNames.ExposureAuto,
        ["ExposureDynamicFramerate"] = CameraControlNames.ExposureAutoPriority,
    };

    // Defensive loop caps: the C922 walk is 19 entries; a buggy driver must not spin the worker.
    private const int MaxWalkEntries = 256;
    private const int MaxMenuItems = 256;

    // Cached fd per device index. Only the control worker's thread touches this (the interface contract),
    // so a plain dictionary needs no lock. Dropped by InvalidateHandles (Reconnect / device switch /
    // worker shutdown) and reopened once per job on a stale-handle error.
    private readonly Dictionary<int, int> _fdsByDeviceIndex = [];

    public CameraControlCatalog? EnumerateCatalog(WebcamWithIndex device)
    {
        var walk = (WalkResult?)null;
        var walked = RunWithFreshHandle(device.Index, fd =>
        {
            walk = WalkControls(fd, out var stale);
            // A stale walk (dead cached fd) must report failure so RunWithFreshHandle reopens once
            // and re-walks — reporting Ok here would dead-code the reopen and publish the fallback
            // ladder's estimated ranges instead of the device's real ones.
            return (!stale, stale);
        });

        if (!walked || walk == null)
            return null;

        // Fallback ladder for devices whose walk misses controls the device still accepts (the DJI
        // pattern): for every canonical CID the kernel NEVER returned, probe G_CTRL; a readable control
        // joins the catalog with the conventional UVC range and the EstimatedRange flag. CIDs the kernel
        // DID return — including DISABLED/CTRL_CLASS entries skipped above — are never resurrected: a
        // driver-disabled control must not come back with fabricated conventional ranges.
        foreach (var (name, cid) in CanonicalControls)
        {
            if (walk.ReturnedCids.Contains(cid))
                continue;

            if (!ReadControl(device.Index, cid, out _))
                continue;

            walk.Controls.Add(BuildConventionalDescriptor(name, cid));
        }

        if (walk.Controls.Count == 0)
            return null;

        return new CameraControlCatalog(device, DateTime.Now, walk.Controls);
    }

    public bool TryGetValue(WebcamWithIndex device, CameraControlDescriptor control, out int value, out bool isAuto)
    {
        value = 0;
        isAuto = false;

        // Buttons are trigger-style — no readable value (the worker skips their read-back anyway).
        if (control.Kind == CameraControlKind.Button || !TryGetCid(control, out var cid))
            return false;

        if (!ReadControl(device.Index, cid, out value))
            return false;

        // isAuto reports the control's LINKED AUTO PARTNER's state; for the auto controls themselves
        // (which carry no link) it is their own value under the auto contract. Everything else is simply
        // not auto-able and reads as manual.
        if (GetAutoStateCid(control, cid) is { } autoCid && ReadControl(device.Index, autoCid, out var autoValue))
            isAuto = IsAutoEngaged(autoCid, autoValue);

        return true;
    }

    public bool TrySetValue(WebcamWithIndex device, CameraControlDescriptor control, int value)
    {
        if (!TryGetCid(control, out var cid))
            return false;

        // Buttons apply as a single trigger write; the requested value is ignored and there is no
        // read-back (the worker reports the trigger without a clamp note).
        if (control.Kind == CameraControlKind.Button)
            return SetControl(device.Index, cid, 1);

        // Write-while-auto: disengage the linked auto partner FIRST — a value write against an INACTIVE
        // control silently no-ops (UVC cameras ship with their autos engaged, graying exactly these
        // sliders). A BOOL partner is disengaged with 0, the exposure-auto MENU with 1 (manual). If the
        // disengage write fails, the apply fails here — never write blind against an INACTIVE control.
        if (!string.IsNullOrEmpty(control.AutoControlName)
            && (AutoPartnerFor(cid) is not { } partner
                || !SetControl(device.Index, partner, DisengageValue(partner))))
        {
            return false;
        }

        return SetControl(device.Index, cid, value);
    }

    public bool TrySetAuto(WebcamWithIndex device, CameraControlDescriptor control, bool auto)
    {
        if (!TryGetCid(control, out var cid))
            return false;

        // A control with an auto link drives its partner; an auto control addressed directly (via
        // ControlName) drives itself; anything else has no auto mode — fail honestly.
        if (!TryGetAutoTargetCid(control, cid, out var target))
            return false;

        var writeValue = target == CidExposureAuto
                             ? (auto ? ExposureAutoTypeAuto : ExposureAutoTypeManual)
                             : auto ? 1 : 0;
        return SetControl(device.Index, target, writeValue);
    }

    public void InvalidateHandles(WebcamWithIndex device)
        => DropFd(device.Index);

    // ---------------------------------------------------------------- enumeration

    private sealed record WalkResult(HashSet<uint> ReturnedCids, List<CameraControlDescriptor> Controls);

    private static WalkResult WalkControls(int fd, out bool stale)
    {
        stale = false;
        var returnedCids = new HashSet<uint>();
        var controls = new List<CameraControlDescriptor>();

        var id = FlagNextCtrl;
        for (var entries = 0; entries < MaxWalkEntries; entries++)
        {
            var query = new V4L2QueryCtrl { Id = id };
            if (ioctl(fd, VidIocQueryCtrl, ref query) != 0)
            {
                // EINVAL etc. ends the walk. Only a failure on the very first query can mean a dead
                // handle (device replaced at the same index) — the caller then reopens once and retries.
                if (returnedCids.Count == 0 && IsStaleHandleError(Marshal.GetLastWin32Error()))
                    stale = true;
                break;
            }

            // Record EVERY returned CID — including CTRL_CLASS markers and entries skipped from the
            // catalog below: the fallback keys on "canonical CID the kernel never returned".
            returnedCids.Add(query.Id);

            if (query.Type != CtrlTypeCtrlClass
                && (query.Flags & FlagDisabled) == 0
                && BuildDescriptor(query, fd) is { } descriptor)
            {
                controls.Add(descriptor);
            }

            // Kernel contract: ids come back strictly ascending. Re-seed with the RETURNED id — never
            // returned+1, which silently dropped every control whose id equals the previous id + 1
            // (5 of 19 on the C922: Contrast, Sharpness, Exposure-Absolute, Tilt, Zoom).
            var returned = query.Id & IdMask;
            if (returned == (id & IdMask))
                break; // defensive: a driver making no progress would otherwise spin to the cap
            id = returned | FlagNextCtrl;
        }

        return new WalkResult(returnedCids, controls);
    }

    private static CameraControlDescriptor? BuildDescriptor(V4L2QueryCtrl query, int fd)
    {
        var kind = MapKind(query.Type);
        if (kind == null)
            return null;

        var cid = query.Id;
        return new CameraControlDescriptor
        {
            Name = CanonicalCidToName.TryGetValue(cid, out var canonical)
                       ? canonical
                       : NormalizeDriverName(query.Name),
            Kind = kind.Value,
            Minimum = query.Minimum,
            Maximum = query.Maximum,
            Step = Math.Max(query.Step, 1),
            DefaultValue = query.DefaultValue,
            States = MapStates(query.Flags),
            Items = kind is CameraControlKind.Menu or CameraControlKind.IntegerMenu
                        ? QueryMenuItems(fd, cid, kind.Value)
                        : [],
            AutoControlName = AutoPartnerName(cid),
            BackendId = FormatBackendId(cid),
        };
    }

    private static CameraControlKind? MapKind(uint type)
    {
        if ((type & CtrlTypeCompoundMask) != 0)
            return null; // compound controls have no representation here

        return type switch
        {
            CtrlTypeInteger => CameraControlKind.Integer,
            CtrlTypeBoolean => CameraControlKind.Boolean,
            CtrlTypeMenu => CameraControlKind.Menu,
            CtrlTypeButton => CameraControlKind.Button,
            CtrlTypeIntegerMenu => CameraControlKind.IntegerMenu,
            // 5 INTEGER64, 6 CTRL_CLASS and 7 STRING / 8 BITMASK are skipped (CTRL_CLASS is filtered
            // before this call; the others cannot round-trip through the shared int surface).
            _ => null,
        };
    }

    private static CameraControlStates MapStates(uint flags)
    {
        var states = CameraControlStates.None;
        if ((flags & FlagReadOnly) != 0)
            states |= CameraControlStates.ReadOnly;
        if ((flags & FlagWriteOnly) != 0)
            states |= CameraControlStates.WriteOnly;
        if ((flags & FlagInactive) != 0)
            states |= CameraControlStates.Inactive;
        if ((flags & FlagVolatile) != 0)
            states |= CameraControlStates.Volatile;
        if ((flags & FlagGrabbed) != 0)
            states |= CameraControlStates.Grabbed;
        return states;
    }

    // VIDIOC_QUERYMENU expansion. An empty item list is legal (uvcvideo returns -EINVAL for the C922's
    // Auto Exposure menu) — the surface then shows raw values. MENU items carry the query index as their
    // value; INTEGER_MENU items carry the driver's own integer (the union's s64 value) and label it with
    // that value, because the integers may be non-contiguous and a position cannot stand in for them.
    private static CameraControlItem[] QueryMenuItems(int fd, uint cid, CameraControlKind kind)
    {
        var items = new List<CameraControlItem>();
        for (var index = 0u; index < MaxMenuItems; index++)
        {
            if (kind == CameraControlKind.Menu)
            {
                var menu = new V4L2QueryMenuName { Id = cid, Index = index };
                if (ioctl(fd, VidIocQueryMenu, ref menu) != 0)
                    break; // EINVAL: end of items — or no items at all, which is fine
                items.Add(new CameraControlItem((int)index, menu.Name.Trim()));
            }
            else
            {
                var menu = new V4L2QueryMenuValue { Id = cid, Index = index };
                if (ioctl(fd, VidIocQueryMenu, ref menu) != 0)
                    break;
                items.Add(new CameraControlItem((int)menu.Value, menu.Value.ToString()));
            }
        }

        return [.. items];
    }

    private static CameraControlDescriptor BuildConventionalDescriptor(string name, uint cid)
    {
        var (min, max, step, defaultValue) = GetConventionalRange(name);
        return new CameraControlDescriptor
        {
            Name = name,
            Kind = CameraControlKind.Integer,
            Minimum = min,
            Maximum = max,
            Step = step,
            DefaultValue = defaultValue,
            States = CameraControlStates.EstimatedRange,
            AutoControlName = AutoPartnerName(cid),
            BackendId = FormatBackendId(cid),
        };
    }

    private static (int Min, int Max, int Step, int Default) GetConventionalRange(string name)
    {
        // UVC class conventions for devices that accept a control but never reported a range — carried
        // over from the old enum backend's estimated ranges, now clearly flagged EstimatedRange.
        return name switch
        {
            CameraControlNames.Pan or CameraControlNames.Tilt => (-11520, 11520, 64, 0), // 1/64 degree
            CameraControlNames.Zoom => (100, 400, 1, 100),                               // 100 = 1x
            CameraControlNames.Focus => (0, 255, 1, 80),
            CameraControlNames.Iris => (0, 10, 1, 5),
            CameraControlNames.Exposure => (1, 5000, 1, 166),                            // 0.1 ms
            CameraControlNames.Saturation => (0, 255, 1, 64),
            CameraControlNames.Hue => (-180, 180, 1, 0),
            CameraControlNames.WhiteBalance => (2500, 6500, 1, 4600),                    // kelvin
            CameraControlNames.BacklightCompensation => (0, 2, 1, 0),
            CameraControlNames.Gain => (0, 128, 1, 32),
            _ => (0, 255, 1, 128), // Brightness / Contrast / Gamma / Sharpness and anything unmapped
        };
    }

    // ---------------------------------------------------------------- naming

    // Canonical-table CIDs map directly onto the shared vocabulary; every other control normalizes its
    // driver label into the same PascalCase form, so ControlName/AutoControlName always match descriptor
    // names — never raw driver labels ("White Balance, Automatic" → WhiteBalanceAuto).
    private static string NormalizeDriverName(string driverLabel)
    {
        var mechanical = new StringBuilder(driverLabel.Length);
        foreach (var token in SplitLabelTokens(driverLabel))
        {
            mechanical.Append(char.ToUpperInvariant(token[0]));
            mechanical.Append(token[1..].ToLowerInvariant());
        }

        if (mechanical.Length == 0)
            return driverLabel.Trim(); // degenerate label — keep it verbatim so Find still has a name

        var normalized = mechanical.ToString();
        return DriverLabelAliases.TryGetValue(normalized, out var alias) ? alias : normalized;
    }

    private static IEnumerable<string> SplitLabelTokens(string label)
    {
        var start = -1;
        for (var i = 0; i <= label.Length; i++)
        {
            if (i < label.Length && char.IsLetterOrDigit(label[i]))
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                yield return label[start..i];
                start = -1;
            }
        }
    }

    // ---------------------------------------------------------------- auto links

    private static bool IsAutoControlCid(uint cid)
        => cid is CidWhiteBalanceAuto or CidFocusAuto or CidExposureAuto;

    private static uint? AutoPartnerFor(uint cid)
    {
        foreach (var (control, partner, _) in AutoLinks)
        {
            if (control == cid)
                return partner;
        }

        return null;
    }

    private static string AutoPartnerName(uint cid)
    {
        foreach (var (control, _, partnerName) in AutoLinks)
        {
            if (control == cid)
                return partnerName;
        }

        return "";
    }

    private static uint? GetAutoStateCid(CameraControlDescriptor control, uint cid)
    {
        if (!string.IsNullOrEmpty(control.AutoControlName))
            return AutoPartnerFor(cid);

        return IsAutoControlCid(cid) ? cid : null;
    }

    private static bool TryGetAutoTargetCid(CameraControlDescriptor control, uint cid, out uint target)
    {
        if (!string.IsNullOrEmpty(control.AutoControlName) && AutoPartnerFor(cid) is { } partner)
        {
            target = partner;
            return true;
        }

        if (IsAutoControlCid(cid))
        {
            target = cid;
            return true;
        }

        target = 0;
        return false;
    }

    private static bool IsAutoEngaged(uint autoCid, int autoValue)
        // BOOL auto partners: 1 = auto / 0 = manual. The exposure-auto MENU: manual is EXACTLY 1 — auto
        // is any of {0 auto, 2 shutter priority, 3 aperture priority}.
        => autoCid == CidExposureAuto ? autoValue != ExposureAutoTypeManual : autoValue != 0;

    private static int DisengageValue(uint partnerCid)
        => partnerCid == CidExposureAuto ? ExposureAutoTypeManual : 0;

    // ---------------------------------------------------------------- device I/O

    private int GetOrOpenFd(int deviceIndex)
    {
        if (_fdsByDeviceIndex.TryGetValue(deviceIndex, out var cached) && cached >= 0)
            return cached;

        var fd = open($"/dev/video{deviceIndex}", O_RDWR);
        if (fd < 0)
        {
            Log.Debug($"V4L2 camera controls: cannot open /dev/video{deviceIndex} "
                      + $"(errno {Marshal.GetLastWin32Error()}).");
            return -1;
        }

        _fdsByDeviceIndex[deviceIndex] = fd;
        return fd;
    }

    private void DropFd(int deviceIndex)
    {
        if (!_fdsByDeviceIndex.Remove(deviceIndex, out var fd))
            return;

        if (fd >= 0)
            _ = close(fd);
    }

    /// <summary>
    /// Runs one job against the device. If the job hits a stale-handle error (device replaced at the same
    /// index, unplugged), the cached fd is dropped and the job runs ONCE more against a fresh open — a
    /// persistent error never fails forever, and a normal EINVAL (absent control, end of menu, end of
    /// walk) never triggers a pointless reopen.
    /// </summary>
    private bool RunWithFreshHandle(int deviceIndex, Func<int, (bool Ok, bool Stale)> job)
    {
        var fd = GetOrOpenFd(deviceIndex);
        if (fd < 0)
            return false;

        var (ok, stale) = job(fd);
        if (ok || !stale)
            return ok;

        DropFd(deviceIndex);
        fd = GetOrOpenFd(deviceIndex);
        if (fd < 0)
            return false;

        (ok, _) = job(fd);
        return ok;
    }

    private bool ReadControl(int deviceIndex, uint cid, out int value)
    {
        var read = (Ok: false, Value: 0);
        RunWithFreshHandle(deviceIndex, fd =>
        {
            read.Ok = GetControlValue(fd, cid, out read.Value, out var errno);
            return (read.Ok, IsStaleHandleError(errno));
        });

        value = read.Value;
        return read.Ok;
    }

    private bool SetControl(int deviceIndex, uint cid, int value)
        => RunWithFreshHandle(deviceIndex, fd =>
        {
            var ok = SetControlValue(fd, cid, value, out var errno);
            return (ok, IsStaleHandleError(errno));
        });

    private static bool GetControlValue(int fd, uint cid, out int value, out int errno)
    {
        var control = new V4L2Control { Id = cid };
        var ok = ioctl(fd, VidIocGCtrl, ref control) == 0;
        errno = ok ? 0 : Marshal.GetLastWin32Error();
        value = control.Value;
        return ok;
    }

    private static bool SetControlValue(int fd, uint cid, int value, out int errno)
    {
        var control = new V4L2Control { Id = cid, Value = value };
        var ok = ioctl(fd, VidIocSCtrl, ref control) == 0;
        errno = ok ? 0 : Marshal.GetLastWin32Error();
        return ok;
    }

    // EBADF: the fd was closed out from under us. ENODEV/EIO: the device went away or its USB link is
    // failing — in all three a fresh open is the only sane response. EINVAL (absent control, end of
    // walk/menu, out-of-range write) and the rest are ordinary protocol outcomes, never stale-handle.
    private static bool IsStaleHandleError(int errno)
        => errno is 9 /* EBADF */ or 19 /* ENODEV */ or 5 /* EIO */;

    private static string FormatBackendId(uint cid)
        => $"v4l2:0x{cid:X8}";

    private static bool TryGetCid(CameraControlDescriptor control, out uint cid)
    {
        cid = 0;
        const string prefix = "v4l2:0x";
        return control.BackendId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && uint.TryParse(control.BackendId[prefix.Length..], NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out cid);
    }

    // v4l2_queryctrl — 68 bytes packed (u32 id, u32 type, u8 name[32], s32 min/max/step/default,
    // u32 flags, u32 reserved[2]).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct V4L2QueryCtrl
    {
        public uint Id;
        public uint Type;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public int Minimum;
        public int Maximum;
        public int Step;
        public int DefaultValue;
        public uint Flags;
        public uint Reserved0;
        public uint Reserved1;
    }

    // v4l2_control — 8 bytes (u32 id, s32 value).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct V4L2Control
    {
        public uint Id;
        public int Value;
    }

    // v4l2_querymenu — 44 bytes packed (u32 id, u32 index, union { u8 name[32]; s64 value }, u32
    // reserved). The union is modeled as two views of the same 44-byte layout; one QUERYMENU call reads
    // one view, so no overlay marshaling is needed — but BOTH views must marshal to the full 44 bytes,
    // which is why the value view carries the union's remaining bytes as explicit padding.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct V4L2QueryMenuName
    {
        public uint Id;
        public uint Index;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct V4L2QueryMenuValue
    {
        public uint Id;
        public uint Index;
        public long Value;      // the union's s64 view (INTEGER_MENU)
        public uint UnionPad0;  // the union is 32 bytes wide — the s64 leaves 24 bytes of tail
        public uint UnionPad1;
        public uint UnionPad2;
        public uint UnionPad3;
        public uint UnionPad4;
        public uint UnionPad5;
        public uint Reserved;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2QueryCtrl arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2QueryMenuName arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2QueryMenuValue arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2Control arg);
}

/// <summary>
/// Registers the V4L2 backend with the shared registry when this package's code first loads — the
/// registry then dispatches to it wherever <see cref="OperatingSystem.IsLinux"/> holds (and to whatever
/// the other platforms registered elsewhere). No backend registered → the worker degrades to honest
/// "device controls are not available" messaging.
/// </summary>
internal static class V4L2CameraControlsRegistration
{
    // CA2255: a module initializer in a library is intentional here — the same idiom
    // VideoExportRegistration already uses for the FFmpeg encoder factory.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
        => CameraControlBackendRegistry.Register(OperatingSystem.IsLinux, () => new V4L2CameraControls());
#pragma warning restore CA2255
}
