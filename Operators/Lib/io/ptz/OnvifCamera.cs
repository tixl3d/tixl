
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Xml.Linq;
using OpenCvSharp;
using SharpDX;
using T3.Core.Animation;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.io.ptz
{
    [Guid("8b23c93b-3b45-4c9b-9c23-4d5e6f7a8b9c")]
    public class OnvifCamera : Instance<OnvifCamera>, IStatusProvider, ICustomDropdownHolder
    {
        [Output(Guid = "e5d0c2f1-4b3a-4829-9c5d-1e6f7a8b9c0d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Texture2D> Texture = new();
        
        [Output(Guid = "9c34d04c-4c56-4d0c-ad34-5e6f7a8b9c0d")]
        public readonly Slot<bool> IsConnected = new();

        [Output(Guid = "0d45e15d-5d67-4e1d-be45-6f7a8b9c0d1e", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Vector3> CurrentPtz = new();

        public OnvifCamera()
        {
            IsConnected.UpdateAction += Update;
            CurrentPtz.UpdateAction += Update;
            Texture.UpdateAction += Update;
            
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _disposeCts = new CancellationTokenSource();
        }

        private void Update(EvaluationContext context)
        {
            _printToLog = PrintToLog.GetValue(context);

            // --- Network Interface Management ---
            var localIpString = LocalIpAddress.GetValue(context);
            
            if (string.IsNullOrEmpty(localIpString))
            {
                if (Playback.RunTimeInSecs - _lastNetworkRefreshTime > 5.0)
                {
                    _lastNetworkRefreshTime = Playback.RunTimeInSecs;
                    _networkInterfaces = GetNetworkInterfaces();
                }
            }

            var address = Address.GetValue(context);
            var username = Username.GetValue(context);
            var password = Password.GetValue(context);
            var shouldConnect = Connect.GetValue(context);
            
            // PTZ Inputs
            var targetPan = Pan.GetValue(context);
            var targetTilt = Tilt.GetValue(context);
            var targetZoom = Zoom.GetValue(context);
            var doMove = Move.GetValue(context);


            var discover = Discover.GetValue(context);
            if (discover != _isDiscovering)
            {
                if (discover) StartDiscovery(localIpString);
                else StopDiscovery();
            }

            if (shouldConnect != _lastConnectState)
            {
                if (shouldConnect)
                {
                    if (_printToLog) Log.Debug($"OnvifCamera: Connecting to {address}...", this);
                    StartCaptureThread(address, username, password);
                }
                else
                {
                    if (_printToLog) Log.Debug("OnvifCamera: Disconnecting...", this);
                    StopCaptureThread();
                }
                _lastConnectState = shouldConnect;
            }

            // --- Smoothing ---
            _dampedPan = targetPan;
            _dampedTilt = targetTilt;
            _dampedZoom = targetZoom;

            // --- PTZ Control ---
            if (_connected && doMove)
            {
                var valuesChanged = Math.Abs(_dampedPan - _lastSentPan) > 0.001f ||
                                    Math.Abs(_dampedTilt - _lastSentTilt) > 0.001f ||
                                    Math.Abs(_dampedZoom - _lastSentZoom) > 0.001f;

                var isRisingEdge = !_wasMoving;
                // Limit update rate to avoid flooding the camera (e.g. max 10 times per second)
                var isThrottled = (Playback.RunTimeInSecs - _lastPtzMoveTime) < 0.1;

                if (isRisingEdge || (valuesChanged && !isThrottled))
                {
                    if (!string.IsNullOrEmpty(_ptzServiceUrl) && !string.IsNullOrEmpty(_profileToken) && _ptzSupported)
                    {
                        if (!_isSendingPtz)
                        {
                            _lastSentPan = _dampedPan;
                            _lastSentTilt = _dampedTilt;
                            _lastSentZoom = _dampedZoom;
                            _lastPtzMoveTime = Playback.RunTimeInSecs;
                            _isSendingPtz = true;

                            var p = _dampedPan;
                            var t = _dampedTilt;
                            var z = _dampedZoom;

                            // Fire and forget to avoid blocking UI
                            Task.Run(async () => 
                            {
                                try 
                                {
                                    await SendPtzMoveAsync(p, t, z, username, password, _disposeCts.Token);
                                }
                                finally 
                                {
                                    _isSendingPtz = false;
                                }
                            });
                        }
                    }
                    else
                    {
                        // If we are connected via RTSP but haven't discovered PTZ services yet
                        if (isRisingEdge && _printToLog) Log.Warning("OnvifCamera: Cannot move. PTZ Service or Profile Token not resolved, or PTZ not supported.", this);
                    }
                }
            }
            _wasMoving = doMove;

            // Update outputs
            IsConnected.Value = _connected;
            
            // --- PTZ Status Polling ---
            if (_connected && !string.IsNullOrEmpty(_ptzServiceUrl) && !string.IsNullOrEmpty(_profileToken) && _ptzSupported)
            {
                var pollInterval = _ptzErrorBackoff ? 2.0 : 0.1;
                if (Playback.RunTimeInSecs - _lastPtzPollTime > pollInterval && !_isPollingPtz)
                {
                    _lastPtzPollTime = Playback.RunTimeInSecs;
                    _isPollingPtz = true;
                    Task.Run(async () =>
                             {
                                 try
                                 {
                                     await UpdatePtzStatusAsync(username, password, _disposeCts.Token);
                                 }
                                 finally
                                 {
                                     _isPollingPtz = false;
                                 }
                             });
                }
                
                // Use the polled value
                if (doMove)
                {
                    CurrentPtz.Value = new Vector3(targetPan, targetTilt, targetZoom);
                }
                else
                {
                    lock (_lockObject)
                    {
                        CurrentPtz.Value = _currentPtzValue;
                    }
                }
            }
            else
            {
                // Fallback to target values if not connected or no PTZ
                CurrentPtz.Value = new Vector3(targetPan, targetTilt, targetZoom);
            }

            // --- Texture Update ---
            lock (_lockObject)
            {
                if (_sharedBgraMat != null && !_sharedBgraMat.Empty())
                {
                    UploadMatToGpu(_sharedBgraMat);
                    Texture.Value = _gpuTexture;
                }
                else
                {
                    Texture.Value = null;
                }
            }
        }
        
        protected override void Dispose(bool isDisposing)
        {
            if (!isDisposing)
                return;

            _disposeCts.Cancel();
            StopDiscovery();
            StopCaptureThread();
            Utilities.Dispose(ref _gpuTexture);
            lock (_lockObject)
            {
                _sharedBgraMat?.Dispose();
                _sharedBgraMat = null;
            }
            _httpClient?.Dispose();
            _disposeCts.Dispose();
        }

        #region Video Capture Logic
        private void StartCaptureThread(string address, string username, string password)
        {
            if (_captureThread != null) return;
            if (_printToLog) Log.Debug("OnvifCamera: Starting capture thread.", this);
            
            _ptzSupported = true;
            _ptzErrorBackoff = false;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            _captureThread = new Thread(() => CaptureLoop(address, username, password, token))
            {
                IsBackground = true,
                Name = "OnvifCaptureThread"
            };
            _captureThread.Start();
        }

        private void StopCaptureThread()
        {
            if (_captureThread == null) return;
            if (_printToLog) Log.Debug("OnvifCamera: Stopping capture thread.", this);

            _cancellationTokenSource?.Cancel();
            
            if (!_captureThread.Join(TimeSpan.FromSeconds(2)))
            {
                Log.Warning("OnvifCamera: Capture thread did not finish in time. Leaking VideoCapture to avoid crash.", this);
                // Do not dispose _capture, as the thread is still using it.
            }
            else
            {
                _capture?.Dispose();
                _capture = null;
            }
            
            _captureThread = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _connected = false;
            SetStatus("Disconnected");
        }

        private void CaptureLoop(string address, string username, string password, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    SetStatus($"Connecting to {address}...");
                    if (_printToLog) Log.Debug($"OnvifCamera: Attempting connection to {address}", this);

                    string streamUri = address?.Trim() ?? string.Empty;
                    string host = GetConnectionAuthority(streamUri);

                    if (!string.IsNullOrEmpty(host))
                    {
                        SetStatus($"Probing ONVIF at {host}...");
                        var discoveredUri = DiscoverOnvifServices(host, username, password, token);
                        if (!string.IsNullOrEmpty(discoveredUri) && !streamUri.Contains("://"))
                        {
                            streamUri = discoveredUri;
                            if (_printToLog) Log.Debug($"OnvifCamera: Discovered Stream URI: {streamUri}", this);
                        }
                    }

                    if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    {
                        if (streamUri.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) && !streamUri.Contains('@'))
                        {
                            var uriWithoutScheme = streamUri.Substring(7);
                            streamUri = $"rtsp://{username}:{password}@{uriWithoutScheme}";
                        }
                    }

                    if (!streamUri.Contains("://"))
                    {
                        if (_printToLog) Log.Warning("OnvifCamera: Invalid RTSP URL. Retrying in 5s...", this);
                        SetStatus("Invalid URL. Retrying...");
                        if (WaitCancelled(5000, token)) return;
                        continue;
                    }

                    if (token.IsCancellationRequested) return;

                    _capture = new VideoCapture(streamUri, VideoCaptureAPIs.FFMPEG);
                    
                    if (token.IsCancellationRequested || !_capture.IsOpened())
                    {
                        var msg = $"Error: Failed to open stream. Retrying in 5s...";
                        SetStatus(msg);
                        if (_printToLog) Log.Warning(msg, this);
                        _capture?.Dispose();
                        _capture = null;
                        if (WaitCancelled(5000, token)) return;
                        continue;
                    }

                    _connected = true;
                    SetStatus("Streaming");
                    if (_printToLog) Log.Debug("OnvifCamera: Stream opened.", this);

                    using var frame = new Mat();
                    int errorCount = 0;

                    while (!token.IsCancellationRequested)
                    {
                        if (_capture.Read(frame) && !frame.Empty())
                        {
                            errorCount = 0;
                            lock (_lockObject)
                            {
                                if (token.IsCancellationRequested) break;
                                if (_sharedBgraMat == null || _sharedBgraMat.IsDisposed || _sharedBgraMat.Width != frame.Width || _sharedBgraMat.Height != frame.Height)
                                {
                                    _sharedBgraMat?.Dispose();
                                    _sharedBgraMat = new Mat(frame.Rows, frame.Cols, MatType.CV_8UC4);
                                }
                                Cv2.CvtColor(frame, _sharedBgraMat, ColorConversionCodes.BGR2BGRA);
                            }
                        }
                        else
                        {
                            // Frame read failed
                            errorCount++;
                            if (errorCount > 10) // Allow a few dropped frames before reconnecting
                            {
                                Log.Warning("OnvifCamera: Stream lost. Reconnecting...", this);
                                break;
                            }
                            Thread.Sleep(10);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Log.Error($"OnvifCamera stream error: {e.Message}", this);
                        SetStatus($"Error: {e.Message}");
                    }
                }
                finally
                {
                    _connected = false;
                    _capture?.Dispose();
                    _capture = null;
                }

                if (!token.IsCancellationRequested)
                {
                    // Wait before reconnecting
                    if (WaitCancelled(2000, token)) return;
                }
            }
            SetStatus("Disconnected");
        }

        private bool WaitCancelled(int ms, CancellationToken token)
        {
            try { Task.Delay(ms, token).Wait(token); return false; }
            catch { return true; }
        }

        private void UploadMatToGpu(Mat mat)
        {
            if (mat.Empty()) return;

            var width = mat.Width;
            var height = mat.Height;

            if (_gpuTexture == null || _gpuTexture.Description.Width != width || _gpuTexture.Description.Height != height)
            {
                Utilities.Dispose(ref _gpuTexture);

                var texDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };
                _gpuTexture = Texture2D.CreateTexture2D(texDesc);
            }

            var dataBox = new DataBox(mat.Data, (int)mat.Step(), 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _gpuTexture);
        }

        private string GetConnectionAuthority(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (address.Contains("://")) 
            {
                // If it's a URL (e.g. RTSP), extract just the Host (IP), ignoring the RTSP port (554) 
                // because we want to connect to the ONVIF HTTP service (usually 80/8080).
                try { return new Uri(address).Host; } catch { return null; }
            }
            // If it's just an address (IP or IP:Port), return as is to preserve non-standard ONVIF ports.
            return address;
        }

        private string DiscoverOnvifServices(string host, string username, string password, CancellationToken token)
        {
            try
            {
                // 0. Sync Time (to avoid 401/500 on auth)
                SyncTime(host, token);

                // 1. GetCapabilities to find Media, PTZ, and Imaging Services
                var deviceServiceUrl = $"http://{host}/onvif/device_service";
                var (mediaUrl, ptzUrl) = GetOnvifCapabilities(deviceServiceUrl, username, password, true, token);
                
                if (string.IsNullOrEmpty(mediaUrl))
                {
                    // Fallback: Try without auth (some cameras don't like auth on GetCapabilities)
                    if (_printToLog) Log.Debug($"OnvifCamera: GetCapabilities failed with auth. Retrying without auth...", this);
                    (mediaUrl, ptzUrl) = GetOnvifCapabilities(deviceServiceUrl, null, null, false, token);
                }

                if (string.IsNullOrEmpty(mediaUrl))
                {
                    if (_printToLog) Log.Debug($"OnvifCamera: Could not find Media Service at {deviceServiceUrl}", this);
                    return null;
                }

                _ptzServiceUrl = ptzUrl;
                if (_printToLog && !string.IsNullOrEmpty(_ptzServiceUrl)) Log.Debug($"OnvifCamera: Found PTZ Service at {_ptzServiceUrl}", this);

                // 2. GetProfiles to find a profile token and video source token
                _profileToken = GetProfileToken(mediaUrl, username, password, token);
                
                if (string.IsNullOrEmpty(_profileToken))
                {
                    if (_printToLog) Log.Debug("OnvifCamera: Could not find any Media Profile", this);
                    return null;
                }

                // 4. GetStreamUri
                return GetOnvifStreamUri(mediaUrl, _profileToken, username, password, token);
            }
            catch (Exception e)
            {
                if (_printToLog) Log.Debug($"OnvifCamera: Discovery exception: {e.Message}", this);
                return null;
            }
        }

        private (string mediaUrl, string ptzUrl) GetOnvifCapabilities(string serviceUrl, string username, string password, bool useAuth, CancellationToken token)
        {
            var body = @"<GetCapabilities xmlns=""http://www.onvif.org/ver10/device/wsdl""><Category>All</Category></GetCapabilities>";
            var response = SendSoapRequestAsync(serviceUrl, body, useAuth ? username : null, useAuth ? password : null, "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", token).GetAwaiter().GetResult();
            
            string mediaUrl = null;
            string ptzUrl = null;

            if (response != null)
            {
                try
                {
                    var xdoc = XDocument.Parse(response);
                    var ns = XNamespace.Get("http://www.onvif.org/ver10/schema");

                    mediaUrl = xdoc.Descendants(ns + "Media").FirstOrDefault()?.Element(ns + "XAddr")?.Value 
                               ?? xdoc.Descendants(ns + "Media").FirstOrDefault()?.Element("XAddr")?.Value; // Try without namespace if failed

                    ptzUrl = xdoc.Descendants(ns + "PTZ").FirstOrDefault()?.Element(ns + "XAddr")?.Value
                             ?? xdoc.Descendants(ns + "PTZ").FirstOrDefault()?.Element("XAddr")?.Value;
                }
                catch (Exception e)
                {
                    if (_printToLog) Log.Debug($"OnvifCamera: Error parsing capabilities: {e.Message}", this);
                }
            }

            // Fallback: If Media not found (e.g. 'All' failed or didn't return it), try requesting it specifically
            if (string.IsNullOrEmpty(mediaUrl))
            {
                var bodyMedia = @"<GetCapabilities xmlns=""http://www.onvif.org/ver10/device/wsdl""><Category>Media</Category></GetCapabilities>";
                var responseMedia = SendSoapRequestAsync(serviceUrl, bodyMedia, useAuth ? username : null, useAuth ? password : null, "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", token).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(responseMedia))
                {
                    try
                    {
                        var xdoc = XDocument.Parse(responseMedia);
                        var ns = XNamespace.Get("http://www.onvif.org/ver10/schema");
                        mediaUrl = xdoc.Descendants(ns + "Media").FirstOrDefault()?.Element(ns + "XAddr")?.Value 
                                   ?? xdoc.Descendants(ns + "Media").FirstOrDefault()?.Element("XAddr")?.Value;
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            // Fallback: If PTZ not found
            if (string.IsNullOrEmpty(ptzUrl))
            {
                var bodyPtz = @"<GetCapabilities xmlns=""http://www.onvif.org/ver10/device/wsdl""><Category>PTZ</Category></GetCapabilities>";
                var responsePtz = SendSoapRequestAsync(serviceUrl, bodyPtz, useAuth ? username : null, useAuth ? password : null, "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", token).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(responsePtz))
                {
                    try
                    {
                        var xdoc = XDocument.Parse(responsePtz);
                        var ns = XNamespace.Get("http://www.onvif.org/ver10/schema");
                        ptzUrl = xdoc.Descendants(ns + "PTZ").FirstOrDefault()?.Element(ns + "XAddr")?.Value
                                 ?? xdoc.Descendants(ns + "PTZ").FirstOrDefault()?.Element("XAddr")?.Value;
                    }
                    catch { }
                }
            }

            return (mediaUrl, ptzUrl);
        }

        private string GetProfileToken(string serviceUrl, string username, string password, CancellationToken token)
        {
            var body = @"<GetProfiles xmlns=""http://www.onvif.org/ver10/media/wsdl""/>";
            var response = SendSoapRequestAsync(serviceUrl, body, username, password, "http://www.onvif.org/ver10/media/wsdl/GetProfiles", token).GetAwaiter().GetResult();
            if (response == null) return null;

            var xdoc = XDocument.Parse(response);
            var ns = XNamespace.Get("http://www.onvif.org/ver10/media/wsdl");

            var profile = xdoc.Descendants(ns + "Profiles").FirstOrDefault();
            if (profile == null) return null;

            return profile.Attribute("token")?.Value;
        }

        private string GetOnvifStreamUri(string serviceUrl, string profileToken, string username, string password, CancellationToken token)
        {
            var body = $@"<GetStreamUri xmlns=""http://www.onvif.org/ver10/media/wsdl""><StreamSetup><Stream xmlns=""http://www.onvif.org/ver10/schema"">RTP-Unicast</Stream><Transport xmlns=""http://www.onvif.org/ver10/schema""><Protocol>RTSP</Protocol></Transport></StreamSetup><ProfileToken>{profileToken}</ProfileToken></GetStreamUri>";
            var response = SendSoapRequestAsync(serviceUrl, body, username, password, "http://www.onvif.org/ver10/media/wsdl/GetStreamUri", token).GetAwaiter().GetResult();
            if (response == null) return null;

            var xdoc = XDocument.Parse(response);
            var ns = XNamespace.Get("http://www.onvif.org/ver10/media/wsdl");
            var nsSch = XNamespace.Get("http://www.onvif.org/ver10/schema");
            return xdoc.Descendants(ns + "MediaUri").FirstOrDefault()?.Element(nsSch + "Uri")?.Value;
        }

        private async Task SendPtzMoveAsync(float pan, float tilt, float zoom, string username, string password, CancellationToken token)
        {
            if (!_ptzSupported) return;

            try
            {
                // Clamp values to -1..1 for Pan/Tilt and 0..1 for Zoom (ONVIF standard generic space)
                // Note: Some cameras might use different ranges, but GenericSpace is usually normalized.
                var p = Math.Clamp(pan, -1f, 1f);
                var t = Math.Clamp(tilt, -1f, 1f);
                var z = Math.Clamp(zoom, 0f, 1f);

                var body = $@"<AbsoluteMove xmlns=""http://www.onvif.org/ver20/ptz/wsdl"">
                                <ProfileToken>{_profileToken}</ProfileToken>
                                <Position>
                                    <PanTilt x=""{p}"" y=""{t}"" space=""http://www.onvif.org/ver10/tptz/PanTiltSpaces/PositionGenericSpace"" xmlns=""http://www.onvif.org/ver10/schema""/>
                                    <Zoom x=""{z}"" space=""http://www.onvif.org/ver10/tptz/ZoomSpaces/PositionGenericSpace"" xmlns=""http://www.onvif.org/ver10/schema""/>
                                </Position>
                              </AbsoluteMove>";

                var response = await SendSoapRequestAsync(_ptzServiceUrl, body, username, password, "http://www.onvif.org/ver20/ptz/wsdl/AbsoluteMove", token);
                
                if (response != null && (response.Contains("PTZNotSupported") || response.Contains("ActionNotSupported")))
                {
                    if (_printToLog) Log.Warning("OnvifCamera: PTZ not supported by device (Move). Disabling PTZ.", this);
                    _ptzSupported = false;
                }
            }
            catch (Exception e)
            {
                if (_printToLog) Log.Warning($"OnvifCamera: PTZ Move failed: {e.Message}", this);
            }
        }

        private async Task UpdatePtzStatusAsync(string username, string password, CancellationToken token)
        {
            try
            {
                var body = $@"<GetStatus xmlns=""http://www.onvif.org/ver20/ptz/wsdl""><ProfileToken>{_profileToken}</ProfileToken></GetStatus>";
                var response = await SendSoapRequestAsync(_ptzServiceUrl, body, username, password, "http://www.onvif.org/ver20/ptz/wsdl/GetStatus", token);
                if (string.IsNullOrEmpty(response))
                {
                    _ptzErrorBackoff = true;
                    return;
                }

                if (response.Contains("PTZNotSupported") || response.Contains("ActionNotSupported"))
                {
                    if (_printToLog) Log.Warning("OnvifCamera: PTZ not supported by device. Disabling polling.", this);
                    _ptzSupported = false;
                    return;
                }

                _ptzErrorBackoff = false;
                var xdoc = XDocument.Parse(response);
                var ns = XNamespace.Get("http://www.onvif.org/ver10/schema");
                var position = xdoc.Descendants(ns + "Position").FirstOrDefault();
                
                if (position != null)
                {
                    var panTilt = position.Element(ns + "PanTilt");
                    var zoom = position.Element(ns + "Zoom");

                    float p = 0, t = 0, z = 0;
                    if (panTilt != null)
                    {
                        float.TryParse(panTilt.Attribute("x")?.Value, out p);
                        float.TryParse(panTilt.Attribute("y")?.Value, out t);
                    }
                    if (zoom != null)
                    {
                        float.TryParse(zoom.Attribute("x")?.Value, out z);
                    }
                    lock (_lockObject)
                    {
                        _currentPtzValue = new Vector3(p, t, z);
                    }
                }
            }
            catch 
            { 
                _ptzErrorBackoff = true;
            }
        }

        private void SyncTime(string host, CancellationToken token)
        {
            try 
            {
                var url = $"http://{host}/onvif/device_service";
                var body = @"<GetSystemDateAndTime xmlns=""http://www.onvif.org/ver10/device/wsdl""/>";
                // Send without auth first to get time
                var response = SendSoapRequestAsync(url, body, null, null, null, token).GetAwaiter().GetResult(); 
                
                if (string.IsNullOrEmpty(response)) return;

                var xdoc = XDocument.Parse(response);
                var ns = XNamespace.Get("http://www.onvif.org/ver10/schema");
                var utc = xdoc.Descendants(ns + "UTCDateTime").FirstOrDefault();
                
                if (utc != null)
                {
                    var time = utc.Element(ns + "Time");
                    var date = utc.Element(ns + "Date");
                    
                    if (time != null && date != null)
                    {
                        int y = int.Parse(date.Element(ns + "Year")?.Value ?? "0");
                        int m = int.Parse(date.Element(ns + "Month")?.Value ?? "0");
                        int d = int.Parse(date.Element(ns + "Day")?.Value ?? "0");
                        int h = int.Parse(time.Element(ns + "Hour")?.Value ?? "0");
                        int min = int.Parse(time.Element(ns + "Minute")?.Value ?? "0");
                        int s = int.Parse(time.Element(ns + "Second")?.Value ?? "0");
                        
                        var deviceTime = new DateTime(y, m, d, h, min, s, DateTimeKind.Utc);
                        _timeOffset = deviceTime - DateTime.UtcNow;
                        if (_printToLog) Log.Debug($"OnvifCamera: Time synced. Offset: {_timeOffset.TotalSeconds:F1}s", this);
                    }
                }
            }
            catch (Exception e)
            {
                if (_printToLog) Log.Debug($"OnvifCamera: Time sync failed: {e.Message}", this);
            }
        }

        private async Task<string> SendSoapRequestAsync(string url, string body, string username, string password, string action, CancellationToken token)
        {
            var header = "";
            if (!string.IsNullOrEmpty(username))
            {
                var created = (DateTime.UtcNow + _timeOffset).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var nonce = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
                var nonceBase64 = Convert.ToBase64String(nonce);
                
                var createdBytes = Encoding.UTF8.GetBytes(created);
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var combined = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
                System.Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
                System.Buffer.BlockCopy(createdBytes, 0, combined, nonce.Length, createdBytes.Length);
                System.Buffer.BlockCopy(passwordBytes, 0, combined, nonce.Length + createdBytes.Length, passwordBytes.Length);
                
                var digest = Convert.ToBase64String(SHA1.HashData(combined));

                header = $@"<s:Header><wsse:Security xmlns:wsse=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"" xmlns:wsu=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd""><wsse:UsernameToken><wsse:Username>{username}</wsse:Username><wsse:Password Type=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"">{digest}</wsse:Password><wsse:Nonce EncodingType=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"">{nonceBase64}</wsse:Nonce><wsu:Created>{created}</wsu:Created></wsse:UsernameToken></wsse:Security></s:Header>";
            }

            var envelope = $@"<?xml version=""1.0"" encoding=""utf-8""?><s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"">{header}<s:Body xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">{body}</s:Body></s:Envelope>";

            using var content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml");
            if (!string.IsNullOrEmpty(action))
            {
                content.Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("action", $"\"{action}\""));
            }
            
            try 
            {
                using var result = await _httpClient.PostAsync(url, content, token);
                var responseBody = await result.Content.ReadAsStringAsync(token);

                if (!result.IsSuccessStatusCode) 
                {
                    if (_printToLog) Log.Debug($"OnvifCamera: SOAP request to {url} failed: {result.StatusCode}. Response: {responseBody}", this);
                }
                return responseBody;
            }
            catch (Exception e)
            {
                if (_printToLog && !token.IsCancellationRequested) 
                {
                    // Filter out cancellation exceptions from logs to avoid spam
                    if (e is TaskCanceledException || e is OperationCanceledException) return null;
                    if (e is AggregateException ae && ae.InnerExceptions.All(x => x is TaskCanceledException || x is OperationCanceledException)) return null;

                    Log.Debug($"OnvifCamera: Connection error to {url}: {e.Message}", this);
                }
                return null;
            }
        }

        #region Discovery Logic
        private void StartDiscovery(string localIpStr)
        {
            if (_isDiscovering) StopDiscovery();

            if (!IPAddress.TryParse(localIpStr, out var localIp))
            {
                Log.Warning("OnvifCamera: Please select a valid Local IP for discovery.", this);
                return;
            }

            try
            {
                _discoveryClient = new UdpClient(new IPEndPoint(localIp, 0));
                _discoveryClient.EnableBroadcast = true;
                _discoveryClient.MulticastLoopback = false;
                
                _isDiscovering = true;
                _discoveredDevices.Clear();
                _discoveryCts = new CancellationTokenSource();
                
                if (_printToLog) Log.Debug($"OnvifCamera: Starting discovery on {localIp}...", this);

                Task.Run(() => ReceiveDiscoveryResponses(_discoveryCts.Token));
                Task.Run(() => SendDiscoveryProbes(_discoveryCts.Token));
            }
            catch (Exception e)
            {
                Log.Warning($"OnvifCamera: Failed to start discovery: {e.Message}", this);
                StopDiscovery();
            }
        }

        private void StopDiscovery()
        {
            if (!_isDiscovering) return;
            
            _isDiscovering = false;
            _discoveryCts?.Cancel();
            _discoveryClient?.Close();
            _discoveryClient = null;
            
            if (_printToLog) Log.Debug("OnvifCamera: Stopped discovery.", this);
        }

        private async Task SendDiscoveryProbes(CancellationToken token)
        {
            var multicastEndpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702);
            var messageId = Guid.NewGuid().ToString();
            var probe = string.Format(WsDiscoveryProbeMessage, messageId);
            var bytes = Encoding.UTF8.GetBytes(probe);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_discoveryClient != null)
                        await _discoveryClient.SendAsync(bytes, bytes.Length, multicastEndpoint);
                }
                catch (Exception e)
                {
                    if (_printToLog) Log.Debug($"OnvifCamera: Probe send error: {e.Message}", this);
                }
                await Task.Delay(3000, token); // Send probe every 3 seconds
            }
        }

        private async Task ReceiveDiscoveryResponses(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _discoveryClient != null)
            {
                try
                {
                    var result = await _discoveryClient.ReceiveAsync(token);
                    var response = Encoding.UTF8.GetString(result.Buffer);
                    ParseDiscoveryResponse(response, result.RemoteEndPoint.Address.ToString());
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception e)
                {
                    if (_printToLog && !token.IsCancellationRequested) Log.Debug($"OnvifCamera: Receive error: {e.Message}", this);
                }
            }
        }

        private void ParseDiscoveryResponse(string xml, string fallbackIp)
        {
            try
            {
                var xdoc = XDocument.Parse(xml);
                var ns = XNamespace.Get("http://schemas.xmlsoap.org/ws/2005/04/discovery");

                var probeMatch = xdoc.Descendants(ns + "ProbeMatch").FirstOrDefault();
                if (probeMatch == null) return;

                var xAddrs = probeMatch.Element(ns + "XAddrs")?.Value;
                var scopes = probeMatch.Element(ns + "Scopes")?.Value;
                
                // Extract IP/Host from XAddrs (space separated list of URLs)
                string address = fallbackIp;
                if (!string.IsNullOrEmpty(xAddrs))
                {
                    var firstAddr = xAddrs.Split(' ')[0];
                    try { address = new Uri(firstAddr).Authority; }
                    catch
                    {
                        // ignored
                    }
                }

                // Extract Name from Scopes (onvif://www.onvif.org/name/MyCamera)
                string name = "Unknown Device";
                if (!string.IsNullOrEmpty(scopes))
                {
                    foreach (var scope in scopes.Split(' '))
                    {
                        if (scope.StartsWith("onvif://www.onvif.org/name/"))
                        {
                            name = WebUtility.UrlDecode(scope.Substring("onvif://www.onvif.org/name/".Length));
                            break;
                        }
                        else if (scope.StartsWith("onvif://www.onvif.org/hardware/"))
                        {
                            name = WebUtility.UrlDecode(scope.Substring("onvif://www.onvif.org/hardware/".Length));
                        }
                    }
                }

                var key = address;
                var display = $"{name} ({address})";
                _discoveredDevices.TryAdd(key, display);
            }
            catch { }
        }

        private const string WsDiscoveryProbeMessage =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
              <e:Envelope xmlns:e=""http://www.w3.org/2003/05/soap-envelope""
                          xmlns:w=""http://schemas.xmlsoap.org/ws/2004/08/addressing""
                          xmlns:d=""http://schemas.xmlsoap.org/ws/2005/04/discovery""
                          xmlns:dn=""http://www.onvif.org/ver10/network/wsdl"">
                <e:Header>
                  <w:MessageID>uuid:{0}</w:MessageID>
                  <w:To e:mustUnderstand=""true"">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
                  <w:Action a:mustUnderstand=""true"">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
                </e:Header>
                <e:Body>
                  <d:Probe>
                    <d:Types>dn:NetworkVideoTransmitter</d:Types>
                  </d:Probe>
                </e:Body>
              </e:Envelope>";
        #endregion
        #endregion

        #region Network Interface Logic
        private static List<NetworkAdapterInfo> _networkInterfaces = new();

        private static List<NetworkAdapterInfo> GetNetworkInterfaces()
        {
            var list = new List<NetworkAdapterInfo>();
            list.Add(new NetworkAdapterInfo(IPAddress.Any, IPAddress.Any, "Any"));
            list.Add(new NetworkAdapterInfo(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), "Localhost"));
            
            try
            {
                list.AddRange(from ni in NetworkInterface.GetAllNetworkInterfaces()
                              where ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                              from ip in ni.GetIPProperties().UnicastAddresses
                              where ip.Address.AddressFamily == AddressFamily.InterNetwork
                              select new NetworkAdapterInfo(ip.Address, ip.IPv4Mask, ni.Name));
            }
            catch (Exception e)
            {
                Log.Warning("Could not enumerate network interfaces: " + e.Message);
            }
            return list;
        }

        private sealed record NetworkAdapterInfo(IPAddress IpAddress, IPAddress SubnetMask, string Name)
        {
            public string DisplayName => $"{Name} ({IpAddress})";
        }
        #endregion

        #region IStatusProvider & ICustomDropdownHolder
        public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusMessage.StartsWith("Error") ? IStatusProvider.StatusLevel.Error : IStatusProvider.StatusLevel.Success;
        public string GetStatusMessage() => _lastStatusMessage;
        private void SetStatus(string message) => _lastStatusMessage = message;
        private volatile string _lastStatusMessage = "Not connected.";

        string ICustomDropdownHolder.GetValueForInput(Guid inputId)
        {
            if (inputId == LocalIpAddress.Id) return LocalIpAddress.Value ?? string.Empty;
            if (inputId == Address.Id) return Address.Value ?? string.Empty;
            return string.Empty;
        }

        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
        {
            if (inputId == LocalIpAddress.Id)
            {
                _networkInterfaces = GetNetworkInterfaces();
                foreach (var adapter in _networkInterfaces) yield return adapter.DisplayName;
            }
            else if (inputId == Address.Id)
            {
                if (_discoveredDevices.IsEmpty) yield return _isDiscovering ? "Searching..." : "Enable 'Discover' to search...";
                
                foreach (var device in _discoveredDevices.Values.OrderBy(d => d))
                    yield return device;
            }
        }

        void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
        {
            if (string.IsNullOrEmpty(selected) || !isAListItem) return;
            if (inputId == LocalIpAddress.Id)
            {
                var foundAdapter = _networkInterfaces.FirstOrDefault(i => i.DisplayName == selected);
                if (foundAdapter != null) LocalIpAddress.SetTypedInputValue(foundAdapter.IpAddress.ToString());
            }
            else if (inputId == Address.Id)
            {
                // Ignore status messages
                if (selected.StartsWith("Enable 'Discover'") || selected.StartsWith("Searching")) return;

                // Robustly extract address from "Name (Address)" format
                var lastOpen = selected.LastIndexOf('(');
                var lastClose = selected.LastIndexOf(')');
                
                string ipToSet = selected;
                if (lastOpen != -1 && lastClose > lastOpen)
                {
                    ipToSet = selected.Substring(lastOpen + 1, lastClose - lastOpen - 1);
                }
                
                ipToSet = ipToSet.Trim();
                if (string.IsNullOrEmpty(ipToSet)) return;

                if (_printToLog) Log.Debug($"OnvifCamera: Selected '{selected}' -> Setting Address to '{ipToSet}'", this);
                Address.SetTypedInputValue(ipToSet);
            }
        }
        #endregion

        private volatile bool _connected;
        private bool _lastConnectState;
        private Thread _captureThread;
        private CancellationTokenSource _cancellationTokenSource;
        private VideoCapture _capture;
        private Texture2D _gpuTexture;
        private Mat _sharedBgraMat;
        private readonly object _lockObject = new();
        private bool _printToLog;
        private double _lastNetworkRefreshTime;
        private string _ptzServiceUrl;
        private string _profileToken;
        private bool _wasMoving;
        private double _lastPtzPollTime;
        private Vector3 _currentPtzValue;
        
        private float _dampedPan;
        private float _dampedTilt;
        private float _dampedZoom;

        private float _lastSentPan;
        private float _lastSentTilt;
        private float _lastSentZoom;
        private double _lastPtzMoveTime;
        
        private volatile bool _isPollingPtz;
        private volatile bool _ptzSupported = true;
        private bool _ptzErrorBackoff;
        private HttpClient _httpClient;
        
        private volatile bool _isDiscovering;
        private UdpClient _discoveryClient;
        private CancellationTokenSource _discoveryCts;
        private TimeSpan _timeOffset = TimeSpan.Zero;
        private readonly ConcurrentDictionary<string, string> _discoveredDevices = new();
        
        private CancellationTokenSource _disposeCts;
        private volatile bool _isSendingPtz;
        
        [Input(Guid = "74185692-2982-4895-8963-125478963214")]
        public readonly InputSlot<string> LocalIpAddress = new();
        
        [Input(Guid = "8451268E-3156-4F5C-8932-156842356842")]
        public readonly InputSlot<bool> Discover = new();

        [Input(Guid = "1e56f26e-6e78-4f2e-cf56-7f8a8b9c0d2f")]
        public readonly InputSlot<string> Address = new();

        [Input(Guid = "2f67a37f-7f89-403f-da67-8a9b0c1d2e3f")]
        public readonly InputSlot<string> Username = new();

        [Input(Guid = "3b78b48b-8b90-514b-eb78-9b0c1d2e3f4b")]
        public readonly InputSlot<string> Password = new();

        [Input(Guid = "4c89c59c-9c01-625c-fc89-0c1d2e3f4c5c")]
        public readonly InputSlot<bool> Connect = new();
        
        [Input(Guid = "8a23a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<bool> Move = new();
        
        [Input(Guid = "5d90d60d-0d12-736d-ad90-1d2e3f4d5d6d")]
        public readonly InputSlot<float> Pan = new();

        [Input(Guid = "6e01e71e-1e23-847e-be01-2e3f4e5e6e7e")]
        public readonly InputSlot<float> Tilt = new();

        [Input(Guid = "7f12f82f-2f34-958f-cf12-3f4f5f6f7f8f")]
        public readonly InputSlot<float> Zoom = new();
        
        [Input(Guid = "9B8C7D6E-5F4A-3B2C-1D0E-9F8E7D6C5B4A")]
        public readonly InputSlot<bool> PrintToLog = new();
    }
}