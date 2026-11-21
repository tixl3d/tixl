/*
* MIT License
*
* Copyright (c) 2025 Open Media Transport Contributors
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace libomtnet
{
    public class OMTPublicConstants
    {
        public static int DISCOVERY_SERVER_DEFAULT_PORT = 6399;
    }
    internal class OMTConstants
    {
        public static int NETWORK_SEND_BUFFER = 65536;
        public static int NETWORK_SEND_RECEIVE_BUFFER = 65536;
        public static int NETWORK_RECEIVE_BUFFER = 1048576 * 8; //8MB is a safe maximum for MacOS platforms

        //On Windows setting a high receive size per async leads to random stalls on certain NICs > 1gbps
        //128KB as a transfer limit is intended to workaround this issue.
        public static int NETWORK_RECEIVE_MAX_TRANSFER = 128 * 1024;

        public static int NETWORK_ASYNC_COUNT = 4;
        public static int NETWORK_ASYNC_BUFFER_AV = 1048576;
        public static int NETWORK_ASYNC_BUFFER_META = 65536;

        //For OMTDiscoveryServer
        public static int NETWORK_ASYNC_COUNT_META_ONLY = 64;
        public static int NETWORK_ASYNC_BUFFER_META_ONLY = 1024;

        public static int VIDEO_FRAME_POOL_COUNT = 4;

        public static int VIDEO_MIN_SIZE = 65536;
        public static int VIDEO_MAX_SIZE = 10485760;

        public static int AUDIO_FRAME_POOL_COUNT = 10;

        public static int AUDIO_MIN_SIZE = 65536;
        public static int AUDIO_MAX_SIZE = 1048576;

        public static int NETWORK_PORT_START = 6400;
        public static int NETWORK_PORT_END = 6600;

        public static int AUDIO_SAMPLE_SIZE = 4;
        public static int METADATA_MAX_COUNT = 60;

        public static int METADATA_FRAME_SIZE = 65536;

        public static string URL_PREFIX = "omt://";

        //Timeout in milliseconds when trying to connect to each IP of a sender
        public static int NETWORK_CONNECTION_TIMEOUT = 2000;
    }
}
