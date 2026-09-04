using System.Threading;
using Rug.Osc;

namespace T3.Editor.Gui.Interaction.Timing;

/// <summary>
/// Receives midi sync signals via OSC. 
/// </summary>
/// <remarks>
/// These signals need to be send once every beat with channel "/beatTimer" once every 24 Midi MTC signals.
///
/// Also see this repository: 
/// </remarks>
public static class OscBeatTiming
{
        
    public static int BeatCounter=0;

    public static void Init()
    {   
        // This is the port we are going to listen on 
        int port = 12345;

        // Create the receiver
        _receiver = new OscReceiver(port);

        // Create a thread to do the listening
        _thread = new Thread(ListenLoop);

        // Connect the receiver
        _receiver.Connect();

        // Start the listen thread
        _thread.Start();
            

        // Wait for the listen thread to exit
        //thread.Join();
        Initialized = true;
    }

    private static void ListenLoop()
    {
        try
        {
            while (_receiver.State != OscSocketState.Closed)
            {
                // if we are in a state to recieve
                if (_receiver.State == OscSocketState.Connected)
                {
                    // get the next message 
                    // this will block until one arrives or the socket is closed
                    var oscPacket = _receiver.Receive();
                        
                    // Write the packet to the console 
                    var oscMessage = OscMessage.Parse(oscPacket.ToString());

                    if (oscMessage.Address == "/beatTimer")
                    {
                        BeatTiming.TriggerSyncTap();
                        BeatCounter++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // if the socket was connected when this happens
            // then tell the user
            if (_receiver.State == OscSocketState.Connected)
            {
                Log.Debug("Exception in listen loop");
                Log.Debug(ex.Message);
            }
        }
    }
    private static OscReceiver _receiver;
    private static Thread _thread;
    public static bool Initialized { get; private set; }
}