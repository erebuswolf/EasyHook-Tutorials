// RemoteFileMonitor (File: FileMonitorHook\InjectionEntryPoint.cs)
//
// Copyright (c) 2017 Justin Stenning
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
// Please visit https://easyhook.github.io for more information
// about the project, latest updates and other tutorials.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


[StructLayout(LayoutKind.Explicit)]
internal struct XInputState {
    /// <summary>
    /// The PacketNumber.
    /// </summary>
    [FieldOffset(0)] public int PacketNumber;

    /// <summary>
    /// The Gamepad.
    /// </summary>
    [FieldOffset(4)] public XInputGamepad Gamepad;

    /// <summary>
    /// Copies the source.
    /// </summary>
    /// <param name="source">The Source.</param>
    public void Copy(XInputState source) {
        PacketNumber = source.PacketNumber;
        Gamepad.Copy(source.Gamepad);
    }
}
[StructLayout(LayoutKind.Explicit)]
internal struct XInputGamepad {
    /// <summary>
    /// The Buttons.
    /// </summary>
    [MarshalAs(UnmanagedType.I2)] [FieldOffset(0)] public short wButtons;

    /// <summary>
    /// The back left trigger.
    /// </summary>
    [MarshalAs(UnmanagedType.I1)] [FieldOffset(2)] public byte bLeftTrigger;

    /// <summary>
    /// The back right trigger.
    /// </summary>
    [MarshalAs(UnmanagedType.I1)] [FieldOffset(3)] public byte bRightTrigger;

    /// <summary>
    /// The thumb left X.
    /// </summary>
    [MarshalAs(UnmanagedType.I2)] [FieldOffset(4)] public short sThumbLX;

    /// <summary>
    /// The thumb left Y.
    /// </summary>
    [MarshalAs(UnmanagedType.I2)] [FieldOffset(6)] public short sThumbLY;

    /// <summary>
    /// The thumb right X.
    /// </summary>
    [MarshalAs(UnmanagedType.I2)] [FieldOffset(8)] public short sThumbRX;

    /// <summary>
    /// The thumb right Y.
    /// </summary>
    [MarshalAs(UnmanagedType.I2)] [FieldOffset(10)] public short sThumbRY;

    /// <summary>
    /// A value indicating whether the button was pressed.
    /// </summary>
    /// <param name="buttonFlags">The ButtonFlags.</param>
    /// <returns>True if the button was pressed.</returns>
    public bool IsButtonPressed(int buttonFlags) {
        return (wButtons & buttonFlags) != 0;
    }

    /// <summary>
    /// A value indicating whether the button is accessable on the gamepad.
    /// </summary>
    /// <param name="buttonFlags">The ButtonFlags.</param>
    /// <returns>True if accessable.</returns>
    public bool IsButtonPresent(int buttonFlags) {
        return (wButtons & buttonFlags) == buttonFlags;
    }

    /// <summary>
    /// Copies the source.
    /// </summary>
    /// <param name="source">The Source.</param>
    public void Copy(XInputGamepad source) {
        sThumbLX = source.sThumbLX;
        sThumbLY = source.sThumbLY;
        sThumbRX = source.sThumbRX;
        sThumbRY = source.sThumbRY;
        bLeftTrigger = source.bLeftTrigger;
        bRightTrigger = source.bRightTrigger;
        wButtons = source.wButtons;
    }
}

namespace FileMonitorHook
{
    /// <summary>
    /// EasyHook will look for a class implementing <see cref="EasyHook.IEntryPoint"/> during injection. This
    /// becomes the entry point within the target process after injection is complete.
    /// </summary>
    public class InjectionEntryPoint: EasyHook.IEntryPoint
    {
        /// <summary>
        /// Reference to the server interface within FileMonitor
        /// </summary>
        ServerInterface _server = null;

        /// <summary>
        /// Message queue of all files accessed
        /// </summary>
        Queue<string> _messageQueue = new Queue<string>();

        int _sleepTime;

