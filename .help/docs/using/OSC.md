# Sending and receiving OSC messages


## Different end points

Sending network messages can be a complex topic, because there are many pitfalls like different protocols and firewall settings.
Here we collect frequent questions on that topic:

## ZigOSC (iOS app)


## Hexler Protokol
Protokol is a very nice debugging and logging application available for various platforms. In Windows you might need to switch the protokol from IP6 to IP4:
![image](https://github.com/tixl3d/tixl/assets/1732545/1e540459-22b7-427d-8a0c-1d8a88333d0c)


## Bitwig

1. Install the [DrivenByMoss](https://www.mossgrabers.de/Software/Bitwig/Bitwig.html) extension for your version of Bitwig.
2. Follow the installation guide for "Open Sound Controllers" and create a new OSC controller on the Controllers page in Settings:

![image](https://github.com/tixl3d/tixl/assets/113698935/ddba4c52-4706-4a8f-8281-7b2461796350)

You should see the controller settings then (or open them with the gear icon):

![image](https://github.com/tixl3d/tixl/assets/113698935/7178155e-78ae-4e21-b5be-e8826c44a37e)

3. Set up the ports and IP address and the resolution for the values you need.

Important: The values for for instance volume faders are sent and expected as integers between 0 and the value you set in the "Value Resolution" dropdown, so 0-127 for Low, 0-1023 for Medium and 0-16383 for High.

## SuperCollider and Tidal Cycles
[TidalCycles](https://tidalcycles.org/) is a music live coding environment using the haskel programming language and built on top of [SuperCollider](https://supercollider.github.io/). It's incredibly powerful and expressive. The great thing about SC is that all internal communication is based on a client server architecture that uses OSC messages for communication. 

This means that intercepting and forwarding these messages to T3 is already build in:

In super collider the only thing you have the execute is a block like this (adjust the 192.169.1.6 IP-Address to your needs).
```
(
var targetAddr = NetAddr("192.168.1.6", 8000);

s.waitForBoot {

    // Add a listener for incoming OSC messages
    OSCFunc({ |msg, time, addr|
        targetAddr.sendBundle(time, msg, addr);
    });
}
)
```

When now in Tidal you execute something like...

```haskel
import Sound.

setcps 0.11

d1 $ n "[12 123 23 23 2]" # s "test2" 
d2 $ n "[1 2 3 4]" # s "test3" 
```

We can do the following steps in TiXL:

1. Create an [OscInput] operator
2. Enable the log-messages parameter

... we should receive OSC messages in TiXL that look like this:

```
/dirt/play, "id", "1", "cps", 0.5208333f, "cycle", 1588.8f, "delta", 0.384f, "n", 2f, "orbit", 0, "s", "test2"
```

Like many other applications, Super Collider encodes data into a list of key/value pairs. In the example message above, you might notice how every second attribute is a string ("id", "cps", "cycle", etc.). To make it more convenient to work with OSC messages in that format, you can enable [OscInput]'s UseKeyValuePairs parameter which will give you a result like this:

![image](https://github.com/tixl3d/tixl/assets/1732545/8087d688-43a9-4e4c-8ebb-4a8453308095)



That's great because we can use the "cycles" attribute to drive the TiXL time via [SetCommandTime].