        /// <summary>
        /// EasyHook requires a constructor that matches <paramref name="context"/> and any additional parameters as provided
        /// in the original call to <see cref="EasyHook.RemoteHooking.Inject(int, EasyHook.InjectionOptions, string, string, object[])"/>.
        /// 
        /// Multiple constructors can exist on the same <see cref="EasyHook.IEntryPoint"/>, providing that each one has a corresponding Run method (e.g. <see cref="Run(EasyHook.RemoteHooking.IContext, string)"/>).
        /// </summary>
        /// <param name="context">The RemoteHooking context</param>
        /// <param name="channelName">The name of the IPC channel</param>
        public InjectionEntryPoint(
            EasyHook.RemoteHooking.IContext context,
            string channelName, 
            int sleepTime)
        {
            // Connect to server object using provided channel name
            _server = EasyHook.RemoteHooking.IpcConnectClient<ServerInterface>(channelName);

            // If Ping fails then the Run method will be not be called
            _server.Ping();
        }

        /// <summary>
        /// The main entry point for our logic once injected within the target process. 
        /// This is where the hooks will be created, and a loop will be entered until host process exits.
        /// EasyHook requires a matching Run method for the constructor
        /// </summary>
        /// <param name="context">The RemoteHooking context</param>
        /// <param name="channelName">The name of the IPC channel</param>
        public void Run(
            EasyHook.RemoteHooking.IContext context,
            string channelName,
            int sleepTime)
        {
            this._sleepTime = sleepTime;

            // Injection is now complete and the server interface is connected
            _server.IsInstalled(EasyHook.RemoteHooking.GetCurrentProcessId());

            // Install hooks
            var readInputHook = EasyHook.LocalHook.Create(
               EasyHook.LocalHook.GetProcAddress("xinput1_3.dll", "XInputGetState"),
               new XInputGetState_Delegate(XInputGetState_Hook),
               this);

            // Activate hooks on all threads except the current thread
            readInputHook.ThreadACL.SetExclusiveACL(new Int32[] { 0 });

            _server.ReportMessage("Read Input hooks installed");

            // Wake up the process (required if using RemoteHooking.CreateAndInject)
            EasyHook.RemoteHooking.WakeUpProcess();

            try
            {
                // Loop until FileMonitor closes (i.e. IPC fails)
                while (true)
                {
                    System.Threading.Thread.Sleep(500);

                    string[] queued = null;

                    lock (_messageQueue)
                    {
                        queued = _messageQueue.ToArray();
                        _messageQueue.Clear();
                    }

                    // Send newly monitored file accesses to FileMonitor
                    if (queued != null && queued.Length > 0)
                    {
                        _server.ReportMessages(queued);
                    }
                    else
                    {
                        _server.Ping();
                    }
                }
            }
            catch
            {
                // Ping() or ReportMessages() will raise an exception if host is unreachable
            }

            // Remove hooks
            readInputHook.Dispose();

            // Finalise cleanup of hooks
            EasyHook.LocalHook.Release();
        }

        /// <summary>
        /// Gets the XInput state.
        /// </summary>
        /// <param name="dwUserIndex">The Index.</param>
        /// <param name="pState">The InputState.</param>
        /// <returns></returns>
        [DllImport("xinput1_3.dll")]
        internal static extern int XInputGetState
            (
            int dwUserIndex,
            ref XInputState pState
            );

        /// <summary>
        /// The CreateFile delegate, this is needed to create a delegate of our hook function <see cref=" XInputGetState_hook(int, ref XInputState )"/>.
        /// </summary>
        /// <param name="dwUserIndex"></param>
        /// <param name="pState"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall,
                    CharSet = CharSet.Unicode,
                    SetLastError = true)]
        delegate int XInputGetState_Delegate(
                    int dwUserIndex,
                    ref XInputState pState);

        /// <summary>
        /// The XInputGetState hook function. This will be called instead of the original XInputGetState once hooked.
        /// </summary>
        /// <param name="dwUserIndex"></param>
        /// <param name="pState"></param>
        /// <returns></returns>
        int XInputGetState_Hook(
                    int dwUserIndex,
                    ref XInputState pState) {
            try {
                lock (this._messageQueue) {
                    if (this._messageQueue.Count < 1000) {
                        string user = dwUserIndex.ToString();

                        // Add message to send to FileMonitor
                        this._messageQueue.Enqueue(
                            string.Format("[{0}:{1}]: XInputGetState call for user ({2})",
                            EasyHook.RemoteHooking.GetCurrentProcessId(),
                            EasyHook.RemoteHooking.GetCurrentThreadId(),
                            user));
                    }
                }
            }
            catch {
                // swallow exceptions so that any issues caused by this code do not crash target process
            }

            if (dwUserIndex == 0 && _sleepTime > 0) {
                Thread.Sleep(_sleepTime);
            }

            // now call the original API...
            return XInputGetState(dwUserIndex, ref pState);
        }
    }
}
